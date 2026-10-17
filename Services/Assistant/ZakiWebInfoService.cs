using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using static COCOBOLOERPNEW.Services.Assistant.ZakiInvoiceQueries;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// معلومات من خارج النظام لزكي:
/// - get_world_news: أخبار عامة من BBC (عربي / إنجليزي) عن طريق RSS.
/// - get_exchange_rate: سعر الصرف مقابل الجنيه من open.er-api.com (سعر السوق التقريبي، مش سعر البنك).
/// ملاحظات:
/// - دي مش بيانات النظام، والرد لازم يوضح المصدر ووقت التحديث.
/// - مفيش صلاحيات خاصة (معلومات عامة)، بس المستخدم لازم يكون مسجل دخول (زكي كله كده).
/// - النتايج بتتخزن في الكاش (15 دقيقة للأخبار و30 دقيقة للأسعار) عشان ما نضغطش على المصادر.
/// </summary>
public class ZakiWebInfoService
{
    public const string GetWorldNews = "get_world_news";
    public const string GetExchangeRate = "get_exchange_rate";

    private const string RatesUrl = "https://open.er-api.com/v6/latest/USD";
    private const string RatesSource = "exchangerate-api.com (open.er-api.com)";

    private static readonly Dictionary<string, (string Url, string Source)> NewsFeeds = new()
    {
        ["ar"] = ("https://feeds.bbci.co.uk/arabic/rss.xml", "BBC عربي"),
        ["en"] = ("https://feeds.bbci.co.uk/news/world/rss.xml", "BBC News (World)")
    };

    private static readonly (string Code, string NameAr)[] Currencies =
    {
        ("USD", "دولار أمريكي"),
        ("EUR", "يورو"),
        ("GBP", "جنيه إسترليني"),
        ("SAR", "ريال سعودي"),
        ("AED", "درهم إماراتي")
    };

    private readonly IHttpClientFactory _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ZakiWebInfoService> _logger;

