using COCOBOLOERPNEW.Models;

namespace COCOBOLOERPNEW.Services;

public interface ISalesDeliveryStatusService
{
    Task<List<VwSalesDeliveryStatus>> GetAllAsync();

    Task<List<VwSalesDeliveryStatus>> GetFilteredAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        string? partyName,
        string? deliveryStatus);

    Task<VwSalesDeliveryStatus?> GetByTransactionIdAsync(int transactionId);

    Task<DeliveryStatusSummary> GetSummaryAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        string? partyName,
        string? deliveryStatus);
}

public class DeliveryStatusSummary
{
    public int TotalCount { get; set; }
    public int PendingCount { get; set; }      // جارى
    public int OverdueCount { get; set; }      // متأخر
    public decimal TotalGrandTotal { get; set; }
    public decimal TotalPaidAmount { get; set; }
    public decimal TotalRemaining => TotalGrandTotal - TotalPaidAmount;
}