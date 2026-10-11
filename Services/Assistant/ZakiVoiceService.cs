using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>نتيجة تحويل الصوت لنص</summary>
public record ZakiVoiceResult(bool Success, string Text, string? ErrorMessage);

/// <summary>التسجيل اللي جاي من المتصفح: الصيغة + البيانات (Base64)</summary>
public record ZakiVoiceClip(string Mime, string Data);

/// <summary>
/// تحويل الصوت لنص عن طريق Whisper على Groq (نفس المفتاح بتاع زكي).
/// الصوت بيتعالج وبيتحذف، ومابيتخزنش (مع ZDR مفعّل على الحساب).
/// </summary>
public class ZakiVoiceService
{
    private const string Endpoint = "https://api.groq.com/openai/v1/audio/transcriptions";
    private const string Model = "whisper-large-v3-turbo";
    private const int MaxBytes = 20 * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly AssistantOptions _opt;
    private readonly ILogger<ZakiVoiceService> _logger;

    public ZakiVoiceService(HttpClient http, IOptions<AssistantOptions> options, ILogger<ZakiVoiceService> logger)
    {
        _http = http;
        _opt = options.Value;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public async Task<ZakiVoiceResult> TranscribeAsync(
        byte[] audio, string fileName, string contentType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.GroqApiKey))
            return new ZakiVoiceResult(false, "", "مفتاح الذكاء الاصطناعي غير مضبوط على السيرفر.");

        if (audio.Length == 0)
            return new ZakiVoiceResult(false, "", "ماتسجلش أي صوت، حاول تاني.");

        if (audio.Length > MaxBytes)
            return new ZakiVoiceResult(false, "", "التسجيل طويل جداً، سجّل سؤال أقصر.");

        using var form = new MultipartFormDataContent();

        var file = new ByteArrayContent(audio);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        form.Add(new StringContent(Model), "model");
        form.Add(new StringContent("ar"), "language");
        form.Add(new StringContent("json"), "response_format");

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = form };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opt.GroqApiKey);

        try
        {
            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Groq transcription HTTP {Status}", (int)resp.StatusCode);
                var msg = resp.StatusCode == HttpStatusCode.TooManyRequests
                    ? "وصلنا للحد المسموح للصوت، حاول بعد دقيقة."
                    : "ما قدرتش أفهم الصوت، حاول تاني.";
                return new ZakiVoiceResult(false, "", msg);
            }

            using var doc = JsonDocument.Parse(body);
            var text = doc.RootElement.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() ?? ""
                : "";

            return new ZakiVoiceResult(true, text.Trim(), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Groq transcription failed");
            return new ZakiVoiceResult(false, "", "حصل خطأ أثناء تحويل الصوت.");
        }
    }
}
