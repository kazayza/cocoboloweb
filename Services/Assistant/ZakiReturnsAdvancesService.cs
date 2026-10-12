using System.Security.Claims;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;
using static COCOBOLOERPNEW.Services.Assistant.ZakiInvoiceQueries;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// أدوات المرتجعات ودفعات العملاء المقدمة في زكي.
/// - get_returns_summary: مرتجعات البيع والشراء لفترة من جدول Transactions المرآة
///   (نفس منطق الشاشات عبر Base).
/// - get_customer_advances: دفعات عميل مقدمة (عربون/رسوم معاينة/دفعة) —
///   إعادة استخدام IInvoiceService.GetCustomerAdvancesAsync عشان نفس أرقام الشاشة.
/// ملاحظات:
/// - «أوامر التشغيل» في الإنتاج = الفواتير غير المسلّمة (مغطاة بأدوات موجودة).
/// - «مشتريات المصنع» = شاشة فواتير الشراء (مغطاة بأدوات موجودة).
/// </summary>
public class ZakiReturnsAdvancesService
{
    public const string GetReturnsSummary = "get_returns_summary";
    public const string GetCustomerAdvances = "get_customer_advances";

    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly IInvoiceService _invoices;
    private readonly ILogger<ZakiReturnsAdvancesService> _logger;

    public ZakiReturnsAdvancesService(
        IDbContextFactory<db24804Context> dbFactory,
        IInvoiceService invoices,
        ILogger<ZakiReturnsAdvancesService> logger)
    {
        _dbFactory = dbFactory;
        _invoices = invoices;
        _logger = logger;
    }

    public static bool Handles(string toolName) =>
        toolName is GetReturnsSummary or GetCustomerAdvances;

    public static bool CanViewReturns(ClaimsPrincipal user) =>
        CanViewSalesInvoices(user) || CanViewPurchaseInvoices(user);

