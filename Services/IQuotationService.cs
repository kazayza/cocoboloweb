using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IQuotationService
{
    // Read
    Task<PagedResult<QuotationListDto>> GetQuotationsAsync(QuotationFilterDto filter);
    Task<QuotationFormDto?> GetQuotationForEditAsync(int quotationId);
    Task<QuotationFormDto?> GetQuotationPublicAsync(int quotationId);
    Task<QuotationPrintDto?> GetQuotationForPrintAsync(int quotationId);
    Task<QuotationStatsDto> GetStatsAsync(DateTime? from = null, DateTime? to = null);
    Task<QuotationStatsDto> GetStatsAsync(QuotationFilterDto filter);
    Task<string> GenerateNextQuotationNumberAsync();
    Task<bool> SaveRejectionReasonAsync(int quotationId, string reason);

    // Write
    Task<(bool Success, string Message, int? QuotationId)> CreateQuotationAsync(
        QuotationFormDto dto, string currentUserName);

    Task<(bool Success, string Message)> UpdateQuotationAsync(
        QuotationFormDto dto, string currentUserName);

   Task<(bool Success, string Message)> ChangeStatusAsync(
    int quotationId, string newStatus, string currentUserName, bool isPublic = false);

    Task<(bool Success, string Message)> DeleteQuotationAsync(
        int quotationId, string currentUserName);

    // التحويل لفاتورة (مع المرآة)
    Task<(bool Success, string Message, int? InvoiceId)> ConvertToInvoiceAsync(
        int quotationId, List<int>? selectedAdvanceChargeIds,
        string currentUserName, DateTime? invoiceDate = null);
    
    Task<(string? Reason, DateTime? RejectedAt, string? RejectedBy)> 
    GetRejectionDetailsAsync(int quotationId);

    Task<(DateTime? AcceptedAt, string? AcceptedBy)> GetAcceptanceDetailsAsync(int quotationId);

    Task<(bool Success, string Message)> SendDiscountRequestAsync(
        QuotationDiscountRequestDto dto, string currentUserName);

    // ⭐ تعديل تكلفة أصناف العرض بواسطة دور المصنع (يكتب أسعار شراء المنتجات + PriceHistory)
    Task<(bool Success, string Message, List<string> ChangedProducts)> SaveQuotationItemCostsAsync(
        int quotationId, List<QuotationItemCostUpdateDto> updates, string currentUserName);

    // ⭐ طلب تسعير: إشعار لدور المصنع بالذهاب لعرض السعر وتحديث تكاليفه
    Task<(bool Success, string Message)> SendPricingRequestAsync(
        int quotationId, string? note, string currentUserName);
}
