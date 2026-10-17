using System.Security.Claims;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;
using static COCOBOLOERPNEW.Services.Assistant.ZakiInvoiceQueries;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// أدوات مالية إضافية في زكي — إعادة استخدام خدمات المشروع عشان الأرقام تطابق الشاشات:
/// - get_expenses_summary: مصروفات فترة بالمجموعات (جدول Expenses مباشرة).
/// - get_cashflow_summary: بيان التدفق النقدي (ICashFlowService).
/// - get_personal_accounts: الذمم/الحسابات الشخصية وأرصدتها (IPersonalAccountService).
/// الصلاحيات نفس بنود السايدبار: frm_ExpensesList / frm_CashFlowNew / frmPersonalAccounts.
/// </summary>
public class ZakiFinanceExtraService
{
    public const string GetExpensesSummary = "get_expenses_summary";
    public const string GetCashflowSummary = "get_cashflow_summary";
    public const string GetPersonalAccounts = "get_personal_accounts";

    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly ICashFlowService _cashflow;
    private readonly IPersonalAccountService _personal;
    private readonly ILogger<ZakiFinanceExtraService> _logger;

    public ZakiFinanceExtraService(
        IDbContextFactory<db24804Context> dbFactory,
        ICashFlowService cashflow,
        IPersonalAccountService personal,
        ILogger<ZakiFinanceExtraService> logger)
    {
        _dbFactory = dbFactory;
        _cashflow = cashflow;
        _personal = personal;
        _logger = logger;
    }

    public static bool Handles(string toolName) =>
        toolName is GetExpensesSummary or GetCashflowSummary or GetPersonalAccounts;

