namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// إعدادات زكي. تتقري من قسم "Assistant" في الإعدادات.
/// المفتاح GroqApiKey ماينفعش يتحط في appsettings المتتبعة في Git،
/// لازم يتحط في User Secrets (على جهازك) أو على السيرفر.
/// </summary>
public class AssistantOptions
{
    public string Provider { get; set; } = "Groq";
    public string Model { get; set; } = "openai/gpt-oss-120b";
    public string GroqApiKey { get; set; } = "";
    public double Temperature { get; set; } = 0.2;
    public int DailyQuestionLimitPerUser { get; set; } = 30;

    /// <summary>
    /// لو true: بنسجل نص السؤال كامل في جدول المراجعة.
    /// لو false: بنسجل الرد بس، والسؤال بيتسجل NULL (أكتر خصوصية).
    /// </summary>
    public bool StoreQuestionText { get; set; } = true;
}
