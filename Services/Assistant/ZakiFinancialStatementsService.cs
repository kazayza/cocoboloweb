using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using COCOBOLOERPNEW.Services;
using Microsoft.EntityFrameworkCore;
using static COCOBOLOERPNEW.Services.Assistant.ZakiInvoiceQueries;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// القوائم المالية في زكي: قائمة الدخل، والمركز المالي، ومقارنة الفروع.
/// - البيانات من نفس خدمة التقارير المالية (IFinancialReportsService) اللي بتستخدمها الشاشات.
/// - الصلاحية (زي شاشات التقارير): frmIncomeStatement:View أو rpt_IncomeStatement:View
///   أو frmCashBoxDashboard:View أو Admin.
/// - بنود الرواتب (الرواتب غير المدفوعة وإجمالي الالتزامات اللي بيشملها) بتظهر بس لصلاحيات المرتبات (زي شاشات المرتبات).
/// - الفرع: الاسم، أو "فرعي" لفرع المستخدم (الفرع الافتراضي، ولو فاضي فرع الموظف المربوط بالحساب).
///   الفرع تفضيل مش تقييد، زي باقي المشروع: كل الفروع متاحة لمن عنده صلاحية القوائم.
/// </summary>
public class ZakiFinancialStatementsService
{
    public const string GetIncomeStatement = "get_income_statement";
    public const string GetBalanceSheet = "get_balance_sheet";
    public const string GetBranchesComparison = "get_branches_comparison";

    private const int MaxBranchesCompared = 10;
    private const string MyBranchWord = "فرعي";

    private readonly IFinancialReportsService _reports;
    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly ILogger<ZakiFinancialStatementsService> _logger;

