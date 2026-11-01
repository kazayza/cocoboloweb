using System.Data;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// تنفيذ حلقة صيانة مرتجعات التسليم (القسم 11.3/11.6). كل انتقال يُدقَّق،
/// وإغلاق الردّ النهائي يعيد بناء إسقاط الفاتورة بالدالة المشتركة
/// (DeliveryProjection) لأنه قد يُكملها دون حدث تسليم جديد.
/// تلف المناولة هنا تصنيف وتقارير فقط — لا خصم ولا أرصدة موظفين إطلاقًا (D9).
/// </summary>
public class DeliveryMaintenanceService : IDeliveryMaintenanceService
{
    private const string ActionStatusChanged = "MaintenanceStatusChanged";
    private const string ActionClosed        = "MaintenanceClosed";
    private const string ActionReclassified  = "MaintenanceReclassified";
    private const string TableName           = "DeliveryMaintenanceItems";

    private readonly IDbContextFactory<db24804Context> _factory;
    private readonly IAuditService _audit;
    private readonly NotificationService _notify;
    private readonly ILogger<DeliveryMaintenanceService> _logger;
    private readonly IHttpContextAccessor _http;

    public DeliveryMaintenanceService(
        IDbContextFactory<db24804Context> factory,
        IAuditService audit,
        NotificationService notify,
        ILogger<DeliveryMaintenanceService> logger,
        IHttpContextAccessor http)
    {
        _factory = factory;
        _audit = audit;
        _notify = notify;
        _logger = logger;
        _http = http;
    }

    private System.Security.Claims.ClaimsPrincipal? User => _http.HttpContext?.User;

    private static bool IsMissingStorageError(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
            if (e is Microsoft.Data.SqlClient.SqlException sql && (sql.Number == 208 || sql.Number == 207))
                return true;
        return false;
    }

    // ════════════════════════════════════════════════════════
    //                        القراءة
    // ════════════════════════════════════════════════════════

