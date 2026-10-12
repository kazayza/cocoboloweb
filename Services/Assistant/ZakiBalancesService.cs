using System.Security.Claims;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;
using static COCOBOLOERPNEW.Services.Assistant.ZakiInvoiceQueries;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// أدوات الأرصدة في زكي: مديونيات العملاء وأرصدة الموردين.
/// المصدر: جدول Transactions المرآة — نفس منطق الشاشات (Base بيطبق
/// استبعاد الملغي + نطاق التاريخ + إخفاء فواتير مديري الحسابات المحمية).
/// الصلاحيات:
/// - مديونيات العملاء: نفس شاشة المدفوعات (CanViewReceipts).
/// - أرصدة الموردين: نفس مدفوعات الموردين (CanViewPurchasePayments).
/// </summary>
public class ZakiBalancesService
{
    public const string GetCustomerBalances = "get_customer_balances";
    public const string GetSupplierBalances = "get_supplier_balances";

    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly ILogger<ZakiBalancesService> _logger;

    public ZakiBalancesService(
        IDbContextFactory<db24804Context> dbFactory,
        ILogger<ZakiBalancesService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public static bool Handles(string toolName) =>
        toolName is GetCustomerBalances or GetSupplierBalances;

    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user)
    {
        var list = new List<AiToolDefinition>();
        if (CanViewReceipts(user))
            list.Add(CustomerBalancesDefinition());
        if (CanViewPurchasePayments(user))
            list.Add(SupplierBalancesDefinition());
        return list;
    }

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string name, string argumentsJson, CancellationToken ct = default)
    {
        var allowed = name == GetCustomerBalances
            ? CanViewReceipts(user)
            : CanViewPurchasePayments(user);

        if (!allowed) return Denied();

        var args = ParseArgs(argumentsJson);

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var scope = await BuildScopeAsync(db, user, null);

            return name == GetCustomerBalances
                ? await BalancesAsync(db, scope, TransactionTypes.Sale, args, "العميل", "مديونيات العملاء", ct)
                : await BalancesAsync(db, scope, TransactionTypes.Purchase, args, "المورد", "أرصدة الموردين", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki balances: query failed ({Tool})", name);
            return Message("ماقدرتش أجيب الأرصدة دلوقتي، حاول تاني بعد شوية.");
        }
    }

    private static async Task<ZakiToolOutcome> BalancesAsync(
        db24804Context db,
        ZakiInvoiceScope scope,
        string txType,
        System.Text.Json.JsonElement args,
        string partyLabel,
        string title,
        CancellationToken ct)
    {
        var search = ReadRawString(args, txType == TransactionTypes.Sale ? "customer_name" : "supplier_name");
        var includeSettled = ReadBool(args, "include_settled", false);
        var limit = ReadInt(args, "limit", 20, 1, 50);
        var today = DateTime.Today;

        var q = Base(db, scope, txType);

        // فلتر الاسم (اختياري)
        if (search.Length > 0)
        {
            var ids = await db.Parties.AsNoTracking()
                .Where(p => p.PartyName.Contains(search))
                .Select(p => p.PartyId)
                .ToListAsync(ct);

            if (ids.Count == 0)
                return Message($"مالقتش {partyLabel} بالاسم ده.");

            q = q.Where(t => ids.Contains(t.PartyId));
        }

        // إجماليات لكل طرف
        var grouped = await q
            .GroupBy(t => t.PartyId)
            .Select(g => new
            {
                PartyId = g.Key,
                Count = g.Count(),
                Total = g.Sum(x => x.GrandTotal),
                Paid = g.Sum(x => x.PaidAmount)
            })
            .ToListAsync(ct);

        // المتأخر من المتبقي (فاتورته عدّت تاريخ الاستحقاق)
        var overdueByParty = await q
            .Where(x => x.GrandTotal > x.PaidAmount && x.DueDate != null && x.DueDate < today)
            .GroupBy(x => x.PartyId)
            .Select(g => new { PartyId = g.Key, Overdue = g.Sum(x => x.GrandTotal - x.PaidAmount) })
            .ToDictionaryAsync(x => x.PartyId, x => x.Overdue, ct);

        var parties = grouped
            .Select(g => new
            {
                g.PartyId,
                g.Count,
                g.Total,
                g.Paid,
                Remaining = g.Total - g.Paid,
                Overdue = overdueByParty.TryGetValue(g.PartyId, out var o) ? o : 0m
            })
            .Where(p => includeSettled || p.Remaining > 0.005m)
            .OrderByDescending(p => p.Remaining)
            .Take(limit)
            .ToList();

        if (parties.Count == 0)
            return Message(includeSettled
                ? $"مفيش {partyLabel.ToLowerInvariant()} مطابق."
                : $"مفيش مديونيات — كل الحسابات مسددة. الحمد لله.");

        var names = await PartyNamesAsync(db, parties.Select(p => p.PartyId), ct);

        var headers = new[]
        {
            partyLabel, "عدد الفواتير", "الإجمالي", "المدفوع", "المتبقي", "المتأخر من المتبقي"
        };

        var rows = parties.Select(p => (IReadOnlyList<string>)new[]
        {
            NameOf(p.PartyId, names),
            p.Count.ToString(Culture),
            Money(p.Total),
            Money(p.Paid),
            Money(p.Remaining),
            p.Overdue > 0 ? Money(p.Overdue) : "—"
        }).ToList();

        var totalRemaining = grouped.Sum(g => g.Total - g.Paid);
        var totalOverdue = overdueByParty.Values.Sum();
        var debtorsCount = grouped.Count(g => g.Total - g.Paid > 0.005m);

        var json = ToJson(new Dictionary<string, object?>
        {
            ["العنوان"] = title,
            ["عدد الأطراف اللي عليها متبقي"] = debtorsCount,
            ["إجمالي المتبقي"] = Money(totalRemaining),
            ["إجمالي المتأخر"] = Money(totalOverdue),
            ["معروض"] = includeSettled ? "كل الحسابات" : "اللي عليها متبقي فقط",
            [title] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, new ZakiTable(title, headers, rows));
    }

    private static AiToolDefinition CustomerBalancesDefinition() => new(
        GetCustomerBalances,
        "مديونيات العملاء: كل عميل وإجمالي فواتيره والمدفوع والمتبقي عليه والمتأخر من المتبقي. للرد على: العميل فلان عليه كام؟ مين أكبر المدينين؟ إجمالي المديونيات كام؟",
        new
        {
            type = "object",
            properties = new
            {
                customer_name = new { type = "string", description = "بحث باسم العميل (اختياري)" },
                include_settled = new { type = "boolean", description = "true لعرض المسددين كمان (الافتراضي false — المدينين فقط)" },
                limit = new { type = "integer", description = "عدد الصفوف (1-50، الافتراضي 20)" }
            }
        });

    private static AiToolDefinition SupplierBalancesDefinition() => new(
        GetSupplierBalances,
        "أرصدة الموردين: إجمالي فواتير الشراء والمدفوع والمتبقي مستحق علينا لكل مورد.",
        new
        {
            type = "object",
            properties = new
            {
                supplier_name = new { type = "string", description = "بحث باسم المورد (اختياري)" },
                include_settled = new { type = "boolean", description = "true لعرض المسدد بالكامل كمان" },
                limit = new { type = "integer", description = "عدد الصفوف (1-50، الافتراضي 20)" }
            }
        });
}
