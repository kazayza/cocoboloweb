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

    /// <summary>
    /// نفس CompleteAsync بس بيبثّ محتوى الرد أثناء التوليد:
    /// onPartial بيستقبل النص المتراكم لحد دلوقتي في الاستدعاء ده (مش القطعة بس).
    /// التنفيذ الافتراضي بيكمّل عادي من غير بثّ — أي مزوّد جديد مش لازم ينفّذه.
    /// </summary>
    Task<AiChatResult> CompleteStreamingAsync(
        IReadOnlyList<AiMessage> messages,
        IReadOnlyList<AiToolDefinition>? tools,
        Action<string> onPartial,
        CancellationToken ct = default) =>
        CompleteAsync(messages, tools, ct);
}
