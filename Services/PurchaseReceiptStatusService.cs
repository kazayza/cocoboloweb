using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class PurchaseReceiptStatusService : IPurchaseReceiptStatusService
{
    private readonly IDbContextFactory<db24804Context> _factory;

    public PurchaseReceiptStatusService(IDbContextFactory<db24804Context> factory)
    {
        _factory = factory;
    }

    public async Task<List<PurchaseReceiptListDto>> GetFilteredAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        string dateFilterType,
        string? searchText,
        string? receiptStatus)
    {
        using var db = await _factory.CreateDbContextAsync();

        var query = BuildBaseQuery(db);
        query = ApplyDateFilter(query, dateFrom, dateTo, dateFilterType);

        var term = string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim();

        var items = await query
            .OrderByDescending(t => t.DueDate ?? t.TransactionDate)
            .ThenByDescending(t => t.TransactionId)
            .Select(t => new PurchaseReceiptListDto
            {
                TransactionId = t.TransactionId,
                ReferenceNumber = t.ReferenceNumber,
                TransactionDate = t.TransactionDate,
                SupplierId = t.PartyId,
                SupplierName = db.Parties.Where(p => p.PartyId == t.PartyId).Select(p => p.PartyName).FirstOrDefault() ?? "المصنع",
                WarehouseId = t.WarehouseId,
                WarehouseName = db.Warehouses.Where(w => w.WarehouseId == t.WarehouseId).Select(w => w.WarehouseName).FirstOrDefault() ?? "",
                DueDate = t.DueDate,
                IsDelivered = t.IsDelivered == true,
                DeliveredAt = t.DeliveredAt,
                DeliveredNotes = t.DeliveredNotes,
                GrandTotal = t.GrandTotal,
                PaidAmount = t.PaidAmount,
                ItemsCount = db.TransactionDetails.Count(d => d.TransactionId == t.TransactionId)
            })
            .ToListAsync();

        foreach (var item in items)
        {
            item.ReceiptStatus = GetReceiptStatus(item.IsDelivered, item.DueDate);
            item.DaysRemaining = item.IsDelivered || !item.DueDate.HasValue
                ? null
                : (item.DueDate.Value.Date - DateTime.Today).Days;
        }

        if (!string.IsNullOrWhiteSpace(receiptStatus))
            items = items.Where(x => x.ReceiptStatus == receiptStatus).ToList();

        return items;
    }

    public async Task<PurchaseReceiptSummaryDto> GetSummaryAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        string dateFilterType,
        string? searchText,
        string? receiptStatus)
    {
        var items = await GetFilteredAsync(dateFrom, dateTo, dateFilterType, searchText, receiptStatus);

        return new PurchaseReceiptSummaryDto
        {
            TotalCount = items.Count,
            PendingCount = items.Count(x => x.ReceiptStatus == PurchaseReceiptStatusNames.Pending),
            ReceivedCount = items.Count(x => x.ReceiptStatus == PurchaseReceiptStatusNames.Received),
            OverdueCount = items.Count(x => x.ReceiptStatus == PurchaseReceiptStatusNames.Overdue),
            TotalGrandTotal = items.Sum(x => x.GrandTotal),
            TotalPaidAmount = items.Sum(x => x.PaidAmount)
        };
    }

    private static IQueryable<Transaction> BuildBaseQuery(db24804Context db)
    {
        return db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Purchase)
            .Where(t => t.ReferenceType == "PurchaseInvoice")
            .Where(t => t.InvoiceStatus != InvoiceStatuses.Cancelled);
    }

    private static IQueryable<Transaction> ApplyDateFilter(
        IQueryable<Transaction> query,
        DateTime? dateFrom,
        DateTime? dateTo,
        string? dateFilterType)
    {
        var filterType = string.IsNullOrWhiteSpace(dateFilterType)
            ? PurchaseReceiptDateFilterTypes.DueDate
            : dateFilterType;

        var from = dateFrom?.Date;
        var toExclusive = dateTo?.Date.AddDays(1);

        return filterType switch
        {
            PurchaseReceiptDateFilterTypes.InvoiceDate => query
                .Where(x => !from.HasValue || x.TransactionDate >= from.Value)
                .Where(x => !toExclusive.HasValue || x.TransactionDate < toExclusive.Value),

            PurchaseReceiptDateFilterTypes.ReceivedDate => query
                .Where(x => !from.HasValue || (x.DeliveredAt.HasValue && x.DeliveredAt.Value >= from.Value))
                .Where(x => !toExclusive.HasValue || (x.DeliveredAt.HasValue && x.DeliveredAt.Value < toExclusive.Value)),

            _ => query
                .Where(x => !from.HasValue || (x.DueDate.HasValue && x.DueDate.Value >= from.Value))
                .Where(x => !toExclusive.HasValue || (x.DueDate.HasValue && x.DueDate.Value < toExclusive.Value))
        };
    }

    private static string GetReceiptStatus(bool isDelivered, DateTime? dueDate)
    {
        if (isDelivered)
            return PurchaseReceiptStatusNames.Received;

        if (dueDate.HasValue && dueDate.Value.Date < DateTime.Today)
            return PurchaseReceiptStatusNames.Overdue;

        return PurchaseReceiptStatusNames.Pending;
    }
}
