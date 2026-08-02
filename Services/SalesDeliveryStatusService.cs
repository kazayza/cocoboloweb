using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class SalesDeliveryStatusService : ISalesDeliveryStatusService
{
    private readonly IDbContextFactory<db24804Context> _factory;
    private readonly IAuditService _audit;
    private readonly NotificationService _notify;
    private readonly ILogger<SalesDeliveryStatusService> _logger;

    public SalesDeliveryStatusService(
        IDbContextFactory<db24804Context> factory,
        IAuditService audit,
        NotificationService notify,
        ILogger<SalesDeliveryStatusService> logger)
    {
        _factory = factory;
        _audit = audit;
        _notify = notify;
        _logger = logger;
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
        string dateFilterType,
        string? partyName,
        string? deliveryStatus)
    {
        using var db = await _factory.CreateDbContextAsync();

        var query = db.VwSalesDeliveryStatuses.AsNoTracking().AsQueryable();
        query = ApplyDateFilter(query, dateFrom, dateTo, dateFilterType);

        if (!string.IsNullOrWhiteSpace(partyName))
        {
            var term = partyName.Trim();
            query = query.Where(x => x.PartyName != null && x.PartyName.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(deliveryStatus))
            query = query.Where(x => x.DeliveryStatus == deliveryStatus);

        return await query
            .OrderByDescending(x => x.DueDate ?? x.TransactionDate)
            .ThenByDescending(x => x.TransactionId)
            .ToListAsync();
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
        string dateFilterType,
        string? partyName,
        string? deliveryStatus)
    {
        using var db = await _factory.CreateDbContextAsync();

        var query = db.VwSalesDeliveryStatuses.AsNoTracking().AsQueryable();
        query = ApplyDateFilter(query, dateFrom, dateTo, dateFilterType);

        if (!string.IsNullOrWhiteSpace(partyName))
        {
            var term = partyName.Trim();
            query = query.Where(x => x.PartyName != null && x.PartyName.Contains(term));
        }

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

        var oldSnapshot = new
        {
            transaction.DeliveryEmployeeId,
            transaction.DeliveryEmployeeName,
            transaction.IsDelivered,
            transaction.DeliveredAt,
            transaction.DeliveredNotes
        };

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

            var newSnapshot = new
            {
                transaction.DeliveryEmployeeId,
                transaction.DeliveryEmployeeName,
                transaction.IsDelivered,
                transaction.DeliveredAt,
                transaction.DeliveredNotes,
                RequestedStatus = dto.Status
            };

            await _audit.LogAsync<object>("Transactions", "UpdateDeliveryStatus",
                dto.TransactionId.ToString(), oldSnapshot, newSnapshot, dto.UserName);

            await NotifyDeliveryUpdateAsync(db, transaction, dto);

            return (true, "تم تحديث حالة التسليم بنجاح");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateDeliveryStatusAsync failed for transaction {Id}", dto.TransactionId);
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

    private static IQueryable<VwSalesDeliveryStatus> ApplyDateFilter(
        IQueryable<VwSalesDeliveryStatus> query,
        DateTime? dateFrom,
        DateTime? dateTo,
        string? dateFilterType)
    {
        var filterType = string.IsNullOrWhiteSpace(dateFilterType)
            ? DeliveryDateFilterTypes.DueDate
            : dateFilterType;

        var from = dateFrom?.Date;
        var toExclusive = dateTo?.Date.AddDays(1);

        return filterType switch
        {
            DeliveryDateFilterTypes.InvoiceDate => query
                .Where(x => !from.HasValue || x.TransactionDate >= from.Value)
                .Where(x => !toExclusive.HasValue || x.TransactionDate < toExclusive.Value),

            DeliveryDateFilterTypes.DeliveredDate => query
                .Where(x => !from.HasValue || (x.DeliveredAt.HasValue && x.DeliveredAt.Value >= from.Value))
                .Where(x => !toExclusive.HasValue || (x.DeliveredAt.HasValue && x.DeliveredAt.Value < toExclusive.Value)),

            _ => query
                .Where(x => !from.HasValue || (x.DueDate.HasValue && x.DueDate.Value >= from.Value))
                .Where(x => !toExclusive.HasValue || (x.DueDate.HasValue && x.DueDate.Value < toExclusive.Value))
        };
    }

    private async Task NotifyDeliveryUpdateAsync(db24804Context db, Transaction transaction, DeliveryUpdateDto dto)
    {
        try
        {
            var partyName = await db.Parties.AsNoTracking()
                .Where(p => p.PartyId == transaction.PartyId)
                .Select(p => p.PartyName)
                .FirstOrDefaultAsync() ?? "غير محدد";

            var title = dto.Status switch
            {
                "تم التسليم" => "🚚 تم تأكيد التسليم",
                "متأخر" => "⏰ تحديث: متأخر في التسليم",
                "مرتجع" => "↩️ تحديث: مرتجع",
                _ => "📦 تحديث حالة التسليم"
            };

            var message = $"تم تحديث حالة تسليم الفاتورة {transaction.ReferenceNumber ?? $"#{transaction.TransactionId}"} للعميل {partyName} إلى ({dto.Status}) بواسطة {dto.UserName}" +
                          (string.IsNullOrWhiteSpace(dto.DeliveryEmployeeName) ? string.Empty : $" — مندوب التسليم: {dto.DeliveryEmployeeName}");

            await _notify.NotifyRoleAsync(title, message, SystemRoles.Admin, dto.UserName,
                "sales-delivery-status", "Transactions", transaction.TransactionId);
            await _notify.NotifyRoleAsync(title, message, SystemRoles.AccountManager, dto.UserName,
                "sales-delivery-status", "Transactions", transaction.TransactionId);
            await _notify.NotifyRoleAsync(title, message, SystemRoles.SalesManager, dto.UserName,
                "sales-delivery-status", "Transactions", transaction.TransactionId);

            if (dto.DeliveryEmployeeId.HasValue)
            {
                var recipientUser = await db.Users.AsNoTracking()
                    .Where(u => u.EmployeeId == dto.DeliveryEmployeeId.Value && u.IsActive == true)
                    .Select(u => u.Username)
                    .FirstOrDefaultAsync();

                if (!string.IsNullOrWhiteSpace(recipientUser))
                {
                    await _notify.AddAsync(title, message, recipientUser!, dto.UserName,
                        "sales-delivery-status", "Transactions", transaction.TransactionId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send delivery notification for transaction {Id}", transaction.TransactionId);
        }
    }
}