using System.Security.Claims;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;
using static COCOBOLOERPNEW.Services.Assistant.ZakiInvoiceQueries;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// أداة فيدباك العملاء في زكي — قراءة فقط من CustomerFeedback.
/// الصلاحية: نفس شاشات خدمة العملاء (CustomerCarePermissions.CanView).
/// </summary>
public class ZakiFeedbackService
{
    public const string GetCustomerFeedback = "get_customer_feedback";

    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly ILogger<ZakiFeedbackService> _logger;

    public ZakiFeedbackService(
        IDbContextFactory<db24804Context> dbFactory,
        ILogger<ZakiFeedbackService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public static bool Handles(string toolName) => toolName == GetCustomerFeedback;

    public static bool CanView(ClaimsPrincipal user) => CustomerCarePermissions.CanView(user);

    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user) =>
        CanView(user) ? new[] { Definition() } : Array.Empty<AiToolDefinition>();

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string name, string argumentsJson, CancellationToken ct = default)
    {
        if (!CanView(user)) return Denied();

        var args = ParseArgs(argumentsJson);

        try
        {
            var (start, label) = PeriodRange(ReadString(args, "period", "month"), DateTime.Today);
            var limit = ReadInt(args, "limit", 10, 1, 30);

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var q = db.Set<CustomerFeedback>().AsNoTracking()
                .Where(f => f.IsActive && (start == null || f.CreatedAt >= start));

            var total = await q.CountAsync(ct);
            var open = await q.CountAsync(f => f.Status == "Open", ct);

            var byOutcome = await q
                .Where(f => f.Outcome != null)
                .GroupBy(f => f.Outcome)
                .Select(g => new { Outcome = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var avgSat = await q
                .Where(f => f.Satisfaction != null)
                .AverageAsync(f => (double?)f.Satisfaction, ct) ?? 0;

            var latest = await q
                .OrderByDescending(f => f.CreatedAt)
                .Take(limit)
                .ToListAsync(ct);

            if (total == 0)
                return Message($"مفيش فيدباك مسجل في الفترة دي ({label}).");

            var headers = new[] { "التاريخ", "النوع", "العميل", "النتيجة", "الرضا", "ملاحظة" };
            var rows = latest.Select(f => (IReadOnlyList<string>)new[]
            {
                Date(f.CreatedAt),
                TypeLabel(f.FeedbackType),
                string.IsNullOrWhiteSpace(f.ClientName) ? "—" : f.ClientName!,
                OutcomeLabel(f.Outcome),
                f.Satisfaction?.ToString(Culture) ?? "—",
                Truncate(string.IsNullOrWhiteSpace(f.Notes) ? "—" : f.Notes!, 60)
            }).ToList();

            var json = ToJson(new Dictionary<string, object?>
            {
                ["الفترة"] = label,
                ["إجمالي الفيدباك"] = total,
                ["مفتوح لسه"] = open,
                ["متوسط الرضا (من 5)"] = avgSat.ToString("0.0", Culture),
                ["توزيع النتائج"] = byOutcome.ToDictionary(o => OutcomeLabel(o.Outcome), o => o.Count),
                ["آخر الفيدباك"] = ToObjects(headers, rows)
            });

            return new ZakiToolOutcome(json, new ZakiTable("فيدباك العملاء — " + label, headers, rows));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki feedback: query failed");
            return Message("ماقدرتش أجيب الفيدباك دلوقتي، حاول تاني بعد شوية.");
        }
    }

    private static string TypeLabel(string? t) => t switch
    {
        "Visit" => "زيارة",
        "Delivery" => "تسليم",
        "ComplaintFollowUp" => "متابعة شكوى",
        _ => t ?? "—"
    };

    private static string OutcomeLabel(string? o) => o switch
    {
        "Positive" => "إيجابي",
        "Negative" => "سلبي",
        "NoAnswer" => "بدون إجابة",
        "NotInterested" => "غير مهتم",
        "CallbackLater" => "اتصال لاحق",
        _ => o ?? "—"
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";

    private static AiToolDefinition Definition() => new(
        GetCustomerFeedback,
        "فيدباك العملاء: عدد وتوزيع النتائج (إيجابي/سلبي...) ومتوسط الرضا وآخر التعليقات. للرد على: الناس راضية عن التسليم؟ إيه آخر تقييمات العملاء؟",
        new
        {
            type = "object",
            properties = new
            {
                period = PeriodSchema("الافتراضي month"),
                limit = new { type = "integer", description = "عدد آخر التعليقات المعروضة (1-30، الافتراضي 10)" }
            }
        });

    private static object PeriodSchema(string description) => new
    {
        type = "string",
        description,
        @enum = new[] { "today", "week", "month", "year", "all" }
    };
}