    public ZakiWebInfoService(
        IHttpClientFactory http,
        IMemoryCache cache,
        ILogger<ZakiWebInfoService> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    public static bool Handles(string toolName) =>
        toolName == GetWorldNews || toolName == GetExchangeRate;

    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user) =>
        new[] { NewsDefinition(), RateDefinition() };

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string name, string argumentsJson, CancellationToken ct = default)
    {
        var args = ParseArgs(argumentsJson);

        try
        {
            return name == GetWorldNews
                ? await NewsAsync(args, ct)
                : await RatesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki web info: fetch failed ({Tool})", name);
            return Message("ماقدرتش أجيب المعلومة من المصدر الخارجي دلوقتي. حاول تاني بعد شوية.");
        }
    }

    // ════════════════════════════════════════════════════════════
    // الأخبار
    // ════════════════════════════════════════════════════════════
    private sealed record NewsItem(string Title, string Link, DateTime? Published);

    private async Task<ZakiToolOutcome> NewsAsync(System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var lang = ReadString(args, "lang", "ar") == "en" ? "en" : "ar";
        var limit = ReadInt(args, "limit", 6, 1, 10);
        var (url, source) = NewsFeeds[lang];

        var items = await _cache.GetOrCreateAsync($"zaki-news:{lang}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
            return await FetchNewsAsync(url, ct);
        }) ?? new List<NewsItem>();

        if (items.Count == 0)
            return Message("مالقتش أخبار دلوقتي من المصدر. حاول تاني بعد شوية.");

        var shown = items.Take(limit).ToList();
        var headers = new[] { "الخبر", "الوقت" };
        var rows = shown.Select(i => (IReadOnlyList<string>)new[]
        {
            i.Title,
            i.Published.HasValue ? CairoTime(i.Published.Value).ToString("yyyy-MM-dd HH:mm", Culture) : "—"
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["المصدر"] = source,
            ["ملاحظة"] = "أخبار من مصدر خارجي للمعلومية، مش بيانات النظام. اذكر المصدر ووقت الخبر في ردك.",
            ["الأخبار"] = shown.Select(i => new Dictionary<string, object?>
            {
                ["العنوان"] = i.Title,
                ["الوقت"] = i.Published.HasValue ? CairoTime(i.Published.Value).ToString("yyyy-MM-dd HH:mm", Culture) : "—",
                ["الرابط"] = i.Link
            }).ToList()
        });

        return new ZakiToolOutcome(json, new ZakiTable("أخبار عامة — " + source, headers, rows));
    }

    private async Task<List<NewsItem>> FetchNewsAsync(string url, CancellationToken ct)
    {
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        var xml = await client.GetStringAsync(url, ct);
        var doc = XDocument.Parse(xml);

        return doc.Descendants("item")
            .Take(20)
            .Select(i => new NewsItem(
                ((string?)i.Element("title") ?? "").Trim(),
                ((string?)i.Element("link") ?? "").Trim(),
                ParseDate((string?)i.Element("pubDate"))))
            .Where(i => i.Title.Length > 0)
            .ToList();
    }

    // ════════════════════════════════════════════════════════════
    // سعر الصرف
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> RatesAsync(CancellationToken ct)
    {
        var data = await _cache.GetOrCreateAsync("zaki-fx:usd", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            return await FetchRatesAsync(ct);
        });

        if (data is null)
            return Message("مالقتش سعر الصرف دلوقتي من المصدر. حاول تاني بعد شوية.");

        var headers = new[] { "العملة", "سعرها بالجنيه المصري (تقريبي)" };
        var rows = data.Value.Rates.Select(r => (IReadOnlyList<string>)new[]
        {
            $"1 {r.NameAr} ({r.Code})",
            r.EgpValue.ToString("0.00", Culture) + " ج.م"
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["المصدر"] = RatesSource,
            ["آخر تحديث للمصدر"] = data.Value.UpdatedAt,
            ["ملاحظة"] = "ده سعر السوق التقريبي (متوسط)، مش سعر البنك. السعر الفعلي بيختلف حسب البنك ووقت التعامل.",
            ["الأسعار"] = data.Value.Rates.Select(r => new Dictionary<string, object?>
            {
                ["العملة"] = r.NameAr,
                ["الكود"] = r.Code,
                ["بالجنيه"] = r.EgpValue.ToString("0.00", Culture)
            }).ToList()
        });

        return new ZakiToolOutcome(json, new ZakiTable("سعر الصرف — تقريبي", headers, rows));
    }

    private sealed record RateRow(string Code, string NameAr, decimal EgpValue);

    private async Task<(string UpdatedAt, List<RateRow> Rates)?> FetchRatesAsync(CancellationToken ct)
    {
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        var body = await client.GetStringAsync(RatesUrl, ct);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!root.TryGetProperty("rates", out var rates) || !rates.TryGetProperty("EGP", out var egpEl))
            return null;

        var usdToEgp = egpEl.GetDecimal();
        var list = new List<RateRow>();

        foreach (var (code, nameAr) in Currencies)
        {
            if (code == "USD")
            {
                list.Add(new RateRow(code, nameAr, usdToEgp));
                continue;
            }

            // rates[X] = كام وحدة X في الدولار. فالجنيه للوحدة = (جنيه للدولار) / (X للدولار)
            if (rates.TryGetProperty(code, out var v) && v.GetDecimal() > 0)
                list.Add(new RateRow(code, nameAr, usdToEgp / v.GetDecimal()));
        }

        var updated = root.TryGetProperty("time_last_update_utc", out var t)
            ? CairoTime(ParseDate(t.GetString()) ?? DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm", Culture) + " (توقيت القاهرة)"
            : "—";

        return (updated, list);
    }

    // ════════════════════════════════════════════════════════════
    // مساعدات
    // ════════════════════════════════════════════════════════════
    private static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)
            ? dto.UtcDateTime
            : null;
    }

    private static DateTime CairoTime(DateTime utc)
    {
        var utcValue = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        foreach (var id in new[] { "Egypt Standard Time", "Africa/Cairo" })
        {
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(utcValue, TimeZoneInfo.FindSystemTimeZoneById(id));
            }
            catch (TimeZoneNotFoundException)
            {
                // نجرّب الاسم التاني (ويندوز / لينكس)
            }
        }
        return utcValue.AddHours(2);
    }

    private static AiToolDefinition NewsDefinition() => new(
        GetWorldNews,
        "أخبار عامة وأخبار العالم اليوم من BBC (من خارج النظام). استخدمها لأي سؤال عن الأخبار أو الأحداث العامة. مش بيانات النظام.",
        new
        {
            type = "object",
            properties = new
            {
                lang = new { type = "string", @enum = new[] { "ar", "en" }, description = "ar = عربي (الافتراضي)، en = إنجليزي" },
                limit = new { type = "integer", minimum = 1, maximum = 10, description = "عدد الأخبار (الافتراضي 6)" }
            }
        });

    private static AiToolDefinition RateDefinition() => new(
        GetExchangeRate,
        "سعر الدولار واليورو والجنيه الإسترليني والريال والدرهم مقابل الجنيه المصري، تقريبي من مصدر خارجي. استخدمها لأي سؤال عن سعر الدولار أو الصرف.",
        new { type = "object", properties = new { } });
}
