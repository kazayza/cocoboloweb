using System.Security.Claims;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static COCOBOLOERPNEW.Services.Assistant.ZakiInvoiceQueries;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// أدوات الخزينة في زكي — إعادة استخدام ICashBoxService الموجود
/// عشان الأرقام تفضل مطابقة لشاشات الخزينة بالظبط.
/// الصلاحيات: نفس بنود السايدبار:
/// - الملخص: frmCashBoxDashboard:View أو frmCashBox:View.
/// - الحركات: frmCashBoxTransactions:View أو frm_CashBoxTransaction:View.
/// </summary>
public class ZakiCashboxService
{
    public const string GetCashboxSummary = "get_cashbox_summary";
    public const string GetCashboxTransactions = "get_cashbox_transactions";

    private readonly ICashBoxService _cashbox;
    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly ILogger<ZakiCashboxService> _logger;

    public ZakiCashboxService(
        ICashBoxService cashbox,
        IDbContextFactory<db24804Context> dbFactory,
        ILogger<ZakiCashboxService> logger)
    {
        _cashbox = cashbox;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public static bool Handles(string toolName) =>
        toolName is GetCashboxSummary or GetCashboxTransactions;

    public static bool CanViewSummary(ClaimsPrincipal user) =>
        user.IsInRole("Admin")
        || user.HasClaim("Permission", "frmCashBoxDashboard:View")
        || user.HasClaim("Permission", "frmCashBox:View");

    public static bool CanViewTransactions(ClaimsPrincipal user) =>
        user.IsInRole("Admin")
        || user.HasClaim("Permission", "frmCashBoxTransactions:View")
        || user.HasClaim("Permission", "frm_CashBoxTransaction:View");

    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user)
    {
        var list = new List<AiToolDefinition>();
        if (CanViewSummary(user)) list.Add(SummaryDefinition());
        if (CanViewTransactions(user)) list.Add(TransactionsDefinition());
        return list;
    }

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string name, string argumentsJson, CancellationToken ct = default)
    {
        var allowed = name == GetCashboxSummary ? CanViewSummary(user) : CanViewTransactions(user);
        if (!allowed) return Denied();

        var args = ParseArgs(argumentsJson);

        try
        {
            return name == GetCashboxSummary
                ? await SummaryAsync(user, args, ct)
                : await TransactionsAsync(user, args, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki cashbox: query failed ({Tool})", name);
            return Message("ماقدرتش أجيب بيانات الخزينة دلوقتي، حاول تاني بعد شوية.");
        }
    }

    // ── ملخص الخزن: أرصدة + وارد/صادر ──
    private async Task<ZakiToolOutcome> SummaryAsync(
        ClaimsPrincipal user, System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var (start, label) = PeriodRange(ReadString(args, "period", "all"), DateTime.Today);

        var boxes = await _cashbox.GetSummaryAsync(start, null);

        // فلتر الفرع اختياري: الخزينة مربوطة بالفرع (CashBox.BranchId)
        var branchSearch = ReadRawString(args, "branch_name");
        if (branchSearch.Length > 0)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var (branchId, branchName, branchError) = await ResolveBranchAsync(db, user, branchSearch, ct);
            if (branchError != null)
                return branchError;

            var branchBoxIds = await db.CashBoxes.AsNoTracking()
                .Where(c => c.BranchId == branchId)
                .Select(c => c.CashBoxId)
                .ToListAsync(ct);

            boxes = boxes.Where(b => branchBoxIds.Contains(b.CashBoxId)).ToList();
            label = label + " — " + branchName;

            if (boxes.Count == 0)
                return Message("مفيش خزن مربوطة بالفرع ده.");
        }

        if (boxes.Count == 0)
            return Message("مفيش خزن معرّفة في النظام.");

        var headers = new[] { "الخزينة", "الرصيد الافتتاحي", "وارد", "صادر", "الرصيد الحالي" };
        var rows = boxes
            .OrderByDescending(b => b.CurrentBalance)
            .Select(b => (IReadOnlyList<string>)new[]
            {
                b.CashBoxName,
                Money(b.OpeningBalance),
                Money(b.TotalIn),
                Money(b.TotalOut),
                Money(b.CurrentBalance)
            }).ToList();

        var totalBalance = boxes.Sum(b => b.CurrentBalance);
        var totalIn = boxes.Sum(b => b.TotalIn);
        var totalOut = boxes.Sum(b => b.TotalOut);

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفترة"] = label,
            ["عدد الخزن"] = boxes.Count,
            ["إجمالي الوارد"] = Money(totalIn),
            ["إجمالي الصادر"] = Money(totalOut),
            ["إجمالي الأرصدة"] = Money(totalBalance),
            ["ملخص الخزائن"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, new ZakiTable("ملخص الخزائن — " + label, headers, rows));
    }

    // ── حركات الخزينة (وارد/صادر) لفترة ──
    private async Task<ZakiToolOutcome> TransactionsAsync(
        ClaimsPrincipal user, System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var (start, label) = PeriodRange(ReadString(args, "period", "today"), DateTime.Today);
        var typeRaw = ReadString(args, "type", "all").ToLowerInvariant();
        var limit = ReadInt(args, "limit", 20, 1, 50);
        var typeFilter = typeRaw == "in" ? "In" : typeRaw == "out" ? "Out" : null;

        // فلتر الفرع اختياري: الخزينة مربوطة بالفرع (CashBox.BranchId).
        // الخدمة بتقرأ خزينة واحدة في المرة، فبنجيب حركات كل خزن الفرع ونجمعهم ونرتبهم.
        var branchSearch = ReadRawString(args, "branch_name");
        List<int>? branchBoxIds = null;
        if (branchSearch.Length > 0)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var (branchId, branchName, branchError) = await ResolveBranchAsync(db, user, branchSearch, ct);
            if (branchError != null)
                return branchError;

            branchBoxIds = await db.CashBoxes.AsNoTracking()
                .Where(c => c.BranchId == branchId)
                .Select(c => c.CashBoxId)
                .ToListAsync(ct);

            if (branchBoxIds.Count == 0)
                return Message("مفيش خزن مربوطة بالفرع ده.");

            label = label + " — " + branchName;
        }

        var filters = branchBoxIds == null
            ? new List<CashBoxTransactionFilterDto>
            {
                new() { DateFrom = start, TransactionType = typeFilter, PageNumber = 1, PageSize = limit }
            }
            : branchBoxIds.Select(id => new CashBoxTransactionFilterDto
            {
                CashBoxId = id,
                DateFrom = start,
                TransactionType = typeFilter,
                PageNumber = 1,
                PageSize = limit
            }).ToList();

        var items = new List<CashBoxTransactionDto>();
        decimal totalIn = 0m;
        decimal totalOut = 0m;
        var totalCount = 0;

        foreach (var filter in filters)
        {
            var page = await _cashbox.GetTransactionsAsync(filter);
            var (inSum, outSum) = await _cashbox.GetTransactionsTotalsAsync(filter);

            items.AddRange(page.Items);
            totalIn += inSum;
            totalOut += outSum;
            totalCount += page.TotalCount;
        }

        items = items.OrderByDescending(t => t.TransactionDate).Take(limit).ToList();

        if (items.Count == 0)
            return Message($"مفيش حركات خزينة في الفترة دي ({label}).");

        var headers = new[] { "التاريخ", "الخزينة", "النوع", "المبلغ", "البيان", "بواسطة" };
        var rows = items.Select(t => (IReadOnlyList<string>)new[]
        {
            Date(t.TransactionDate),
            t.CashBoxName,
            t.TransactionType == "In" ? "وارد" : "صادر",
            Money(t.Amount),
            string.IsNullOrWhiteSpace(t.Notes) ? "—" : t.Notes!,
            string.IsNullOrWhiteSpace(t.CreatedBy) ? "—" : t.CreatedBy!
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفترة"] = label,
            ["الفلتر"] = typeRaw switch { "in" => "وارد فقط", "out" => "صادر فقط", _ => "الكل" },
            ["إجمالي الوارد"] = Money(totalIn),
            ["إجمالي الصادر"] = Money(totalOut),
            ["صافي الفترة"] = Money(totalIn - totalOut),
            ["عدد الحركات المعروضة"] = items.Count,
            ["إجمالي عدد الحركات"] = totalCount,
            ["حركات الخزينة"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, new ZakiTable("حركات الخزينة — " + label, headers, rows));
    }

    private static AiToolDefinition SummaryDefinition() => new(
        GetCashboxSummary,
        "ملخص الخزائن: الرصيد الافتتاحي والوارد والصادر والرصيد الحالي لكل خزينة. للرد على: رصيد الخزينة كام؟ فلوس الصندوق كام؟",
        new
        {
            type = "object",
            properties = new
            {
                period = PeriodSchema("الافتراضي all — الوارد/صادر بيتحسب من بداية الفترة"),
                branch_name = new { type = "string", description = "اسم الفرع (اختياري). فاضي = كل الفروع. «فرعي» = فرع المستخدم." }
            }
        });

    private static AiToolDefinition TransactionsDefinition() => new(
        GetCashboxTransactions,
        "حركات الخزينة (وارد/صادر) لفترة. type: in للوارد، out للصادر، all للكل.",
        new
        {
            type = "object",
            properties = new
            {
                period = PeriodSchema("الافتراضي today"),
                type = new { type = "string", description = "in / out / all (الافتراضي all)", @enum = new[] { "in", "out", "all" } },
                limit = new { type = "integer", description = "عدد الصفوف (1-50، الافتراضي 20)" },
                branch_name = new { type = "string", description = "اسم الفرع (اختياري). فاضي = كل الفروع. «فرعي» = فرع المستخدم." }
            }
        });

    private static object PeriodSchema(string description) => new
    {
        type = "string",
        description,
        @enum = new[] { "today", "week", "month", "year", "all" }
    };
}
