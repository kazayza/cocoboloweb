using System.Globalization;
using System.Security.Claims;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// تقرير نشاط موظف (للـ Admin بس).
/// ملاحظات الخصوصية:
/// - بنعد الأرقام بس، ومابنقراش OldData ولا NewData من سجل التدقيق (ممكن يحتوي مرتبات).
/// - الموظف لازم يتحدد باسمه، ولو الاسم مش واضح بنطلب الاسم الكامل.
/// </summary>
public class ZakiEmployeeActivityService
{
    public const string ToolName = "get_employee_activity";

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly ILogger<ZakiEmployeeActivityService> _logger;

    public ZakiEmployeeActivityService(
        IDbContextFactory<db24804Context> dbFactory,
        ILogger<ZakiEmployeeActivityService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>الأداة تظهر للـ Admin بس</summary>
    public static bool CanView(ClaimsPrincipal user) => user.IsInRole("Admin");

    public static AiToolDefinition Definition() => new(
        ToolName,
        "تقرير نشاط موظف واحد (للـ Admin بس): عدد العمليات المسجلة في النظام، والتواصلات، والفرص الجديدة، والمهام المكتملة ونسبة الالتزام بالمواعيد. استخدمها لأي سؤال عن إنتاجية موظف أو مين عمل إيه.",
        new
        {
            type = "object",
            properties = new
            {
                employeeName = new { type = "string", description = "اسم الموظف أو جزء منه (لازم حرفين على الأقل)" },
                period = new { type = "string", @enum = new[] { "today", "week", "month" }, description = "today = النهارده، week = آخر 7 أيام، month = الشهر ده" }
            },
            required = new[] { "employeeName" }
        });

    public async Task<ZakiToolOutcome> ExecuteAsync(string? employeeName, string? period, CancellationToken ct = default)
    {
        var name = (employeeName ?? "").Trim();
        if (name.Length < 2)
            return Note("اكتب اسم الموظف (حرفين على الأقل).");

        var (from, to, label) = Range(period);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var matches = await db.Employees.AsNoTracking()
            .Where(e => e.FullName.Contains(name))
            .OrderBy(e => e.FullName)
            .Select(e => new { e.EmployeeId, e.FullName })
            .Take(6)
            .ToListAsync(ct);

        if (matches.Count == 0)
            return Note("مفيش موظف بالاسم ده.");

        if (matches.Count > 1)
        {
            var names = string.Join("، ", matches.Select(m => m.FullName));
            return Note("في أكتر من موظف بالاسم ده، اكتب الاسم كامل. الأسماء: " + names);
        }

        var emp = matches[0];

        // اسم دخول اليوزر المربوط بالموظف (عشان نربط سجل التدقيق)
        var usernames = await db.Users.AsNoTracking()
            .Where(u => u.EmployeeId == emp.EmployeeId && u.Username != null)
            .Select(u => u.Username)
            .ToListAsync(ct);

        int auditCount = 0;
        var auditByAction = new List<Dictionary<string, object?>>();

        if (usernames.Count > 0)
        {
            var audits = db.AuditLogs.AsNoTracking()
                .Where(a => a.ActionDate >= from && a.ActionDate < to)
                .Where(a => usernames.Contains(a.LoginName!) || usernames.Contains(a.AccessUserName!));

            auditCount = await audits.CountAsync(ct);

            var groups = await audits
                .GroupBy(a => a.ActionType)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            auditByAction = groups
                .Select(g => new Dictionary<string, object?>
                {
                    ["نوع العملية"] = g.Type ?? "—",
                    ["العدد"] = g.Count
                })
                .ToList();
        }

        var interactions = await db.CustomerInteractions.AsNoTracking()
            .CountAsync(i => i.EmployeeId == emp.EmployeeId && i.InteractionDate >= from && i.InteractionDate < to, ct);

        var newOpps = await db.SalesOpportunities.AsNoTracking()
            .CountAsync(o => o.EmployeeId == emp.EmployeeId && o.CreatedAt >= from && o.CreatedAt < to, ct);

        var completed = db.CrmTasks.AsNoTracking()
            .Where(t => t.AssignedTo == emp.EmployeeId
                        && t.Status == "Completed"
                        && t.CompletedDate >= from && t.CompletedDate < to);

        var completedCount = await completed.CountAsync(ct);
        var onTimeCount = await completed.CountAsync(t => t.CompletedDate!.Value.Date <= t.DueDate.Date, ct);

        var openOverdue = await db.CrmTasks.AsNoTracking()
            .CountAsync(t => t.AssignedTo == emp.EmployeeId
                             && t.IsActive
                             && t.Status != "Completed"
                             && t.Status != "Cancelled"
                             && t.DueDate < DateTime.Today, ct);

        var onTimePct = completedCount == 0 ? "—" : Math.Round(onTimeCount * 100m / completedCount, 1).ToString(Culture) + "%";

        var headers = new[] { "المؤشر", "القيمة" };
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "العمليات المسجلة في النظام (إضافة/تعديل/حذف)", auditCount.ToString(Culture) },
            new[] { "التواصلات مع العملاء", interactions.ToString(Culture) },
            new[] { "فرص جديدة", newOpps.ToString(Culture) },
            new[] { "مهام مكتملة", completedCount.ToString(Culture) },
            new[] { "مهام مكتملة في موعدها", onTimeCount.ToString(Culture) },
            new[] { "نسبة الالتزام بالمواعيد", onTimePct },
            new[] { "مهام متأخرة مفتوحة الآن", openOverdue.ToString(Culture) }
        };

