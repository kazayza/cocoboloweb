using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

// ═══════════════════════════════════════════════════════════
// نظام جزاءات الموظفين — مرآة وحدة الـ API
// (modules/employee-penalties) في السلطة والحالات والأرقام:
// الولادة بمستويين (توقيع مباشر ⇒ معتمد فورًا، وإلا معلق)،
// منع الاعتماد الذاتي، قاعدة التجاوز للشهر المفتوح، وقيمة
// اليوم من محرك الرواتب نفسه (PayrollService.GetWorkingDaysAsync).
// كل الحراس داخل الخدمة — الواجهة تعرض فقط، والخادم يحكم.
// ═══════════════════════════════════════════════════════════
public interface IEmployeePenaltyService
{
    // سياق الفاعل (مرآة req.auth): أدوار + مطالبة توقيع مباشر + ربط الموظف
    Task<PenaltyActorDto> GetActorAsync(string userName);

    // قائمة مرشحة بنطاق رؤية الفاعل + إحصائيات بنفس النطاق
    Task<PagedResult<PenaltyListDto>> GetPenaltiesAsync(PenaltyFilterDto filter, string userName);
    Task<PenaltyStatsDto>             GetStatsAsync(PenaltyFilterDto filter, string userName);

    // معاينة حية لنموذج الإنشاء (أيام): أيام العمل + قيمة اليوم + المبلغ عند الشهر النهائي
    Task<PenaltyPreviewDto> GetPreviewAsync(int employeeId, string month, decimal days, string userName);

    // عمليات دورة الحياة
    Task<PenaltyMutationResult> CreateAsync(PenaltyCreateDto dto, string userName);
    Task<PenaltyMutationResult> ApproveAsync(int penaltyId, string userName);
    Task<PenaltyMutationResult> RejectAsync(int penaltyId, string userName);
    Task<PenaltyMutationResult> CancelAsync(int penaltyId, string userName);
}
