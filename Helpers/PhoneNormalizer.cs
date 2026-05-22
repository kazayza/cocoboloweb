namespace COCOBOLOERPNEW.Helpers;

/// <summary>
/// Smart phone number normalizer لمصر (وغيرها).
/// يتعامل مع كل الأشكال الشائعة:
///   - 01001234567        → 201001234567 (مصري)
///   - 00201001234567     → 201001234567 (دولي بـ 00)
///   - +201001234567      → 201001234567 (دولي بـ +)
///   - 201001234567       → 201001234567 (دولي صحيح)
///   - 1001234567         → 201001234567 (مصري بدون 0)
///   - أرقام بمسافات/شرط  → بتتشال
/// </summary>
public static class PhoneNormalizer
{
    /// <summary>
    /// أكواد الدول الشائعة
    /// </summary>
    public static readonly Dictionary<string, CountryInfo> Countries = new()
    {
        ["EG"] = new("🇪🇬 مصر", "20", 10, new[] { "10", "11", "12", "15" }),
        ["SA"] = new("🇸🇦 السعودية", "966", 9, new[] { "5" }),
        ["AE"] = new("🇦🇪 الإمارات", "971", 9, new[] { "5" }),
        ["KW"] = new("🇰🇼 الكويت", "965", 8, new[] { "5", "6", "9" }),
        ["QA"] = new("🇶🇦 قطر", "974", 8, new[] { "3", "5", "6", "7" }),
        ["BH"] = new("🇧🇭 البحرين", "973", 8, new[] { "3", "6", "9" }),
        ["OM"] = new("🇴🇲 عمان", "968", 8, new[] { "7", "9" }),
        ["JO"] = new("🇯🇴 الأردن", "962", 9, new[] { "7" }),
        ["LB"] = new("🇱🇧 لبنان", "961", 7, new[] { "3", "7", "8" }),
        ["MA"] = new("🇲🇦 المغرب", "212", 9, new[] { "6", "7" }),
        ["DZ"] = new("🇩🇿 الجزائر", "213", 9, new[] { "5", "6", "7" }),
        ["TN"] = new("🇹🇳 تونس", "216", 8, new[] { "2", "4", "5", "9" })
    };

    public record CountryInfo(string DisplayName, string DialCode, int LocalLength, string[] MobilePrefixes);

    /// <summary>
    /// ينظّف الرقم من أي رموز ويرجعه أرقام فقط.
    /// </summary>
    public static string CleanDigits(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var c in raw)
        {
            if (char.IsDigit(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// يحوّل الرقم لصيغة WhatsApp الدولية (بدون + أو 00).
    /// لو فشل، يرجع null.
    /// </summary>
    /// <param name="raw">الرقم الخام كما هو</param>
    /// <param name="defaultCountryCode">كود الدولة الافتراضي لو الرقم محلي (مثال: "20")</param>
    public static string? ToWhatsAppFormat(string? raw, string defaultCountryCode = "20")
    {
        var digits = CleanDigits(raw);
        if (digits.Length < 7) return null;

        // 00 prefix → نشيله (دولي)
        if (digits.StartsWith("00"))
            digits = digits.Substring(2);

        // ابحث إذا كان الرقم يبدأ بكود دولة معروف
        var country = Countries.Values.FirstOrDefault(c => digits.StartsWith(c.DialCode));
        if (country != null)
        {
            var afterCode = digits.Substring(country.DialCode.Length);
            // لو الباقي يبدأ بـ 0 → نشيله
            if (afterCode.StartsWith("0")) afterCode = afterCode.Substring(1);
            return country.DialCode + afterCode;
        }

        // مش يبدأ بكود دولة → نفترض إنه محلي
        // لو يبدأ بـ 0 → نشيله ونضيف الكود الافتراضي
        if (digits.StartsWith("0"))
            return defaultCountryCode + digits.Substring(1);

        // مفيش 0 → نضيف الكود الافتراضي مباشرة
        return defaultCountryCode + digits;
    }

    /// <summary>
    /// يتحقق إذا كان الرقم صالح لـ WhatsApp
    /// </summary>
    public static bool IsValidWhatsAppNumber(string? normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized)) return false;
        if (!normalized.All(char.IsDigit)) return false;
        // الحد الأدنى عادة 10، الأقصى 15 (E.164 standard)
        return normalized.Length >= 10 && normalized.Length <= 15;
    }

    /// <summary>
    /// تنسيق للعرض (مثال: 201001234567 → +20 100 123 4567)
    /// </summary>
    public static string FormatForDisplay(string? normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized)) return "";
        if (normalized.Length < 4) return normalized;

        var country = Countries.Values.FirstOrDefault(c => normalized.StartsWith(c.DialCode));
        if (country == null) return "+" + normalized;

        var code = country.DialCode;
        var rest = normalized.Substring(code.Length);

        // نقسم الباقي لمجموعات من 3-4
        var formatted = new System.Text.StringBuilder("+" + code);
        for (int i = 0; i < rest.Length; i += 3)
        {
            formatted.Append(' ');
            formatted.Append(rest.Substring(i, Math.Min(3, rest.Length - i)));
        }
        return formatted.ToString();
    }
}
