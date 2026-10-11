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
/// مهام موظف معين بالاسم في زكي (غير المستخدم الحالي).
/// - الصلاحية: نفس شاشة المهام (Admin أو frmDailyTasks:View أو frmCrmTasks:View).
/// - البيانات: من نفس خدمة المهام اللي بتستخدمها الشاشة (نفس قواعد النطاق والـ B2B).
/// - البائع (Sales): يشوف مهامه هو بس.
/// - مطابقة الاسم مرنة: بتتجاهل الهمزات والياء (يحيى = يحي)، ولو الاسم مش واضح بتطلب الاسم بالكامل.
/// </summary>
public class ZakiEmployeeTasksService
{
    public const string GetEmployeeTasks = "get_employee_tasks";

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private readonly ITaskService _tasks;
    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly ILogger<ZakiEmployeeTasksService> _logger;

    public ZakiEmployeeTasksService(
        ITaskService tasks,
        IDbContextFactory<db24804Context> dbFactory,
        ILogger<ZakiEmployeeTasksService> logger)
    {
        _tasks = tasks;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public static bool Handles(string toolName) => toolName == GetEmployeeTasks;

    public static bool CanView(ClaimsPrincipal user) =>
        user.IsInRole("Admin")
        || user.HasClaim("Permission", "frmDailyTasks:View")
        || user.HasClaim("Permission", "frmCrmTasks:View");

    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user) =>
        CanView(user) ? new[] { Definition() } : Array.Empty<AiToolDefinition>();

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string argumentsJson, int? ownEmployeeId, CancellationToken ct = default)
    {
        if (!CanView(user))
            return Denied();

        var args = ParseArgs(argumentsJson);
        var search = ReadRawString(args, "employeeName");
        if (search.Length < 2)
            return Message("اكتب اسم الموظف (أو جزء منه) اللي عايز مهامه.");

        var overdueOnly = ReadBool(args, "overdue_only", true);
        var limit = ReadInt(args, "limit", 20, 1, 50);

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var employees = await db.Employees
                .AsNoTracking()
                .Select(e => new EmployeeName(e.EmployeeId, e.FullName))
                .ToListAsync(ct);

            var key = SearchKey(search);
            var matched = employees.Where(e => SearchKey(e.FullName).Contains(key)).ToList();

            if (matched.Count == 0)
                return Message("مالقيتش موظف بالاسم ده.");

            var exact = matched.Where(e => SearchKey(e.FullName) == key).ToList();
            var candidates = exact.Count > 0 ? exact : matched;

            if (candidates.Count > 1)
            {
                return new ZakiToolOutcome(ToJson(new Dictionary<string, object?>
                {
                    ["ملاحظة"] = "في أكتر من موظف بالاسم ده. اطلب من المستخدم يكتب الاسم بالكامل.",
                    ["الموظفين المطابقين"] = candidates.Take(8).Select(c => c.FullName).ToList()
                }), null);
            }

            var emp = candidates[0];

            // البائع يشوف مهامه هو بس
            if (ZakiToolExecutor.IsSalesOnly(user) && emp.EmployeeId != ownEmployeeId)
                return Message("البائع يقدر يشوف مهامه هو بس.");

            var today = DateTime.Today;

            // المتأخرة (نفس فلتر الشاشة: الموعد فات والمهمة مش مكتملة)
            var overdueQ = await _tasks.GetTasksAsync(new TaskFilterDto
            {
                AssignedTo = emp.EmployeeId,
                IsOverdue = true,
                PageNumber = 1,
                PageSize = 500
            });

            // كل المفتوحة (العدد الكلي)
            var openQ = await _tasks.GetTasksAsync(new TaskFilterDto
            {
                AssignedTo = emp.EmployeeId,
                PageNumber = 1,
                PageSize = 1
            });

            // اللي هيتعرض في الجدول
            IReadOnlyList<TaskListDto> shown;
            if (overdueOnly)
            {
                shown = overdueQ.Items;
            }
            else
            {
                var openFull = await _tasks.GetTasksAsync(new TaskFilterDto
                {
                    AssignedTo = emp.EmployeeId,
                    PageNumber = 1,
                    PageSize = 500
                });
                shown = openFull.Items;
            }

            var list = shown.Take(limit).ToList();

            var headers = new[]
            {
                "رقم المهمة", "العميل", "نوع المهمة", "موعد التنفيذ", "أيام التأخير", "الحالة", "الأولوية"
            };

            var rows = list.Select(t =>
            {
                var late = t.DueDate.Date < today;
                return (IReadOnlyList<string>)new[]
                {
                    t.TaskId.ToString(Culture),
                    t.ClientName ?? "—",
                    t.TaskTypeNameAr ?? t.TaskTypeName ?? "—",
                    Date(t.DueDate),
                    late ? (today - t.DueDate.Date).Days.ToString(Culture) : "—",
                    TaskStatusAr(t.Status),
                    PriorityAr(t.Priority)
                };
            }).ToList();

            var oldestLate = overdueQ.Items.Count > 0
                ? overdueQ.Items.Max(t => (today - t.DueDate.Date).Days)
                : 0;

            var json = ToJson(new Dictionary<string, object?>
            {
                ["الموظف"] = emp.FullName,
                ["عدد المهام المتأخرة"] = overdueQ.TotalCount,
                ["أقدم مهمة متأخرة (بالأيام)"] = oldestLate,
                ["إجمالي المهام المفتوحة"] = openQ.TotalCount,
                ["عدد المعروض"] = rows.Count,
                ["المهام"] = ToObjects(headers, rows)
            });

            var title = (overdueOnly ? "المهام المتأخرة" : "المهام المفتوحة") + " — " + emp.FullName;

            return new ZakiToolOutcome(json, rows.Count == 0 ? null : new ZakiTable(title, headers, rows));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki employee tasks: query failed");
            return Message("ماقدرتش أجيب مهام الموظف دلوقتي، حاول تاني بعد شوية.");
        }
    }

    /// <summary>مفتاح مطابقة الأسماء: بدون همزات ولا ياء ولا مسافات (يحيى = يحي = يحيي)</summary>
    private static string SearchKey(string text) =>
        text.NormalizeArabic().Replace("ي", "").Replace(" ", "").ToLowerInvariant();

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

    private static AiToolDefinition Definition() => new(
        GetEmployeeTasks,
        "مهام موظف معين بالاسم (غير المستخدم الحالي): المتأخرة أو المفتوحة مع عددها وأقدم مهمة متأخرة. استخدمها لأي سؤال عن مهام موظف بعينه. overdue_only=true للمتأخرة بس (الافتراضي).",
        new
        {
            type = "object",
            properties = new
            {
                employeeName = new { type = "string", description = "اسم الموظف أو جزء منه، زي ما المستخدم كتبه" },
                overdue_only = new { type = "boolean", description = "true = المتأخرة بس. الافتراضي true" },
                limit = new { type = "integer", minimum = 1, maximum = 50 }
            },
            required = new[] { "employeeName" }
        });

    private sealed record EmployeeName(int EmployeeId, string FullName);
}