    public static bool CanViewAdvances(ClaimsPrincipal user) =>
        CanViewSalesInvoices(user) || CanViewReceipts(user);

    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user)
    {
        var list = new List<AiToolDefinition>();
        if (CanViewReturns(user)) list.Add(ReturnsDefinition());
        if (CanViewAdvances(user)) list.Add(AdvancesDefinition());
        return list;
    }

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string name, string argumentsJson, CancellationToken ct = default)
    {
        var args = ParseArgs(argumentsJson);

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var scope = await BuildScopeAsync(db, user, null);

            return name == GetReturnsSummary
                ? (CanViewReturns(user)
                    ? await ReturnsAsync(db, scope, user, args, ct)
                    : Denied())
                : (CanViewAdvances(user)
                    ? await AdvancesAsync(db, args, ct)
                    : Denied());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki returns/advances: query failed ({Tool})", name);
            return Message("ماقدرتش أجيب البيانات دلوقتي، حاول تاني بعد شوية.");
        }
    }

    // ── مرتجعات البيع والشراء لفترة ──
    private static async Task<ZakiToolOutcome> ReturnsAsync(
        db24804Context db, ZakiInvoiceScope scope, ClaimsPrincipal user,
        System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var (start, label) = PeriodRange(ReadString(args, "period", "month"), DateTime.Today);

        var headers = new[] { "النوع", "العدد", "القيمة" };
        var rows = new List<IReadOnlyList<string>>();
        var jsonParts = new Dictionary<string, object?> { ["الفترة"] = label };

        if (CanViewSalesInvoices(user))
        {
            var sr = Base(db, scope, TransactionTypes.SaleReturn).FromDate(start);
            var srCount = await sr.CountAsync(ct);
            var srTotal = await sr.SumAsync(t => (decimal?)t.GrandTotal, ct) ?? 0m;

            rows.Add(new[] { "مرتجعات البيع", srCount.ToString(Culture), Money(srTotal) });
            jsonParts["مرتجعات البيع (عدد)"] = srCount;
            jsonParts["مرتجعات البيع (قيمة)"] = Money(srTotal);
        }

        if (CanViewPurchaseInvoices(user))
        {
            var pr = Base(db, scope, TransactionTypes.PurchaseReturn).FromDate(start);
            var prCount = await pr.CountAsync(ct);
            var prTotal = await pr.SumAsync(t => (decimal?)t.GrandTotal, ct) ?? 0m;

            rows.Add(new[] { "مرتجعات الشراء", prCount.ToString(Culture), Money(prTotal) });
            jsonParts["مرتجعات الشراء (عدد)"] = prCount;
            jsonParts["مرتجعات الشراء (قيمة)"] = Money(prTotal);
        }

        if (rows.Count == 0)
            return Message("مفيش صلاحية لعرض المرتجعات.");

        jsonParts["ملخص المرتجعات"] = ToObjects(headers, rows);

        return new ZakiToolOutcome(ToJson(jsonParts),
            new ZakiTable("ملخص المرتجعات — " + label, headers, rows));
    }

    // ── دفعات عميل مقدمة ──
    private async Task<ZakiToolOutcome> AdvancesAsync(
        db24804Context db, System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var search = ReadRawString(args, "customer_name");
        if (search.Length == 0)
            return Message("محتاج اسم العميل — قوللي اسم العميل اللي عايز دفعاته المقدمة.");

        var unappliedOnly = ReadBool(args, "unapplied_only", false);

        var parties = await db.Parties.AsNoTracking()
            .Where(p => p.PartyName.Contains(search))
            .OrderBy(p => p.PartyName)
            .Take(3)
            .Select(p => new { p.PartyId, p.PartyName })
            .ToListAsync(ct);

        if (parties.Count == 0)
            return Message($"مالقتش عميل بالاسم ده: {search}");

        var headers = new[] { "العميل", "النوع", "البيان", "المبلغ", "الحالة", "التاريخ" };
        var rows = new List<IReadOnlyList<string>>();
        var jsonParts = new Dictionary<string, object?>();
        decimal grandTotal = 0m;

        foreach (var party in parties)
        {
            var advances = await _invoices.GetCustomerAdvancesAsync(party.PartyId, unappliedOnly);
            var balance = await _invoices.GetCustomerAdvanceBalanceAsync(party.PartyId);

            foreach (var a in advances)
            {
                rows.Add(new[]
                {
                    party.PartyName,
                    string.IsNullOrWhiteSpace(a.ChargeType) ? "دفعة مقدمة" : a.ChargeType!,
                    string.IsNullOrWhiteSpace(a.ChargeDescription) ? "—" : a.ChargeDescription!,
                    Money(a.ChargeAmount),
                    a.IsApplied ? "مخصومة" : "متاحة",
                    a.CreatedAt.HasValue ? a.CreatedAt.Value.ToString("yyyy/MM/dd", Culture) : "—"
                });
                grandTotal += a.ChargeAmount;
            }

            jsonParts[$"الرصيد المتاح — {party.PartyName}"] = Money(balance);
        }

        if (rows.Count == 0)
        {
            jsonParts["ملاحظة"] = unappliedOnly ? "مفيش دفعات متاحة (غير مخصومة)" : "مفيش دفعات مقدمة";
            return new ZakiToolOutcome(ToJson(jsonParts), null);
        }

        jsonParts["عدد الدفعات"] = rows.Count;
        jsonParts["إجمالي قيمة الدفعات المعروضة"] = Money(grandTotal);
        jsonParts["دفعات مقدمة"] = ToObjects(headers, rows);

        var title = parties.Count == 1
            ? $"الدفعات المقدمة — {parties[0].PartyName}"
            : $"الدفعات المقدمة — نتائج البحث عن «{search}»";

        return new ZakiToolOutcome(ToJson(jsonParts), new ZakiTable(title, headers, rows));
    }

    private static AiToolDefinition ReturnsDefinition() => new(
        GetReturnsSummary,
        "ملخص المرتجعات لفترة: عدد وقيمة مرتجعات البيع ومرتجعات الشراء.",
        new
        {
            type = "object",
            properties = new
            {
                period = PeriodSchema("الافتراضي month")
            }
        });

    private static AiToolDefinition AdvancesDefinition() => new(
        GetCustomerAdvances,
        "الدفعات المقدمة لعميل بالاسم (عربون/رسوم معاينة/دفعة مقدمة) ورصيده المتاح منها. لازم customer_name.",
        new
        {
            type = "object",
            properties = new
            {
                customer_name = new { type = "string", description = "اسم العميل (مطلوب)" },
                unapplied_only = new { type = "boolean", description = "true للدفعات المتاحة (غير المخصومة) فقط" }
            }
        });

    private static object PeriodSchema(string description) => new
    {
        type = "string",
        description,
        @enum = new[] { "today", "week", "month", "year", "all" }
    };
}
