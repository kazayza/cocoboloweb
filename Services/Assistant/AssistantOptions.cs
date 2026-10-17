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

    /// <summary>ميزانية التوكنز اليومية لكل مستخدم (0 = من غير حد للمستخدم). الـ Admin معفي</summary>
    public int PerUserDailyTokenBudget { get; set; } = 60000;

    /// <summary>ميزانية التوكنز اليومية لكل الحساب (Groq المجاني = 200,000 في اليوم). 0 = من غير حماية</summary>
    public int DailyTokenBudget { get; set; } = 180000;

    /// <summary>
    /// لو true: بنسجل نص السؤال كامل في جدول المراجعة.
    /// لو false: بنسجل الرد بس، والسؤال بيتسجل NULL (أكتر خصوصية).
    /// </summary>
    public bool StoreQuestionText { get; set; } = true;

    /// <summary>يختصر أوصاف المعاملات في الأدوات قبل الإرسال للموديل (بيوفر توكنز كل سؤال)</summary>
    public bool CompactToolSchemas { get; set; } = true;

    /// <summary>الملخص الصباحي: إشعار واحد لكل مستخدم كل يوم حسب صلاحياته</summary>
    public bool DigestEnabled { get; set; } = true;

    /// <summary>الساعة (بتوقيت القاهرة) اللي بعدها الملخص بيتبعت</summary>
    public int DigestHour { get; set; } = 8;
}
