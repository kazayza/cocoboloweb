using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IQuotationService
{
    // Read
    Task<PagedResult<QuotationListDto>> GetQuotationsAsync(QuotationFilterDto filter);
    Task<QuotationFormDto?> GetQuotationForEditAsync(int quotationId);
    Task<QuotationPrintDto?> GetQuotationForPrintAsync(int quotationId);
    Task<QuotationStatsDto> GetStatsAsync(DateTime? from = null, DateTime? to = null);
    Task<string> GenerateNextQuotationNumberAsync();

    // Write
    Task<(bool Success, string Message, int? QuotationId)> CreateQuotationAsync(
        QuotationFormDto dto, string currentUserName);

    Task<(bool Success, string Message)> UpdateQuotationAsync(
        QuotationFormDto dto, string currentUserName);

    Task<(bool Success, string Message)> ChangeStatusAsync(
        int quotationId, string newStatus, string currentUserName);

    Task<(bool Success, string Message)> DeleteQuotationAsync(
        int quotationId, string currentUserName);

    // ⭐ التحويل لفاتورة (مع المرآة)
    Task<(bool Success, string Message, int? InvoiceId)> ConvertToInvoiceAsync(
        int quotationId, decimal initialPaidAmount, int? cashBoxId,
        string paymentMethod, string currentUserName);
}