    public async Task<List<DeliveryMaintenanceItemDto>> GetQueueAsync(
        string? status, string? reasonClass, string? search, bool includeTerminal)
    {
        try
        {
            using var db = await _factory.CreateDbContextAsync();

            var query = db.DeliveryMaintenanceItems.AsNoTracking()
                .Where(m => !m.IsVoided);

            if (!includeTerminal)
            {
                var inLoop = DeliveryMaintenanceStatuses.InLoop;
                query = query.Where(m => inLoop.Contains(m.Status));
            }
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(m => m.Status == status);
            if (!string.IsNullOrWhiteSpace(reasonClass))
                query = query.Where(m => m.ReasonClass == reasonClass);

            var records = await query
                .OrderBy(m => m.CreatedAt)
                .Take(1000)
                .ToListAsync();
            if (records.Count == 0) return new List<DeliveryMaintenanceItemDto>();

            var txIds = records.Select(r => r.TransactionId).Distinct().ToList();
            var txs = await db.Transactions.AsNoTracking()
                .Where(t => txIds.Contains(t.TransactionId))
                .Select(t => new { t.TransactionId, t.ReferenceNumber, t.PartyId })
                .ToListAsync();
            var txById = txs.ToDictionary(t => t.TransactionId);

            var partyIds = txs.Select(t => t.PartyId).Distinct().ToList();
            var partyById = await db.Parties.AsNoTracking()
                .Where(p => partyIds.Contains(p.PartyId))
                .Select(p => new { p.PartyId, p.PartyName })
                .ToDictionaryAsync(p => p.PartyId, p => p.PartyName);

            var detailIds = records.Select(r => r.TransactionDetailId).Distinct().ToList();
            var productByDetail = await db.TransactionDetails.AsNoTracking()
                .Where(d => detailIds.Contains(d.DetailId))
                .Select(d => new { d.DetailId, ProductName = d.Product != null ? d.Product.ProductName : "" })
                .ToDictionaryAsync(d => d.DetailId, d => d.ProductName);

            var chargeIds = records.Where(r => r.AdditionalChargeId.HasValue)
                .Select(r => r.AdditionalChargeId!.Value).Distinct().ToList();
            var chargeById = chargeIds.Count == 0
                ? new Dictionary<int, AdditionalCharge>()
                : await db.AdditionalCharges.AsNoTracking()
                    .Where(c => chargeIds.Contains(c.ChargeId))
                    .ToDictionaryAsync(c => c.ChargeId);

            var empIds = records.Where(r => r.HandledByEmployeeId.HasValue)
                .Select(r => r.HandledByEmployeeId!.Value).Distinct().ToList();
            var empById = empIds.Count == 0
                ? new Dictionary<int, string>()
                : await db.Employees.AsNoTracking()
                    .Where(e => empIds.Contains(e.EmployeeId))
                    .Select(e => new { e.EmployeeId, e.FullName })
                    .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName);

            var canSeeCost = User != null && DeliveryPermissions.CanSeeRepairCost(User);

            var dtos = records.Select(m => new DeliveryMaintenanceItemDto
            {
                MaintenanceId       = m.MaintenanceId,
                DeliveryItemId      = m.DeliveryItemId,
                DeliveryId          = m.DeliveryId,
                TransactionId       = m.TransactionId,
                TransactionRef      = txById.TryGetValue(m.TransactionId, out var tx) ? tx.ReferenceNumber : null,
                PartyName           = txById.TryGetValue(m.TransactionId, out var tx2)
                                      && partyById.TryGetValue(tx2.PartyId, out var pn) ? pn : null,
                TransactionDetailId = m.TransactionDetailId,
                ProductName         = productByDetail.TryGetValue(m.TransactionDetailId, out var pr) ? pr : "",
                Quantity            = m.Quantity,
                ReasonClass         = m.ReasonClass,
                IssueDescription    = m.IssueDescription,
                Status              = m.Status,
                Billable            = m.Billable,
                AdditionalChargeId  = m.AdditionalChargeId,
                ChargeStatus        = m.AdditionalChargeId.HasValue
                                      && chargeById.TryGetValue(m.AdditionalChargeId.Value, out var ch) ? ch.Status : null,
                ChargeAmount        = m.AdditionalChargeId.HasValue
                                      && chargeById.TryGetValue(m.AdditionalChargeId.Value, out var ch2) ? ch2.ChargeAmount : null,
                RepairCost          = canSeeCost ? m.RepairCost : null,
                RepairCostNotes     = canSeeCost ? m.RepairCostNotes : null,
                HandledByEmployeeId = m.HandledByEmployeeId,
                HandledByEmployeeName = m.HandledByEmployeeId.HasValue
                                      && empById.TryGetValue(m.HandledByEmployeeId.Value, out var en) ? en : null,
                CreatedAt           = m.CreatedAt,
                StartedAt           = m.StartedAt,
                ReadyAt             = m.ReadyAt,
                ClosedAt            = m.ClosedAt,
                StatusBy            = m.StatusBy,
                ResolutionNotes     = m.ResolutionNotes,
                IsVoided            = m.IsVoided
            }).ToList();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                dtos = dtos.Where(d =>
                    (d.TransactionRef ?? "").Contains(s, StringComparison.OrdinalIgnoreCase)
                    || (d.PartyName ?? "").Contains(s, StringComparison.OrdinalIgnoreCase)
                    || d.ProductName.Contains(s, StringComparison.OrdinalIgnoreCase)
                    || d.TransactionId.ToString() == s).ToList();
            }

