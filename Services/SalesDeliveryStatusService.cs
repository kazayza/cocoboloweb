using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class SalesDeliveryStatusService : ISalesDeliveryStatusService
{
    private readonly IDbContextFactory<db24804Context> _factory;

    public SalesDeliveryStatusService(IDbContextFactory<db24804Context> factory)
    {
        _factory = factory;
    }

    // ──────────────────────────────────────────────
    // جلب الكل
    // ──────────────────────────────────────────────
    public async Task<List<VwSalesDeliveryStatus>> GetAllAsync()
    {
        using var db = await _factory.CreateDbContextAsync();

        return await db.VwSalesDeliveryStatuses
            .AsNoTracking()
            .Where(x => x.TransactionType == "Sale")
            .OrderByDescending(x => x.TransactionDate)
            .ToListAsync();
    }

    // ──────────────────────────────────────────────
    // جلب مع فلترة
    // ──────────────────────────────────────────────
    public async Task<List<VwSalesDeliveryStatus>> GetFilteredAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        string? partyName,
        string? deliveryStatus)
    {
        using var db = await _factory.CreateDbContextAsync();

        var query = db.VwSalesDeliveryStatuses
            .AsNoTracking()
            .Where(x => x.TransactionType == "Sale")
            .AsQueryable();

        // فلتر التاريخ من
        if (dateFrom.HasValue)
            query = query.Where(x => x.TransactionDate >= dateFrom.Value.Date);

        // فلتر التاريخ إلى
        if (dateTo.HasValue)
            query = query.Where(x => x.TransactionDate <= dateTo.Value.Date.AddDays(1).AddTicks(-1));

        // فلتر اسم العميل
        if (!string.IsNullOrWhiteSpace(partyName))
            query = query.Where(x => x.PartyName.Contains(partyName.Trim()));

        // فلتر حالة التسليم
        if (!string.IsNullOrWhiteSpace(deliveryStatus))
            query = query.Where(x => x.DeliveryStatus == deliveryStatus);

        return await query
            .OrderByDescending(x => x.TransactionDate)
            .ToListAsync();
    }

    // ──────────────────────────────────────────────
    // جلب عملية واحدة بالـ ID
    // ──────────────────────────────────────────────
    public async Task<VwSalesDeliveryStatus?> GetByTransactionIdAsync(int transactionId)
    {
        using var db = await _factory.CreateDbContextAsync();

        return await db.VwSalesDeliveryStatuses
            .AsNoTracking()
            .Where(x => x.TransactionType == "Sale")
            .FirstOrDefaultAsync(x => x.TransactionId == transactionId);
    }

    // ──────────────────────────────────────────────
    // إحصائيات الملخص
    // ──────────────────────────────────────────────
    public async Task<DeliveryStatusSummary> GetSummaryAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        string? partyName,
        string? deliveryStatus)
    {
        using var db = await _factory.CreateDbContextAsync();

        var query = db.VwSalesDeliveryStatuses
            .AsNoTracking()
            .Where(x => x.TransactionType == "Sale")
            .AsQueryable();

        // نفس الفلاتر
        if (dateFrom.HasValue)
            query = query.Where(x => x.TransactionDate >= dateFrom.Value.Date);

        if (dateTo.HasValue)
            query = query.Where(x => x.TransactionDate <= dateTo.Value.Date.AddDays(1).AddTicks(-1));

        if (!string.IsNullOrWhiteSpace(partyName))
            query = query.Where(x => x.PartyName.Contains(partyName.Trim()));

        if (!string.IsNullOrWhiteSpace(deliveryStatus))
            query = query.Where(x => x.DeliveryStatus == deliveryStatus);

        var data = await query.ToListAsync();

        return new DeliveryStatusSummary
        {
            TotalCount     = data.Count,
            PendingCount   = data.Count(x => x.DeliveryStatus == "جارى"),
            OverdueCount   = data.Count(x => x.DeliveryStatus == "متأخر"),
            TotalGrandTotal = data.Sum(x => x.GrandTotal),
            TotalPaidAmount = data.Sum(x => x.PaidAmount)
        };
    }
}