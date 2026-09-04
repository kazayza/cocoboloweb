namespace COCOBOLOERPNEW.Services;

/// <summary>
/// مراحل الخروج الثابتة في CRM (خسارة / غير مهتم) — مصدر واحد للحقيقة.
/// مستخدمة في: OpportunityService، RecoveryService، InteractionService.
/// ⚠️ إن تغيّرت أرقام المراحل في قاعدة البيانات فحدّثها هنا فقط.
/// </summary>
public static class CrmStages
{
    public const int LostStageId = 4;          // خسارة
    public const int NotInterestedStageId = 5; // غير مهتم

    /// <summary>هل المرحلة من مراحل الخروج (نهاية دورة البيع)؟</summary>
    public static bool IsExitStage(int stageId)
        => stageId == LostStageId || stageId == NotInterestedStageId;
}