        var title = $"نشاط {emp.FullName} — {label}";

        var notes = usernames.Count == 0
            ? "الموظف مش مربوط بيوزر دخول في النظام، فعمليات التدقيق مش متاحة."
            : null;

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الموظف"] = emp.FullName,
            ["الفترة"] = label,
            ["المؤشرات"] = ToObjects(headers, rows),
            ["العمليات حسب النوع"] = auditByAction,
            ["ملاحظة"] = notes
        });

        _logger.LogInformation("Zaki employee activity: employee {EmployeeId}, period {Period}", emp.EmployeeId, period);

        return new ZakiToolOutcome(json, new ZakiTable(title, headers, rows));
    }

    /// <summary>يقرأ مدخلات الموديل (JSON) وينفّذ التقرير</summary>
    public Task<ZakiToolOutcome> ExecuteFromArgsAsync(string argumentsJson, CancellationToken ct = default)
    {
        string? employeeName = null;
        string? period = null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (root.TryGetProperty("employeeName", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String)
                    employeeName = n.GetString();
                if (root.TryGetProperty("period", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String)
                    period = p.GetString();
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // مدخلات غلط = نرجع رسالة الخطأ من الدالة نفسها
        }

        return ExecuteAsync(employeeName, period, ct);
    }

    private static ZakiToolOutcome Note(string message) =>
        new(JsonSerializerHelper.Serialize(new Dictionary<string, object?> { ["ملاحظة"] = message }), null);

    private static (DateTime From, DateTime To, string Label) Range(string? period)
    {
        var today = DateTime.Today;
        return period switch
        {
            "today" => (today, today.AddDays(1), "النهارده"),
            "week" => (today.AddDays(-6), today.AddDays(1), "آخر 7 أيام"),
            _ => (new DateTime(today.Year, today.Month, 1), today.AddDays(1), "الشهر ده")
        };
    }

    private static List<Dictionary<string, object?>> ToObjects(string[] headers, IEnumerable<IReadOnlyList<string>> rows) =>
        rows.Select(r =>
        {
            var d = new Dictionary<string, object?>();
            for (var i = 0; i < headers.Length && i < r.Count; i++)
                d[headers[i]] = r[i];
            return d;
        }).ToList();

    private static string ToJson(object value) => JsonSerializerHelper.Serialize(value);
}

/// <summary>مساعد صغير للـ JSON بعربي مقروء (بدون \u escape)</summary>
internal static class JsonSerializerHelper
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize(object value) => System.Text.Json.JsonSerializer.Serialize(value, Options);
}
