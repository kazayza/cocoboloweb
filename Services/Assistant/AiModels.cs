namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// رسالة في المحادثة مع الموديل.
/// Role: system | user | assistant | tool
/// </summary>
public record AiMessage(
    string Role,
    string? Content,
    string? ToolCallId = null,
    string? Name = null,
    IReadOnlyList<AiToolCall>? ToolCalls = null);

/// <summary>طلب أداة من الموديل (الموديل بيقول: نادي الأداة دي بالمدخلات دي)</summary>
public record AiToolCall(string Id, string Name, string ArgumentsJson);

/// <summary>تعريف أداة بنبعته للموديل (الاسم + الوصف + شكل المدخلات JSON Schema)</summary>
public record AiToolDefinition(string Name, string Description, object ParametersSchema);

/// <summary>نتيجة استدعاء الموديل. مابنرميش Exceptions للواجهة.</summary>
public class AiChatResult
{
    public bool Success { get; init; }
    public string? Content { get; init; }
    public IReadOnlyList<AiToolCall> ToolCalls { get; init; } = Array.Empty<AiToolCall>();

    /// <summary>NOT_CONFIGURED | RATE_LIMITED | AUTH_FAILED | TIMEOUT | PROVIDER_ERROR</summary>
    public string? ErrorCode { get; init; }

    /// <summary>رسالة عربية للمستخدم</summary>
    public string? ErrorMessage { get; init; }

    public int PromptTokens { get; init; }

    /// <summary>التوكنز اللي اتقرت من الـ cache (مش بتحسب في حدود الدقيقة)</summary>
    public int CachedPromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens { get; init; }

    public static AiChatResult Fail(string code, string message) =>
        new() { Success = false, ErrorCode = code, ErrorMessage = message };
}
