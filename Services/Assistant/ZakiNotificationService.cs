using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// أداة الإشعارات في زكي:
/// - get_unread_notifications: الإشعارات غير المقروءة للمستخدم الحالي (باسمه أو بدوره، زي الجرس بالظبط).
/// - فتح الإشعار من الجدول: بيعلّمه مقروء ويرجّع الرابط (MarkAsReadAsync).
/// - الإشعارات شخصية، فالأداة متاحة لأي مستخدم مسجل دخول.
/// - ResolveUrl نسخة حرفية من منطق الجرس (NotificationBell). لو غيرت روابط الجرس، غيّرها هنا كمان.
/// </summary>
public class ZakiNotificationService
{
    public const string GetUnreadNotifications = "get_unread_notifications";

    private const int MaxMessageChars = 150;

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly ILogger<ZakiNotificationService> _logger;

    public ZakiNotificationService(
        IDbContextFactory<db24804Context> dbFactory,
        ILogger<ZakiNotificationService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public static bool Handles(string toolName) => toolName == GetUnreadNotifications;

    /// <summary>الأداة متاحة لأي مستخدم مسجل دخول</summary>
    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
            ? new[] { Definition() }
            : Array.Empty<AiToolDefinition>();

    private static AiToolDefinition Definition() => new(
        GetUnreadNotifications,
        "ترجع الإشعارات غير المقروءة للمستخدم الحالي (الأحدث الأول) مع عددها الكلي. استخدمها لأي سؤال عن الإشعارات أو اللي وصلني. الجدول بيعرض زرار فتح لكل إشعار.",
        new
        {
            type = "object",
            properties = new
            {
                limit = new { type = "integer", description = "عدد الإشعارات المطلوبة من 1 إلى 20", minimum = 1, maximum = 20 }
            }
        });

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string name, string argumentsJson, CancellationToken ct = default)
    {
        if (!Handles(name))
            return Note("أداة غير معروفة.");

        var limit = ReadInt(ParseArgs(argumentsJson), "limit", 10, 1, 20);
        var username = user.Identity?.Name ?? "";
        var roles = RolesOf(user);

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var query = db.Notifications
                .AsNoTracking()
                .Where(n => !n.IsRead && (n.RecipientUser == username || roles.Contains(n.RecipientUser)));

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(n => n.CreatedAt)
                .Take(limit)
                .ToListAsync(ct);

            var headers = new[] { "العنوان", "التفاصيل", "من", "التاريخ" };

            var rows = items.Select(n => (IReadOnlyList<string>)new[]
            {
                n.Title ?? "—",
                Truncate(n.Message, MaxMessageChars),
                n.CreatedBy ?? "—",
                n.CreatedAt.ToString("yyyy-MM-dd HH:mm", Culture)
            }).ToList();

            var links = items.Select(n =>
            {
                var url = ResolveUrl(n);
                return string.IsNullOrWhiteSpace(url) ? null : new ZakiRowLink(url, n.NotificationId);
            }).ToList();

            var json = ToJson(new Dictionary<string, object?>
            {
                ["عدد الإشعارات غير المقروءة الكلي"] = total,
                ["عدد المعروض"] = rows.Count,
                ["الإشعارات"] = ToObjects(headers, rows)
            });

            var table = rows.Count == 0
                ? null
                : new ZakiTable("الإشعارات غير المقروءة", headers, rows, links);

            return new ZakiToolOutcome(json, table);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki notifications: query failed");
            return Note("ماقدرتش أجيب الإشعارات دلوقتي، حاول تاني بعد شوية.");
        }
    }

    /// <summary>
    /// يعلّم الإشعار كمقروء لو هو بتاع المستخدم ده (نفس شرط الجرس).
    /// بيرجّع false لو الإشعار مش بتاعه أو حصل خطأ.
    /// </summary>
    public async Task<bool> MarkAsReadAsync(int notificationId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var username = user.Identity?.Name ?? "";
        var roles = RolesOf(user);

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var notification = await db.Notifications.FirstOrDefaultAsync(n =>
                n.NotificationId == notificationId
                && (n.RecipientUser == username || roles.Contains(n.RecipientUser)), ct);

            if (notification is null)
                return false;

            if (!notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.Now;
                await db.SaveChangesAsync(ct);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki notifications: mark as read failed");
            return false;
        }
    }

    // ════════════════════════════════════════════════════════════
    // مساعدات
    // ════════════════════════════════════════════════════════════

    /// <summary>أدوار المستخدم من الـ Claims (نفس طريقة الجرس)</summary>
    private static List<string> RolesOf(ClaimsPrincipal user) =>
        user.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role" || c.Type.EndsWith("/role"))
            .Select(c => c.Value)
            .Distinct()
            .ToList();

    private static ZakiToolOutcome Note(string message) =>
        new(ToJson(new Dictionary<string, object?> { ["ملاحظة"] = message }), null);

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

    // ════════════════════════════════════════════════════════════
    // الرابط (نسخة من NotificationBell.GetNotificationUrl)
    // ════════════════════════════════════════════════════════════
    public static string? ResolveUrl(Notification notif)
{
    if (string.Equals(notif.RelatedTable, "LeadsCRM", StringComparison.OrdinalIgnoreCase))
    {
        if (string.Equals(notif.FormName, "crm/leads/details", StringComparison.OrdinalIgnoreCase) && notif.RelatedId.HasValue)
            return $"/crm/leads/{notif.RelatedId.Value}";

        if (string.Equals(notif.FormName, "crm/leads/my", StringComparison.OrdinalIgnoreCase))
            return "/crm/leads/my";

        return "/crm/leads";
    }

    if (string.Equals(notif.RelatedTable, "PayrollRuns", StringComparison.OrdinalIgnoreCase) && notif.RelatedId.HasValue)
        return $"/hr/payroll/run/{notif.RelatedId.Value}";

    if (string.Equals(notif.RelatedTable, "AdditionalCharges", StringComparison.OrdinalIgnoreCase) && notif.RelatedId.HasValue)
        return $"/additional-charges/{notif.RelatedId.Value}/edit";

    if (string.Equals(notif.RelatedTable, "Payments", StringComparison.OrdinalIgnoreCase))
    {
        if ((notif.Title?.Contains("إلغاء") ?? false) || (notif.Message?.Contains("إلغاء") ?? false))
            return "/payments";

        if (notif.RelatedId.HasValue)
            return $"/payments/{notif.RelatedId.Value}/receipt";

        return "/payments";
    }

    if (string.Equals(notif.RelatedTable, "Payroll", StringComparison.OrdinalIgnoreCase))
    {
        if (string.Equals(notif.FormName, "frm_PayrollPayment", StringComparison.OrdinalIgnoreCase))
            return "/hr/payroll/bonuses";

        if (string.Equals(notif.FormName, "frm_Payroll", StringComparison.OrdinalIgnoreCase))
            return "/hr/payroll";
    }

    if (string.Equals(notif.RelatedTable, "OpportunityClosureApprovalRequests", StringComparison.OrdinalIgnoreCase)
        || string.Equals(notif.FormName, "crm/opportunities/closure-requests", StringComparison.OrdinalIgnoreCase))
        return "/crm/opportunities/closure-requests";

    if (string.IsNullOrWhiteSpace(notif.FormName))
        return null;

    var form = notif.FormName.Trim().TrimStart('/');

    // 📋 شاشات القوائم (لا تقبل id في المسار) — نتجاهل RelatedId نهائياً
    if (string.Equals(form, "production/job-orders", StringComparison.OrdinalIgnoreCase))
        return "/production/job-orders";

    if (string.Equals(form, "purchase-receipt-status", StringComparison.OrdinalIgnoreCase))
        return "/purchase-receipt-status";

    // 🧾 فواتير المبيعات — القائمة أو تفاصيل فاتورة محددة
    if (string.Equals(form, "sales/invoices", StringComparison.OrdinalIgnoreCase))
        return notif.RelatedId.HasValue ? $"/sales/invoices/{notif.RelatedId.Value}" : "/sales";

    // 🏭 أمر التصنيع: المسار مدمج كامل داخل FormName (sales/invoices/{id}/job-order)
    // يجب ألا نضيف RelatedId بعده — وإلا أصبح رابطاً مكسوراً بمفتاح مكرر
    if (form.StartsWith("sales/invoices/", StringComparison.OrdinalIgnoreCase)
        && form.EndsWith("/job-order", StringComparison.OrdinalIgnoreCase))
        return $"/{form}";

    if (string.Equals(form, "payments", StringComparison.OrdinalIgnoreCase))
    {
        if ((notif.Title?.Contains("إلغاء") ?? false) || (notif.Message?.Contains("إلغاء") ?? false))
            return "/payments";

        if (notif.RelatedId.HasValue)
            return $"/payments/{notif.RelatedId.Value}/receipt";

        return "/payments";
    }

    if (notif.RelatedId.HasValue)
        return $"/{form}/{notif.RelatedId.Value}";

    return $"/{form}";
}
}
