using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IPaymentService
{
    // Read
    Task<PagedResult<PaymentListDto>> GetPaymentsAsync(PaymentFilterDto filter);
    Task<PaymentListDto?> GetPaymentByIdAsync(int paymentId);
    Task<PaymentReceiptDto?> GetPaymentForReceiptAsync(int paymentId);
    Task<List<PaymentHistoryDto>> GetPaymentHistoryAsync(int transactionId);
    Task<PartyBalanceDto> GetPartyBalanceAsync(int partyId, string transactionType);
    Task<IEnumerable<PartyLookupDto>> SearchPartiesAsync(string search);
    Task<PaymentStatsDto> GetStatsAsync(string transactionType, DateTime? from = null, DateTime? to = null);

    // Analysis
    Task<PaymentAnalysisDto?> GetPaymentAnalysisAsync(int transactionId, decimal? proposedAmount = null);

    // Lookups
    Task<List<InvoiceLookupDto>> SearchInvoicesAsync(
        string transactionType, string? search, bool onlyWithRemaining = true, int max = 20);

    // Write
    Task<(bool Success, string Message, int? PaymentId)> CreatePaymentAsync(
        PaymentFormDto dto, string currentUserName);

    Task<(bool Success, string Message)> CancelPaymentAsync(
        int paymentId, string reason, string currentUserName);
    //  Edit
    Task<PaymentFormDto?> GetPaymentForEditAsync(int paymentId);
    Task<(bool Success, string Message)> UpdatePaymentAsync(
        int paymentId, PaymentFormDto dto, string currentUserName);

    // Helper
    string ConvertNumberToArabicWords(decimal number);
    string GenerateReceiptNumber(int paymentId, string transactionType, DateTime date);
}