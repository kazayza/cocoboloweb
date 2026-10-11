namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// الواجهة اللي بيتكلم بيها زكي مع أي موديل.
/// تغيير المزود (Groq / Gemini / OpenAI) = تنفيذ جديد للواجهة دي بس.
/// </summary>
public interface IAiProvider
{
    Task<AiChatResult> CompleteAsync(
        IReadOnlyList<AiMessage> messages,
        IReadOnlyList<AiToolDefinition>? tools = null,
        CancellationToken ct = default);
}
