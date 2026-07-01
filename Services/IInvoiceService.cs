using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IInvoiceService
{
    // قراءة
    Task<PagedResult<InvoiceListDto>> GetInvoicesAsync(InvoiceFilterDto filter);
    Task<InvoiceDetailsDto?> GetInvoiceDetailsAsync(int transactionId);
    Task<InvoiceFormDto?> GetInvoiceForEditAsync(int transactionId);
    Task<InvoicePrintDto?> GetInvoiceForPrintAsync(int transactionId);
    Task<InvoiceStatsDto> GetStatsAsync(DateTime? from = null, DateTime? to = null, string transactionType = "Sale");
    Task<string> GenerateNextInvoiceNumberAsync(string transactionType = "Sale");

    // كتابة
    Task<(bool Success, string Message, int? TransactionId, int? MirrorTransactionId)> CreateInvoiceAsync(
        InvoiceFormDto dto, string currentUserName);

    Task<(bool Success, string Message)> UpdateInvoiceAsync(
        InvoiceFormDto dto, string currentUserName);

    Task<(bool Success, string Message)> RequestInvoiceEditAsync(
        int transactionId, string reason, string currentUserName);

    Task<(bool Success, string Message)> ProcessInvoiceEditRequestAsync(
        int transactionId, bool approve, string? notes, string currentUserName);

    Task<(bool Success, string Message)> CancelInvoiceAsync(
        int transactionId, string reason, string currentUserName);

    Task<(bool Success, string Message)> PermanentlyDeleteInvoiceAsync(
        int transactionId, string currentUserName);

    Task<(bool Success, string Message)> AddPaymentAsync(
        int transactionId, decimal amount, string method, int? cashBoxId,
        string? notes, string currentUserName);

    // Lookups
    Task<List<PartyLookupDto>> SearchPartiesAsync(string? search, int max = 20);

    // ⭐ منتجات عميل معين فقط
    Task<List<ProductLookupDto>> SearchProductsForPartyAsync(int partyId, string? search, int max = 50);

    Task<List<Models.Warehouse>> GetWarehousesAsync();
    Task<List<Models.CashBox>> GetCashBoxesAsync();

    // ⭐ Helper - جلب EmployeeId من اسم المستخدم
    Task<int?> GetEmployeeIdByUserNameAsync(string userName);
    Task<string?> GetEmployeeNameByIdAsync(int employeeId);

    // الدفعات المقدمة
    Task<List<CustomerAdvanceDto>> GetCustomerAdvancesAsync(int partyId, bool unappliedOnly = true);
    Task<decimal> GetCustomerAdvanceBalanceAsync(int partyId);

    Task<(bool Success, string Message, int? ChargeId)> AddCustomerAdvanceAsync(
        int partyId, decimal amount, string description, string? notes, string currentUserName);

    Task<(bool Success, string Message)> DeleteCustomerAdvanceAsync(
        int chargeId, string currentUserName);
}
