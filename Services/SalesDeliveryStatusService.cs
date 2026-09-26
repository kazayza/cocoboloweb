using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class SalesDeliveryStatusService : ISalesDeliveryStatusService
{
    private const string ActionDeliveryPermissionRequest = "DeliveryPermissionRequest";
    private const string ActionDeliveryPermissionGranted = "DeliveryPermissionGranted";

    private readonly IDbContextFactory<db24804Context> _factory;
    private readonly IAuditService _audit;
    private readonly NotificationService _notify;
    private readonly ILogger<SalesDeliveryStatusService> _logger;
    private readonly IHttpContextAccessor _http;

    public SalesDeliveryStatusService(
        IDbContextFactory<db24804Context> factory,
        IAuditService audit,
        NotificationService notify,
        ILogger<SalesDeliveryStatusService> logger,
        IHttpContextAccessor http)
    {
        _factory = factory;
        _audit = audit;
        _notify = notify;
        _logger = logger;
        _http = http;
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

    // ⭐ جلب الموظف (كود + اسم) من اسم المستخدم — Users.EmployeeId → Employees.FullName
    public async Task<(int? EmployeeId, string? FullName)> GetEmployeeByUserNameAsync(string userName)
    {
        using var db = await _factory.CreateDbContextAsync();

        var empId = await db.Users.AsNoTracking()
            .Where(u => u.Username == userName && u.EmployeeId.HasValue)
            .Select(u => u.EmployeeId)
            .FirstOrDefaultAsync();

        if (empId == null) return (null, null);

        var fullName = await db.Employees.AsNoTracking()
            .Where(e => e.EmployeeId == empId.Value)
            .Select(e => e.FullName)
            .FirstOrDefaultAsync();

        return (empId, fullName);
    }

    public async Task<(bool Success, string Message)> UpdateDeliveryStatusAsync(
        DeliveryUpdateDto dto)
    {
        using var db = await _factory.CreateDbContextAsync();

        var transaction = await db.Transactions
            .FirstOrDefaultAsync(t => t.TransactionId == dto.TransactionId);

        if (transaction == null)
            return (false, "الفاتورة غير موجودة");

        // 🚫 لا تسليم لفاتورة ملغية
        if (transaction.InvoiceStatus == InvoiceStatuses.Cancelled)
            return (false, "لا يمكن تسليم فاتورة ملغية.");

        // 🔒 منع التسليم وجود مبالغ متبقية — إلا بموافقة المدير/مدير الحسابات أو بإذن ممنوح
        string? approvalNote = null;
        if (dto.Status == "تم التسليم")
        {
            var remaining = transaction.GrandTotal - transaction.PaidAmount;
            if (remaining > 0)
            {
                var httpUser = _http.HttpContext?.User;
                var isApprover = httpUser != null && DeliveryPermissions.CanOverrideRemaining(httpUser);

                if (isApprover)
                {
                    approvalNote = $" — تم التسليم بموافقة {dto.UserName} رغم وجود مبالغ متبقية";
                }
                else
                {
                    var grant = await db.AuditLogs.AsNoTracking()
                        .Where(a => a.TableName == "Transactions"
                                 && a.ActionType == ActionDeliveryPermissionGranted
                                 && a.PrimaryKeyValue == dto.TransactionId.ToString())
                        .OrderByDescending(a => a.AuditId)
                        .FirstOrDefaultAsync();

                    if (grant == null)
                        return (false, "لا يمكن التسليم — يوجد مبالغ متبقية على هذه الفاتورة. برجاء مراجعة مدير الحسابات أو مسئول الحسابات أو المدير.");

                    approvalNote = $" — تم التسليم بموافقة {grant.LoginName} رغم وجود مبالغ متبقية";
                }
            }
        }

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
            // ⭐ إصلاح باج: لو الـdto جاي من غير كود لكن بنفس اسم المندوب المسجل — نحتفظ بالكود القديم ولا يُمسح
            var keepEmployeeId = dto.DeliveryEmployeeId
                ?? (transaction.DeliveryEmployeeId != null
                    && !string.IsNullOrWhiteSpace(dto.DeliveryEmployeeName)
                    && dto.DeliveryEmployeeName == transaction.DeliveryEmployeeName
                    ? transaction.DeliveryEmployeeId
                    : null);

            transaction.DeliveryEmployeeName = dto.DeliveryEmployeeName;
            transaction.DeliveryEmployeeId   = keepEmployeeId;

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
            if (approvalNote != null)
                transaction.DeliveredNotes = ((transaction.DeliveredNotes ?? "").TrimEnd() + approvalNote).Trim();

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

    // ════════════════════════════════════════════════════════
    //        إذن التسليم لفواتير عليها مبالغ متبقية
    // ════════════════════════════════════════════════════════

    public async Task<(bool Success, string Message)> RequestDeliveryPermissionAsync(
        int transactionId, string requestedBy)
    {
        using var db = await _factory.CreateDbContextAsync();

        var t = await db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TransactionId == transactionId);
        if (t == null) return (false, "الفاتورة غير موجودة.");
        if (t.InvoiceStatus == InvoiceStatuses.Cancelled)
            return (false, "لا يمكن طلب إذن تسليم لفاتورة ملغية.");
        if (t.IsDelivered == true)
            return (false, "الفاتورة مسلَّمة بالفعل — لا حاجة لإذن.");
        if (t.GrandTotal - t.PaidAmount <= 0)
            return (false, "لا توجد مبالغ متبقية على هذه الفاتورة — يمكنك التسليم مباشرة.");

        // منع تكرار الطلب المعلق
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.TableName == "Transactions"
                     && a.PrimaryKeyValue == transactionId.ToString()
                     && (a.ActionType == ActionDeliveryPermissionRequest
                      || a.ActionType == ActionDeliveryPermissionGranted))
            .OrderBy(a => a.AuditId)
            .Select(a => new { a.AuditId, a.ActionType })
            .ToListAsync();

        var last = rows.LastOrDefault();
        if (last != null && last.ActionType == ActionDeliveryPermissionRequest)
            return (false, "يوجد طلب إذن معلق بالفعل لهذه الفاتورة — بانتظار موافقة الإدارة.");

        await _audit.LogAsync<object>("Transactions", ActionDeliveryPermissionRequest,
            transactionId.ToString(), null,
            new { Note = "طلب إذن تسليم فاتورة عليها مبالغ متبقية" }, requestedBy);

        var v = await db.VwSalesDeliveryStatuses.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TransactionId == transactionId);
        var partyName = v?.PartyName ?? $"#{transactionId}";

        var msg = $"الفاتورة رقم {transactionId} — العميل {partyName} — عليها مبالغ متبقية. " +
                  $"طلب إذن تسليم من: {requestedBy}";

        await _notify.NotifyRoleAsync("طلب إذن تسليم", msg, "Admin",
            requestedBy, formName: "sales-delivery-status", relatedTable: "Transactions", relatedId: transactionId);
        await _notify.NotifyRoleAsync("طلب إذن تسليم", msg, "AccountManager",
            requestedBy, formName: "sales-delivery-status", relatedTable: "Transactions", relatedId: transactionId);

        _logger.LogInformation("Delivery permission requested for {TxId} by {User}", transactionId, requestedBy);
        return (true, "تم إرسال طلب إذن التسليم للمدير ومدير الحسابات — سيصلك إشعار عند الموافقة.");
    }

    public async Task<(bool Success, string Message)> GrantDeliveryPermissionAsync(
        int transactionId, string grantedBy)
    {
        // 🔒 الخدمة هي مصدر الحقيقة: المدير ومدير الحسابات فقط
        var httpUser = _http.HttpContext?.User;
        if (httpUser == null || !DeliveryPermissions.CanOverrideRemaining(httpUser))
            return (false, "منح إذن التسليم متاح للمدير ومدير الحسابات فقط.");

        using var db = await _factory.CreateDbContextAsync();

        var t = await db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TransactionId == transactionId);
        if (t == null) return (false, "الفاتورة غير موجودة.");
        if (t.IsDelivered == true) return (false, "الفاتورة مسلَّمة بالفعل.");
        if (t.GrandTotal - t.PaidAmount <= 0)
            return (false, "لا توجد مبالغ متبقية — يمكن التسليم مباشرة بدون إذن.");

        await _audit.LogAsync<object>("Transactions", ActionDeliveryPermissionGranted,
            transactionId.ToString(), null, new { Granted = true }, grantedBy);

        // إشعار آخر طالب إذن
        var lastRequest = await db.AuditLogs.AsNoTracking()
            .Where(a => a.TableName == "Transactions"
                     && a.ActionType == ActionDeliveryPermissionRequest
                     && a.PrimaryKeyValue == transactionId.ToString())
            .OrderByDescending(a => a.AuditId)
            .FirstOrDefaultAsync();

        var message = $"تم منح إذن التسليم للفاتورة رقم {transactionId}.";
        if (!string.IsNullOrWhiteSpace(lastRequest?.LoginName))
        {
            await _notify.AddAsync("تم منح إذن تسليم",
                $"تم منحك إذن تسليم الفاتورة رقم {transactionId} بواسطة {grantedBy}. يمكنك التسليم الآن.",
                lastRequest.LoginName, grantedBy,
                formName: "sales-delivery-status", relatedTable: "Transactions", relatedId: transactionId);
            message += $" وتم إشعار {lastRequest.LoginName}.";
        }

        _logger.LogInformation("Delivery permission granted for {TxId} by {User}", transactionId, grantedBy);
        return (true, message);
    }

    public async Task<Dictionary<int, DeliveryPermissionStateDto>> GetDeliveryPermissionStatesAsync(
        List<int> transactionIds)
    {
        var result = new Dictionary<int, DeliveryPermissionStateDto>();
        if (transactionIds == null || transactionIds.Count == 0) return result;

        var ids = transactionIds.Select(x => x.ToString()).ToList();
        using var db = await _factory.CreateDbContextAsync();

        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.TableName == "Transactions"
                     && a.PrimaryKeyValue != null
                     && ids.Contains(a.PrimaryKeyValue)
                     && (a.ActionType == ActionDeliveryPermissionRequest
                      || a.ActionType == ActionDeliveryPermissionGranted))
            .OrderBy(a => a.AuditId)
            .Select(a => new { a.AuditId, a.ActionType, a.PrimaryKeyValue, a.LoginName, a.ActionDate })
            .ToListAsync();

        foreach (var grp in rows.GroupBy(r => r.PrimaryKeyValue!))
        {
            if (!int.TryParse(grp.Key, out var txId)) continue;

            var lastReq = grp.LastOrDefault(r => r.ActionType == ActionDeliveryPermissionRequest);
            var lastGrant = grp.LastOrDefault(r => r.ActionType == ActionDeliveryPermissionGranted);
            if (lastReq == null && lastGrant == null) continue;

            var st = new DeliveryPermissionStateDto { TransactionId = txId };
            if (lastReq != null) { st.RequestedBy = lastReq.LoginName; st.RequestedAt = lastReq.ActionDate; }
            if (lastGrant != null) { st.GrantedBy = lastGrant.LoginName; st.GrantedAt = lastGrant.ActionDate; }
            st.HasPendingRequest = lastReq != null && (lastGrant == null || lastReq.AuditId > lastGrant.AuditId);
            result[txId] = st;
        }

        return result;
    }

    public async Task<List<DailyDeliverySheetRowDto>> GetDailyDeliverySheetAsync(DateTime? from, DateTime? to)
    {
        using var db = await _factory.CreateDbContextAsync();

        var q = db.VwSalesDeliveryStatuses.AsNoTracking()
            .Where(x => x.DeliveryStatus != "تم التسليم");

        if (from.HasValue) q = q.Where(x => x.DueDate >= from.Value);
        if (to.HasValue)   q = q.Where(x => x.DueDate < to.Value.AddDays(1));

        var rows = await q
            .OrderBy(x => x.DueDate ?? DateTime.MaxValue)
            .ThenBy(x => x.TransactionId)
            .Take(200)
            .Select(x => new
            {
                x.TransactionId, x.PartyId, x.PartyName, x.DueDate,
                x.DaysRemaining, x.DeliveryStatus, x.DeliveryEmployeeName
            })
            .ToListAsync();

        var partyIds = rows.Select(r => r.PartyId).Distinct().ToList();
        var parties = await db.Parties.AsNoTracking()
            .Where(p => partyIds.Contains(p.PartyId))
            .Select(p => new { p.PartyId, p.Phone, p.Address })
            .ToDictionaryAsync(p => p.PartyId);

        return rows.Select(r => new DailyDeliverySheetRowDto
        {
            TransactionId = r.TransactionId,
            PartyName = r.PartyName,
            Phone = parties.TryGetValue(r.PartyId, out var pt) ? pt.Phone : null,
            Address = parties.TryGetValue(r.PartyId, out var pa) ? pa.Address : null,
            DueDate = r.DueDate,
            DaysRemaining = r.DaysRemaining,
            DeliveryStatus = r.DeliveryStatus,
            DeliveryEmployeeName = r.DeliveryEmployeeName
        }).ToList();
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