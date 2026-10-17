using System.Text.RegularExpressions;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// بيترجم رد 429 من Groq لرسالة واضحة للمستخدم:
/// - حد اليوم (TPD/RPD): الرسالة بتقول إنه هيتجدد بكرة (أو بعد المدة اللي Groq قالها).
/// - حد الدقيقة (TPM/RPM): الرسالة بتقول جرّب بعد كام ثانية.
/// Groq بيكتب في الرد جملة زي: "Please try again in 14m30s".
/// </summary>
public static class ZakiRateLimit
{
    private static readonly Regex WaitRegex = new(
        @"try again in\s+(?:(\d+)m)?\s*(?:(\d+(?:\.\d+)?)s)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static AiChatResult Build(string? body)
    {
        var text = body ?? "";
        var lower = text.ToLowerInvariant();

        var isDaily = lower.Contains("per day") || lower.Contains("(tpd)") || lower.Contains("(rpd)");
        var wait = ParseWait(text);

        if (isDaily)
        {
            var when = wait.HasValue
                ? " تقريباً بعد " + Friendly(wait.Value)
                : " بكرة";

            return AiChatResult.Fail(
                "RATE_LIMITED_DAILY",
                "وصلنا للحد اليومي لخدمة الذكاء الاصطناعي، هيتجدد" + when + ". جرّب تاني بعدين.");
        }

        var minute = wait ?? TimeSpan.FromSeconds(60);
        return AiChatResult.Fail(
            "RATE_LIMITED",
            "وصلنا للحد المسموح في الدقيقة، جرّب بعد " + Friendly(minute) + ".");
    }

    private static TimeSpan? ParseWait(string text)
    {
        var m = WaitRegex.Match(text);
        if (!m.Success) return null;

        var minutes = m.Groups[1].Success && int.TryParse(m.Groups[1].Value, out var mm) ? mm : 0;
        var seconds = m.Groups[2].Success && double.TryParse(m.Groups[2].Value,
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ss) ? ss : 0;

        if (minutes == 0 && seconds == 0) return null;
        return TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
    }

    private static string Friendly(TimeSpan t)
    {
        if (t.TotalMinutes >= 1)
            return Math.Ceiling(t.TotalMinutes).ToString("0") + " دقيقة";
        return Math.Max(1, Math.Ceiling(t.TotalSeconds)).ToString("0") + " ثانية";
    }
}
