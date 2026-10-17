using System.Security.Claims;
using COCOBOLOERPNEW.Services;
using static COCOBOLOERPNEW.Services.Assistant.ZakiInvoiceQueries;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// أداة البحث عن الشاشات: بتدلّ المستخدم على الشاشة الصح (اسم + قسم + رابط).
/// بتعرض بس الشاشات اللي المستخدم يشوفها حسب صلاحياته (نفس منطق القائمة الجانبية).
/// الشاشات بتتقرأ من ZakiScreenMap (مولّدة من SidebarNav).
/// </summary>
public class ZakiKnowledgeService
{
    public const string FindScreen = "find_screen";

    public static bool Handles(string toolName) => toolName == FindScreen;

    /// <summary>متاحة لأي مستخدم مسجل دخول (بتفلتر النتايج بالصلاحيات)</summary>
    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
            ? new[] { Definition() }
            : Array.Empty<AiToolDefinition>();

    public Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string argumentsJson, CancellationToken ct = default)
    {
        var args = ParseArgs(argumentsJson);
        var query = ReadRawString(args, "query");

        if (query.Length < 2)
            return Task.FromResult(Message("اكتب اسم الشاشة أو اللي عايز تعمله."));

        var key = Normalize(query);
        var tokens = key.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2)
            .ToArray();

        var matches = ZakiScreenMap.All
            .Where(s => ZakiScreenMap.CanSee(s, user))
            .Select(s => (Screen: s, Score: Score(s, tokens, key)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(8)
            .ToList();

        if (matches.Count == 0)
            return Task.FromResult(Message("مالقيتش شاشة بالاسم ده في الصلاحيات بتاعتك."));

        var headers = new[] { "الشاشة", "القسم", "الرابط" };
        var rows = matches.Select(m => (IReadOnlyList<string>)new[]
        {
            m.Screen.Title,
            m.Screen.Section,
            m.Screen.Route
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["عدد النتايج"] = rows.Count,
            ["الشاشات"] = ToObjects(headers, rows)
        });

        return Task.FromResult(new ZakiToolOutcome(json, new ZakiTable("الشاشات المطابقة", headers, rows)));
    }

    private static int Score(ZakiScreenMap.Screen screen, string[] tokens, string key)
    {
        var hay = Normalize(screen.Title + " " + screen.Section + " " + screen.Route);
        var score = 0;

        foreach (var t in tokens)
        {
            if (hay.Contains(t)) score += 2;
        }

        if (key.Length > 0 && hay.Contains(key)) score += 5;

        return score;
    }

    private static string Normalize(string text) =>
        text.NormalizeArabic().ToLowerInvariant();

    private static AiToolDefinition Definition() => new(
        FindScreen,
        "تدلّ المستخدم على الشاشة الصح في النظام (اسم الشاشة + القسم + الرابط). استخدمها لأي سؤال عن مكان شاشة، أو إزاي أعمل حاجة في النظام. بترجع بس الشاشات المسموحة للمستخدم.",
        new
        {
            type = "object",
            properties = new
            {
                query = new { type = "string", description = "اسم الشاشة أو الكلمة المفتاحية زي: فاتورة، مخزن، حضور، عروض أسعار" }
            },
            required = new[] { "query" }
        });
}
