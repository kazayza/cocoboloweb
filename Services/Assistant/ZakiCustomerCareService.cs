using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// أدوات خدمة العملاء والشكاوى في زكي.
/// الصلاحيات (زي الشاشات):
/// - الشكاوى: frm_Complaints_Main:View (أو Admin). اللي مش بيشوف الكل بيشوف شكاواه بس (MineOnly) زي الشاشة.
/// - فيدباك العملاء (خدمة العملاء): CustomerCarePermissions.CanView. الخدمة نفسها بتطبق النطاق حسب الدور.
/// </summary>
public class ZakiCustomerCareService
{
    public const string GetOpenComplaints = "get_open_complaints";
    public const string GetCustomerCareSummary = "get_customer_care_summary";

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IComplaintService _complaints;
    private readonly ICustomerCareService _careService;
    private readonly ILogger<ZakiCustomerCareService> _logger;

    public ZakiCustomerCareService(
        IComplaintService complaints,
        ICustomerCareService careService,
        ILogger<ZakiCustomerCareService> logger)
    {
        _complaints = complaints;
        _careService = careService;
        _logger = logger;
    }

    public static bool Handles(string toolName) =>
        toolName is GetOpenComplaints or GetCustomerCareSummary;

    /// <summary>الأدوات اللي المستخدم مسموح بيها</summary>
    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user)
    {
        var list = new List<AiToolDefinition>();

        if (CanViewComplaints(user)) list.Add(OpenComplaintsDefinition());
        if (CanViewCareSummary(user)) list.Add(CareSummaryDefinition());

        return list;
    }

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string name, string argumentsJson, CancellationToken ct = default)
    {
        var allowed = DefinitionsFor(user).Any(d => d.Name == name);
        if (!allowed)
        {
            return new ZakiToolOutcome(
                ToJson(new Dictionary<string, object?> { ["خطأ"] = "ليس لديك صلاحية لهذه البيانات." }),
                null,
                IsDenied: true);
        }

        var args = ParseArgs(argumentsJson);

        return name switch
        {
            GetOpenComplaints => await OpenComplaintsAsync(user, args),
            GetCustomerCareSummary => await CareSummaryAsync(user),
            _ => Note("أداة غير معروفة.")
        };
    }

    // ════════════════════════════════════════════════════════════
    // 1) الشكاوى المفتوحة
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> OpenComplaintsAsync(ClaimsPrincipal user, JsonElement args)
    {
        var limit = ReadInt(args, "limit", 20, 1, 20);

        // اللي مش بيشوف كل الشكاوى يشوف اللي هو عامله بس (زي الشاشة)
        var seeAll = CanViewAllComplaints(user);

        var page = await _complaints.GetComplaintsAsync(new ComplaintFilterDto
        {
            OpenOnly = true,
            MineOnly = !seeAll,
            Page = 1,
            PageSize = limit
        }, user.Identity?.Name);

        var headers = new[]
        {
            "رقم الشكوى", "التاريخ", "العميل", "النوع", "الموضوع",
            "الأولوية", "الحالة", "الموظف المسؤول", "الأيام المفتوحة"
        };

        var rows = page.Items.Select(c => (IReadOnlyList<string>)new[]
        {
            c.ComplaintId.ToString(Culture),
            Date(c.ComplaintDate),
            c.ClientName ?? "—",
            c.ComplaintType ?? "—",
            Truncate(c.Subject, 80),
            c.PriorityName ?? "—",
            c.StatusName ?? "—",
            c.EmployeeName ?? "—",
            (c.DaysOpen ?? 0).ToString(Culture)
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["عدد الشكاوى المفتوحة في نطاقك"] = page.TotalCount,
            ["عدد الشكاوى المعروضة"] = rows.Count,
            ["الشكاوى"] = ToObjects(headers, rows)
        });

        _logger.LogInformation("Zaki complaints: {Count} open (seeAll={SeeAll})", rows.Count, seeAll);

        return new ZakiToolOutcome(json, rows.Count == 0 ? null : new ZakiTable("الشكاوى المفتوحة", headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 2) ملخص خدمة العملاء (الزيارات والتسليمات والشكاوى والمتابعات)
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> CareSummaryAsync(ClaimsPrincipal user)
    {
        var d = await _careService.GetDashboardAsync(new CustomerCareDashboardFilterDto(), user);
        var k = d.Kpis;

        var headers = new[] { "المؤشر", "القيمة" };
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "زيارات مفتوحة", k.OpenVisits.ToString(Culture) },
            new[] { "تسليمات مفتوحة", k.OpenDeliveries.ToString(Culture) },
            new[] { "فواتير مبيعات لم تُسلَّم", k.OpenSalesInvoices.ToString(Culture) },
            new[] { "شكاوى مفتوحة", k.OpenComplaints.ToString(Culture) },
            new[] { "تمت اليوم", k.DoneToday.ToString(Culture) },
            new[] { "إجمالي المنجز", k.DoneTotal.ToString(Culture) },
            new[] { "متوسط الرضا", k.AvgSatisfaction.HasValue ? k.AvgSatisfaction.Value.ToString("0.0", Culture) : "—" },
            new[] { "تقييمات إيجابية", k.PositiveCount.ToString(Culture) }
        };

        var json = ToJson(new Dictionary<string, object?>
        {
            ["ملخص خدمة العملاء"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, new ZakiTable("ملخص خدمة العملاء", headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // الصلاحيات
    // ════════════════════════════════════════════════════════════
    private static bool CanViewComplaints(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("Permission", "frm_Complaints_Main:View");

    private static bool CanViewAllComplaints(ClaimsPrincipal user) =>
        user.IsInRole("Admin")
        || user.IsInRole("AccountManager")
        || user.IsInRole("SalesManager")
        || user.IsInRole("GeneralManager")
        || user.IsInRole("Account");

    private static bool CanViewCareSummary(ClaimsPrincipal user) =>
        CustomerCarePermissions.CanView(user);

    // ════════════════════════════════════════════════════════════
    // تعريفات الأدوات
    // ════════════════════════════════════════════════════════════
    private static AiToolDefinition OpenComplaintsDefinition() => new(
        GetOpenComplaints,
        "ترجع الشكاوى المفتوحة (غير المحلولة وغير المرفوضة) بنفس بيانات شاشة الشكاوى. استخدمها لأي سؤال عن الشكاوى المفتوحة أو حالتها أو مين مسؤول عنها.",
        new
        {
            type = "object",
            properties = new
            {
                limit = new { type = "integer", description = "عدد الشكاوى المطلوبة من 1 إلى 20", minimum = 1, maximum = 20 }
            }
        });

    private static AiToolDefinition CareSummaryDefinition() => new(
        GetCustomerCareSummary,
        "ترجع ملخص خدمة العملاء: الزيارات والتسليمات وفواتير المبيعات اللي لسه ماتسلمتش والشكاوى المفتوحة والمنجز ومتوسط الرضا. استخدمها لأي سؤال عن أداء خدمة العملاء.",
        new
        {
            type = "object",
            properties = new { }
        });

    // ════════════════════════════════════════════════════════════
    // مساعدات
    // ════════════════════════════════════════════════════════════
    private static ZakiToolOutcome Note(string message) =>
        new(ToJson(new Dictionary<string, object?> { ["ملاحظة"] = message }), null);

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