    public ZakiFinancialStatementsService(
        IFinancialReportsService reports,
        IDbContextFactory<db24804Context> dbFactory,
        ILogger<ZakiFinancialStatementsService> logger)
    {
        _reports = reports;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public static bool Handles(string toolName) =>
        toolName is GetIncomeStatement or GetBalanceSheet or GetBranchesComparison;

    /// <summary>صلاحية القوائم المالية (نفس شاشات التقارير)</summary>
    public static bool CanView(ClaimsPrincipal user) =>
        user.HasClaim("Permission", "frmIncomeStatement:View")
        || user.HasClaim("Permission", "rpt_IncomeStatement:View")
        || user.HasClaim("Permission", "frmCashBoxDashboard:View")
        || user.IsInRole("Admin");

    /// <summary>بنود الرواتب: نفس صلاحيات شاشات المرتبات</summary>
    public static bool CanViewPayroll(ClaimsPrincipal user) =>
        user.IsInRole("Admin")
        || user.HasClaim("Permission", "frm_Payroll:View")
        || user.HasClaim("Permission", "frmPayrollProcessor:View")
        || user.HasClaim("Permission", "frmPayrollReport:View")
        || user.HasClaim("Permission", "frm_PayrollPayment:View");

    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user) =>
        CanView(user)
            ? new[] { IncomeDefinition(), BalanceDefinition(), BranchesDefinition() }
            : Array.Empty<AiToolDefinition>();

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string name, string argumentsJson, CancellationToken ct = default)
    {
        if (!CanView(user))
            return Denied();

        var args = ParseArgs(argumentsJson);

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return name switch
            {
                GetIncomeStatement => await IncomeAsync(db, user, args, ct),
                GetBalanceSheet => await BalanceAsync(db, user, args, ct),
                GetBranchesComparison => await BranchesAsync(db, args, ct),
                _ => Message("أداة غير معروفة.")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki financial statements: query failed ({Tool})", name);
            return Message("ماقدرتش أجيب القوائم المالية دلوقتي، حاول تاني بعد شوية.");
        }
    }

    // ════════════════════════════════════════════════════════════
    // 1) قائمة الدخل
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> IncomeAsync(
        db24804Context db, ClaimsPrincipal user, JsonElement args, CancellationToken ct)
    {
        var (from, to, label) = ResolvePeriod(args);

        var (branchId, branchName, error) = await ResolveBranchAsync(db, user, ReadRawString(args, "branch_name"), ct);
        if (error != null) return error;

        var filter = BuildFilter(from, to, branchId);
        filter.IncludeComparison = true;   // نفس المقارنات اللي في الشاشة
        var dto = await _reports.GetIncomeStatementAsync(filter);

        // المصروفات التشغيلية زي الشاشة (إجمالي)، وبنود الرواتب المنفصلة مش بتظهر هنا
        var headers = new[] { "البند", "القيمة" };
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "إيرادات الفواتير", Money(dto.InvoiceRevenue) },
            new[] { "إيرادات أخرى (رسوم مكتسبة)", Money(dto.OtherRevenue) },
            new[] { "صافي الإيرادات", Money(dto.NetRevenue) },
            new[] { "عدد الفواتير", dto.InvoicesCount.ToString(Culture) },
            new[] { "متوسط قيمة الفاتورة", Money(dto.AverageInvoiceValue) },
            new[] { "تكلفة البضاعة المباعة", Money(dto.CostOfGoodsSold) },
            new[] { "مجمل الربح", Money(dto.GrossProfit) },
            new[] { "هامش مجمل الربح", Pct(dto.GrossProfitMargin) },
            new[] { "المصروفات التشغيلية", Money(dto.TotalOperatingExpenses) },
            new[] { "صافي الربح", Money(dto.NetProfit) },
            new[] { "هامش صافي الربح", Pct(dto.NetProfitMargin) }
        };

        if (dto.PreviousPeriod != null)
        {
            rows.Add(new[] { "صافي الربح — " + dto.PreviousPeriod.Label, Money(dto.PreviousPeriod.NetProfit) });
            rows.Add(new[] { "تغيير صافي الربح عن الفترة السابقة", Pct(dto.PreviousPeriod.NetProfitChange) });
        }

        if (dto.PreviousYear != null)
        {
            rows.Add(new[] { "صافي الربح — " + dto.PreviousYear.Label, Money(dto.PreviousYear.NetProfit) });
            rows.Add(new[] { "تغيير صافي الربح عن نفس الفترة السنة اللي فاتت", Pct(dto.PreviousYear.NetProfitChange) });
        }

        var expenseGroups = dto.ExpensesByGroup
            .OrderByDescending(e => e.Amount)
            .Select(e => new Dictionary<string, object?>
            {
                ["المجموعة"] = e.GroupName,
                ["المبلغ"] = Money(e.Amount)
            })
            .ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفترة"] = label,
            ["الفرع"] = branchName ?? "كل الفروع",
            ["قائمة الدخل"] = ToObjects(headers, rows),
            ["المصروفات حسب المجموعة"] = expenseGroups
        });

        var title = "قائمة الدخل — " + label + " — " + (branchName ?? "كل الفروع");
        return new ZakiToolOutcome(json, new ZakiTable(title, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 2) المركز المالي (في تاريخ اليوم)
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> BalanceAsync(
        db24804Context db, ClaimsPrincipal user, JsonElement args, CancellationToken ct)
    {
        var (branchId, branchName, error) = await ResolveBranchAsync(db, user, ReadRawString(args, "branch_name"), ct);
        if (error != null) return error;

        var dto = await _reports.GetBalanceSheetAsync(branchId);
        var showPayroll = CanViewPayroll(user);

        var headers = new[] { "البند", "القيمة" };
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "الأصول: رصيد الخزائن", Money(dto.CashBalance) },
            new[] { "الأصول: مدينون (متبقي فواتير البيع)", Money(dto.AccountsReceivable) },
            new[] { "الأصول: المخزون", Money(dto.InventoryValue) },
            new[] { "الأصول: سلف الموظفين القائمة", Money(dto.EmployeeLoansOutstanding) },
            new[] { "الأصول: مصروفات مدفوعة مقدماً", Money(dto.PrepaidExpensesAsset) },
            new[] { "إجمالي الأصول", Money(dto.TotalAssets) },
            new[] { "الالتزامات: دائنون (متبقي فواتير الشراء)", Money(dto.SupplierPayables) },
            new[] { "الالتزامات: دفعات عملاء مقدمة", Money(dto.CustomerAdvances) }
        };

        if (showPayroll)
            rows.Add(new[] { "الالتزامات: رواتب غير مدفوعة", Money(dto.PayrollOutstanding) });

        // إجمالي الالتزامات: من غير الرواتب لو المستخدم مش شايف بنود المرتبات (ومكتوب عليه ده صراحة)
        var liabilities = showPayroll ? dto.TotalLiabilities : dto.TotalLiabilities - dto.PayrollOutstanding;
        rows.Add(new[] { showPayroll ? "إجمالي الالتزامات" : "إجمالي الالتزامات (بدون بنود الرواتب)", Money(liabilities) });

        rows.Add(new[] { "رأس المال", Money(dto.OpeningCapital) });
        rows.Add(new[] { "أرباح محتجزة", Money(dto.RetainedEarnings) });
        rows.Add(new[] { "إجمالي حقوق الملكية", Money(dto.TotalEquity) });

        if (showPayroll)
            rows.Add(new[] { "الالتزامات + حقوق الملكية", Money(dto.LiabilitiesAndEquity) });

        var json = ToJson(new Dictionary<string, object?>
        {
            ["في تاريخ"] = Date(dto.AsOfDate),
            ["الفرع"] = branchName ?? "كل الفروع",
            ["المركز المالي"] = ToObjects(headers, rows),
            ["ملاحظة"] = showPayroll
                ? null
                : "بنود الرواتب مش متاحة لحسابك، فالإجمالي بيتعرض من غيرها."
        });

        var title = "المركز المالي — " + (branchName ?? "كل الفروع") + " — في " + Date(dto.AsOfDate);
        return new ZakiToolOutcome(json, new ZakiTable(title, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 3) مقارنة الفروع (إيرادات ومصاريف وربح لكل فرع)
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> BranchesAsync(db24804Context db, JsonElement args, CancellationToken ct)
    {
        var (from, to, label) = ResolvePeriod(args);

        var branches = await db.Branches
            .AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.BranchNameAr)
            .Take(MaxBranchesCompared)
            .Select(b => new BranchLite(b.BranchId, b.BranchNameAr))
            .ToListAsync(ct);

        if (branches.Count == 0)
            return Message("مفيش فروع نشطة في النظام.");

        var results = new List<(string Name, IncomeStatementDto Dto)>();
        foreach (var b in branches)
        {
            var dto = await _reports.GetIncomeStatementAsync(BuildFilter(from, to, b.Id));
            results.Add((b.Name, dto));
        }

        var headers = new[]
        {
            "الفرع", "صافي الإيرادات", "عدد الفواتير", "المصروفات التشغيلية", "مجمل الربح", "صافي الربح", "هامش صافي الربح"
        };

        var rows = results
            .OrderByDescending(r => r.Dto.NetRevenue)
            .Select(r => (IReadOnlyList<string>)new[]
            {
                r.Name,
                Money(r.Dto.NetRevenue),
                r.Dto.InvoicesCount.ToString(Culture),
                Money(r.Dto.TotalOperatingExpenses),
                Money(r.Dto.GrossProfit),
                Money(r.Dto.NetProfit),
                Pct(r.Dto.NetProfitMargin)
            }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفترة"] = label,
            ["عدد الفروع"] = rows.Count,
            ["مقارنة الفروع"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json,
            new ZakiTable("مقارنة الفروع — " + label, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // الفترات والفروع
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// الفترة: من/إلى بتواريخ (yyyy-MM-dd) لو موجودة، وغير كده period:
    /// today / week / month / last_month / quarter / year / all. الافتراضي الشهر ده.
    /// </summary>
    private static (DateTime From, DateTime To, string Label) ResolvePeriod(JsonElement args)
    {
        var today = DateTime.Today;

        var fromText = ReadRawString(args, "from");
        var toText = ReadRawString(args, "to");
        if (DateTime.TryParse(fromText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var customFrom)
            && DateTime.TryParse(toText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var customTo))
        {
            return (customFrom.Date, customTo.Date,
                "من " + customFrom.ToString("yyyy-MM-dd", Culture) + " إلى " + customTo.ToString("yyyy-MM-dd", Culture));
        }

        var monthStart = new DateTime(today.Year, today.Month, 1);
        var quarterStart = new DateTime(today.Year, ((today.Month - 1) / 3) * 3 + 1, 1);

        return ReadString(args, "period", "month") switch
        {
            "today" => (today, today, "اليوم"),
            "week" => (today.AddDays(-6), today, "آخر 7 أيام"),
            "last_month" => (monthStart.AddMonths(-1), monthStart.AddDays(-1), "الشهر اللي فات"),
            "quarter" => (quarterStart, today, "الربع ده"),
            "year" => (new DateTime(today.Year, 1, 1), today, "السنة دي"),
            "all" => (new DateTime(2000, 1, 1), today, "كل الفترات"),
            _ => (monthStart, today, "الشهر ده")
        };
    }

    private static IncomeStatementFilterDto BuildFilter(DateTime from, DateTime to, int? branchId) => new()
    {
        FromDate = from,
        ToDate = to,
        PeriodType = IncomePeriodTypes.Custom,
        IncludeComparison = false,
        IncludeMonthlyTrend = false,
        BranchId = branchId
    };

    /// <summary>
    /// الفرع: "فرعي" = فرع المستخدم (الافتراضي، ولو فاضي فرع الموظف المربوط بالحساب)،
    /// أو اسم فرع (بيتجاهل الهمزات). فاضي = كل الفروع.
    /// </summary>
    private static async Task<(int? Id, string? Name, ZakiToolOutcome? Error)> ResolveBranchAsync(
        db24804Context db, ClaimsPrincipal user, string search, CancellationToken ct)
    {
        if (search.Length == 0)
            return (null, null, null);

        if (search.NormalizeArabic() == MyBranchWord.NormalizeArabic())
        {
            var myId = await MyBranchIdAsync(db, user, ct);
            if (!myId.HasValue)
                return (null, null, Message("حسابك مش مربوط بفرع. اكتب اسم الفرع اللي عايزه."));

            var myName = await db.Branches.AsNoTracking()
                .Where(b => b.BranchId == myId.Value)
                .Select(b => b.BranchNameAr)
                .FirstOrDefaultAsync(ct);

            return (myId, myName ?? "فرعي", null);
        }

        var branches = await db.Branches
            .AsNoTracking()
            .Select(b => new BranchLite(b.BranchId, b.BranchNameAr))
            .ToListAsync(ct);

        var matched = branches.Where(b => (b.Name ?? "").ContainsArabic(search)).ToList();

        if (matched.Count == 0)
            return (null, null, Message("مالقيتش فرع بالاسم ده."));

        if (matched.Count > 1)
        {
            return (null, null, new ZakiToolOutcome(ToJson(new Dictionary<string, object?>
            {
                ["ملاحظة"] = "في أكتر من فرع بالاسم ده. اطلب من المستخدم يكتب اسم الفرع بالكامل.",
                ["الفروع المطابقة"] = matched.Select(m => m.Name).ToList()
            }), null));
        }

        return (matched[0].Id, matched[0].Name, null);
    }

    /// <summary>فرع المستخدم: الفرع الافتراضي في الحساب، ولو فاضي فرع الموظف المربوط بيه</summary>
    private static async Task<int?> MyBranchIdAsync(db24804Context db, ClaimsPrincipal user, CancellationToken ct)
    {
        var username = user.Identity?.Name ?? "";
        if (username.Length == 0) return null;

        var account = await db.Users
            .AsNoTracking()
            .Where(u => u.Username == username)
            .Select(u => new { u.DefaultBranchId, u.EmployeeId })
            .FirstOrDefaultAsync(ct);

        if (account == null) return null;
        if (account.DefaultBranchId.HasValue) return account.DefaultBranchId;
        if (!account.EmployeeId.HasValue) return null;

        return await db.Employees
            .AsNoTracking()
            .Where(e => e.EmployeeId == account.EmployeeId.Value)
            .Select(e => e.BranchId)
            .FirstOrDefaultAsync(ct);
    }

    private static string Pct(decimal value) => value.ToString("0.##", Culture) + "%";

    private sealed record BranchLite(int Id, string Name);

    // ════════════════════════════════════════════════════════════
    // تعريفات الأدوات
    // ════════════════════════════════════════════════════════════
    private static object PeriodSchema(string description) => new
    {
        type = "string",
        description,
        @enum = new[] { "today", "week", "month", "last_month", "quarter", "year", "all" }
    };

    private static AiToolDefinition IncomeDefinition() => new(
        GetIncomeStatement,
        "قائمة الدخل لفترة: الإيرادات والمصروفات وتكلفة البضاعة وصافي الربح وهوامشه، وتفصيل المصروفات حسب المجموعة. الفرع: اسمه، أو «فرعي» لفرع المستخدم. الافتراضي month.",
        new
        {
            type = "object",
            properties = new
            {
                period = PeriodSchema("الافتراضي month"),
                from = new { type = "string", description = "تاريخ البداية yyyy-MM-dd (اختياري، لو مع to)" },
                to = new { type = "string", description = "تاريخ النهاية yyyy-MM-dd (اختياري)" },
                branch_name = new { type = "string", description = "اسم الفرع، أو «فرعي» (اختياري)" }
            }
        });

    private static AiToolDefinition BalanceDefinition() => new(
        GetBalanceSheet,
        "المركز المالي في تاريخ اليوم: الأصول والالتزامات وحقوق الملكية. الفرع: اسمه، أو «فرعي» لفرع المستخدم.",
        new
        {
            type = "object",
            properties = new
            {
                branch_name = new { type = "string", description = "اسم الفرع، أو «فرعي» (اختياري)" }
            }
        });

    private static AiToolDefinition BranchesDefinition() => new(
        GetBranchesComparison,
        "مقارنة الفروع لفترة: صافي الإيرادات وعدد الفواتير والمصروفات ومجمل الربح وصافي الربح لكل فرع. الافتراضي month.",
        new
        {
            type = "object",
            properties = new
            {
                period = PeriodSchema("الافتراضي month"),
                from = new { type = "string", description = "تاريخ البداية yyyy-MM-dd (اختياري، لو مع to)" },
                to = new { type = "string", description = "تاريخ النهاية yyyy-MM-dd (اختياري)" }
            }
        });
}
