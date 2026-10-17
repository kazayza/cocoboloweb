using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// تنفيذ IAiProvider على Groq (Chat Completions متوافق مع OpenAI).
/// - بناخد "content" و"tool_calls" بس. حقل "reasoning" مابيتعرضش.
/// - مابنسجلش نص السؤال ولا الرد في اللوج (خصوصية بيانات الشركة).
/// </summary>
public class GroqAiProvider : IAiProvider
{
    private const string Endpoint = "https://api.groq.com/openai/v1/chat/completions";

    private readonly HttpClient _http;
    private readonly AssistantOptions _opt;
    private readonly ILogger<GroqAiProvider> _logger;

    public GroqAiProvider(HttpClient http, IOptions<AssistantOptions> options, ILogger<GroqAiProvider> logger)
    {
        _http = http;
        _opt = options.Value;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public async Task<AiChatResult> CompleteAsync(
        IReadOnlyList<AiMessage> messages,
        IReadOnlyList<AiToolDefinition>? tools = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.GroqApiKey))
            return AiChatResult.Fail("NOT_CONFIGURED", "مفتاح الذكاء الاصطناعي غير مضبوط على السيرفر.");

        var payload = new Dictionary<string, object?>
        {
            ["model"] = _opt.Model,
            ["temperature"] = _opt.Temperature,
            // حد صريح للرد: Groq بيحسب حد الدقيقة على الحد المعلن مش على الرد الفعلي
            ["max_completion_tokens"] = 1500,
            ["messages"] = messages.Select(ToWire).ToArray()
        };

        // موديلات gpt-oss بتفكر داخلياً (وبتستهلك توكنز). الإعداد ده بيقلل التفكير للأسئلة العادية
        if (_opt.Model.StartsWith("openai/gpt-oss", StringComparison.OrdinalIgnoreCase))
            payload["reasoning_effort"] = "low";

        if (tools is { Count: > 0 })
        {
            payload["tools"] = tools.Select(t => new
            {
                type = "function",
                function = new { name = t.Name, description = t.Description, parameters = ToToolParameters(t.ParametersSchema) }
            }).ToArray();
            payload["tool_choice"] = "auto";
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opt.GroqApiKey);

        try
        {
            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                var detail = body.Length > 300 ? body[..300] : body;
                _logger.LogWarning("Groq API returned HTTP {Status}: {Detail}", (int)resp.StatusCode, detail);

                return resp.StatusCode switch
                {
                    HttpStatusCode.TooManyRequests => ZakiRateLimit.Build(body),
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AiChatResult.Fail("AUTH_FAILED", "مفتاح الذكاء الاصطناعي غير صالح."),
                    HttpStatusCode.RequestEntityTooLarge => AiChatResult.Fail(
                        "REQUEST_TOO_LARGE",
                        "السؤال ده محتاج بيانات أكتر من اللي الحساب يستوعبه دلوقتي. جرّب سؤال أضيق (فترة أقصر، أو عدد أقل)."),
                    _ => AiChatResult.Fail("PROVIDER_ERROR", "حصل خطأ من خدمة الذكاء الاصطناعي، حاول لاحقاً.")
                };
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var message = root.GetProperty("choices")[0].GetProperty("message");

            var content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString() ?? ""
                : "";

            var calls = new List<AiToolCall>();
            if (message.TryGetProperty("tool_calls", out var tc) && tc.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in tc.EnumerateArray())
                {
                    var fn = item.GetProperty("function");
                    calls.Add(new AiToolCall(
                        item.GetProperty("id").GetString() ?? "",
                        fn.GetProperty("name").GetString() ?? "",
                        fn.GetProperty("arguments").GetString() ?? "{}"));
                }
            }

            var usage = root.TryGetProperty("usage", out var u) ? u : default;

            return new AiChatResult
            {
                Success = true,
                Content = content.Trim(),
                ToolCalls = calls,
                PromptTokens = ReadInt(usage, "prompt_tokens"),
                CachedPromptTokens = usage.ValueKind == JsonValueKind.Object
                    && usage.TryGetProperty("prompt_tokens_details", out var details)
                        ? ReadInt(details, "cached_tokens")
                        : 0,
                CompletionTokens = ReadInt(usage, "completion_tokens"),
                TotalTokens = ReadInt(usage, "total_tokens")
            };
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return AiChatResult.Fail("TIMEOUT", "الرد أخذ وقت طويل، حاول تاني.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Groq call failed");
            return AiChatResult.Fail("PROVIDER_ERROR", "حصل خطأ أثناء الاتصال بخدمة الذكاء الاصطناعي.");
        }
    }

    /// <summary>تحويل الرسالة لشكل Groq (بنحط الحقول الاختيارية بس لو موجودة)</summary>
    private static object ToWire(AiMessage m)
    {
        var d = new Dictionary<string, object?>
        {
            ["role"] = m.Role,
            ["content"] = m.Content ?? ""
        };

        if (m.ToolCallId != null) d["tool_call_id"] = m.ToolCallId;
        if (m.Name != null) d["name"] = m.Name;

        if (m.ToolCalls is { Count: > 0 })
        {
            d["tool_calls"] = m.ToolCalls.Select(c => new
            {
                id = c.Id,
                type = "function",
                function = new { name = c.Name, arguments = c.ArgumentsJson }
            }).ToArray();
        }

        return d;
    }

    private static int ReadInt(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : 0;

    /// <summary>
    /// بيختصر الـ schema قبل الإرسال: بيشيل وصف كل معامل (الـ enum والاسم بيكفوا).
    /// الوصف العام للأداة بيفضل. ده بيقلل توكنز كل طلب من غير ما نغيّر معنى الأدوات.
    /// لو CompactToolSchemas = false بيرجع الـ schema زي ما هو.
    /// </summary>
    private object? ToToolParameters(object schema)
    {
        if (!_opt.CompactToolSchemas)
            return schema;

        var node = JsonSerializer.SerializeToNode(schema);
        if (node is JsonObject root && root["properties"] is JsonObject props)
        {
            foreach (var prop in props)
            {
                if (prop.Value is JsonObject p)
                    p.Remove("description");
            }
        }
        return node;
    }
}
