using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IAdditionalChargeService
{
    Task<PagedResult<AdditionalChargeListDto>> GetChargesAsync(AdditionalChargeFilterDto filter);
    Task<AdditionalChargeStatsDto> GetStatsAsync(AdditionalChargeFilterDto? filter = null);
    Task<AdditionalChargeFormDto?> GetChargeForEditAsync(int chargeId);
    Task<AdditionalChargeReceiptDto?> GetChargeReceiptAsync(int chargeId);
    Task<(bool Success, string Message, int? ChargeId)> CreateChargeAsync(AdditionalChargeFormDto dto, string currentUserName);
    Task<(bool Success, string Message)> UpdateChargeAsync(int chargeId, AdditionalChargeFormDto dto, string currentUserName);
    Task<(bool Success, string Message)> DeleteChargeAsync(int chargeId, string currentUserName);
    Task<(bool Success, string Message)> ApplyToInvoiceAsync(int chargeId, int transactionId, string currentUserName);
    Task<(bool Success, string Message)> MarkAsNonRefundableAsync(int chargeId, string reason, string currentUserName);

    // ─── رسوم تعديل العميل المعلّقة (القسم 11.4) ───────────
    /// <summary>اعتماد رسم تعديل معلّق وتسعيره نهائيًا — المدير ومدير الحسابات فقط، وبلا مبلغ نهائي لا اعتماد</summary>
    Task<(bool Success, string Message)> ApproveModificationChargeAsync(int chargeId, decimal? amount, string currentUserName);
    /// <summary>رفض رسم تعديل معلّق بسبب إلزامي — يُلغي سجل صيانة التعديل المرتبط، وبلا حركة خزينة أبدًا</summary>
    Task<(bool Success, string Message)> RejectModificationChargeAsync(int chargeId, string reason, string currentUserName);
    /// <summary>تحصيل رسم تعديل معتمَد — حركة خزينة «قبض» بنفس بناء الإنشاء، والحالة تصبح Paid</summary>
    Task<(bool Success, string Message)> CollectModificationChargeAsync(int chargeId, int? cashBoxId, string currentUserName);
}