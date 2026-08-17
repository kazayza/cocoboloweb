using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IPurchaseReceiptStatusService
{
    Task<List<PurchaseReceiptListDto>> GetFilteredAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        string dateFilterType,
        string? searchText,
        string? receiptStatus);

    Task<PurchaseReceiptSummaryDto> GetSummaryAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        string dateFilterType,
        string? searchText,
        string? receiptStatus);
}