    public static bool CanViewExpenses(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("Permission", "frm_ExpensesList:View");

    public static bool CanViewCashflow(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("Permission", "frm_CashFlowNew:View");

    public static bool CanViewPersonal(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("Permission", "frmPersonalAccounts:View");

    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user)
    {
        var list = new List<AiToolDefinition>();
        if (CanViewExpenses(user)) list.Add(ExpensesDefinition());
        if (CanViewCashflow(user)) list.Add(CashflowDefinition());
        if (CanViewPersonal(user)) list.Add(PersonalDefinition());
        return list;
    }

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string name, string argumentsJson, CancellationToken ct = default)
    {
        var args = ParseArgs(argumentsJson);

        try
        {
            if (name == GetExpensesSummary)
                return CanViewExpenses(user) ? await ExpensesAsync(user, args, ct) : Denied();
            if (name == GetCashflowSummary)
                return CanViewCashflow(user) ? await CashflowAsync(user, args, ct) : Denied();
            return CanViewPersonal(user) ? await PersonalAsync(args, ct) : Denied();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki finance-extra: query failed ({Tool})", name);
            return Message("ماقدرتش أجيب البيانات دلوقتي، حاول تاني بعد شوية.");
        }
    }

    // ── المصروفات بالمجموعات ──
    private async Task<ZakiToolOutcome> ExpensesAsync(
        ClaimsPrincipal user, System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var (start, label) = PeriodRange(ReadString(args, "period", "month"), DateTime.Today);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // فلتر الفرع اختياري: Expense.BranchId مباشرة (فاضي = كل الفروع)
        var q = db.Expenses.AsNoTracking().Where(e => start == null || e.ExpenseDate >= start);

        var branchSearch = ReadRawString(args, "branch_name");
        if (branchSearch.Length > 0)
        {
            var (branchId, branchName, branchError) = await ResolveBranchAsync(db, user, branchSearch, ct);
            if (branchError != null)
                return branchError;

            q = q.Where(e => e.BranchId == branchId);
            label = label + " — " + branchName;
        }

        var groups = await q
            .GroupBy(e => e.ExpenseGroup.ExpenseGroupName)
            .Select(g => new { Group = g.Key, Cnt = g.Count(), Total = g.Sum(x => x.Amount) })
            .OrderByDescending(g => g.Total)
            .ToListAsync(ct);

        if (groups.Count == 0)
            return Message($"مفيش مصروفات في الفترة دي ({label}).");

        var headers = new[] { "المجموعة", "عدد المصروفات", "الإجمالي" };
        var rows = groups.Select(g => (IReadOnlyList<string>)new[]
        {
            g.Group,
            g.Cnt.ToString(Culture),
            Money(g.Total)
        }).ToList();

        var grand = groups.Sum(g => g.Total);

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفترة"] = label,
            ["إجمالي المصروفات"] = Money(grand),
            ["عدد المصروفات"] = groups.Sum(g => g.Cnt),
            ["المصروفات بالمجموعات"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, new ZakiTable("المصروفات — " + label, headers, rows));
    }

    // ── التدفق النقدي (نفس خدمة صفحة /financial/cash-flow وبنفس الأرقام) ──
    private async Task<ZakiToolOutcome> CashflowAsync(
        ClaimsPrincipal user, System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var period = ReadString(args, "period", "month");
        var (start, _) = PeriodRange(period, DateTime.Today);

        // فلتر الفرع اختياري: الحركات بتتنسب للفرع عن طريق فرع الخزينة (CashFlowFilterDto.BranchId)
        int? branchId = null;
        string? branchName = null;
        var branchSearch = ReadRawString(args, "branch_name");
        if (branchSearch.Length > 0)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var (id, name, branchError) = await ResolveBranchAsync(db, user, branchSearch, ct);
            if (branchError != null)
                return branchError;

            branchId = id;
            branchName = name;
        }

        var statement = await _cashflow.GetCashFlowStatementAsync(new CashFlowFilterDto
        {
            FromDate = start ?? new DateTime(2000, 1, 1),
            ToDate = DateTime.Today,
            IncludeForecast = false,
            IncludeMonthlyTrend = false,
            BranchId = branchId
        });

        // مقارنة بنفس المدة من الفترة اللي قبلها
        var prevPeriod = PreviousPeriodRange(period, DateTime.Today);
        CashFlowStatementDto? prevStatement = null;
        if (prevPeriod is { } pp)
        {
            prevStatement = await _cashflow.GetCashFlowStatementAsync(new CashFlowFilterDto
            {
                FromDate = pp.Start,
                ToDate = pp.End.AddDays(-1),
                IncludeForecast = false,
                IncludeMonthlyTrend = false,
                BranchId = branchId
            });
        }

        var periodLabel = branchName == null
            ? statement.PeriodLabel
            : statement.PeriodLabel + " — " + branchName;

        var headers = new[] { "البند", "القيمة" };
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "رصيد بداية الفترة", Money(statement.OpeningBalance) },
            new[] { "إجمالي التدفقات الداخلة", Money(statement.TotalInflows) },
            new[] { "إجمالي التدفقات الخارجة", Money(statement.TotalOutflows) },
            new[] { "صافي التدفق النقدي", Money(statement.NetCashFlow) },
            new[] { "رصيد نهاية الفترة", Money(statement.ClosingBalance) }
        };

        if (prevPeriod is { } pp2 && prevStatement != null)
        {
            rows.Add(new[] { "صافي التدفق — " + pp2.Label, Money(prevStatement.NetCashFlow) });
            rows.Add(new[] { "فرق صافي التدفق عن الفترة السابقة", Money(statement.NetCashFlow - prevStatement.NetCashFlow) });
        }

        // أكبر 5 بنود داخلة وخارجة (زي الشاشة) عشان الرد يشرح الأرقام
        var inflows = statement.Inflows.OrderByDescending(c => c.Amount).Take(5).ToList();
        var outflows = statement.Outflows.OrderByDescending(c => c.Amount).Take(5).ToList();

        foreach (var c in inflows)
            rows.Add(new[] { "داخل — " + c.CategoryName, Money(c.Amount) });
        foreach (var c in outflows)
            rows.Add(new[] { "خارج — " + c.CategoryName, Money(c.Amount) });

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفترة"] = periodLabel,
            ["صافي التدفق"] = Money(statement.NetCashFlow),
            ["بيان التدفق النقدي"] = ToObjects(headers, rows),
            ["أكبر البنود الداخلة"] = inflows.Select(c => new Dictionary<string, object?>
            {
                ["البند"] = c.CategoryName,
                ["المبلغ"] = Money(c.Amount),
                ["النسبة"] = c.Percentage.ToString("0.##", Culture) + "%"
            }).ToList(),
            ["أكبر البنود الخارجة"] = outflows.Select(c => new Dictionary<string, object?>
            {
                ["البند"] = c.CategoryName,
                ["المبلغ"] = Money(c.Amount),
                ["النسبة"] = c.Percentage.ToString("0.##", Culture) + "%"
            }).ToList(),
            ["حسب الخزينة"] = statement.ByCashBox
                .Select(b => new Dictionary<string, object?>
                {
                    ["الخزينة"] = b.CashBoxName,
                    ["داخل"] = Money(b.Inflows),
                    ["خارج"] = Money(b.Outflows)
                }).ToList()
        });

        return new ZakiToolOutcome(json, new ZakiTable("التدفق النقدي — " + periodLabel, headers, rows));
    }

    // ── الذمم / الحسابات الشخصية ──
    private async Task<ZakiToolOutcome> PersonalAsync(System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var search = ReadRawString(args, "account_name");
        var limit = ReadInt(args, "limit", 20, 1, 50);

        var page = await _personal.GetAccountsAsync(new PersonalAccountFilterDto
        {
            SearchText = search.Length > 0 ? search : null,
            PageSize = limit
        });

        if (page.Items.Count == 0)
            return Message(search.Length > 0 ? "مالقتش حساب شخصي بالاسم ده." : "مفيش حسابات شخصية (ذمم).");

        var headers = new[] { "الحساب", "النوع", "الرصيد", "الحالة", "حركات" };
        var rows = page.Items.Select(a => (IReadOnlyList<string>)new[]
        {
            a.AccountName,
            a.AccountTypeAr,
            Money(a.CurrentBalance),
            a.BalanceStatus,
            a.TransactionsCount.ToString(Culture)
        }).ToList();

        var total = page.Items.Sum(a => a.CurrentBalance);

        var json = ToJson(new Dictionary<string, object?>
        {
            ["عدد الحسابات المعروضة"] = page.Items.Count,
            ["إجمالي عدد الحسابات"] = page.TotalCount,
            ["صافي أرصدة المعروض"] = Money(total),
            ["الذمم (حسابات شخصية)"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, new ZakiTable("الذمم — الحسابات الشخصية", headers, rows));
    }

    private static AiToolDefinition ExpensesDefinition() => new(
        GetExpensesSummary,
        "ملخص المصروفات لفترة: إجمالي وعدد ومقسمة بالمجموعات. ممكن تحدد فرع.",
        new
        {
            type = "object",
            properties = new
            {
                period = PeriodSchema("الافتراضي month"),
                branch_name = new { type = "string", description = "اسم الفرع (اختياري). فاضي = كل الفروع. «فرعي» = فرع المستخدم." }
            }
        });

    private static AiToolDefinition CashflowDefinition() => new(
        GetCashflowSummary,
        "بيان التدفق النقدي لفترة: داخلي وخارجي وصافي وأرصدة بداية ونهاية، وأكبر البنود والخزائن. ممكن تحدد فرع.",
        new
        {
            type = "object",
            properties = new
            {
                period = PeriodSchema("الافتراضي month"),
                branch_name = new { type = "string", description = "اسم الفرع (اختياري). فاضي = كل الفروع. «فرعي» = فرع المستخدم." }
            }
        });

    private static AiToolDefinition PersonalDefinition() => new(
        GetPersonalAccounts,
        "الذمم/الحسابات الشخصية: الأسماء والأنواع والأرصدة (له على الشركة/عليه للشركة). ممكن بحث بالاسم.",
        new
        {
            type = "object",
            properties = new
            {
                account_name = new { type = "string", description = "بحث باسم الحساب (اختياري)" },
                limit = new { type = "integer", description = "عدد الصفوف (1-50، الافتراضي 20)" }
            }
        });

    private static object PeriodSchema(string description) => new
    {
        type = "string",
        description,
        @enum = new[] { "today", "week", "month", "year", "all" }
    };
}
