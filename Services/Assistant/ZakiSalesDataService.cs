using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using COCOBOLOERPNEW.Models;
using COCOBOLOERPNEW.Services;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// بيانات البائع (Sales) في زكي.
/// قاعدة "بياناتي":
/// - الفرصة بتاعتي = EmployeeId هو أنا، أو EmployeeId فاضي و CreatedBy هو اسمي.
/// - التواصلات والمهام بتاعتي = المرتبطة بفرصة بتاعتي، أو المتعينة ليا (المهام AssignedTo).
/// - تاريخ الاطلاع (CrmAccessFrom) بيتطبق زي الشاشات.
/// - البائع مايشوفش بيانات موظفين تانيين.
/// </summary>
public class ZakiSalesDataService
{
    private static readonly string[] ClosedStageNames =
    {
        "تم البيع", "خسارة", "غير مهتم", "Closed Deal", "Lost", "Not Interested"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly ILogger<ZakiSalesDataService> _logger;

    public ZakiSalesDataService(IDbContextFactory<db24804Context> dbFactory, ILogger<ZakiSalesDataService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>الأدوات اللي البائع بيتعامل معاها من هنا</summary>
    public static bool Handles(string toolName) =>
        toolName is ZakiToolExecutor.GetOpenOpportunities
            or ZakiToolExecutor.GetOpenTasks
            or ZakiToolExecutor.GetOverdueTasks
            or ZakiToolExecutor.GetRecentInteractions;

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string name, string argumentsJson, ZakiUserInfo info, CancellationToken ct = default)
    {
        if (!info.EmployeeId.HasValue)
        {
            return Message("حسابك مش مربوط بموظف في النظام، فمش هقدر أعرض بياناتك. كلم الإدارة عشان يتربط حسابك.");
        }

        if (!IsAllowed(user, name))
        {
            return new ZakiToolOutcome(
                ToJson(new Dictionary<string, object?> { ["خطأ"] = "ليس لديك صلاحية لهذه البيانات." }),
                null,
                IsDenied: true);
        }

        var emp = info.EmployeeId.Value;
        var username = user.Identity?.Name ?? "";
        var crmFrom = user.GetAccessFromDate();
        var args = ParseArgs(argumentsJson);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var myOppIds = await MyOpportunityIdsAsync(db, emp, username, crmFrom, ct);

        var outcome = name switch
        {
            ZakiToolExecutor.GetOpenOpportunities => await OpenOpportunitiesAsync(db, myOppIds, args, ct),
            ZakiToolExecutor.GetOpenTasks => await TasksAsync(db, myOppIds, emp, crmFrom, args, overdueOnly: false, ct),
            ZakiToolExecutor.GetOverdueTasks => await TasksAsync(db, myOppIds, emp, crmFrom, args, overdueOnly: true, ct),
            ZakiToolExecutor.GetRecentInteractions => await InteractionsAsync(db, myOppIds, emp, crmFrom, args, ct),
            _ => Message("أداة غير معروفة.")
        };

        // تشخيص مؤقت: لو النتيجة فاضية، بنعرض جدول يوضح السبب تحت الرد
        if (outcome.Table == null)
        {
            var diag = await DiagnosticAsync(db, emp, username, info.DisplayName, crmFrom, ct);
            return new ZakiToolOutcome(outcome.Json, diag);
        }

        return outcome;

    }

    // ════════════════════════════════════════════════════════════
    // الصلاحيات (نفس صلاحيات الشاشات)
    // ════════════════════════════════════════════════════════════
    private static bool IsAllowed(ClaimsPrincipal user, string name) => name switch
    {
        ZakiToolExecutor.GetOpenOpportunities => user.HasClaim("Permission", "frmSalesPipeline:View")
                                                 || user.HasClaim("Permission", "frmOpportunities:View"),
        ZakiToolExecutor.GetOpenTasks or ZakiToolExecutor.GetOverdueTasks => user.HasClaim("Permission", "frmDailyTasks:View")
                                                 || user.HasClaim("Permission", "frmCrmTasks:View"),
        ZakiToolExecutor.GetRecentInteractions => user.HasClaim("Permission", "frmInteractionsLog:View")
                                                  || user.HasClaim("Permission", "frmNewInteraction:View")
                                                  || user.HasClaim("Permission", "frmInteractions:View"),
        _ => false
    };

    // ════════════════════════════════════════════════════════════
    // الفرص: أنا (EmployeeId = أنا، أو فاضي و CreatedBy = أنا)
    // ════════════════════════════════════════════════════════════
    private static async Task<List<int>> MyOpportunityIdsAsync(
        db24804Context db, int emp, string username, DateTime? crmFrom, CancellationToken ct)
    {
        var q = db.SalesOpportunities.AsNoTracking()
            .Where(o => o.IsActive
                        && (o.EmployeeId == emp || (o.EmployeeId == null && o.CreatedBy == username)));

        if (crmFrom.HasValue)
            q = q.Where(o => o.CreatedAt >= crmFrom.Value);

        return await q.Select(o => o.OpportunityId).ToListAsync(ct);
    }

    private static async Task<ZakiToolOutcome> OpenOpportunitiesAsync(
        db24804Context db, List<int> myIds, JsonElement args, CancellationToken ct)
    {
        var limit = ReadInt(args, "limit", 20, 1, 20);

        var all = await db.VwSalesOpportunities.AsNoTracking()
            .Where(o => o.IsActive && myIds.Contains(o.OpportunityId))
            .OrderByDescending(o => o.CreatedAt)
            .Take(300)
            .ToListAsync(ct);

        var open = all.Where(o => !IsClosed(o.StageNameAr, o.StageName)).ToList();
        var shown = open.Take(limit).ToList();

        var headers = new[]
        {
            "رقم الفرصة", "العميل", "المرحلة", "القيمة المتوقعة",
            "المسؤول", "تاريخ الإنشاء", "آخر تواصل", "المتابعة القادمة"
        };

        var rows = shown.Select(o => (IReadOnlyList<string>)new[]
        {
            o.OpportunityId.ToString(Culture),
            o.ClientName ?? "—",
            o.StageNameAr ?? o.StageName ?? "—",
            Money(o.ExpectedValue),
            o.EmployeeName ?? "—",
            Date(o.CreatedAt),
            Date(o.LastContactDate),
            Date(o.NextFollowUpDate)
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["عدد الفرص المعروضة"] = rows.Count,
            ["إجمالي الفرص المفتوحة بتاعتك"] = open.Count,
            ["الفرص"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, rows.Count == 0 ? null : new ZakiTable("الفرص المفتوحة", headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // المهام: المرتبطة بفرصي، أو المتعينة لي
    // ════════════════════════════════════════════════════════════
    private static async Task<ZakiToolOutcome> TasksAsync(
        db24804Context db, List<int> myIds, int emp, DateTime? crmFrom,
        JsonElement args, bool overdueOnly, CancellationToken ct)
    {
        var limit = ReadInt(args, "limit", 20, 1, 20);
        var today = DateTime.Today;

        var q = db.VwCrmTasks.AsNoTracking()
            .Where(t => t.Status != "Completed" && t.Status != "Cancelled")
            .Where(t => t.AssignedTo == emp
                        || (t.OpportunityId != null && myIds.Contains(t.OpportunityId.Value)));

        if (crmFrom.HasValue)
            q = q.Where(t => t.CreatedAt >= crmFrom.Value);

        if (overdueOnly)
            q = q.Where(t => t.DueDate < today);

        var total = await q.CountAsync(ct);

        var list = await q.OrderBy(t => t.DueDate)
            .Take(limit)
            .ToListAsync(ct);

        var headers = new[]
        {
            "رقم المهمة", "العميل", "نوع المهمة", "موعد التنفيذ",
            "الحالة", "الأولوية", "المسؤول"
        };

        var rows = list.Select(t => (IReadOnlyList<string>)new[]
        {
            t.TaskId.ToString(Culture),
            t.ClientName ?? "—",
            t.TaskTypeNameAr ?? t.TaskTypeName ?? "—",
            Date(t.DueDate),
            TaskStatusAr(t.Status),
            PriorityAr(t.Priority),
            t.AssignedToName ?? "—"
        }).ToList();

        var title = overdueOnly ? "المهام المتأخرة" : "المهام المفتوحة";

        var json = ToJson(new Dictionary<string, object?>
        {
            ["نوع القائمة"] = title,
            ["عدد المهام المعروضة"] = rows.Count,
            ["إجمالي المهام في نطاقك"] = total,
            ["المهام"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, rows.Count == 0 ? null : new ZakiTable(title, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // التواصلات: المرتبطة بفرصي، أو اللي أنا عملتها
    // ════════════════════════════════════════════════════════════
    private static async Task<ZakiToolOutcome> InteractionsAsync(
        db24804Context db, List<int> myIds, int emp, DateTime? crmFrom,
        JsonElement args, CancellationToken ct)
    {
        var limit = ReadInt(args, "limit", 10, 1, 20);
        var oppFilter = ReadInt(args, "opportunityId", 0, 0, int.MaxValue);

        var q = db.VwCustomerInteractions.AsNoTracking()
            .Where(i => myIds.Contains(i.OpportunityId) || i.EmployeeId == emp);

        if (oppFilter > 0)
            q = q.Where(i => i.OpportunityId == oppFilter);

        if (crmFrom.HasValue)
            q = q.Where(i => i.InteractionDate >= crmFrom.Value);

        var list = await q.OrderByDescending(i => i.InteractionDate)
            .ThenByDescending(i => i.InteractionId)
            .Take(limit)
            .ToListAsync(ct);

        var headers = new[] { "التاريخ", "العميل", "الموظف", "المرحلة بعد التواصل", "الملخص" };

        var rows = list.Select(i => (IReadOnlyList<string>)new[]
        {
            Date(i.InteractionDate),
            i.ClientName ?? "—",
            i.EmployeeName ?? "—",
            i.StageAfterNameAr ?? i.StageAfterName ?? "—",
            Truncate(i.Summary, 120)
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["عدد التواصلات المعروضة"] = rows.Count,
            ["التواصلات"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, rows.Count == 0 ? null : new ZakiTable("آخر التواصلات", headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // مساعدات
    // ════════════════════════════════════════════════════════════
    /// <summary>جدول تشخيص مؤقت: بيوضح ليه البائع مش شايف بياناته (هيتشال بعد ما نحدد السبب)</summary>
    private static async Task<ZakiTable> DiagnosticAsync(
        db24804Context db, int emp, string username, string displayName, DateTime? crmFrom, CancellationToken ct)
    {
        var byEmployee = await db.SalesOpportunities.AsNoTracking()
            .CountAsync(o => o.IsActive && o.EmployeeId == emp, ct);

        var byUsername = await db.SalesOpportunities.AsNoTracking()
            .CountAsync(o => o.IsActive && o.CreatedBy == username, ct);

        var byDisplayName = await db.SalesOpportunities.AsNoTracking()
            .CountAsync(o => o.IsActive && o.CreatedBy == displayName, ct);

        var afterDate = crmFrom.HasValue
            ? await db.SalesOpportunities.AsNoTracking()
                .CountAsync(o => o.IsActive && o.EmployeeId == emp && o.CreatedAt >= crmFrom.Value, ct)
            : byEmployee;

        var myTasks = await db.VwCrmTasks.AsNoTracking()
            .CountAsync(t => t.AssignedTo == emp && t.Status != "Completed" && t.Status != "Cancelled", ct);

        var headers = new[] { "البند", "القيمة" };
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "رقم الموظف المربوط بحسابك", emp.ToString(Culture) },
            new[] { "اسم دخولك", username },
            new[] { "تاريخ بداية الاطلاع", crmFrom?.ToString("yyyy-MM-dd", Culture) ?? "بدون تحديد" },
            new[] { "فرص مربوطة برقم موظفك", byEmployee.ToString(Culture) },
            new[] { "فرص باسم دخولك في عمود المنشئ", byUsername.ToString(Culture) },
            new[] { "فرص باسمك الكامل في عمود المنشئ", byDisplayName.ToString(Culture) },
            new[] { "فرص موظفك بعد تاريخ الاطلاع", afterDate.ToString(Culture) },
            new[] { "مهام متعينة لموظفك (مش مكتملة)", myTasks.ToString(Culture) }
        };

        return new ZakiTable("تشخيص مؤقت (لتحديد السبب)", headers, rows);
    }

    private static ZakiToolOutcome Message(string text) =>
        new(ToJson(new Dictionary<string, object?> { ["ملاحظة"] = text }), null);

    private static bool IsClosed(string? nameAr, string? name) =>
        ClosedStageNames.Contains(nameAr ?? "") || ClosedStageNames.Contains(name ?? "");

    private static string TaskStatusAr(string? status) => status switch
    {
        "Pending" => "قيد الانتظار",
        "InProgress" => "جاري التنفيذ",
        "Started" => "جاري التنفيذ",
        "Active" => "نشطة",
        "Completed" => "مكتملة",
        "Cancelled" => "ملغاة",
        _ => status ?? "—"
    };

    private static string PriorityAr(string? priority) => priority switch
    {
        "High" => "عالية",
        "Medium" => "متوسطة",
        "Normal" => "متوسطة",
        "Low" => "منخفضة",
        _ => priority ?? "—"
    };

    private static string Money(decimal? value) =>
        value.HasValue ? value.Value.ToString("#,0", Culture) + " ج.م" : "—";

    private static string Date(DateTime? value) =>
        value.HasValue ? value.Value.ToString("yyyy-MM-dd", Culture) : "—";

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return "—";
        return text.Length <= max ? text : text[..max] + "…";
    }

    private static List<Dictionary<string, object?>> ToObjects(string[] headers, IEnumerable<IReadOnlyList<string>> rows) =>
        rows.Select(r =>
        {
            var d = new Dictionary<string, object?>();
            for (var i = 0; i < headers.Length && i < r.Count; i++)
                d[headers[i]] = r[i];
            return d;
        }).ToList();

    private static JsonElement ParseArgs(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static int ReadInt(JsonElement args, string name, int fallback, int min, int max)
    {
        if (args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt32(out var n))
        {
            return Math.Clamp(n, min, max);
        }
        return fallback;
    }

    private static string ToJson(object value) => JsonSerializer.Serialize(value, JsonOptions);
}
