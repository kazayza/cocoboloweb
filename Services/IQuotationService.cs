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
        int quotationId, decimal initialPaidAmount, int? cashBoxId,
        string paymentMethod, string currentUserName, DateTime? invoiceDate = null);
    
    Task<(string? Reason, DateTime? RejectedAt, string? RejectedBy)> 
    GetRejectionDetailsAsync(int quotationId);
    

    
}
