using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Services;
using static COCOBOLOERPNEW.Services.Assistant.ZakiInvoiceQueries;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// التكليفات العامة في زكي (قراءة فقط): مهامي المفتوحة، تكليفات أنشأتها،
/// خيط نقاش تكليف برقمه، ومتأخرات الفريق للإدارة.
/// النطاق من نفس خدمة المهام التي تستخدمها الشاشات — الموظف يرى نفسه،
/// والمنشئ يرى تكليفاته، وأدوار الإدارة ترى الفريق. لا إنشاء عبر زكي.
/// </summary>
public class ZakiGeneralTasksService
{
    public const string GetMyTasks = "get_my_tasks";
    public const string GetMyCreatedTasks = "get_my_created_tasks";
    public const string GetTaskThread = "get_task_thread";
    public const string GetTeamOverdueTasks = "get_team_overdue_tasks";

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private readonly ITaskService _tasks;
    private readonly ILogger<ZakiGeneralTasksService> _logger;

    public ZakiGeneralTasksService(ITaskService tasks, ILogger<ZakiGeneralTasksService> logger)
    {
        _tasks = tasks;
        _logger = logger;
    }

    public static bool Handles(string toolName) =>
        toolName is GetMyTasks or GetMyCreatedTasks or GetTaskThread or GetTeamOverdueTasks;

    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) return Array.Empty<AiToolDefinition>();

        var list = new List<AiToolDefinition> { MyTasksDefinition(), MyCreatedDefinition(), ThreadDefinition() };
        if (GeneralTaskPermissions.CanManageAll(user))
            list.Add(TeamOverdueDefinition());
        return list;
    }

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string toolName, string argumentsJson, int? ownEmployeeId, CancellationToken ct = default)
    {
        var userName = user.Identity?.Name ?? "System";
        try
        {
            return toolName switch
            {
                GetMyTasks => await MyTasksAsync(userName, ownEmployeeId, argumentsJson),
                GetMyCreatedTasks => await MyCreatedAsync(userName, argumentsJson),
                GetTaskThread => await ThreadAsync(userName, ownEmployeeId, argumentsJson),
                GetTeamOverdueTasks => await TeamOverdueAsync(user, userName),
                _ => Message("أداة غير معروفة.")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki general tasks: {Tool} failed", toolName);
            return Message("ماقدرتش أجيب بيانات التكليفات دلوقتي، حاول تاني بعد شوية.");
        }
    }

    private async Task<ZakiToolOutcome> MyTasksAsync(string userName, int? ownEmployeeId, string argumentsJson)
    {
        if (!ownEmployeeId.HasValue)
            return Message("حسابك مش مربوط بموظف، فمش قادر أحدد مهامك. اربط الحساب بموظف من إدارة المستخدمين.");

        var args = ParseArgs(argumentsJson);
        var overdueOnly = ReadBool(args, "overdue_only", false);
        var limit = ReadInt(args, "limit", 20, 1, 50);
        var today = DateTime.Today;

        var mine = await _tasks.GetMyTasksAsync(userName);
        var open = mine.Assigned
            .Where(t => t.Status != "Completed" && t.Status != "Cancelled")
            .ToList();
        var overdue = open.Where(t => t.DueDate.Date < today).ToList();
        var shown = (overdueOnly ? overdue : open).Take(limit).ToList();

        var headers = new[] { "رقم التكليف", "الوصف", "من", "موعد التنفيذ", "الحالة", "الأولوية", "المستند المرتبط" };
        var rows = shown.Select(t => (IReadOnlyList<string>)new[]
        {
            t.TaskId.ToString(Culture),
            Truncate(t.TaskDescription, 60),
            t.CreatedBy ?? "—",
            Date(t.DueDate),
            StatusAr(t.Status) + (t.DueDate.Date < today && t.Status != "Completed" ? $" (متأخر {(today - t.DueDate.Date).Days} يوم)" : ""),
            PriorityAr(t.Priority),
            t.LinkedEntityTitle ?? "—"
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["مهامي المفتوحة"] = open.Count,
            ["منها متأخرة"] = overdue.Count,
            ["ذُكرت في تكليفات"] = mine.Mentioned.Count,
            ["عدد المعروض"] = rows.Count,
            ["المهام"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json,
            rows.Count == 0 ? null : new ZakiTable(overdueOnly ? "مهامي المتأخرة" : "مهامي المفتوحة", headers, rows));
    }

    private async Task<ZakiToolOutcome> MyCreatedAsync(string userName, string argumentsJson)
    {
        var args = ParseArgs(argumentsJson);
        var limit = ReadInt(args, "limit", 20, 1, 50);
        var today = DateTime.Today;

        var mine = await _tasks.GetMyTasksAsync(userName);
        var open = mine.Created
            .Where(t => t.Status != "Completed" && t.Status != "Cancelled")
            .ToList();
        var overdue = open.Where(t => t.DueDate.Date < today).ToList();
        var shown = open.Take(limit).ToList();

        var headers = new[] { "رقم التكليف", "الوصف", "المكلَّف", "موعد التنفيذ", "الحالة", "مُستلم؟" };
        var rows = shown.Select(t => (IReadOnlyList<string>)new[]
        {
            t.TaskId.ToString(Culture),
            Truncate(t.TaskDescription, 60),
            t.AssignedToName ?? "—",
            Date(t.DueDate),
            StatusAr(t.Status) + (t.DueDate.Date < today ? " (متأخر)" : ""),
            t.AcknowledgedAt.HasValue ? "نعم" : "لا"
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["تكليفات أنشأتها وما زالت مفتوحة"] = open.Count,
            ["منها متأخرة"] = overdue.Count,
            ["عدد المعروض"] = rows.Count,
            ["التكليفات"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json,
            rows.Count == 0 ? null : new ZakiTable("تكليفات أنشأتها — المفتوحة", headers, rows));
    }

    private async Task<ZakiToolOutcome> ThreadAsync(string userName, int? ownEmployeeId, string argumentsJson)
    {
        var args = ParseArgs(argumentsJson);
        var taskId = ReadInt(args, "task_id", 0, 0, int.MaxValue);
        if (taskId <= 0)
            return Message("اكتب رقم التكليف اللي عايز خيط نقاشه.");

        var detail = await _tasks.GetTaskDetailAsync(taskId, userName);
        if (detail is null)
            return Message("مالقيتش تكليف بالرقم ده.");

        var isMentioned = ownEmployeeId.HasValue
            && detail.Comments.Any(c => c.MentionedEmployeeIds.Contains(ownEmployeeId.Value));
        if (!detail.ViewerIsAssignee && !detail.ViewerCanAdminister && !isMentioned)
            return Message("خيط التكليف ده متاح لأطرافه بس (المكلَّف والمنشئ والإدارة والمذكورين فيه).");

        var t = detail.Task;
        var lastComments = detail.Comments.TakeLast(10).Select(c => new Dictionary<string, object?>
        {
            ["الكاتب"] = c.AuthorName ?? c.AuthorUserName,
            ["النوع"] = c.Kind == "Progress" ? "تحديث تقدم" : "تعليق",
            ["النص"] = Truncate(c.Body, 200),
            ["الوقت"] = c.CreatedAt.ToString("yyyy/MM/dd HH:mm", Culture)
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["رقم التكليف"] = t.TaskId,
            ["الوصف"] = t.TaskDescription,
            ["الحالة"] = StatusAr(t.Status),
            ["المكلَّف"] = t.AssignedToName,
            ["المنشئ"] = t.CreatedBy,
            ["الاستحقاق"] = Date(t.DueDate),
            ["مُستلم"] = t.AcknowledgedAt.HasValue,
            ["سبب الإرجاع"] = t.ReturnReason,
            ["عدد التعليقات"] = detail.Comments.Count,
            ["آخر التعليقات"] = lastComments
        });

        return new ZakiToolOutcome(json, null);
    }

    private async Task<ZakiToolOutcome> TeamOverdueAsync(ClaimsPrincipal user, string userName)
    {
        if (!GeneralTaskPermissions.CanManageAll(user))
            return Denied();

        var summary = await _tasks.GetGeneralTasksManagerSummaryAsync(userName);
        var withOverdue = summary.Where(r => r.OverdueCount > 0).ToList();

        var headers = new[] { "الموظف", "متأخرة", "مفتوحة", "قيد التنفيذ", "مرتجعة بانتظار قرار", "مكتملة" };
        var rows = withOverdue.Select(r => (IReadOnlyList<string>)new[]
        {
            r.EmployeeName,
            r.OverdueCount.ToString(Culture),
            r.OpenCount.ToString(Culture),
            r.InProgressCount.ToString(Culture),
            r.ReturnedPendingCount.ToString(Culture),
            r.CompletedCount.ToString(Culture)
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["إجمالي التكليفات المتأخرة على الفريق"] = withOverdue.Sum(r => r.OverdueCount),
            ["موظفون عندهم تأخير"] = withOverdue.Count,
            ["الفريق"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json,
            rows.Count == 0 ? null : new ZakiTable("متأخرات الفريق", headers, rows));
    }

    private static string StatusAr(string? status) => status switch
    {
        "Pending" => "قيد الانتظار",
        "In Progress" => "قيد التنفيذ",
        "Completed" => "مكتملة",
        "Cancelled" => "ملغاة",
        _ => status ?? "—"
    };

    private static string PriorityAr(string? priority) => priority switch
    {
        "Urgent" => "عاجلة",
        "High" => "عالية",
        "Low" => "منخفضة",
        "Normal" => "عادية",
        _ => "متوسطة"
    };

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return "—";
        var t = text.Trim();
        return t.Length <= max ? t : t[..(max - 1)] + "…";
    }

    private static AiToolDefinition MyTasksDefinition() => new(
        GetMyTasks,
        "مهام المستخدم الحالي نفسه (التكليفات المُسندة إليه والمفتوحة، ومنها المتأخرة وعدد إشاراته). استخدمها لما يسأل «مهامي إيه؟» أو «عندي إيه متأخر؟». overdue_only=true للمتأخرة بس.",
        new
        {
            type = "object",
            properties = new
            {
                overdue_only = new { type = "boolean", description = "true = المتأخرة بس. الافتراضي false" },
                limit = new { type = "integer", minimum = 1, maximum = 50 }
            }
        });

    private static AiToolDefinition MyCreatedDefinition() => new(
        GetMyCreatedTasks,
        "التكليفات التي أنشأها المستخدم الحالي بنفسه وما زالت مفتوحة (والمكلَّف بها وهل استلمها). استخدمها لما يسأل «التكليفات اللي طلبتها وصلت لإيه؟».",
        new
        {
            type = "object",
            properties = new
            {
                limit = new { type = "integer", minimum = 1, maximum = 50 }
            }
        });

    private static AiToolDefinition ThreadDefinition() => new(
        GetTaskThread,
        "ملخص تكليف برقمه مع آخر تعليقات خيط نقاشه وتحديثات التقدم. متاحة لأطراف التكليف فقط (المكلَّف/المنشئ/الإدارة/المذكورين).",
        new
        {
            type = "object",
            properties = new
            {
                task_id = new { type = "integer", description = "رقم التكليف" }
            },
            required = new[] { "task_id" }
        });

    private static AiToolDefinition TeamOverdueDefinition() => new(
        GetTeamOverdueTasks,
        "متأخرات التكليفات العامة على مستوى الفريق كله لكل موظف (للإدارة فقط: المدير العام/مدير الحسابات/الأدمن). استخدمها لما يسأل مدير «مين متأخر في التكليفات؟».",
        new { type = "object", properties = new { } });
}