            // داخل الحلقة أولًا (الأقدم أولًا — طابور عمل)، ثم المنتهية بالأحدث إغلاقًا
            return dtos
                .OrderBy(d => d.IsTerminal ? 1 : 0)
                .ThenBy(d => d.IsTerminal ? DateTime.MaxValue : d.CreatedAt)
                .ThenByDescending(d => d.ClosedAt ?? DateTime.MinValue)
                .Take(500)
                .ToList();
        }
        catch (Exception ex) when (IsMissingStorageError(ex))
        {
            _logger.LogWarning("Delivery maintenance storage is missing — queue empty until delivery-outcomes.sql runs.");
            return new List<DeliveryMaintenanceItemDto>();
        }
    }

    // ════════════════════════════════════════════════════════
    //                       الانتقالات
    // ════════════════════════════════════════════════════════

    private async Task<(DeliveryMaintenanceItem? Rec, string? Error)> LoadForTransitionAsync(
        db24804Context db, int maintenanceId)
    {
        var rec = await db.DeliveryMaintenanceItems
            .FirstOrDefaultAsync(m => m.MaintenanceId == maintenanceId);
        if (rec == null) return (null, "سجل الصيانة غير موجود.");
        if (rec.IsVoided) return (null, "سجل الصيانة ملغي.");
        if (DeliveryMaintenanceStatuses.IsTerminal(rec.Status))
            return (null, $"سجل الصيانة منتهٍ بحالة «{DeliveryMaintenanceStatuses.NameOf(rec.Status)}» — لا انتقالات بعدها.");
        return (rec, null);
    }

    private async Task AuditTransitionAsync(
        DeliveryMaintenanceItem rec, string action, string oldStatus, string userName, object? extra = null)
    {
        try
        {
            await _audit.LogAsync<object>(TableName, action, rec.MaintenanceId.ToString(),
                new { Status = oldStatus },
                new
                {
                    rec.Status, rec.Quantity, rec.ReasonClass, rec.TransactionId,
                    rec.RepairCost, rec.ResolutionNotes, Extra = extra
                }, userName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit failed for maintenance {Id}", rec.MaintenanceId);
        }
    }

    public async Task<(bool Success, string Message)> StartAsync(
        int maintenanceId, int? handledByEmployeeId, string userName)
    {
        if (User == null || !DeliveryPermissions.CanTransitionMaintenance(User))
            return (false, "بدء الصيانة متاح لمدير الإنتاج ومدير المصنع والمدير.");

        using var db = await _factory.CreateDbContextAsync();
        var (rec, error) = await LoadForTransitionAsync(db, maintenanceId);
        if (rec == null) return (false, error!);
        if (rec.Status != DeliveryMaintenanceStatuses.Pending)
            return (false, "بدء الصيانة من حالة «بانتظار الصيانة» فقط.");

        var old = rec.Status;
        rec.Status = DeliveryMaintenanceStatuses.InProgress;
        rec.StartedAt = DateTime.Now;
        rec.StatusBy = userName;
        if (handledByEmployeeId.HasValue) rec.HandledByEmployeeId = handledByEmployeeId;
        await db.SaveChangesAsync();

        await AuditTransitionAsync(rec, ActionStatusChanged, old, userName);
        return (true, "بدأت صيانة البند.");
    }

    public async Task<(bool Success, string Message)> MarkReadyAsync(
        int maintenanceId, decimal? repairCost, string? repairCostNotes,
        int? handledByEmployeeId, string userName)
    {
        if (User == null || !DeliveryPermissions.CanTransitionMaintenance(User))
            return (false, "تعليم الجاهزية متاح لمدير الإنتاج ومدير المصنع والمدير.");
        if (repairCost.HasValue && !DeliveryPermissions.CanSeeRepairCost(User))
            return (false, "إدخال تكلفة الإصلاح متاح لأدوار الإدارة والحسابات والإنتاج والمصنع فقط.");
        if (repairCost is < 0) return (false, "تكلفة الإصلاح لا يمكن أن تكون سالبة.");

        using var db = await _factory.CreateDbContextAsync();
        var (rec, error) = await LoadForTransitionAsync(db, maintenanceId);
        if (rec == null) return (false, error!);
        if (rec.Status != DeliveryMaintenanceStatuses.Pending
            && rec.Status != DeliveryMaintenanceStatuses.InProgress)
            return (false, "الجاهزية من «بانتظار الصيانة» أو «قيد الصيانة» فقط.");

        var old = rec.Status;
        rec.Status = DeliveryMaintenanceStatuses.ReadyForRedelivery;
        rec.ReadyAt = DateTime.Now;
        rec.StatusBy = userName;
        if (repairCost.HasValue) rec.RepairCost = repairCost;
        if (!string.IsNullOrWhiteSpace(repairCostNotes)) rec.RepairCostNotes = repairCostNotes.Trim();
        if (handledByEmployeeId.HasValue) rec.HandledByEmployeeId = handledByEmployeeId;
        await db.SaveChangesAsync();

        await AuditTransitionAsync(rec, ActionStatusChanged, old, userName);
        await NotifyReadyAsync(db, rec, userName);
        return (true, "البند جاهز لإعادة التسليم — أُشعر البائع ومسؤول التسليمات.");
    }

    public async Task<(bool Success, string Message)> MarkUnrepairableAsync(
        int maintenanceId, string? notes, string userName)
    {
        if (User == null || !DeliveryPermissions.CanTransitionMaintenance(User))
            return (false, "تعليم التعذّر متاح لمدير الإنتاج ومدير المصنع والمدير.");

        using var db = await _factory.CreateDbContextAsync();
        var (rec, error) = await LoadForTransitionAsync(db, maintenanceId);
        if (rec == null) return (false, error!);

        var old = rec.Status;
        rec.Status = DeliveryMaintenanceStatuses.Unrepairable;
        rec.StatusBy = userName;
        if (!string.IsNullOrWhiteSpace(notes)) rec.ResolutionNotes = notes.Trim();
        await db.SaveChangesAsync();

        await AuditTransitionAsync(rec, ActionStatusChanged, old, userName);
        return (true, "عُلّم البند «تعذّر إصلاحه» — بانتظار قرار استبدال أو ردّ نهائي من الإدارة/الحسابات.");
    }

    public async Task<(bool Success, string Message)> CloseByReplacementAsync(
        int maintenanceId, string? notes, string userName)
    {
        if (User == null || !DeliveryPermissions.CanCloseMaintenanceByReplacement(User))
            return (false, "الإغلاق باستبدال متاح للمصنع/الإنتاج/المدير ومدير الحسابات.");

        using var db = await _factory.CreateDbContextAsync();
        var (rec, error) = await LoadForTransitionAsync(db, maintenanceId);
        if (rec == null) return (false, error!);
        if (rec.Status != DeliveryMaintenanceStatuses.Unrepairable)
            return (false, "الإغلاق باستبدال يأتي بعد تعليم «تعذّر إصلاحه» (قرار لكل حالة — D11).");

        var old = rec.Status;
        rec.Status = DeliveryMaintenanceStatuses.ClosedByReplacement;
        rec.ClosedAt = DateTime.Now;
        rec.StatusBy = userName;
        if (!string.IsNullOrWhiteSpace(notes)) rec.ResolutionNotes = notes.Trim();
        await db.SaveChangesAsync();

        await AuditTransitionAsync(rec, ActionClosed, old, userName,
            new { Resolution = "Replacement" });
        return (true, "أُغلق البند باستبدال — كميته عادت «بانتظار التسليم» وستخرج وحدة بديلة بحدث تسليم عادي.");
    }

    public async Task<(bool Success, string Message)> CloseByCreditAsync(
        int maintenanceId, string? notes, string userName)
    {
        if (User == null || !DeliveryPermissions.CanCloseMaintenanceByCredit(User))
            return (false, "الإغلاق بردّ نهائي/رصيد متاح للمدير ومدير الحسابات فقط (قرار مالي الأثر).");

        using var db = await _factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var (rec, error) = await LoadForTransitionAsync(db, maintenanceId);
            if (rec == null) return (false, error!);
            if (rec.Status != DeliveryMaintenanceStatuses.Unrepairable)
                return (false, "الإغلاق بردّ نهائي يأتي بعد تعليم «تعذّر إصلاحه» (قرار لكل حالة — D11).");

            var old = rec.Status;
            rec.Status = DeliveryMaintenanceStatuses.ClosedByCredit;
            rec.ClosedAt = DateTime.Now;
            rec.StatusBy = userName;
            if (!string.IsNullOrWhiteSpace(notes)) rec.ResolutionNotes = notes.Trim();
            await db.SaveChangesAsync();

            // التسوية قد تُكمل الفاتورة دون حدث جديد — إعادة بناء إسقاطها هنا
            var invoice = await db.Transactions
                .FirstOrDefaultAsync(t => t.TransactionId == rec.TransactionId);
            if (invoice != null)
            {
                await DeliveryProjection.RecomputeAsync(db, invoice);
                await db.SaveChangesAsync();
            }
            await tx.CommitAsync();

            await AuditTransitionAsync(rec, ActionClosed, old, userName,
                new { Resolution = "FinalReturnCredit" });
            return (true, "أُغلق البند بردّ نهائي — كميته سُوّيت ولن تُسلَّم، والتسوية المالية نفسها تتبع مسار المرتجعات القائم.");
        }
        catch (Exception ex) when (IsMissingStorageError(ex))
        {
            return (false, "تخزين نتائج التسليم غير مهيأ بعد — برجاء تشغيل سكربت delivery-outcomes.sql.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CloseByCredit failed for maintenance {Id}", maintenanceId);
            return (false, $"حدث خطأ أثناء الإغلاق: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> CancelAsync(
        int maintenanceId, string reason, string userName)
    {
        if (User == null || !DeliveryPermissions.CanCloseMaintenanceByCredit(User))
            return (false, "إلغاء طلب الصيانة متاح للمدير ومدير الحسابات.");
        if (string.IsNullOrWhiteSpace(reason))
            return (false, "سبب الإلغاء إلزامي.");

        using var db = await _factory.CreateDbContextAsync();
        var (rec, error) = await LoadForTransitionAsync(db, maintenanceId);
        if (rec == null) return (false, error!);

        var old = rec.Status;
        rec.Status = DeliveryMaintenanceStatuses.Cancelled;
        rec.ClosedAt = DateTime.Now;
        rec.StatusBy = userName;
        rec.ResolutionNotes = reason.Trim();
        await db.SaveChangesAsync();

        await AuditTransitionAsync(rec, ActionStatusChanged, old, userName,
            new { Cancelled = true, Reason = reason.Trim() });
        return (true, "أُلغي طلب الصيانة — كمية البند عادت «بانتظار التسليم» كما هي.");
    }

    public async Task<(bool Success, string Message)> ReclassifyAsync(
        int maintenanceId, string newClass, string reason, string userName)
    {
        if (User == null || !DeliveryPermissions.CanReclassifyMaintenance(User))
            return (false, "إعادة تصنيف العيب متاحة للمدير ومدير الحسابات ومدير الإنتاج فقط (D12).");
        if (string.IsNullOrWhiteSpace(reason))
            return (false, "سبب إعادة التصنيف إلزامي — التصنيف يوزّع المسؤولية بين المصنع وعمالة التسليم.");
        if (newClass != DeliveryDispositions.ManufacturingDefect
            && newClass != DeliveryDispositions.HandlingDamage
            && newClass != DeliveryDispositions.CustomerModification)
            return (false, "تصنيف غير معروف.");

        using var db = await _factory.CreateDbContextAsync();
        var (rec, error) = await LoadForTransitionAsync(db, maintenanceId);
        if (rec == null) return (false, error!);
        if (rec.ReasonClass == newClass)
            return (false, "السجل مصنّف بهذا التصنيف أصلًا.");

        var oldClass = rec.ReasonClass;
        rec.ReasonClass = newClass;
        rec.Billable = newClass == DeliveryDispositions.CustomerModification;
        rec.StatusBy = userName;
        await db.SaveChangesAsync();

        try
        {
            await _audit.LogAsync<object>(TableName, ActionReclassified, rec.MaintenanceId.ToString(),
                new { ReasonClass = oldClass },
                new { ReasonClass = newClass, Reason = reason.Trim(), rec.TransactionId, rec.Quantity },
                userName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit failed for reclassified maintenance {Id}", maintenanceId);
        }

        return (true, $"أُعيد تصنيف الرجوع من «{DeliveryDispositions.NameOf(oldClass)}» إلى «{DeliveryDispositions.NameOf(newClass)}» ووثّق السبب في التدقيق.");
    }

    /// <summary>إشعار «جاهز لإعادة التسليم» (11.6): بائع الفاتورة + دور مسؤول التسليمات.</summary>
    private async Task NotifyReadyAsync(db24804Context db, DeliveryMaintenanceItem rec, string actorUserName)
    {
        try
        {
            var info = await db.Transactions.AsNoTracking()
                .Where(t => t.TransactionId == rec.TransactionId)
                .Select(t => new { t.ReferenceNumber, t.PartyId, t.EmpId })
                .FirstOrDefaultAsync();
            var partyName = info == null ? "غير محدد" : await db.Parties.AsNoTracking()
                .Where(p => p.PartyId == info.PartyId)
                .Select(pA => pA.PartyName)
                .FirstOrDefaultAsync() ?? "غير محدد";
            var productName = await db.TransactionDetails.AsNoTracking()
                .Where(d => d.DetailId == rec.TransactionDetailId)
                .Select(d => d.Product != null ? d.Product.ProductName : "")
                .FirstOrDefaultAsync() ?? "";

            var message = $"الفاتورة {info?.ReferenceNumber ?? $"#{rec.TransactionId}"} — العميل {partyName}: " +
                          $"بند «{productName}» × {rec.Quantity:0.###} ({DeliveryDispositions.NameOf(rec.ReasonClass)}) أصبح جاهزًا لإعادة التسليم.";

            await _notify.NotifyRoleAsync("🔧 بند جاهز لإعادة التسليم", message, SystemRoles.DeliveryOfficer,
                actorUserName, "delivery-maintenance", "Transactions", rec.TransactionId);

            if (info?.EmpId != null)
            {
                var sellerUser = await db.Users.AsNoTracking()
                    .Where(u => u.EmployeeId == info.EmpId.Value && u.IsActive == true)
                    .Select(u => u.Username)
                    .FirstOrDefaultAsync();
                if (!string.IsNullOrWhiteSpace(sellerUser))
                    await _notify.AddAsync("🔧 بند جاهز لإعادة التسليم", message, sellerUser!, actorUserName,
                        "delivery-maintenance", "Transactions", rec.TransactionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send maintenance-ready notification for {Id}", rec.MaintenanceId);
        }
    }
}
