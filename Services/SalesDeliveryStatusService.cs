using COCOBOLOERPNEW.DTOs;
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

    public async Task<List<VwSalesDeliveryStatus>> GetAllAsync()
    {
        using var db = await _factory.CreateDbContextAsync();
        return await db.VwSalesDeliveryStatuses
            .AsNoTracking()
            .OrderByDescending(x => x.TransactionDate)
            .ToListAsync();
    }

    public async Task<List<VwSalesDeliveryStatus>> GetFilteredAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        string? partyName,
        string? deliveryStatus)
    {
        using var db = await _factory.CreateDbContextAsync();

        var query = db.VwSalesDeliveryStatuses.AsNoTracking().AsQueryable();

        if (dateFrom.HasValue)
            query = query.Where(x => x.TransactionDate >= dateFrom.Value.Date);

        if (dateTo.HasValue)
            query = query.Where(x => x.TransactionDate <= dateTo.Value.Date.AddDays(1).AddTicks(-1));

        if (!string.IsNullOrWhiteSpace(partyName))
            query = query.Where(x => x.PartyName != null && x.PartyName.Contains(partyName.Trim()));

        if (!string.IsNullOrWhiteSpace(deliveryStatus))
            query = query.Where(x => x.DeliveryStatus == deliveryStatus);

        return await query.OrderByDescending(x => x.TransactionDate).ToListAsync();
    }

    public async Task<VwSalesDeliveryStatus?> GetByTransactionIdAsync(int transactionId)
    {
        using var db = await _factory.CreateDbContextAsync();
        return await db.VwSalesDeliveryStatuses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TransactionId == transactionId);
    }

    public async Task<DeliverySummaryDto> GetSummaryAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        string? partyName,
        string? deliveryStatus)
    {
        using var db = await _factory.CreateDbContextAsync();

        var query = db.VwSalesDeliveryStatuses.AsNoTracking().AsQueryable();

        if (dateFrom.HasValue)
            query = query.Where(x => x.TransactionDate >= dateFrom.Value.Date);

        if (dateTo.HasValue)
            query = query.Where(x => x.TransactionDate <= dateTo.Value.Date.AddDays(1).AddTicks(-1));

        if (!string.IsNullOrWhiteSpace(partyName))
            query = query.Where(x => x.PartyName != null && x.PartyName.Contains(partyName.Trim()));

        if (!string.IsNullOrWhiteSpace(deliveryStatus))
            query = query.Where(x => x.DeliveryStatus == deliveryStatus);

        var data = await query.ToListAsync();

        return new DeliverySummaryDto
        {
            TotalCount     = data.Count,
            PendingCount   = data.Count(x => x.DeliveryStatus == "جارى"),
            DeliveredCount = data.Count(x => x.DeliveryStatus == "تم التسليم"),
            OverdueCount   = data.Count(x => x.DeliveryStatus == "متأخر"),
            ReturnedCount  = data.Count(x => x.DeliveryStatus == "مرتجع"),
            TotalGrandTotal = data.Sum(x => x.GrandTotal),
            TotalPaidAmount = data.Sum(x => x.PaidAmount)
        };
    }

    public async Task<DeliveryDetailDto?> GetDeliveryDetailsAsync(int transactionId)
    {
        using var db = await _factory.CreateDbContextAsync();

        var transaction = await db.VwSalesDeliveryStatuses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TransactionId == transactionId);

        if (transaction == null) return null;

        var party = await db.Parties
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PartyId == transaction.PartyId);

        var products = await db.TransactionDetails
            .AsNoTracking()
            .Where(td => td.TransactionId == transactionId)
            .Select(td => new DeliveryProductDto
            {
                ProductId = td.ProductId,
                ProductName = td.Product != null ? td.Product.ProductName : "",
                Quantity = td.Quantity,           // ✅ FIX
                UnitPrice = td.UnitPrice,
                TotalAmount = td.TotalAmount ?? 0      // ✅ FIX
            })
            .ToListAsync();

        return new DeliveryDetailDto
        {
            TransactionId = transaction.TransactionId,
            TransactionDate = transaction.TransactionDate,
            DueDate = transaction.DueDate,
            TransactionType = transaction.TransactionType,
            PartyId = transaction.PartyId,
            PartyName = transaction.PartyName,
            PartyPhone = party?.Phone,
            PartyAddress = party?.Address,
            SalesEmployeeId = transaction.EmpId,
            SalesEmployeeName = transaction.EmployeeName,
            DeliveryEmployeeId = transaction.DeliveryEmployeeId,
            DeliveryEmployeeName = transaction.DeliveryEmployeeName,
            DeliveryStatus = transaction.DeliveryStatus,
            DeliveredAt = transaction.DeliveredAt,
            DeliveredNotes = transaction.DeliveredNotes,
            GrandTotal = transaction.GrandTotal,
            PaidAmount = transaction.PaidAmount,
            DaysRemaining = transaction.DaysRemaining,
            Products = products
        };
    }

    public async Task<(bool Success, string Message)> UpdateDeliveryStatusAsync(
        DeliveryUpdateDto dto)
    {
        using var db = await _factory.CreateDbContextAsync();

        var transaction = await db.Transactions
            .FirstOrDefaultAsync(t => t.TransactionId == dto.TransactionId);

        if (transaction == null)
            return (false, "الفاتورة غير موجودة");

        try
        {
            transaction.DeliveryEmployeeName = dto.DeliveryEmployeeName;
            transaction.DeliveryEmployeeId   = dto.DeliveryEmployeeId;
            
            if (dto.Status == "تم التسليم")
            {
                transaction.IsDelivered = true;
                transaction.DeliveredAt = dto.DeliveredAt ?? DateTime.Now;
            }
            else
            {
                transaction.IsDelivered = false;
                transaction.DeliveredAt = null;
            }
            
            transaction.DeliveredNotes = dto.Notes;

            await db.SaveChangesAsync();

            return (true, "تم تحديث حالة التسليم بنجاح");
        }
        catch (Exception ex)
        {
            return (false, $"حدث خطأ: {ex.Message}");
        }
    }

    public async Task<List<EmployeeLookupDto>> GetDeliveryEmployeesAsync()
    {
        using var db = await _factory.CreateDbContextAsync();

        return await db.Employees
            .AsNoTracking()
            .Where(e => e.Status == "نشط" || e.Status == "Working" || e.Status == "Active")
            .OrderBy(e => e.FullName)
            .Select(e => new EmployeeLookupDto
            {
                EmployeeId = e.EmployeeId,
                FullName = e.FullName,
                MobilePhone = e.MobilePhone,
                JobTitle = e.JobTitle
            })
            .ToListAsync();
    }

    public async Task<byte[]> GenerateDeliveryPdfAsync(int transactionId)
    {
        var details = await GetDeliveryDetailsAsync(transactionId);
        if (details == null)
            throw new Exception("التسليم غير موجود");

        // TODO: QuestPDF Implementation
        return Array.Empty<byte>();
    }
}