using System.Data;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace COCOBOLOERPNEW.Services;

public class SalesDeliveryStatusService : ISalesDeliveryStatusService
{
    private const string ActionDeliveryPermissionRequest = "DeliveryPermissionRequest";
    private const string ActionDeliveryPermissionGranted = "DeliveryPermissionGranted";

    // ⭐ التسليم الجزئي: أفعال تدقيق أحداث التسليم
    private const string ActionDeliveryEventRecorded = "DeliveryEventRecorded";
    private const string ActionDeliveryEventVoided   = "DeliveryEventVoided";

    // مسامحة عشرية عند اختبار «المتبقي = صفر» (الكميات decimal(18,3) وقد تحمل كسورًا)
    private const decimal QtyEpsilon = 0.0005m;

    private const string MissingTablesMessage =
        "جداول التسليم الجزئي غير مهيأة بعد — برجاء تشغيل سكربتات SQL (الجداول ثم الترحيل) قبل استخدام تسجيل التسليمات.";

    // ⭐ نتائج التسليم (القسم 11): تسجيل/إلغاء الأحداث صارا يكتبان أعمدة الحصص
    // وجدول الصيانة — غياب سكربت delivery-outcomes.sql يُرفض برسالة واضحة.
    private const string MissingOutcomesMessage =
        "تخزين نتائج التسليم غير مهيأ بعد — برجاء تشغيل سكربت delivery-outcomes.sql (أعمدة الحصص + جدول الصيانة) قبل تسجيل التسليمات.";

    private const string ActionModificationChargeCreated = "ModificationChargeCreated";

    // ⭐ طلبات تعديل مواعيد التسليم: أفعال تدقيق دورة الطلب (تقديم/اعتماد/رفض/سحب)
    private const string ActionScheduleChangeRequested = "DeliveryScheduleChangeRequested";
    private const string ActionScheduleChangeApproved  = "DeliveryScheduleChangeApproved";
    private const string ActionScheduleChangeRejected  = "DeliveryScheduleChangeRejected";
    private const string ActionScheduleChangeWithdrawn = "DeliveryScheduleChangeWithdrawn";

    // غياب جدولي طلبات تعديل المواعيد (سكربت delivery-schedule-requests.sql لم يُشغَّل بعد)
    private const string MissingScheduleRequestsMessage =
        "جداول طلبات تعديل المواعيد غير مهيأة بعد — برجاء تشغيل سكربت delivery-schedule-requests.sql أولًا.";

    private readonly IDbContextFactory<db24804Context> _factory;
    private readonly IAuditService _audit;
    private readonly NotificationService _notify;
    private readonly ILogger<SalesDeliveryStatusService> _logger;
    private readonly IHttpContextAccessor _http;
    private readonly IConfiguration _config;

    public SalesDeliveryStatusService(
        IDbContextFactory<db24804Context> factory,
        IAuditService audit,
        NotificationService notify,
        ILogger<SalesDeliveryStatusService> logger,
        IHttpContextAccessor http,
        IConfiguration config)
    {
        _factory = factory;
        _audit = audit;
        _notify = notify;
        _logger = logger;
        _http = http;
        _config = config;
    }

    // ════════════════════════════════════════════════════════════
    // ⭐ إخفاء تاريخ الاستحقاق لغير المستثنين (عرض فقط — لا يُكتب أي تاريخ مخفي في قاعدة البيانات)
    // المستثنون يرون التاريخ الحقيقي (DeliveryPermissions.CanSeeRealDeliveryDate)،
    // ومن عداهم يرونه منقوصًا هامش الأيام كضغط تنفيذي قبل الموعد الحقيقي.
    // ════════════════════════════════════════════════════════════
    private bool CurrentUserSeesRealDate =>
        _http.HttpContext?.User is { } u && DeliveryPermissions.CanSeeRealDeliveryDate(u);

    private int DateBufferDays =>
        _config.GetValue<int?>("Delivery:DateBufferDays") is int b && b >= 0 ? b : 4;

    /// <summary>يحسب قيم العرض المخفية (تاريخ/أيام متبقية/حالة) من القيم الحقيقية.</summary>
    private (DateTime? DueDate, int? DaysRemaining, string Status) MaskValues(
        DateTime? dueDate, DateTime transactionDate, int? daysRemaining, string status)
    {
        if (dueDate == null) return (dueDate, daysRemaining, status);

        var shown = dueDate.Value.AddDays(-DateBufferDays);
        if (shown < transactionDate) shown = transactionDate;   // المعروض لا يسبق تاريخ الفاتورة
        var days = (shown.Date - DateTime.Today).Days;
        // الصفوف المعلَّقة («جارى») فقط تتحول لـ«متأخر» مبكرًا — المسَلَّمة والمرتجعة نتائج نهائية تحتفظ بحالتها
        if (status == "جارى" && shown.Date < DateTime.Today)
            status = "متأخر";
        return (shown, days, status);
    }

    private void MaskRow(VwSalesDeliveryStatus row)
    {
        var (d, days, s) = MaskValues(row.DueDate, row.TransactionDate, row.DaysRemaining, row.DeliveryStatus);
        row.DueDate = d;
        row.DaysRemaining = days;
        row.DeliveryStatus = s;
    }

    /// <summary>
    /// فلاتر تاريخ الاستحقاق للمخفيين تُزاح +الهامش قبل الاستعلام، فتطابق النتائج التواريخ المعروضة لهم.
    /// (الترتيب بـ DueDate إزاحة ثابتة — لا يتأثر.)
    /// </summary>
    private (DateTime? From, DateTime? To) ShiftDueDateRange(DateTime? from, DateTime? to, string? dateFilterType)
    {
        var type = string.IsNullOrWhiteSpace(dateFilterType) ? DeliveryDateFilterTypes.DueDate : dateFilterType;
        if (type != DeliveryDateFilterTypes.DueDate) return (from, to);
        var b = DateBufferDays;
        return (from?.AddDays(b), to?.AddDays(b));
    }

    public async Task<List<VwSalesDeliveryStatus>> GetAllAsync()
    {
        using var db = await _factory.CreateDbContextAsync();
        var rows = await db.VwSalesDeliveryStatuses
            .AsNoTracking()
            .OrderByDescending(x => x.TransactionDate)
            .ToListAsync();

        if (!CurrentUserSeesRealDate)
            foreach (var r in rows) MaskRow(r);

        return rows;
    }

    public async Task<List<VwSalesDeliveryStatus>> GetFilteredAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        string dateFilterType,
        string? partyName,
        string? deliveryStatus)
    {
        using var db = await _factory.CreateDbContextAsync();

        var seeReal = CurrentUserSeesRealDate;
        if (!seeReal)
            (dateFrom, dateTo) = ShiftDueDateRange(dateFrom, dateTo, dateFilterType);

        var query = db.VwSalesDeliveryStatuses.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(partyName))
        {
            var term = partyName.Trim();
            query = query.Where(x => x.PartyName != null && x.PartyName.Contains(term));
        }

        // ⭐ فلتر الحالة لم يعد في SQL: الحالة الفعّالة تُشتق في الذاكرة من أحداث
        // التسليم (جزئي/مكتمل) وتُدمج في الصفوف، ثم يُفلتر على «المعروض» نفسه —
        // فيتطابق الفلتر مع ما يراه المستخدم فعليًا (إصلاح عيب فلتر المقنّعين).
        // ⭐ وفلتر «تاريخ الاستحقاق» صار على مستوى البنود: تضييق SQL أولي (تاريخ
        // الفاتورة أو أي بند مفصول داخل المدى) ثم تدقيق دقيق في الذاكرة على
        // المواعيد الفعّالة لكل البنود — فتظهر الفاتورة في أيام مواعيد بنودها كلها.
        var isDueType = (string.IsNullOrWhiteSpace(dateFilterType)
            ? DeliveryDateFilterTypes.DueDate : dateFilterType) == DeliveryDateFilterTypes.DueDate;

        List<VwSalesDeliveryStatus> rows;
        if (isDueType)
        {
            try
            {
                rows = await ApplyDuePreFilter(db, query, dateFrom, dateTo)
                    .OrderByDescending(x => x.DueDate ?? x.TransactionDate)
                    .ThenByDescending(x => x.TransactionId)
                    .ToListAsync();
            }
            catch (Exception ex) when (IsMissingColumnError(ex))
            {
                _logger.LogWarning("Line scheduling columns are missing — due filter uses invoice dates (legacy).");
                rows = await ApplyDateFilter(query, dateFrom, dateTo, dateFilterType)
                    .OrderByDescending(x => x.DueDate ?? x.TransactionDate)
                    .ThenByDescending(x => x.TransactionId)
                    .ToListAsync();
            }

            rows = await FilterRowsByLineDueRangeAsync(db, rows, dateFrom, dateTo);
        }
        else
        {
            rows = await ApplyDateFilter(query, dateFrom, dateTo, dateFilterType)
                .OrderByDescending(x => x.DueDate ?? x.TransactionDate)
                .ThenByDescending(x => x.TransactionId)
                .ToListAsync();
        }

        if (!seeReal)
            foreach (var r in rows) MaskRow(r);

        await ApplyEffectiveStatusesAsync(rows);

        // ⭐ فلتر الحالة يُقيَّم على طبقة صفوف المواعيد لا على حالة الفاتورة وحدها:
        // فاتورة حالتها العامة «جارى» ولها موعد بند مبكر فات تظهر في فلتر «متأخر»
        // لأن صف ذلك الموعد «متأخر» في عرض «الكل» — فيتطابق الفلتر مع ما يُعرض.
        if (!string.IsNullOrWhiteSpace(deliveryStatus))
            rows = await FilterRowsByRowStatusAsync(rows, deliveryStatus);

        return rows;
    }

    /// <summary>
    /// مطابقة فلتر الحالة على مستوى صفوف (فاتورة × موعد) — نفس اشتقاق حالة الصف
    /// في قائمة الشاشة حرفيًا: المرتجع حالة فاتورة نهائية تسرى على كل صفوفها،
    /// ولكل مجموعة مواعيد: مكتملة البنود = «تم التسليم»، وفيها متأخر = «متأخر»،
    /// وإلا تُورث حالة الفاتورة الفعّالة (جزئي/تم التسليم) أو «جارى».
    /// الفاتورة تُبقى إن طابق أيٌّ من صفوفها الفلتر. بلا بيانات جدولة (تخزين
    /// غائب) يعود السلوك لمطابقة حالة الفاتورة كما كان. كروت الملخص لا تتأثر —
    /// لها مسارها المستقل على مستوى الفاتورة (GetSummaryAsync).
    /// </summary>
    private async Task<List<VwSalesDeliveryStatus>> FilterRowsByRowStatusAsync(
        List<VwSalesDeliveryStatus> rows, string deliveryStatus)
    {
        if (rows.Count == 0) return rows;

        Dictionary<int, InvoiceScheduleSummaryDto> schedules;
        try
        {
            schedules = await GetLineScheduleSummariesBatchAsync(
                rows.Select(r => r.TransactionId).ToList());
        }
        catch (Exception ex) when (IsMissingStorageError(ex))
        {
            schedules = new Dictionary<int, InvoiceScheduleSummaryDto>();
        }

        bool Matches(VwSalesDeliveryStatus row)
        {
            if (row.DeliveryStatus == DeliveryEffectiveStatuses.Returned)
                return deliveryStatus == DeliveryEffectiveStatuses.Returned;

            if (schedules.TryGetValue(row.TransactionId, out var sched) && sched.Groups.Count > 0)
            {
                foreach (var g in sched.Groups)
                {
                    var rowStatus =
                        g.PendingLineCount == 0            ? DeliveryEffectiveStatuses.Delivered
                        : g.HasOverdue                     ? DeliveryEffectiveStatuses.Overdue
                        : row.DeliveryStatus == DeliveryEffectiveStatuses.Partial   ? DeliveryEffectiveStatuses.Partial
                        : row.DeliveryStatus == DeliveryEffectiveStatuses.Delivered ? DeliveryEffectiveStatuses.Delivered
                        : DeliveryEffectiveStatuses.Pending;
                    if (rowStatus == deliveryStatus) return true;
                }
                return false;
            }

            return row.DeliveryStatus == deliveryStatus;
        }

        return rows.Where(Matches).ToList();
    }

    public async Task<VwSalesDeliveryStatus?> GetByTransactionIdAsync(int transactionId)
    {
        using var db = await _factory.CreateDbContextAsync();
        var row = await db.VwSalesDeliveryStatuses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TransactionId == transactionId);

        if (row != null && !CurrentUserSeesRealDate)
            MaskRow(row);

        return row;
    }

    public async Task<DeliverySummaryDto> GetSummaryAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        string dateFilterType,
        string? partyName,
        string? deliveryStatus)
    {
        using var db = await _factory.CreateDbContextAsync();

        var seeReal = CurrentUserSeesRealDate;
        if (!seeReal)
            (dateFrom, dateTo) = ShiftDueDateRange(dateFrom, dateTo, dateFilterType);

        var query = db.VwSalesDeliveryStatuses.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(partyName))
        {
            var term = partyName.Trim();
            query = query.Where(x => x.PartyName != null && x.PartyName.Contains(term));
        }

        // ⭐ نفس فلتر الاستحقاق على مستوى البنود في القائمة حرفيًا — حتى لا يختلف
        // الملخص عنها في أي رقم (تضييق SQL أولي + تدقيق المواعيد الفعّالة)
        var isDueType = (string.IsNullOrWhiteSpace(dateFilterType)
            ? DeliveryDateFilterTypes.DueDate : dateFilterType) == DeliveryDateFilterTypes.DueDate;

        List<VwSalesDeliveryStatus> data;
        if (isDueType)
        {
            try
            {
                data = await ApplyDuePreFilter(db, query, dateFrom, dateTo).ToListAsync();
            }
            catch (Exception ex) when (IsMissingColumnError(ex))
            {
                _logger.LogWarning("Line scheduling columns are missing — summary due filter uses invoice dates (legacy).");
                data = await ApplyDateFilter(query, dateFrom, dateTo, dateFilterType).ToListAsync();
            }

            data = await FilterRowsByLineDueRangeAsync(db, data, dateFrom, dateTo);
        }
        else
        {
            data = await ApplyDateFilter(query, dateFrom, dateTo, dateFilterType).ToListAsync();
        }

        // ⭐ عدّادات الملخص تُحسب من الحالات المعروضة للمخفيين حتى تطابق قائمتهم
        if (!seeReal)
            foreach (var r in data) MaskRow(r);

        // ⭐ الحالة الفعّالة من أحداث التسليم (جزئي/مكتمل) قبل العدّ والفلترة —
        // نفس ترتيب القائمة حرفيًا حتى لا يختلف الملخص عنها أبدًا
        await ApplyEffectiveStatusesAsync(data);

        if (!string.IsNullOrWhiteSpace(deliveryStatus))
            data = data.Where(x => x.DeliveryStatus == deliveryStatus).ToList();

        return new DeliverySummaryDto
        {
            TotalCount     = data.Count,
            PendingCount   = data.Count(x => x.DeliveryStatus == "جارى"),
            PartialCount   = data.Count(x => x.DeliveryStatus == "جزئي"),
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

        var rawProducts = await db.TransactionDetails
            .AsNoTracking()
            .Where(td => td.TransactionId == transactionId)
            .Select(td => new
            {
                td.DetailId,
                td.ProductId,
                ProductName = td.Product != null ? td.Product.ProductName : "",
                td.Quantity,           // ✅ FIX
                td.UnitPrice,
                TotalAmount = td.TotalAmount ?? 0,      // ✅ FIX
                td.DeliveryDate,
                td.DeliveryDateIsCustom
            })
            .ToListAsync();

        var products = rawProducts.Select(rp => new DeliveryProductDto
        {
            TransactionDetailId = rp.DetailId,
            ProductId = rp.ProductId,
            ProductName = rp.ProductName,
            Quantity = rp.Quantity,
            UnitPrice = rp.UnitPrice,
            TotalAmount = rp.TotalAmount
        }).ToList();

        // ⭐ جدولة البنود: الموعد الفعّال لكل بند (خاصه إن فُصل، وإلا استحقاق
        // الفاتورة الحقيقي) بصيغة العرض لهذا المستخدم — نفس إخفاء الاستحقاق تمامًا
        var rawByDetail = rawProducts.ToDictionary(rp => rp.DetailId);
        foreach (var p in products)
        {
            var rp = rawByDetail[p.TransactionDetailId];
            var effective = DeliveryScheduleResolver.EffectiveDate(
                rp.DeliveryDate, rp.DeliveryDateIsCustom, transaction.DueDate);
            p.ScheduledDate = DeliveryScheduleResolver.DisplayDate(
                effective, transaction.TransactionDate, CurrentUserSeesRealDate, DateBufferDays);
            p.ScheduledDateIsCustom = rp.DeliveryDateIsCustom && rp.DeliveryDate.HasValue;
        }

        // ⭐ بيانات التفاصيل/الإذن تمر بنفس إخفاء القائمة لغير المستثنين
        var dueDate = transaction.DueDate;
        var daysRemaining = transaction.DaysRemaining;
        var deliveryStatus = transaction.DeliveryStatus;
        if (!CurrentUserSeesRealDate)
            (dueDate, daysRemaining, deliveryStatus) = MaskValues(
                dueDate, transaction.TransactionDate, daysRemaining, deliveryStatus);

        // ⭐ التسليم الجزئي: دمج تقدم الأحداث — المسلَّم لكل بند + الحالة الفعّالة
        // (المرتجع نهائي، والمكتمل من الأحداث يتجاوز نص الـView، والجزئي يبقى
        // «متأخر» إن كان كذلك على التاريخ المعروض لهذا المستخدم)
        var progress = await SafeProgressAsync(transactionId);
        if (progress != null)
        {
            var byDetail = progress.Lines.ToDictionary(l => l.TransactionDetailId);
            foreach (var p in products)
                if (byDetail.TryGetValue(p.TransactionDetailId, out var line))
                {
                    p.DeliveredQty = line.DeliveredQty;
                    p.InLoopQty = line.InLoopQty;
                    p.SettledQty = line.SettledQty;
                    p.LastDeliveryDate = line.LastDeliveredDate;
                }

            if (progress.EffectiveStatus == DeliveryEffectiveStatuses.Returned)
                deliveryStatus = DeliveryEffectiveStatuses.Returned;
            else if (progress.EffectiveStatus == DeliveryEffectiveStatuses.Delivered)
                deliveryStatus = DeliveryEffectiveStatuses.Delivered;
            else if (progress.EffectiveStatus == DeliveryEffectiveStatuses.Partial)
                deliveryStatus = deliveryStatus == DeliveryEffectiveStatuses.Overdue
                    ? DeliveryEffectiveStatuses.Overdue
                    : DeliveryEffectiveStatuses.Partial;
        }

        // ⭐ مخطط × فعلي على مستوى البند: التأخر يُقيَّم على الموعد «المعروض»
        // لهذا المستخدم (نفس أساس حالة «متأخر» القائمة) — من يرى الحقيقي يراه دقيقًا
        foreach (var p in products)
        {
            p.IsScheduleOverdue = DeliveryScheduleResolver.IsLineOverdue(
                p.ScheduledDate, p.RemainingQty);
            p.DeliveredLate = p.RemainingQty <= QtyEpsilon
                && p.LastDeliveryDate.HasValue && p.ScheduledDate.HasValue
                && p.LastDeliveryDate.Value.Date > p.ScheduledDate.Value.Date;
        }

        return new DeliveryDetailDto
        {
            TransactionId = transaction.TransactionId,
            TransactionDate = transaction.TransactionDate,
            DueDate = dueDate,
            TransactionType = transaction.TransactionType,
            PartyId = transaction.PartyId,
            PartyName = transaction.PartyName,
            PartyPhone = party?.Phone,
            PartyAddress = party?.Address,
            SalesEmployeeId = transaction.EmpId,
            SalesEmployeeName = transaction.EmployeeName,
            DeliveryEmployeeId = transaction.DeliveryEmployeeId,
            DeliveryEmployeeName = transaction.DeliveryEmployeeName,
            DeliveryStatus = deliveryStatus,
            DeliveredAt = transaction.DeliveredAt,
            DeliveredNotes = transaction.DeliveredNotes,
            GrandTotal = transaction.GrandTotal,
            PaidAmount = transaction.PaidAmount,
            DaysRemaining = daysRemaining,
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

        // ⭐ التسليم الجزئي: «تم التسليم» لم تعد تُكتب من هنا — التسليم يُسجَّل بأحداث
        // وكميات عبر RecordDeliveryAsync، وحقلا IsDelivered/DeliveredAt صارا إسقاطًا
        // يصونه مسار الأحداث فقط. أي مستدعٍ قديم يطلبها يُرفض برسالة توجيهية واضحة.
        // تبقى هذه الدالة للحالات غير الكمية فقط: مندوب/ملاحظات على مستوى الفاتورة
        // ووسم الحالات اليدوية (جارى/متأخر/مرتجع) دون أي مساس بالإسقاط.
        if (dto.Status == "تم التسليم")
            return (false, "تسجيل التسليم أصبح يتم من زر «تسجيل تسليمة» بكميات البنود (التسليم على دفعات) — برجاء استخدام حوار تسجيل تسليمة.");

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

            // ⚠️ لا كتابة على IsDelivered/DeliveredAt هنا — إسقاط يصونه مسار أحداث
            // التسليم فقط (RecordDeliveryAsync/VoidDeliveryEventAsync)، فلا يُمسح
            // تاريخ تسليمات سابقة بتعديل حالة يدوي كما كان يحدث قبل التسليم الجزئي.
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

    // ════════════════════════════════════════════════════════
    //        التسليم الجزئي — أحداث التسليم (مصدر الحقيقة)
    // ════════════════════════════════════════════════════════

    /// <summary>الحالة الأساسية من الأحداث فقط: لا أحداث = جارى، وأحداث بمتبقٍّ = جزئي، وأحداث بلا متبقٍّ = تم التسليم.</summary>
    private static string DeriveBaseStatus(bool hasEvents, decimal totalRemaining)
    {
        if (!hasEvents) return DeliveryEffectiveStatuses.Pending;
        return totalRemaining <= QtyEpsilon
            ? DeliveryEffectiveStatuses.Delivered
            : DeliveryEffectiveStatuses.Partial;
    }

    /// <summary>خطأ «الجدول غير موجود» (208) — شبكة الأمان الأخيرة إن نُشر الكود قبل سكربتات SQL.</summary>
    private static bool IsMissingTablesError(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
            if (e is Microsoft.Data.SqlClient.SqlException sql && sql.Number == 208)
                return true;
        return false;
    }

    /// <summary>خطأ «عمود غير موجود» (207) — أعمدة جدولة البنود قبل تشغيل سكربتها.</summary>
    private static bool IsMissingColumnError(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
            if (e is Microsoft.Data.SqlClient.SqlException sql && sql.Number == 207)
                return true;
        return false;
    }

    /// <summary>أي نقص في تخزين الميزات الجديدة (جداول الأحداث 208 أو أعمدة الجدولة 207).</summary>
    private static bool IsMissingStorageError(Exception ex)
        => IsMissingTablesError(ex) || IsMissingColumnError(ex);

    private async Task<Dictionary<int, DeliveryProgressSummaryDto>> SafeProgressBatchAsync(List<int> ids)
    {
        try { return await GetDeliveryProgressBatchAsync(ids); }
        catch (Exception ex) when (IsMissingStorageError(ex))
        {
            _logger.LogWarning("Delivery event tables are missing — progress derivation skipped until SQL scripts run.");
            return new Dictionary<int, DeliveryProgressSummaryDto>();
        }
    }

    private async Task<InvoiceDeliveryProgressDto?> SafeProgressAsync(int transactionId)
    {
        try { return await GetDeliveryProgressAsync(transactionId); }
        catch (Exception ex) when (IsMissingStorageError(ex))
        {
            _logger.LogWarning("Delivery event tables are missing — progress skipped until SQL scripts run.");
            return null;
        }
    }

    /// <summary>
    /// يدمج الحالة الفعّالة المشتقة من أحداث التسليم في صفوف الـView (بعد الإخفاء):
    /// المرتجع يبقى نهائيًا كما هو، والمكتمل من الأحداث يتجاوز نص الـView، والجزئي
    /// يُعرض «جزئي» إلا إن كان الصف «متأخر» على التاريخ المعروض فيبقى «متأخر».
    /// </summary>
    private async Task ApplyEffectiveStatusesAsync(List<VwSalesDeliveryStatus> rows)
    {
        if (rows.Count == 0) return;
        var progress = await SafeProgressBatchAsync(rows.Select(r => r.TransactionId).ToList());

        foreach (var row in rows)
        {
            if (row.DeliveryStatus == DeliveryEffectiveStatuses.Returned) continue;
            if (!progress.TryGetValue(row.TransactionId, out var p) || !p.HasEvents) continue;

            row.DeliveryStatus = p.EffectiveStatus == DeliveryEffectiveStatuses.Delivered
                ? DeliveryEffectiveStatuses.Delivered
                : row.DeliveryStatus == DeliveryEffectiveStatuses.Overdue
                    ? DeliveryEffectiveStatuses.Overdue
                    : DeliveryEffectiveStatuses.Partial;
        }
    }

    // ════════════════════════════════════════════════════════
    //        جدولة تسليم البنود — المواعيد الفعّالة لكل بند
    // ════════════════════════════════════════════════════════

    /// <summary>بند بجدولته الخام: الموعد الفعّال الحقيقي (قبل الإخفاء) والمقبول من أحداث التسليم.</summary>
    private sealed class LineScheduleRaw
    {
        public int TransactionDetailId;
        public int TransactionId;
        public string ProductName = "";
        public decimal InvoicedQty;

        /// <summary>المقبول نهائيًا (حصص القبول في الأحداث غير الملغاة — القسم 11.2)</summary>
        public decimal DeliveredQty;

        /// <summary>المُسوّى بردّ نهائي من الصيانة — محسوم ولن يُسلَّم (القسم 11.2)</summary>
        public decimal SettledQty;
        public DateTime? EffectiveDate;
        public bool IsCustom;
        public DateTime TransactionDate;

        /// <summary>المفتوح = المفوتر − المقبول − المُسوّى (يحل محل «المتبقي»)</summary>
        public decimal RemainingQty => InvoicedQty - DeliveredQty - SettledQty;
    }

    /// <summary>
    /// يحمّل جدولة بنود مجموعة فواتير بثلاثة استعلامات مجمعة: ترويسات الفواتير
    /// (الاستحقاق/التاريخ)، البنود (التاريخ الخاص/الفصل)، والمسلَّم من الأحداث
    /// غير الملغاة. الموعد الفعّال يُحسب عبر DeliveryScheduleResolver وحده.
    /// </summary>
    private async Task<List<LineScheduleRaw>> LoadLineSchedulesAsync(
        db24804Context db, List<int> transactionIds)
    {
        var result = new List<LineScheduleRaw>();
        if (transactionIds == null || transactionIds.Count == 0) return result;
        var ids = transactionIds.Distinct().ToList();

        var headers = await db.Transactions.AsNoTracking()
            .Where(t => ids.Contains(t.TransactionId))
            .Select(t => new { t.TransactionId, t.DueDate, t.TransactionDate })
            .ToListAsync();
        var headerById = headers.ToDictionary(h => h.TransactionId);

        var details = await db.TransactionDetails.AsNoTracking()
            .Where(d => ids.Contains(d.TransactionId))
            .Select(d => new
            {
                d.DetailId, d.TransactionId, d.Quantity, d.DeliveryDate, d.DeliveryDateIsCustom,
                ProductName = d.Product != null ? d.Product.ProductName : ""
            })
            .ToListAsync();

        // ⭐ نتائج التسليم: «المسلَّم» في الجدولة = المقبول فقط، والمفتوح يُخصم
        //    منه أيضًا المُسوّى بردّ نهائي — فالبند المُسوّى لا يبقى مستحقًا
        var delivered = await (from i in db.InvoiceDeliveryItems.AsNoTracking()
                               join dl in db.InvoiceDeliveries.AsNoTracking()
                                   on i.DeliveryId equals dl.DeliveryId
                               where ids.Contains(dl.TransactionId) && !dl.IsVoided
                               group i by i.TransactionDetailId into g
                               select new { DetailId = g.Key, Total = g.Sum(x => x.AcceptedQty) })
            .ToListAsync();
        var deliveredByDetail = delivered.ToDictionary(x => x.DetailId, x => x.Total);

        var settled = await db.DeliveryMaintenanceItems.AsNoTracking()
            .Where(m => ids.Contains(m.TransactionId) && !m.IsVoided
                     && m.Status == DeliveryMaintenanceStatuses.ClosedByCredit)
            .GroupBy(m => m.TransactionDetailId)
            .Select(g => new { DetailId = g.Key, Total = g.Sum(m => m.Quantity) })
            .ToListAsync();
        var settledByDetail = settled.ToDictionary(x => x.DetailId, x => x.Total);

        foreach (var d in details)
        {
            headerById.TryGetValue(d.TransactionId, out var h);
            result.Add(new LineScheduleRaw
            {
                TransactionDetailId = d.DetailId,
                TransactionId       = d.TransactionId,
                ProductName         = d.ProductName,
                InvoicedQty         = d.Quantity,
                DeliveredQty        = deliveredByDetail.TryGetValue(d.DetailId, out var q) ? q : 0m,
                SettledQty          = settledByDetail.TryGetValue(d.DetailId, out var sq) ? sq : 0m,
                EffectiveDate       = DeliveryScheduleResolver.EffectiveDate(
                                          d.DeliveryDate, d.DeliveryDateIsCustom, h?.DueDate),
                IsCustom            = d.DeliveryDateIsCustom && d.DeliveryDate.HasValue,
                TransactionDate     = h?.TransactionDate ?? DateTime.MinValue
            });
        }
        return result;
    }

    /// <summary>نسخة آمنة: غياب تخزين الميزة (207/208) يعيد قائمة فارغة بدل كسر الشاشة.</summary>
    private async Task<List<LineScheduleRaw>> SafeLineSchedulesAsync(
        db24804Context db, List<int> transactionIds)
    {
        try { return await LoadLineSchedulesAsync(db, transactionIds); }
        catch (Exception ex) when (IsMissingStorageError(ex))
        {
            _logger.LogWarning("Line scheduling storage is missing — schedule derivation skipped until SQL scripts run.");
            return new List<LineScheduleRaw>();
        }
    }

    /// <summary>
    /// ملخصات جدولة لقائمة التسليمات: مواعيد كل فاتورة المميزة (بصيغة العرض لهذا
    /// المستخدم — المواعيد المتفق عليها تُخفى كتاريخ الاستحقاق) وبنود كل موعد.
    /// </summary>
    public async Task<Dictionary<int, InvoiceScheduleSummaryDto>> GetLineScheduleSummariesBatchAsync(
        List<int> transactionIds)
    {
        var result = new Dictionary<int, InvoiceScheduleSummaryDto>();
        if (transactionIds == null || transactionIds.Count == 0) return result;

        using var db = await _factory.CreateDbContextAsync();
        var lines = await SafeLineSchedulesAsync(db, transactionIds);
        var seeReal = CurrentUserSeesRealDate;
        var buffer = DateBufferDays;

        foreach (var txGroup in lines.GroupBy(l => l.TransactionId))
        {
            var groups = txGroup
                .GroupBy(l => DeliveryScheduleResolver.DisplayDate(
                    l.EffectiveDate, l.TransactionDate, seeReal, buffer))
                .Select(gr => new DeliveryScheduleGroupDto
                {
                    DisplayDate      = gr.Key,
                    LineCount        = gr.Count(),
                    PendingLineCount = gr.Count(l => l.RemainingQty > QtyEpsilon),
                    PendingQty       = gr.Sum(l => l.RemainingQty),
                    HasOverdue       = gr.Any(l => DeliveryScheduleResolver.IsLineOverdue(
                                           DeliveryScheduleResolver.DisplayDate(
                                               l.EffectiveDate, l.TransactionDate, seeReal, buffer),
                                           l.RemainingQty))
                })
                .OrderBy(gr => gr.DisplayDate ?? DateTime.MaxValue)
                .ToList();

            result[txGroup.Key] = new InvoiceScheduleSummaryDto
            {
                TransactionId  = txGroup.Key,
                HasCustomDates = txGroup.Any(l => l.IsCustom),
                Groups         = groups
            };
        }
        return result;
    }

    // ════════════════════════════════════════════════════════
    //        المواعيد المفتوحة — رئيسية «القادمة» + تنبيه الأسبوع
    // ════════════════════════════════════════════════════════

    /// <summary>موعد مفتوح خام: فاتورة × موعد فعّال حقيقي ببنوده المستحقة وكميتها المفتوحة</summary>
    private sealed class OpenAppointmentRaw
    {
        public int TransactionId;
        public string Reference = "";
        public string PartyName = "";
        public DateTime TransactionDate;
        public DateTime RealDate;
        public int DueLineCount;
        public decimal RemainingQty;
    }

    /// <summary>
    /// المواعيد المفتوحة التي موعدها الحقيقي داخل نافذة [from, to): مرشحوها فواتير
    /// بيع غير ملغاة وغير مرتجعة (حالة الـView) وغير مكتملة الإسقاط، وبنودها من
    /// نفس تحميل الجدولة الذي يغذي قائمة التسليمات — فالموعد هنا هو موعد القائمة
    /// نفسه، والبند المستحق هو ذو الكمية المفتوحة (مفوتر − مقبول − مُسوّى).
    /// </summary>
    private async Task<List<OpenAppointmentRaw>> LoadOpenAppointmentsAsync(
        db24804Context db, DateTime realFrom, DateTime realToExclusive)
    {
        var result = new List<OpenAppointmentRaw>();

        // مرشحون أوليون: بند مفصول بموعد داخل النافذة، أو فاتورة استحقاقها داخلها (بنودها الوارثة)
        var customIds = await db.TransactionDetails.AsNoTracking()
            .Where(d => d.DeliveryDateIsCustom && d.DeliveryDate.HasValue
                     && d.DeliveryDate.Value >= realFrom && d.DeliveryDate.Value < realToExclusive)
            .Select(d => d.TransactionId)
            .Distinct()
            .ToListAsync();
        var dueIds = await db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Sale
                     && t.InvoiceStatus != InvoiceStatuses.Cancelled
                     && t.DueDate.HasValue
                     && t.DueDate.Value >= realFrom && t.DueDate.Value < realToExclusive)
            .Select(t => t.TransactionId)
            .ToListAsync();
        var ids = customIds.Union(dueIds).Distinct().ToList();
        if (ids.Count == 0) return result;

        var headers = await db.Transactions.AsNoTracking()
            .Where(t => ids.Contains(t.TransactionId)
                     && t.TransactionType == TransactionTypes.Sale
                     && t.InvoiceStatus != InvoiceStatuses.Cancelled
                     && t.IsDelivered != true)
            .Select(t => new { t.TransactionId, t.ReferenceNumber, t.TransactionDate })
            .ToListAsync();
        if (headers.Count == 0) return result;
        var headerById = headers.ToDictionary(h => h.TransactionId);

        // المرتجع حالة نهائية من الـView — تُستبعد مواعيده وإن بقيت بنوده مفتوحة
        var partyById = (await db.VwSalesDeliveryStatuses.AsNoTracking()
                .Where(v => ids.Contains(v.TransactionId))
                .Select(v => new { v.TransactionId, v.PartyName, v.DeliveryStatus })
                .ToListAsync())
            .Where(v => v.DeliveryStatus != DeliveryEffectiveStatuses.Returned)
            .ToDictionary(v => v.TransactionId, v => v.PartyName);

        var lines = await SafeLineSchedulesAsync(db, headerById.Keys.ToList());

        foreach (var grp in lines
            .Where(l => headerById.ContainsKey(l.TransactionId)
                     && partyById.ContainsKey(l.TransactionId)
                     && l.EffectiveDate.HasValue
                     && l.EffectiveDate.Value >= realFrom && l.EffectiveDate.Value < realToExclusive
                     && l.RemainingQty > QtyEpsilon)
            .GroupBy(l => new { l.TransactionId, Date = l.EffectiveDate!.Value.Date }))
        {
            var h = headerById[grp.Key.TransactionId];
            result.Add(new OpenAppointmentRaw
            {
                TransactionId = grp.Key.TransactionId,
                Reference = string.IsNullOrWhiteSpace(h.ReferenceNumber)
                    ? $"#{grp.Key.TransactionId}" : h.ReferenceNumber!,
                PartyName = partyById[grp.Key.TransactionId],
                TransactionDate = h.TransactionDate,
                RealDate = grp.Key.Date,
                DueLineCount = grp.Count(),
                RemainingQty = grp.Sum(l => l.RemainingQty)
            });
        }
        return result;
    }

    private static UpcomingDeliveryAppointmentDto MapUpcoming(
        OpenAppointmentRaw r, bool seesReal, int buffer)
        => new()
        {
            TransactionId = r.TransactionId,
            Reference = r.Reference,
            PartyName = r.PartyName,
            RealDate = r.RealDate,
            DisplayDate = DeliveryScheduleResolver.DisplayDate(
                r.RealDate, r.TransactionDate, seesReal, buffer) ?? r.RealDate,
            DueLineCount = r.DueLineCount,
            RemainingQty = r.RemainingQty
        };

    /// <summary>
    /// مواعيد قادمة للمستخدم الحالي خلال نافذة أيام — على تواريخه المعروضة هو
    /// (قسم «التسليمات القادمة» في الرئيسية). النافذة الحقيقية تُطلب أوسع بهامش
    /// الإخفاء ثم تُرشَّح على المعروض بدقة.
    /// </summary>
    public async Task<List<UpcomingDeliveryAppointmentDto>> GetUpcomingAppointmentsAsync(int withinDays)
    {
        var today = DateTime.Today;
        var seeReal = CurrentUserSeesRealDate;
        var buffer = DateBufferDays;
        using var db = await _factory.CreateDbContextAsync();

        var raws = await LoadOpenAppointmentsAsync(
            db, today.AddDays(-buffer), today.AddDays(withinDays + buffer + 1));

        return raws
            .Select(r => MapUpcoming(r, seeReal, buffer))
            .Where(a => a.DisplayDate.Date >= today
                     && a.DisplayDate.Date <= today.AddDays(withinDays))
            .OrderBy(a => a.DisplayDate).ThenBy(a => a.PartyName)
            .ToList();
    }

    /// <summary>
    /// المواعيد المفتوحة التي يطابق تاريخها المعروض لفئة بعينها يومًا مستهدفًا —
    /// لتنبيهات ما قبل الموعد: كل فئة تُنبَّه قبل التاريخ الذي تراه هي بأسبوع.
    /// </summary>
    public async Task<List<UpcomingDeliveryAppointmentDto>> GetAppointmentsDueOnAsync(
        DateTime targetDisplayedDate, bool seesRealDate)
    {
        var target = targetDisplayedDate.Date;
        var buffer = DateBufferDays;
        using var db = await _factory.CreateDbContextAsync();

        var raws = await LoadOpenAppointmentsAsync(
            db, target.AddDays(-1), target.AddDays(buffer + 2));

        return raws
            .Select(r => MapUpcoming(r, seesRealDate, buffer))
            .Where(a => a.DisplayDate.Date == target)
            .OrderBy(a => a.PartyName)
            .ToList();
    }

    /// <summary>
    /// فلتر مدى الاستحقاق على مستوى البنود (للقائمة والملخص): الفاتورة تطابق المدى
    /// إن كان لأيٍّ من بنودها موعد فعّال حقيقي داخله — فتظهر الفاتورة في يوم موعد
    /// بندها المفصول وإن كان تاريخها العام في يوم آخر. التواريخ هنا «حقيقية» دائمًا
    /// (المدى مُزاح مسبقًا للمخفيين في ShiftDueDateRange). عند غياب أعمدة الجدولة
    /// يعود لسلوك ما قبل الميزة حرفيًا (تاريخ الفاتورة نفسه من الصف).
    /// </summary>
    private async Task<List<VwSalesDeliveryStatus>> FilterRowsByLineDueRangeAsync(
        db24804Context db, List<VwSalesDeliveryStatus> rows, DateTime? from, DateTime? to)
    {
        if (rows.Count == 0 || (!from.HasValue && !to.HasValue)) return rows;

        var fromD  = from?.Date;
        var toExcl = to?.Date.AddDays(1);
        bool InRange(DateTime? d) => d.HasValue
            && (!fromD.HasValue  || d.Value.Date >= fromD.Value)
            && (!toExcl.HasValue || d.Value.Date <  toExcl.Value);

        List<LineScheduleRaw> lines;
        try
        {
            lines = await LoadLineSchedulesAsync(db, rows.Select(r => r.TransactionId).ToList());
        }
        catch (Exception ex) when (IsMissingStorageError(ex))
        {
            _logger.LogWarning("Line scheduling storage is missing — due filter falls back to invoice dates.");
            return rows.Where(r => InRange(r.DueDate)).ToList();
        }

        var matching = lines.Where(l => InRange(l.EffectiveDate))
            .Select(l => l.TransactionId).ToHashSet();
        return rows.Where(r => matching.Contains(r.TransactionId)).ToList();
    }

    /// <summary>
    /// تضييق SQL أولي لفلتر الاستحقاق: تاريخ الفاتورة داخل المدى أو بند مفصول
    /// بتاريخ داخل المدى — قبل التدقيق الدقيق في الذاكرة على المواعيد الفعّالة.
    /// </summary>
    private IQueryable<VwSalesDeliveryStatus> ApplyDuePreFilter(
        db24804Context db, IQueryable<VwSalesDeliveryStatus> query, DateTime? from, DateTime? to)
    {
        var f = from?.Date;
        var tEx = to?.Date.AddDays(1);
        return query.Where(x =>
            (x.DueDate.HasValue
                && (!f.HasValue   || x.DueDate.Value >= f.Value)
                && (!tEx.HasValue || x.DueDate.Value <  tEx.Value))
            || db.TransactionDetails.Any(d => d.TransactionId == x.TransactionId
                && d.DeliveryDateIsCustom && d.DeliveryDate.HasValue
                && (!f.HasValue   || d.DeliveryDate.Value >= f.Value)
                && (!tEx.HasValue || d.DeliveryDate.Value <  tEx.Value)));
    }

    public async Task<Dictionary<int, DeliveryProgressSummaryDto>> GetDeliveryProgressBatchAsync(
        List<int> transactionIds)
    {
        var result = new Dictionary<int, DeliveryProgressSummaryDto>();
        if (transactionIds == null || transactionIds.Count == 0) return result;
        var ids = transactionIds.Distinct().ToList();

        using var db = await _factory.CreateDbContextAsync();

        // إجمالي المفوتر لكل فاتورة (استعلام مجمع واحد)
        var invoiced = await db.TransactionDetails.AsNoTracking()
            .Where(d => ids.Contains(d.TransactionId))
            .GroupBy(d => d.TransactionId)
            .Select(g => new { TransactionId = g.Key, Total = g.Sum(d => d.Quantity) })
            .ToListAsync();

        // ⭐ نتائج التسليم: المسلَّم = المقبول فقط (حصص القبول)، والاكتمال على
        //    «المفتوح» = مفوتر − مقبول − مُسوّى (القسم 11.2)
        var delivered = await (from i in db.InvoiceDeliveryItems.AsNoTracking()
                               join d in db.InvoiceDeliveries.AsNoTracking()
                                   on i.DeliveryId equals d.DeliveryId
                               where ids.Contains(d.TransactionId) && !d.IsVoided
                               group i by d.TransactionId into g
                               select new { TransactionId = g.Key, Total = g.Sum(x => x.AcceptedQty) })
            .ToListAsync();

        // المُسوّى بردّ نهائي + ما في حلقة الصيانة الآن (استعلامان مجمعان)
        var settled = await db.DeliveryMaintenanceItems.AsNoTracking()
            .Where(m => ids.Contains(m.TransactionId) && !m.IsVoided
                     && m.Status == DeliveryMaintenanceStatuses.ClosedByCredit)
            .GroupBy(m => m.TransactionId)
            .Select(g => new { TransactionId = g.Key, Total = g.Sum(m => m.Quantity) })
            .ToListAsync();
        var inLoopStatuses = DeliveryMaintenanceStatuses.InLoop;
        var inLoop = await db.DeliveryMaintenanceItems.AsNoTracking()
            .Where(m => ids.Contains(m.TransactionId) && !m.IsVoided
                     && inLoopStatuses.Contains(m.Status))
            .GroupBy(m => m.TransactionId)
            .Select(g => new { TransactionId = g.Key, Total = g.Sum(m => m.Quantity) })
            .ToListAsync();

        // الأحداث غير الملغاة نفسها (للعدّ وآخر تاريخ)
        var events = await db.InvoiceDeliveries.AsNoTracking()
            .Where(d => ids.Contains(d.TransactionId) && !d.IsVoided)
            .Select(d => new { d.DeliveryId, d.TransactionId, d.DeliveryDate })
            .ToListAsync();

        var deliveredByTx = delivered.ToDictionary(x => x.TransactionId, x => x.Total);
        var settledByTx = settled.ToDictionary(x => x.TransactionId, x => x.Total);
        var inLoopByTx = inLoop.ToDictionary(x => x.TransactionId, x => x.Total);
        var eventsByTx = events.GroupBy(e => e.TransactionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var inv in invoiced)
        {
            deliveredByTx.TryGetValue(inv.TransactionId, out var delQty);
            eventsByTx.TryGetValue(inv.TransactionId, out var txEvents);
            var hasEvents = txEvents is { Count: > 0 };

            var dto = new DeliveryProgressSummaryDto
            {
                TransactionId    = inv.TransactionId,
                TotalInvoicedQty = inv.Total,
                TotalDeliveredQty = delQty,
                TotalSettledQty  = settledByTx.TryGetValue(inv.TransactionId, out var sq) ? sq : 0m,
                TotalInLoopQty   = inLoopByTx.TryGetValue(inv.TransactionId, out var lq) ? lq : 0m,
                HasEvents        = hasEvents,
                EventCount       = txEvents?.Count ?? 0,
                LastEventDate    = hasEvents ? txEvents!.Max(e => e.DeliveryDate) : null
            };
            dto.EffectiveStatus = DeriveBaseStatus(hasEvents, dto.TotalRemainingQty);
            result[inv.TransactionId] = dto;
        }

        return result;
    }

    public async Task<InvoiceDeliveryProgressDto?> GetDeliveryProgressAsync(int transactionId)
    {
        using var db = await _factory.CreateDbContextAsync();

        var header = await db.Transactions.AsNoTracking()
            .Where(t => t.TransactionId == transactionId)
            .Select(t => new { t.DueDate, t.TransactionDate })
            .FirstOrDefaultAsync();
        if (header == null) return null;

        var details = await db.TransactionDetails.AsNoTracking()
            .Where(d => d.TransactionId == transactionId)
            .Select(d => new
            {
                d.DetailId, d.ProductId, d.Quantity, d.DeliveryDate, d.DeliveryDateIsCustom,
                ProductName = d.Product != null ? d.Product.ProductName : ""
            })
            .ToListAsync();

        var events = await db.InvoiceDeliveries.AsNoTracking()
            .Where(d => d.TransactionId == transactionId)
            .OrderByDescending(d => d.DeliveryDate)
            .ThenByDescending(d => d.DeliveryId)
            .ToListAsync();

        var eventIds = events.Select(e => e.DeliveryId).ToList();
        var items = eventIds.Count == 0
            ? new List<InvoiceDeliveryItem>()
            : await db.InvoiceDeliveryItems.AsNoTracking()
                .Where(i => eventIds.Contains(i.DeliveryId))
                .ToListAsync();

        var voidedIds = events.Where(e => e.IsVoided).Select(e => e.DeliveryId).ToHashSet();
        var nameByDetail = details.ToDictionary(d => d.DetailId, d => d.ProductName);

        // ⭐ نتائج التسليم (11.2): المقبول = حصص القبول فقط؛ والمفتوح يُخصم منه
        //    المُسوّى بردّ نهائي من حلقة الصيانة
        var deliveredByDetail = items
            .Where(i => !voidedIds.Contains(i.DeliveryId))
            .GroupBy(i => i.TransactionDetailId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.AcceptedQty));

        // سجلات صيانة الفاتورة + رسوم التعديل المولودة من أحداثها
        var maintenance = await db.DeliveryMaintenanceItems.AsNoTracking()
            .Where(m => m.TransactionId == transactionId && !m.IsVoided)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
        var modCharges = await db.AdditionalCharges.AsNoTracking()
            .Where(c => c.TransactionId == transactionId && c.SourceDeliveryId != null
                     && c.ChargeType == ChargeTypes.Modification)
            .ToListAsync();
        var chargeById = modCharges.ToDictionary(c => c.ChargeId);
        var chargesByEvent = modCharges.Where(c => c.SourceDeliveryId.HasValue)
            .GroupBy(c => c.SourceDeliveryId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var inLoopByDetail = maintenance
            .Where(m => DeliveryMaintenanceStatuses.IsInLoop(m.Status))
            .GroupBy(m => m.TransactionDetailId)
            .ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));
        var settledByDetail = maintenance
            .Where(m => m.Status == DeliveryMaintenanceStatuses.ClosedByCredit)
            .GroupBy(m => m.TransactionDetailId)
            .ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));

        // التكلفة الداخلية لا تُعرض إلا لأدوارها (11.6) — حتى داخل DTO التقدم
        var httpUserP = _http.HttpContext?.User;
        var canSeeCostP = httpUserP != null && DeliveryPermissions.CanSeeRepairCost(httpUserP);

        DeliveryMaintenanceItemDto MapMaint(DeliveryMaintenanceItem m) => new()
        {
            MaintenanceId       = m.MaintenanceId,
            DeliveryItemId      = m.DeliveryItemId,
            DeliveryId          = m.DeliveryId,
            TransactionId       = m.TransactionId,
            TransactionDetailId = m.TransactionDetailId,
            ProductName         = nameByDetail.TryGetValue(m.TransactionDetailId, out var pn) ? pn : "",
            Quantity            = m.Quantity,
            ReasonClass         = m.ReasonClass,
            IssueDescription    = m.IssueDescription,
            Status              = m.Status,
            Billable            = m.Billable,
            AdditionalChargeId  = m.AdditionalChargeId,
            ChargeStatus        = m.AdditionalChargeId.HasValue
                                  && chargeById.TryGetValue(m.AdditionalChargeId.Value, out var ch0) ? ch0.Status : null,
            ChargeAmount        = m.AdditionalChargeId.HasValue
                                  && chargeById.TryGetValue(m.AdditionalChargeId.Value, out var ch1) ? ch1.ChargeAmount : null,
            RepairCost          = canSeeCostP ? m.RepairCost : null,
            RepairCostNotes     = canSeeCostP ? m.RepairCostNotes : null,
            HandledByEmployeeId = m.HandledByEmployeeId,
            CreatedAt           = m.CreatedAt,
            StartedAt           = m.StartedAt,
            ReadyAt             = m.ReadyAt,
            ClosedAt            = m.ClosedAt,
            StatusBy            = m.StatusBy,
            ResolutionNotes     = m.ResolutionNotes,
            IsVoided            = m.IsVoided
        };

        // ⭐ آخر تاريخ فعلي قُبل فيه من كل بند (أحداث غير ملغاة وحصص مقبولة فقط)
        var dateByEvent = events.ToDictionary(e => e.DeliveryId, e => e.DeliveryDate);
        var lastByDetail = items
            .Where(i => !voidedIds.Contains(i.DeliveryId) && i.AcceptedQty > 0)
            .GroupBy(i => i.TransactionDetailId)
            .ToDictionary(g => g.Key, g => g.Max(i => dateByEvent[i.DeliveryId]));

        // ⭐ جدولة كل بند (موعد معروض لهذا المستخدم) + مخطط × فعلي
        var seeRealP = CurrentUserSeesRealDate;
        var bufferP = DateBufferDays;
        var lines = details.Select(d =>
        {
            var deliveredQty = deliveredByDetail.TryGetValue(d.DetailId, out var q) ? q : 0m;
            var inLoopQty    = inLoopByDetail.TryGetValue(d.DetailId, out var lq) ? lq : 0m;
            var settledQty   = settledByDetail.TryGetValue(d.DetailId, out var sq) ? sq : 0m;
            var remaining    = d.Quantity - deliveredQty - settledQty;   // المفتوح (11.2)
            var display = DeliveryScheduleResolver.DisplayDate(
                DeliveryScheduleResolver.EffectiveDate(d.DeliveryDate, d.DeliveryDateIsCustom, header.DueDate),
                header.TransactionDate, seeRealP, bufferP);
            var last = lastByDetail.TryGetValue(d.DetailId, out var ld) ? ld : (DateTime?)null;
            return new DeliveryProgressLineDto
            {
                TransactionDetailId   = d.DetailId,
                ProductId             = d.ProductId,
                ProductName           = d.ProductName,
                InvoicedQty           = d.Quantity,
                DeliveredQty          = deliveredQty,
                InLoopQty             = inLoopQty,
                SettledQty            = settledQty,
                ScheduledDate         = display,
                ScheduledDateIsCustom = d.DeliveryDateIsCustom && d.DeliveryDate.HasValue,
                IsScheduleOverdue     = DeliveryScheduleResolver.IsLineOverdue(display, remaining),
                LastDeliveredDate     = last,
                DeliveredLate         = remaining <= QtyEpsilon && last.HasValue && display.HasValue
                                          && last.Value.Date > display.Value.Date
            };
        }).ToList();

        var eventDtos = events.Select(e => new DeliveryEventDto
        {
            DeliveryId           = e.DeliveryId,
            DeliveryDate         = e.DeliveryDate,
            DeliveryEmployeeId   = e.DeliveryEmployeeId,
            DeliveryEmployeeName = e.DeliveryEmployeeName,
            Notes                = e.Notes,
            RecordedBy           = e.RecordedBy,
            RecordedAt           = e.RecordedAt,
            IsVoided             = e.IsVoided,
            VoidedBy             = e.VoidedBy,
            VoidedAt             = e.VoidedAt,
            VoidReason           = e.VoidReason,
            CompletedInvoice     = e.CompletedInvoice,
            IsLegacy             = e.IsLegacy,
            Items = items.Where(i => i.DeliveryId == e.DeliveryId)
                .Select(i => new DeliveryEventItemDto
                {
                    TransactionDetailId = i.TransactionDetailId,
                    ProductName = nameByDetail.TryGetValue(i.TransactionDetailId, out var n) ? n : "",
                    Quantity = i.Quantity,
                    AcceptedQty = i.AcceptedQty,
                    ReturnedManufacturingQty = i.ReturnedManufacturingQty,
                    ReturnedHandlingQty = i.ReturnedHandlingQty,
                    ReturnedModificationQty = i.ReturnedModificationQty,
                    DispositionNotes = i.DispositionNotes,
                    MaintenanceId = i.MaintenanceId
                }).ToList(),
            MaintenanceRecords = maintenance
                .Where(m => m.DeliveryId == e.DeliveryId)
                .Select(MapMaint).ToList(),
            ModificationCharges = chargesByEvent.TryGetValue(e.DeliveryId, out var evCharges)
                ? evCharges.Select(c => new DeliveryEventChargeDto
                    {
                        ChargeId = c.ChargeId,
                        Status = c.Status ?? "",
                        ChargeAmount = c.ChargeAmount
                    }).ToList()
                : new List<DeliveryEventChargeDto>()
        }).ToList();

        var hasActiveEvents = events.Any(e => !e.IsVoided);
        var totalInvoiced  = lines.Sum(l => l.InvoicedQty);
        var totalDelivered = lines.Sum(l => l.DeliveredQty);

        var viewRow = await db.VwSalesDeliveryStatuses.AsNoTracking()
            .FirstOrDefaultAsync(v => v.TransactionId == transactionId);

        var dto = new InvoiceDeliveryProgressDto
        {
            TransactionId     = transactionId,
            HasEvents         = hasActiveEvents,
            TotalInvoicedQty  = totalInvoiced,
            TotalDeliveredQty = totalDelivered,
            TotalInLoopQty    = lines.Sum(l => l.InLoopQty),
            TotalSettledQty   = lines.Sum(l => l.SettledQty),
            LastEventDate     = events.Where(e => !e.IsVoided)
                                      .Select(e => (DateTime?)e.DeliveryDate).Max(),
            Lines  = lines,
            Events = eventDtos,
            ReadyMaintenance = maintenance
                .Where(m => m.Status == DeliveryMaintenanceStatuses.ReadyForRedelivery)
                .Select(MapMaint).ToList()
        };
        dto.EffectiveStatus = viewRow?.DeliveryStatus == DeliveryEffectiveStatuses.Returned
            ? DeliveryEffectiveStatuses.Returned
            : DeriveBaseStatus(hasActiveEvents, dto.TotalRemainingQty);
        return dto;
    }

    /// <summary>
    /// ملخص نتائج تسليم فاتورة لشريط تفاصيل الفاتورة (11.5): أحداث بملاحظات +
    /// كمية في الصيانة الآن + رسوم تعديل معلّقة. غياب تخزين النتائج ⇒ null
    /// (لا شريط) — تدهور آمن كسائر قراءات الميزة.
    /// </summary>
    public async Task<InvoiceOutcomesSummaryDto?> GetInvoiceOutcomesSummaryAsync(int transactionId)
    {
        try
        {
            using var db = await _factory.CreateDbContextAsync();

            var activeEventIds = await db.InvoiceDeliveries.AsNoTracking()
                .Where(d => d.TransactionId == transactionId && !d.IsVoided)
                .Select(d => d.DeliveryId)
                .ToListAsync();

            var withRemarks = 0;
            if (activeEventIds.Count > 0)
            {
                var items = await db.InvoiceDeliveryItems.AsNoTracking()
                    .Where(i => activeEventIds.Contains(i.DeliveryId))
                    .Select(i => new
                    {
                        i.DeliveryId,
                        Returned = i.ReturnedManufacturingQty + i.ReturnedHandlingQty
                                 + i.ReturnedModificationQty
                    })
                    .ToListAsync();
                withRemarks = items.GroupBy(i => i.DeliveryId)
                    .Count(g => g.Sum(x => x.Returned) > QtyEpsilon);
            }

            var records = await db.DeliveryMaintenanceItems.AsNoTracking()
                .Where(m => m.TransactionId == transactionId && !m.IsVoided)
                .Select(m => new { m.Quantity, m.Status })
                .ToListAsync();
            var open = records.Where(r => DeliveryMaintenanceStatuses.IsInLoop(r.Status)).ToList();

            var pendingCharges = await db.AdditionalCharges.AsNoTracking()
                .CountAsync(c => c.TransactionId == transactionId
                              && c.ChargeType == ChargeTypes.Modification
                              && c.Status == ChargeStatuses.PendingApproval);

            return new InvoiceOutcomesSummaryDto
            {
                TransactionId = transactionId,
                EventsWithRemarks = withRemarks,
                InMaintenanceQty = open.Sum(r => r.Quantity),
                OpenMaintenanceRecords = open.Count,
                PendingModificationCharges = pendingCharges
            };
        }
        catch (Exception ex) when (IsMissingStorageError(ex))
        {
            _logger.LogWarning("Delivery outcomes storage is missing — invoice outcomes summary skipped until delivery-outcomes.sql runs.");
            return null;
        }
    }

    /// <summary>
    /// إذن تسليم حدث واحد (ورقة الدفعة): بنود هذا الحدث وكمياته فقط، كما خرجت
    /// فعليًا — فلا يوقّع العميل على بضاعة لم تخرج. الملخص التراكمي محسوب
    /// بترتيب الأحداث (التاريخ ثم الرقم) حتى هذا الحدث ضمنًا، من الأحداث غير
    /// الملغاة وحدها. مواعيد البنود بصيغة عرض المستخدم الحالي (نفس إخفاء
    /// الاستحقاق)، وتاريخ الحدث نفسه حقيقة منجزة كما هو.
    /// </summary>
    public async Task<DeliveryEventPermitDto?> GetDeliveryEventPermitAsync(int deliveryId)
    {
        using var db = await _factory.CreateDbContextAsync();

        var ev = await db.InvoiceDeliveries.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DeliveryId == deliveryId);
        if (ev == null || ev.IsVoided) return null;

        var viewRow = await db.VwSalesDeliveryStatuses.AsNoTracking()
            .FirstOrDefaultAsync(v => v.TransactionId == ev.TransactionId);
        if (viewRow == null) return null;

        var party = await db.Parties.AsNoTracking()
            .Where(p => p.PartyId == viewRow.PartyId)
            .Select(p => new { p.Phone, p.Address })
            .FirstOrDefaultAsync();

        var details = await db.TransactionDetails.AsNoTracking()
            .Where(d => d.TransactionId == ev.TransactionId)
            .Select(d => new
            {
                d.DetailId, d.Quantity, d.DeliveryDate, d.DeliveryDateIsCustom,
                ProductName = d.Product != null ? d.Product.ProductName : ""
            })
            .ToListAsync();
        var detailById = details.ToDictionary(d => d.DetailId);

        var eventItems = await db.InvoiceDeliveryItems.AsNoTracking()
            .Where(i => i.DeliveryId == deliveryId)
            .ToListAsync();

        // التراكمي حتى هذا الحدث: أحداث غير ملغاة بترتيب (التاريخ، الرقم)
        var throughIds = await db.InvoiceDeliveries.AsNoTracking()
            .Where(d => d.TransactionId == ev.TransactionId && !d.IsVoided
                     && (d.DeliveryDate < ev.DeliveryDate
                         || (d.DeliveryDate == ev.DeliveryDate && d.DeliveryId <= deliveryId)))
            .Select(d => d.DeliveryId)
            .ToListAsync();
        // ⭐ نتائج التسليم: «المسلَّم حتى هذه الدفعة» = المقبول فعليًا عند العميل
        //    (حصص القبول) — الراجع من دفعة لا يُحتسب مُسلَّمًا في إذنها
        var deliveredThrough = throughIds.Count == 0
            ? 0m
            : await db.InvoiceDeliveryItems.AsNoTracking()
                .Where(i => throughIds.Contains(i.DeliveryId))
                .SumAsync(i => i.AcceptedQty);

        var seeReal = CurrentUserSeesRealDate;
        var buffer = DateBufferDays;
        var (dueShown, _, _) = seeReal
            ? (viewRow.DueDate, viewRow.DaysRemaining, viewRow.DeliveryStatus)
            : MaskValues(viewRow.DueDate, viewRow.TransactionDate, viewRow.DaysRemaining, viewRow.DeliveryStatus);

        return new DeliveryEventPermitDto
        {
            DeliveryId           = ev.DeliveryId,
            TransactionId        = ev.TransactionId,
            TransactionDate      = viewRow.TransactionDate,
            DueDate              = dueShown,
            PartyName            = viewRow.PartyName,
            PartyPhone           = party?.Phone,
            PartyAddress         = party?.Address,
            SalesEmployeeName    = viewRow.EmployeeName,
            DeliveryDate         = ev.DeliveryDate,
            DeliveryEmployeeName = ev.DeliveryEmployeeName,
            Notes                = ev.Notes,
            RecordedBy           = ev.RecordedBy,
            CompletedInvoice     = ev.CompletedInvoice,
            IsLegacy             = ev.IsLegacy,
            TotalInvoicedQty     = details.Sum(d => d.Quantity),
            DeliveredThroughEventQty = deliveredThrough,
            Items = eventItems.Select(i =>
            {
                detailById.TryGetValue(i.TransactionDetailId, out var det);
                var effective = det == null
                    ? (DateTime?)null
                    : DeliveryScheduleResolver.EffectiveDate(
                        det.DeliveryDate, det.DeliveryDateIsCustom, viewRow.DueDate);
                return new DeliveryEventPermitItemDto
                {
                    ProductName = det?.ProductName ?? "",
                    // العميل يوقّع على ما استلمه فعلًا = الحصة المقبولة من البند
                    Quantity = i.AcceptedQty,
                    ScheduledDate = DeliveryScheduleResolver.DisplayDate(
                        effective, viewRow.TransactionDate, seeReal, buffer),
                    ScheduledDateIsCustom = det != null && det.DeliveryDateIsCustom && det.DeliveryDate.HasValue
                };
            }).ToList()
        };
    }

    // مسامحة نقدية عند اختبار «المدفوع يغطي القيمة» (الجنيه وكسوره)
    private const decimal MoneyEpsilon = 0.01m;

    /// <summary>قيمة وحدة بند الفاتورة: إجمالي سطره الصافي (TotalAmount) ÷ كميته المفوترة.</summary>
    private static decimal LineUnitValue(TransactionDetail d)
        => d.Quantity > QtyEpsilon
            ? (d.TotalAmount ?? d.Quantity * d.UnitPrice) / d.Quantity
            : 0m;

    /// <summary>
    /// قيمتا التغطية: قيمة المقبول سابقًا (من خريطة DetailId ← كمية مقبولة في
    /// أحداث غير ملغاة) + قيمة مقبول الدفعة الحالية — بنفس أساس قيمة البند في
    /// بوابة التسجيل وفي عرض الحوار قبل الحفظ، فلا يختلف الحكم بينهما.
    /// </summary>
    private static (decimal PrevValue, decimal BatchValue) EvaluateCoverageValues(
        List<TransactionDetail> details,
        IReadOnlyDictionary<int, decimal> priorAcceptedByDetail,
        IEnumerable<(int DetailId, decimal AcceptedQty)> batchAccepted)
    {
        var unitByDetail = details.ToDictionary(d => d.DetailId, LineUnitValue);
        decimal ValueOf(int detailId, decimal qty)
            => unitByDetail.TryGetValue(detailId, out var u) ? qty * u : 0m;

        var prev  = priorAcceptedByDetail.Sum(kv => ValueOf(kv.Key, kv.Value));
        var batch = batchAccepted.Sum(b => ValueOf(b.DetailId, b.AcceptedQty));
        return (prev, batch);
    }

    /// <summary>أحدث إذن تسليم ممنوح للفاتورة في سجل التدقيق (آلية الإذن الموجودة كما هي).</summary>
    private async Task<AuditLog?> LatestDeliveryGrantAsync(db24804Context db, int transactionId)
        => await db.AuditLogs.AsNoTracking()
            .Where(a => a.TableName == "Transactions"
                     && a.ActionType == ActionDeliveryPermissionGranted
                     && a.PrimaryKeyValue == transactionId.ToString())
            .OrderByDescending(a => a.AuditId)
            .FirstOrDefaultAsync();

    /// <summary>
    /// بوابة التسليم المالية على مستوى الدفعة (قرار المالك): لا تُسجَّل دفعة إلا
    /// إذا كان المدفوع من الفاتورة يغطي قيمة ما قُبل عند العميل سابقًا + قيمة
    /// مقبول هذه الدفعة. فإن لم يغطِّ، فالمسار هو آلية الإذن الموجودة كما هي:
    /// موافقة المدير/مدير الحسابات بنفسه، أو إذن ممنوح موثّق في سجل التدقيق
    /// (يُطلب من شاشات التسليم بـ RequestDeliveryPermissionAsync).
    /// فاتورة بلا متبقٍّ (المدفوع ≥ الإجمالي) تمر دائمًا — كما كان الحال دائمًا.
    /// </summary>
    private async Task<(bool Allowed, string? Error, string? ApprovalNote)> CheckBatchCoverageGateAsync(
        db24804Context db, Transaction transaction, string userName,
        List<TransactionDetail> details,
        IReadOnlyDictionary<int, decimal> priorAcceptedByDetail,
        IEnumerable<(int DetailId, decimal AcceptedQty)> batchAccepted)
    {
        var remaining = transaction.GrandTotal - transaction.PaidAmount;
        if (remaining <= 0) return (true, null, null);

        var (prevValue, batchValue) = EvaluateCoverageValues(
            details, priorAcceptedByDetail, batchAccepted);
        if (transaction.PaidAmount + MoneyEpsilon >= prevValue + batchValue)
            return (true, null, null);   // المدفوع يغطي المسلَّم سابقًا + هذه الدفعة

        var httpUser = _http.HttpContext?.User;
        if (httpUser != null && DeliveryPermissions.CanOverrideRemaining(httpUser))
            return (true, null, $" — تم التسليم بموافقة {userName} رغم أن المدفوع لا يغطي قيمة الدفعة");

        var grant = await LatestDeliveryGrantAsync(db, transaction.TransactionId);
        if (grant != null)
            return (true, null, $" — تم التسليم بموافقة {grant.LoginName} رغم أن المدفوع لا يغطي قيمة الدفعة");

        return (false,
            $"لا يمكن التسليم — المدفوع من الفاتورة ({transaction.PaidAmount:N2} ج) لا يغطي قيمة ما سُلّم سابقًا ({prevValue:N2} ج) + هذه الدفعة ({batchValue:N2} ج). برجاء سداد الفرق أو إرسال طلب إذن تسليم.",
            null);
    }

    /// <summary>
    /// تغطية دفعة مقترحة قبل الحفظ (لعرض حوار التسجيل): نفس تقييم بوابة التسجيل
    /// حرفيًا على البنود المقترحة، مع حالة الإذن/الطلب من مصدر شاشات التفاصيل.
    /// </summary>
    public async Task<DeliveryBatchCoverageDto> GetBatchCoverageAsync(
        int transactionId, List<RecordDeliveryItemDto> items)
    {
        var dto = new DeliveryBatchCoverageDto { TransactionId = transactionId };
        using var db = await _factory.CreateDbContextAsync();

        var transaction = await db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId);
        if (transaction == null) return dto;

        dto.PaidAmount = transaction.PaidAmount;
        dto.GrandTotal = transaction.GrandTotal;

        var details = await db.TransactionDetails.AsNoTracking()
            .Where(d => d.TransactionId == transactionId)
            .ToListAsync();

        // المقبول السابق من الأحداث غير الملغاة (تدهور آمن عند غياب تخزين النتائج)
        var priorAccepted = new Dictionary<int, decimal>();
        try
        {
            var rows = await (from i in db.InvoiceDeliveryItems.AsNoTracking()
                              join dl in db.InvoiceDeliveries.AsNoTracking()
                                  on i.DeliveryId equals dl.DeliveryId
                              where dl.TransactionId == transactionId && !dl.IsVoided
                              group i by i.TransactionDetailId into g
                              select new { DetailId = g.Key, Qty = g.Sum(x => x.AcceptedQty) })
                .ToListAsync();
            priorAccepted = rows.ToDictionary(x => x.DetailId, x => x.Qty);
        }
        catch (Exception ex) when (IsMissingStorageError(ex))
        {
            _logger.LogWarning("Outcomes storage missing — batch coverage treats prior accepted as zero for {TxId}.", transactionId);
        }

        // مقبول الدفعة المقترحة بنفس حلّ خادم التسجيل (AcceptedQty صريح أو مشتق)
        var batch = (items ?? new List<RecordDeliveryItemDto>())
            .Where(i => i.Quantity > 0)
            .Select(i => (i.TransactionDetailId,
                          i.AcceptedQty ?? (i.TotalReturnedQty <= QtyEpsilon ? i.Quantity : 0m)))
            .ToList();

        var (prevValue, batchValue) = EvaluateCoverageValues(details, priorAccepted, batch);
        dto.PreviouslyDeliveredValue = prevValue;
        dto.ThisBatchValue = batchValue;
        dto.Covered = dto.RemainingAmount <= 0
                      || dto.PaidAmount + MoneyEpsilon >= dto.RequiredValue;

        try
        {
            var states = await GetDeliveryPermissionStatesAsync(new List<int> { transactionId });
            if (states.TryGetValue(transactionId, out var st))
            {
                dto.HasGrant = !string.IsNullOrWhiteSpace(st.GrantedBy);
                dto.HasPendingRequest = st.HasPendingRequest;
            }
        }
        catch { /* العرض فقط — بوابة التسجيل هي الحاكم */ }

        var httpUser = _http.HttpContext?.User;
        dto.CurrentUserCanOverride = httpUser != null
            && DeliveryPermissions.CanOverrideRemaining(httpUser);
        dto.GatePasses = dto.Covered || dto.HasGrant || dto.CurrentUserCanOverride;
        return dto;
    }

    /// <summary>
    /// يعيد بناء إسقاط التسليم على صف الفاتورة — يفُوّض للدالة المشتركة الواحدة
    /// (DeliveryProjection، القسم 11.2) التي تستخدمها أيضًا تسويات الصيانة:
    /// الاكتمال ⟺ المفتوح = 0 (مقبول + مُسوّى)، وDeliveredAt = تاريخ حدث
    /// الإكمال أو تاريخ قرار التسوية المُكمل في حالة الاكتمال بالتسوية وحدها.
    /// </summary>
    private Task RecomputeDeliveryProjectionAsync(db24804Context db, Transaction transaction)
        => DeliveryProjection.RecomputeAsync(db, transaction);

    /// <summary>خطة بند واحد داخل حدث تسليم بعد حلّ الحصص والتحقق الشكلي منها.</summary>
    private sealed class RecordItemPlan
    {
        public RecordDeliveryItemDto Source = null!;
        public decimal Accepted;
        public decimal ReturnedTotal => Source.TotalReturnedQty;
    }

    public async Task<(bool Success, string Message)> RecordDeliveryAsync(RecordDeliveryDto dto)
    {
        // ─── تحققات شكلية سريعة ───
        if (dto == null) return (false, "بيانات التسليمة غير مكتملة.");
        if (dto.DeliveryDate.Date > DateTime.Today)
            return (false, "تاريخ التسليم لا يمكن أن يكون في المستقبل.");
        if (dto.DeliveryEmployeeId == null && string.IsNullOrWhiteSpace(dto.DeliveryEmployeeName))
            return (false, "برجاء تحديد مندوب التسليم.");

        // ─── حلّ الحصص (القسم 11.1): مجموع الحصص = المحاوَلة، والوصف إلزامي
        //     مع أي حصة راجعة. بلا حصص مدخلة ⇒ المسار القديم: الكل مقبول.
        var rawItems = (dto.Items ?? new List<RecordDeliveryItemDto>())
            .Where(i => i.Quantity > 0)
            .ToList();
        if (rawItems.Count == 0)
            return (false, "برجاء إدخال كمية تسليم واحدة على الأقل.");

        var plans = new List<RecordItemPlan>();
        foreach (var it in rawItems)
        {
            var returned = it.TotalReturnedQty;
            if (it.ReturnedManufacturingQty < 0 || it.ReturnedHandlingQty < 0
                || it.ReturnedModificationQty < 0 || (it.AcceptedQty.HasValue && it.AcceptedQty.Value < 0))
                return (false, "حصص التسليم لا يمكن أن تكون سالبة.");
            var accepted = it.AcceptedQty ?? (returned <= QtyEpsilon ? it.Quantity : 0m);
            if (Math.Abs(accepted + returned - it.Quantity) > QtyEpsilon)
                return (false, $"مجموع حصص أحد البنود (مقبول {accepted:0.###} + راجع {returned:0.###}) لا يساوي كميته المحاوَلة ({it.Quantity:0.###}) — راجع تقسيم الكمية عند التسليم.");
            if (returned > QtyEpsilon && string.IsNullOrWhiteSpace(it.DispositionNotes))
                return (false, "وصف العيب/التلف/التعديل المطلوب إلزامي عند وجود كمية راجعة في أي بند.");
            if (it.AgreedModificationPrice.HasValue && it.AgreedModificationPrice.Value < 0)
                return (false, "سعر التعديل المتفق عليه لا يمكن أن يكون سالبًا.");
            plans.Add(new RecordItemPlan { Source = it, Accepted = accepted });
        }

        var httpUser = _http.HttpContext?.User;
        if (httpUser?.Identity?.IsAuthenticated == true && !DeliveryPermissions.CanConfirmDelivery(httpUser))
            return (false, "ليس لديك صلاحية تسجيل التسليمات.");

        var userName = !string.IsNullOrWhiteSpace(dto.UserName)
            ? dto.UserName
            : httpUser?.Identity?.Name ?? "system";
        var eventGuid = dto.EventGuid == Guid.Empty ? Guid.NewGuid() : dto.EventGuid;

        using var db = await _factory.CreateDbContextAsync();

        // ─── منع التكرار: نفس EventGuid سُجّل من قبل ⇒ نجاح صامت بالحدث الأصلي ───
        try
        {
            var dup = await db.InvoiceDeliveries.AsNoTracking()
                .FirstOrDefaultAsync(d => d.EventGuid == eventGuid);
            if (dup != null)
                return (true, "تم تسجيل هذه التسليمة مسبقًا — لم يتم إنشاء حدث مكرر.");
        }
        catch (Exception ex) when (IsMissingStorageError(ex))
        {
            return (false, MissingOutcomesMessage);
        }

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var transaction = await db.Transactions
                .FirstOrDefaultAsync(t => t.TransactionId == dto.TransactionId);
            if (transaction == null)
                return (false, "الفاتورة غير موجودة");
            if (transaction.InvoiceStatus == InvoiceStatuses.Cancelled)
                return (false, "لا يمكن تسليم فاتورة ملغية.");
            if (transaction.TransactionType != TransactionTypes.Sale)
                return (false, "تسجيل التسليمات متاح لفواتير البيع فقط.");

            var viewRow = await db.VwSalesDeliveryStatuses.AsNoTracking()
                .FirstOrDefaultAsync(v => v.TransactionId == dto.TransactionId);
            if (viewRow?.DeliveryStatus == DeliveryEffectiveStatuses.Returned)
                return (false, "لا يمكن تسجيل تسليمة لفاتورة مرتجعة.");
            // ملحوظة: لا فحص على IsDelivered هنا — الأحداث هي مصدر الحقيقة، واكتمال
            // الفاتورة يثبته فحص المفتوح لكل بند أدناه (كمية موجبة على مفتوح صفري تُرفض).

            // تكرار داخل المعاملة (إرسالان متزامنان بنفس المفتاح)
            if (await db.InvoiceDeliveries.AnyAsync(d => d.EventGuid == eventGuid))
            {
                await tx.CommitAsync();
                return (true, "تم تسجيل هذه التسليمة مسبقًا — لم يتم إنشاء حدث مكرر.");
            }

            // البنود والمقبول/المُسوّى/في الصيانة تُحسب داخل المعاملة — لا اعتماد على أرقام العميل
            var details = await db.TransactionDetails
                .Where(d => d.TransactionId == dto.TransactionId)
                .ToListAsync();
            var detailById = details.ToDictionary(d => d.DetailId);
            var nameByDetail = await db.TransactionDetails.AsNoTracking()
                .Where(d => d.TransactionId == dto.TransactionId)
                .Select(d => new { d.DetailId, ProductName = d.Product != null ? d.Product.ProductName : "" })
                .ToDictionaryAsync(d => d.DetailId, d => d.ProductName);

            var acceptedRows = await (from i in db.InvoiceDeliveryItems.AsNoTracking()
                                      join d in db.InvoiceDeliveries.AsNoTracking()
                                          on i.DeliveryId equals d.DeliveryId
                                      where d.TransactionId == dto.TransactionId && !d.IsVoided
                                      group i by i.TransactionDetailId into g
                                      select new { DetailId = g.Key, Qty = g.Sum(x => x.AcceptedQty) })
                .ToListAsync();
            var acceptedNow = acceptedRows.ToDictionary(x => x.DetailId, x => x.Qty);
            foreach (var d in details)
                if (!acceptedNow.ContainsKey(d.DetailId)) acceptedNow[d.DetailId] = 0m;

            var txMaintenance = await db.DeliveryMaintenanceItems
                .Where(m => m.TransactionId == dto.TransactionId && !m.IsVoided)
                .ToListAsync();
            var settledNow = txMaintenance
                .Where(m => m.Status == DeliveryMaintenanceStatuses.ClosedByCredit)
                .GroupBy(m => m.TransactionDetailId)
                .ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));
            var inLoopNow = txMaintenance
                .Where(m => DeliveryMaintenanceStatuses.IsInLoop(m.Status))
                .GroupBy(m => m.TransactionDetailId)
                .ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));
            var maintenanceById = txMaintenance.ToDictionary(m => m.MaintenanceId);
            decimal SettledOf(int detailId) => settledNow.TryGetValue(detailId, out var v) ? v : 0m;
            decimal InLoopOf(int detailId) => inLoopNow.TryGetValue(detailId, out var v) ? v : 0m;

            // ─── بوابة التغطية المالية على مستوى الدفعة: المدفوع مقابل المقبول
            //     السابق (لقطة acceptedNow قبل أن تضيف حلقة التحقق مقبول هذا
            //     الحدث) + مقبول هذه الدفعة — تُفرض هنا في قلب التسجيل لا في الواجهة
            var priorAccepted = new Dictionary<int, decimal>(acceptedNow);
            var (gateOk, gateError, approvalNote) = await CheckBatchCoverageGateAsync(
                db, transaction, userName, details, priorAccepted,
                plans.Select(p => (p.Source.TransactionDetailId, p.Accepted)));
            if (!gateOk) return (false, gateError!);

            // ─── تحقق الكميات وقاعدة التحميل (11.2): المحاوَلة ≤ المتاح الآن
            //     (المفتوح − في الصيانة)، وبنود إعادة التسليم تُسحب من سجلها الجاهز
            var usedMaintenanceIds = new HashSet<int>();
            foreach (var plan in plans)
            {
                var it = plan.Source;
                if (!detailById.TryGetValue(it.TransactionDetailId, out var detail))
                    return (false, "أحد بنود التسليمة لا يخص هذه الفاتورة.");

                if (it.MaintenanceId is int mid)
                {
                    if (!usedMaintenanceIds.Add(mid))
                        return (false, "سجل الصيانة نفسه مربوط ببندين في نفس التسليمة.");
                    if (!maintenanceById.TryGetValue(mid, out var rec))
                        return (false, "سجل الصيانة المرتبط غير موجود لهذه الفاتورة.");
                    if (rec.TransactionDetailId != it.TransactionDetailId)
                        return (false, "سجل الصيانة المرتبط يخص بندًا آخر.");
                    if (rec.Status != DeliveryMaintenanceStatuses.ReadyForRedelivery)
                        return (false, "سجل الصيانة المرتبط ليس «جاهزًا لإعادة التسليم».");
                    if (it.Quantity > rec.Quantity + QtyEpsilon)
                        return (false, $"كمية إعادة التسليم تتجاوز كمية سجل الصيانة ({rec.Quantity:0.###}).");
                    // الجزء المعالَج يخرج من السجل؛ الراجع منه يعود بسجلات جديدة أدناه
                    inLoopNow[it.TransactionDetailId] = InLoopOf(it.TransactionDetailId) - it.Quantity;
                }
                else
                {
                    var available = detail.Quantity - acceptedNow[it.TransactionDetailId]
                                    - SettledOf(it.TransactionDetailId) - InLoopOf(it.TransactionDetailId);
                    if (it.Quantity > available + QtyEpsilon)
                    {
                        var loop = InLoopOf(it.TransactionDetailId);
                        return (false, loop > QtyEpsilon
                            ? $"كمية التسليم تتجاوز المتاح للتحميل الآن لأحد البنود (المتاح {available:0.###} — منها {loop:0.###} في الصيانة لا تُحمَّل مرتين)."
                            : $"كمية التسليم تتجاوز المتاح للتحميل الآن لأحد البنود (المتاح {available:0.###}).");
                    }
                }

                acceptedNow[it.TransactionDetailId] += plan.Accepted;
                inLoopNow[it.TransactionDetailId] = InLoopOf(it.TransactionDetailId) + plan.ReturnedTotal;
            }

            var totalInvoiced   = details.Sum(d => d.Quantity);
            var totalAcceptedAfter = acceptedNow.Values.Sum();
            var totalSettled    = settledNow.Values.Sum();
            var openAfter       = totalInvoiced - totalAcceptedAfter - totalSettled;
            var completes       = openAfter <= QtyEpsilon;

            var ev = new InvoiceDelivery
            {
                TransactionId        = dto.TransactionId,
                DeliveryDate         = dto.DeliveryDate,
                DeliveryEmployeeId   = dto.DeliveryEmployeeId,
                DeliveryEmployeeName = dto.DeliveryEmployeeName,
                Notes   = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
                RecordedBy           = userName,
                RecordedAt           = DateTime.Now,
                IsVoided             = false,
                CompletedInvoice     = completes,
                IsLegacy             = false,
                EventGuid            = eventGuid
            };
            var itemEntities = new List<(RecordItemPlan Plan, InvoiceDeliveryItem Entity)>();
            foreach (var plan in plans)
            {
                var it = plan.Source;
                var entity = new InvoiceDeliveryItem
                {
                    TransactionDetailId       = it.TransactionDetailId,
                    Quantity                  = it.Quantity,
                    AcceptedQty               = plan.Accepted,
                    ReturnedManufacturingQty  = it.ReturnedManufacturingQty,
                    ReturnedHandlingQty       = it.ReturnedHandlingQty,
                    ReturnedModificationQty   = it.ReturnedModificationQty,
                    DispositionNotes          = string.IsNullOrWhiteSpace(it.DispositionNotes)
                                                    ? null : it.DispositionNotes.Trim(),
                    MaintenanceId             = it.MaintenanceId
                };
                ev.InvoiceDeliveryItems.Add(entity);
                itemEntities.Add((plan, entity));
            }
            db.InvoiceDeliveries.Add(ev);
            await db.SaveChangesAsync();

            // ─── حلقة الصيانة + رسوم التعديل (نفس المعاملة — القسم 11.3/11.4):
            //     كل حصة راجعة ⇒ سجل صيانة، وحصة التعديل ⇒ رسم معلّق بلا خزينة
            var createdRecords = new List<DeliveryMaintenanceItem>();
            var createdCharges = new List<AdditionalCharge>();
            foreach (var (plan, entity) in itemEntities)
            {
                var it = plan.Source;
                var productName = nameByDetail.TryGetValue(it.TransactionDetailId, out var pn) ? pn : "";

                DeliveryMaintenanceItem NewRecord(string cls, decimal qty, bool billable) => new()
                {
                    DeliveryItemId      = entity.DeliveryItemId,
                    DeliveryId          = ev.DeliveryId,
                    TransactionId       = dto.TransactionId,
                    TransactionDetailId = it.TransactionDetailId,
                    Quantity            = qty,
                    ReasonClass         = cls,
                    IssueDescription    = entity.DispositionNotes ?? "",
                    Status              = DeliveryMaintenanceStatuses.Pending,
                    Billable            = billable,
                    CreatedAt           = DateTime.Now,
                    StatusBy            = userName
                };

                if (it.ReturnedManufacturingQty > QtyEpsilon)
                {
                    var r = NewRecord(DeliveryDispositions.ManufacturingDefect, it.ReturnedManufacturingQty, false);
                    db.DeliveryMaintenanceItems.Add(r);
                    createdRecords.Add(r);
                }
                if (it.ReturnedHandlingQty > QtyEpsilon)
                {
                    var r = NewRecord(DeliveryDispositions.HandlingDamage, it.ReturnedHandlingQty, false);
                    db.DeliveryMaintenanceItems.Add(r);
                    createdRecords.Add(r);
                }
                if (it.ReturnedModificationQty > QtyEpsilon)
                {
                    var charge = new AdditionalCharge
                    {
                        TransactionId     = dto.TransactionId,
                        PartyId           = transaction.PartyId,
                        ChargeType        = ChargeTypes.Modification,
                        ChargeDescription = entity.DispositionNotes,
                        ChargeAmount      = it.AgreedModificationPrice is > 0 ? it.AgreedModificationPrice : null,
                        Status            = ChargeStatuses.PendingApproval,
                        SourceDeliveryId  = ev.DeliveryId,
                        Notes             = $"رسوم تعديل من تسليمة — {productName}",
                        CreatedBy         = userName,
                        CreatedAt         = DateTime.Now
                    };
                    db.AdditionalCharges.Add(charge);
                    createdCharges.Add(charge);

                    var r = NewRecord(DeliveryDispositions.CustomerModification, it.ReturnedModificationQty, true);
                    r.AdditionalCharge = charge;   // تُملأ AdditionalChargeId عند الحفظ بالتنقل
                    db.DeliveryMaintenanceItems.Add(r);
                    createdRecords.Add(r);
                }

                // ─── إغلاق/تجزئة سجل الصيانة المرتبط ببند إعادة تسليم (11.3)
                if (it.MaintenanceId is int linkId
                    && maintenanceById.TryGetValue(linkId, out var linked))
                {
                    var addressed = it.Quantity;
                    var unaddressed = linked.Quantity - addressed;
                    if (plan.Accepted > QtyEpsilon)
                    {
                        if (unaddressed > QtyEpsilon)
                        {
                            // تجزئة: سجل جديد بالباقي غير المعالَج يرث حالة «جاهز»
                            var remainder = new DeliveryMaintenanceItem
                            {
                                DeliveryItemId      = linked.DeliveryItemId,
                                DeliveryId          = linked.DeliveryId,
                                TransactionId       = linked.TransactionId,
                                TransactionDetailId = linked.TransactionDetailId,
                                Quantity            = unaddressed,
                                ReasonClass         = linked.ReasonClass,
                                IssueDescription    = linked.IssueDescription,
                                Status              = DeliveryMaintenanceStatuses.ReadyForRedelivery,
                                Billable            = linked.Billable,
                                AdditionalChargeId  = linked.AdditionalChargeId,
                                RepairCost          = linked.RepairCost,
                                RepairCostNotes     = linked.RepairCostNotes,
                                HandledByEmployeeId = linked.HandledByEmployeeId,
                                CreatedAt           = DateTime.Now,
                                ReadyAt             = linked.ReadyAt,
                                StatusBy            = userName,
                                ResolutionNotes     = $"باقي سجل صيانة #{linked.MaintenanceId} بعد إعادة تسليم جزئية"
                            };
                            db.DeliveryMaintenanceItems.Add(remainder);
                        }
                        linked.Quantity = plan.Accepted;
                        linked.Status   = DeliveryMaintenanceStatuses.Redelivered;
                        linked.ClosedAt = DateTime.Now;
                        linked.StatusBy = userName;
                        linked.ResolutionNotes = "أُعيد تسليمه وقُبل عند العميل";
                    }
                    else
                    {
                        // لم يُقبل منه شيء: الراجع تغطيه السجلات الجديدة أعلاه،
                        // والسجل الأصلي إما يبقى «جاهزًا» بباقيه أو يُلغى منطقيًا
                        if (unaddressed > QtyEpsilon)
                        {
                            linked.Quantity = unaddressed;
                        }
                        else
                        {
                            linked.IsVoided  = true;
                            linked.VoidedBy  = userName;
                            linked.VoidedAt  = DateTime.Now;
                            linked.VoidReason = "أُعيدت محاولة تسليمه ورُدّت من جديد — تتابع كميته في سجلات التسليمة الجديدة";
                        }
                    }
                }
            }
            await db.SaveChangesAsync();

            // الإسقاط على الفاتورة داخل نفس المعاملة
            await RecomputeDeliveryProjectionAsync(db, transaction);
            await db.SaveChangesAsync();
            await tx.CommitAsync();

            // التدقيق والإشعارات بعد الالتزام — فشلهما لا يُسقط العملية
            var totalReturned = plans.Sum(p => p.ReturnedTotal);
            try
            {
                await _audit.LogAsync<object>("Transactions", ActionDeliveryEventRecorded,
                    dto.TransactionId.ToString(), null,
                    new
                    {
                        ev.DeliveryId, ev.DeliveryDate, ev.DeliveryEmployeeId, ev.DeliveryEmployeeName,
                        Items = plans.Select(p => new
                        {
                            p.Source.TransactionDetailId, p.Source.Quantity,
                            AcceptedQty = p.Accepted,
                            p.Source.ReturnedManufacturingQty,
                            p.Source.ReturnedHandlingQty,
                            p.Source.ReturnedModificationQty,
                            p.Source.DispositionNotes,
                            p.Source.MaintenanceId
                        }).ToList(),
                        MaintenanceRecordIds = createdRecords.Select(r => r.MaintenanceId).ToList(),
                        ModificationChargeIds = createdCharges.Select(c => c.ChargeId).ToList(),
                        CompletedInvoice = completes,
                        ApprovalNote = approvalNote
                    }, userName);

                foreach (var charge in createdCharges)
                    await _audit.LogAsync<object>("AdditionalCharges", ActionModificationChargeCreated,
                        charge.ChargeId.ToString(), null,
                        new
                        {
                            charge.ChargeId, charge.TransactionId, charge.PartyId,
                            charge.ChargeAmount, charge.Status, charge.SourceDeliveryId,
                            charge.ChargeDescription
                        }, userName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audit failed for delivery event {Id}", ev.DeliveryId);
            }

            var courierNote = string.IsNullOrWhiteSpace(dto.DeliveryEmployeeName)
                ? "" : $" — مندوب التسليم: {dto.DeliveryEmployeeName}";
            // محتوى هذه الدفعة تحديدًا (بنود وكميات) + الحالة الناتجة — لإشعار التسليم
            var batchQty      = plans.Sum(p => p.Source.Quantity);
            var batchAccepted = plans.Sum(p => p.Accepted);
            var batchNote = $"هذه الدفعة: {plans.Count} بند بكمية إجمالية {batchQty:0.###} — مقبول منها {batchAccepted:0.###}{(totalReturned > QtyEpsilon ? $" وراجع {totalReturned:0.###}" : "")}";
            await NotifyDeliveryEventAsync(dto.TransactionId,
                completes ? "✅ اكتمل تسليم الفاتورة" : "🚚 تسليمة جزئية",
                completes
                    ? $"اكتمل تسليم الفاتورة بواسطة {userName} — {batchNote} — الحالة: اكتمل التسليم (إجمالي مقبول {totalAcceptedAfter:0.###}{(totalSettled > QtyEpsilon ? $" + {totalSettled:0.###} مُسوّاة بردّ نهائي" : "")}){courierNote}"
                    : $"تسليمة بواسطة {userName} — {batchNote} — الحالة: جزئي (مقبول حتى الآن {totalAcceptedAfter:0.###} من {totalInvoiced:0.###}، مفتوح {openAfter:0.###}){courierNote}",
                userName, dto.DeliveryEmployeeId, includeProductionManager: true);

            // ⭐ إشعارات النتائج (11.6): مرتجعات للمصنع والبائع + رسم تعديل للحسابات
            if (totalReturned > QtyEpsilon)
            {
                var mfg = plans.Sum(p => p.Source.ReturnedManufacturingQty);
                var hnd = plans.Sum(p => p.Source.ReturnedHandlingQty);
                var mod = plans.Sum(p => p.Source.ReturnedModificationQty);
                await NotifyDeliveryReturnsAsync(dto.TransactionId,
                    $"↩️ مرتجع صيانة من تسليمة — عيب تصنيع {mfg:0.###} · تلف مناولة {hnd:0.###} · تعديل عميل {mod:0.###}",
                    userName, transaction.EmpId);
            }
            if (createdCharges.Count > 0)
            {
                await NotifyModificationChargePendingAsync(dto.TransactionId,
                    createdCharges.Count, userName);
            }

            var outcomeNote = totalReturned > QtyEpsilon
                ? $" — منها راجعة للصيانة {totalReturned:0.###}" : "";
            return (true, completes
                ? "تم تسجيل التسليمة واكتمل تسليم الفاتورة بالكامل."
                : $"تم تسجيل التسليمة — مفتوح {openAfter:0.###} من {totalInvoiced:0.###}{outcomeNote}.");
        }
        catch (Exception ex) when (IsMissingStorageError(ex))
        {
            return (false, MissingOutcomesMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RecordDeliveryAsync failed for transaction {Id}", dto.TransactionId);
            return (false, $"حدث خطأ أثناء تسجيل التسليمة: {ex.Message}");
        }
    }

    /// <summary>
    /// إشعار مرتجعات تسليمة (11.6): بائع الفاتورة (مستخدم موظفها) + أدوار
    /// ProductionManager/FactoryManager — بلا مبالغ، بتفصيلة التصنيف والكميات.
    /// </summary>
    private async Task NotifyDeliveryReturnsAsync(
        int transactionId, string breakdown, string actorUserName, int? salesEmployeeId)
    {
        try
        {
            using var db = await _factory.CreateDbContextAsync();

            var info = await db.Transactions.AsNoTracking()
                .Where(t => t.TransactionId == transactionId)
                .Select(t => new { t.ReferenceNumber, t.PartyId })
                .FirstOrDefaultAsync();
            var partyName = info == null ? "غير محدد" : await db.Parties.AsNoTracking()
                .Where(p => p.PartyId == info.PartyId)
                .Select(p => p.PartyName)
                .FirstOrDefaultAsync() ?? "غير محدد";

            var message = $"الفاتورة {info?.ReferenceNumber ?? $"#{transactionId}"} — العميل {partyName}: {breakdown}. راجع شاشة صيانة التسليمات.";

            await _notify.NotifyRoleAsync("↩️ مرتجع صيانة من تسليمة", message, SystemRoles.ProductionManager,
                actorUserName, "delivery-maintenance", "Transactions", transactionId);
            await _notify.NotifyRoleAsync("↩️ مرتجع صيانة من تسليمة", message, SystemRoles.FactoryManager,
                actorUserName, "delivery-maintenance", "Transactions", transactionId);

            if (salesEmployeeId.HasValue)
            {
                var sellerUser = await db.Users.AsNoTracking()
                    .Where(u => u.EmployeeId == salesEmployeeId.Value && u.IsActive == true)
                    .Select(u => u.Username)
                    .FirstOrDefaultAsync();
                if (!string.IsNullOrWhiteSpace(sellerUser))
                    await _notify.AddAsync("↩️ مرتجع صيانة من تسليمة", message, sellerUser!, actorUserName,
                        "delivery-maintenance", "Transactions", transactionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send delivery returns notification for transaction {Id}", transactionId);
        }
    }

    /// <summary>إشعار رسم تعديل جديد بانتظار الاعتماد/التسعير (11.6): المدير + مدير الحسابات.</summary>
    private async Task NotifyModificationChargePendingAsync(
        int transactionId, int chargeCount, string actorUserName)
    {
        try
        {
            using var db = await _factory.CreateDbContextAsync();

            var info = await db.Transactions.AsNoTracking()
                .Where(t => t.TransactionId == transactionId)
                .Select(t => new { t.ReferenceNumber, t.PartyId })
                .FirstOrDefaultAsync();
            var partyName = info == null ? "غير محدد" : await db.Parties.AsNoTracking()
                .Where(p => p.PartyId == info.PartyId)
                .Select(p => p.PartyName)
                .FirstOrDefaultAsync() ?? "غير محدد";

            var message = $"الفاتورة {info?.ReferenceNumber ?? $"#{transactionId}"} — العميل {partyName}: " +
                          $"{chargeCount} رسم تعديل بانتظار الاعتماد/التسعير من تسليمة سجّلها {actorUserName}. راجع قائمة الرسوم الإضافية.";

            await _notify.NotifyRoleAsync("💰 رسوم تعديل بانتظار الاعتماد", message, SystemRoles.Admin,
                actorUserName, "additional-charges", "Transactions", transactionId);
            await _notify.NotifyRoleAsync("💰 رسوم تعديل بانتظار الاعتماد", message, SystemRoles.AccountManager,
                actorUserName, "additional-charges", "Transactions", transactionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send modification charge notification for transaction {Id}", transactionId);
        }
    }

    public async Task<(bool Success, string Message)> VoidDeliveryEventAsync(
        int deliveryId, string reason, string userName)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return (false, "سبب الإلغاء إلزامي.");

        // الافتراضي الآمن (قرار المالك D2 عند غياب قرار أوسع): المدير ومدير الحسابات فقط
        var httpUser = _http.HttpContext?.User;
        if (httpUser == null || !DeliveryPermissions.CanOverrideRemaining(httpUser))
            return (false, "إلغاء التسليمات متاح للمدير ومدير الحسابات فقط.");

        using var db = await _factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var ev = await db.InvoiceDeliveries
                .FirstOrDefaultAsync(d => d.DeliveryId == deliveryId);
            if (ev == null) return (false, "التسليمة غير موجودة.");
            if (ev.IsVoided) return (false, "هذه التسليمة ملغاة بالفعل.");

            var oldSnapshot = new
            {
                ev.DeliveryId, ev.TransactionId, ev.DeliveryDate,
                ev.DeliveryEmployeeId, ev.DeliveryEmployeeName,
                ev.CompletedInvoice, ev.IsLegacy
            };

            // ─── نتائج التسليم (11.2): قيود إلغاء حدث له مرتجعات/رسوم ───
            var evItems = await db.InvoiceDeliveryItems
                .Where(i => i.DeliveryId == deliveryId)
                .ToListAsync();

            // حدث إعادة تسليم أغلق سجلات صيانة بقبوله — إلغاؤه يفسد الحلقة
            if (evItems.Any(i => i.MaintenanceId != null))
                return (false, "لا يمكن إلغاء تسليمة إعادة تسليم أغلقت سجلات صيانة — راجع شاشة صيانة التسليمات لتصحيح الحالة أولًا.");

            // سجلات الصيانة المولودة من هذا الحدث: إن تحرك أيٌّ منها عن
            // «بانتظار الصيانة» يُمنع الإلغاء؛ وإلا تُلغى معه في نفس المعاملة
            var bornRecords = await db.DeliveryMaintenanceItems
                .Where(m => m.DeliveryId == deliveryId && !m.IsVoided)
                .ToListAsync();
            if (bornRecords.Any(m => m.Status != DeliveryMaintenanceStatuses.Pending))
                return (false, "لا يمكن إلغاء هذه التسليمة — بدأ العمل على صيانة بنود راجعة منها. أنهِ سجلات الصيانة أو أغلقها أولًا من شاشة صيانة التسليمات.");

            // رسوم التعديل المولودة من الحدث: المعتمدة/المحصّلة/المطبقة تمنع
            // الإلغاء، والمعلّقة تُرفض تلقائيًا معه (بلا أي حركة خزينة أبدًا)
            var bornCharges = await db.AdditionalCharges
                .Where(c => c.SourceDeliveryId == deliveryId)
                .ToListAsync();
            if (bornCharges.Any(c => c.Status == ChargeStatuses.Approved
                                  || c.Status == ChargeStatuses.Paid
                                  || c.Status == ChargeStatuses.Applied))
                return (false, "لا يمكن إلغاء هذه التسليمة — رسم التعديل المرتبط بها اعتُمد أو حُصّل أو طُبّق. عالِج الرسم أولًا من قائمة الرسوم الإضافية.");

            foreach (var rec in bornRecords)
            {
                rec.IsVoided   = true;
                rec.VoidedBy   = userName;
                rec.VoidedAt   = DateTime.Now;
                rec.VoidReason = "أُلغي حدث التسليم المصدر";
            }
            foreach (var chg in bornCharges.Where(c => c.Status == ChargeStatuses.PendingApproval))
            {
                chg.Status = ChargeStatuses.Rejected;
                chg.Notes = string.IsNullOrWhiteSpace(chg.Notes)
                    ? "رُفض تلقائيًا — أُلغيت التسليمة المصدر"
                    : $"{chg.Notes} | رُفض تلقائيًا — أُلغيت التسليمة المصدر";
            }

            // إلغاء منطقي فقط + إعادة بناء الإسقاط داخل نفس المعاملة
            ev.IsVoided   = true;
            ev.VoidedBy   = userName;
            ev.VoidedAt   = DateTime.Now;
            ev.VoidReason = reason.Trim();
            await db.SaveChangesAsync();

            var transaction = await db.Transactions
                .FirstOrDefaultAsync(t => t.TransactionId == ev.TransactionId);
            if (transaction != null)
            {
                await RecomputeDeliveryProjectionAsync(db, transaction);
                await db.SaveChangesAsync();
            }
            await tx.CommitAsync();

            try
            {
                await _audit.LogAsync<object>("Transactions", ActionDeliveryEventVoided,
                    ev.TransactionId.ToString(), oldSnapshot,
                    new { Voided = true, Reason = ev.VoidReason, VoidedBy = userName }, userName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audit failed for voided delivery event {Id}", deliveryId);
            }

            await NotifyDeliveryEventAsync(ev.TransactionId, "↩️ أُلغيت تسليمة",
                $"أُلغيت تسليمة بتاريخ {ev.DeliveryDate:yyyy/MM/dd} بواسطة {userName} — السبب: {ev.VoidReason}",
                userName, ev.DeliveryEmployeeId);

            return (true, "تم إلغاء التسليمة وإعادة حساب حالة الفاتورة.");
        }
        catch (Exception ex) when (IsMissingStorageError(ex))
        {
            return (false, MissingOutcomesMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VoidDeliveryEventAsync failed for delivery {Id}", deliveryId);
            return (false, $"حدث خطأ أثناء إلغاء التسليمة: {ex.Message}");
        }
    }

    /// <summary>
    /// إشعارات حدث تسليم (تسجيل/إكمال/إلغاء) — Admin ومدير الحسابات ومدير المبيعات
    /// دائمًا، ومدير الإنتاج عند تسجيل تسليمة فعلية (includeProductionManager) بطلب
    /// المالك: يُبلَّغ بكل تسليمة تتم. كل دور يُشعَر مرة واحدة فقط لكل حدث.
    /// </summary>
    private async Task NotifyDeliveryEventAsync(
        int transactionId, string title, string detail, string actorUserName, int? courierEmployeeId,
        bool includeProductionManager = false)
    {
        try
        {
            using var db = await _factory.CreateDbContextAsync();

            var info = await db.Transactions.AsNoTracking()
                .Where(t => t.TransactionId == transactionId)
                .Select(t => new { t.ReferenceNumber, t.PartyId })
                .FirstOrDefaultAsync();

            var partyName = info == null ? "غير محدد" : await db.Parties.AsNoTracking()
                .Where(p => p.PartyId == info.PartyId)
                .Select(p => p.PartyName)
                .FirstOrDefaultAsync() ?? "غير محدد";

            var message = $"الفاتورة {info?.ReferenceNumber ?? $"#{transactionId}"} — العميل {partyName}: {detail}";

            await _notify.NotifyRoleAsync(title, message, SystemRoles.Admin, actorUserName,
                "sales-delivery-status", "Transactions", transactionId);
            await _notify.NotifyRoleAsync(title, message, SystemRoles.AccountManager, actorUserName,
                "sales-delivery-status", "Transactions", transactionId);
            if (includeProductionManager)
            {
                await _notify.NotifyRoleAsync(title, message, SystemRoles.ProductionManager, actorUserName,
                    "sales-delivery-status", "Transactions", transactionId);
            }
            await _notify.NotifyRoleAsync(title, message, SystemRoles.SalesManager, actorUserName,
                "sales-delivery-status", "Transactions", transactionId);

            if (courierEmployeeId.HasValue)
            {
                var recipientUser = await db.Users.AsNoTracking()
                    .Where(u => u.EmployeeId == courierEmployeeId.Value && u.IsActive == true)
                    .Select(u => u.Username)
                    .FirstOrDefaultAsync();

                if (!string.IsNullOrWhiteSpace(recipientUser))
                    await _notify.AddAsync(title, message, recipientUser!, actorUserName,
                        "sales-delivery-status", "Transactions", transactionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send delivery event notification for transaction {Id}", transactionId);
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

    /// <summary>
    /// كشف التسليمات مدفوعًا بمواعيد البنود: سطر لكل (فاتورة × موعد) فيه بنود
    /// مستحقة لم تكتمل. إن غاب تخزين الميزة (سكربتات SQL لم تُشغَّل بعد) يعود
    /// للمسار القديم على مستوى الفاتورة حتى لا يتعطل الكشف أبدًا.
    /// </summary>
    public async Task<List<DailyDeliverySheetRowDto>> GetDailyDeliverySheetAsync(DateTime? from, DateTime? to)
    {
        try
        {
            return await GetDailyDeliverySheetByLinesAsync(from, to);
        }
        catch (Exception ex) when (IsMissingStorageError(ex))
        {
            _logger.LogWarning("Line scheduling storage is missing — daily sheet falls back to invoice-level dates.");
            return await GetDailyDeliverySheetLegacyAsync(from, to);
        }
    }

    /// <summary>
    /// الكشف بمواعيد البنود: «المستحق في يوم» = بنود موعدها الفعّال ذلك اليوم ولم
    /// تكتمل من أحداث التسليم. المواعيد المعروضة/الأيام تُحسب لكل مجموعة بنود
    /// بنفس قاعدة إخفاء الاستحقاق (الإزاحة للمخفيين تتم هنا على المدى أولًا).
    /// </summary>
    private async Task<List<DailyDeliverySheetRowDto>> GetDailyDeliverySheetByLinesAsync(
        DateTime? from, DateTime? to)
    {
        using var db = await _factory.CreateDbContextAsync();

        var seeReal = CurrentUserSeesRealDate;
        DateTime? realFrom = from, realTo = to;
        if (!seeReal)
        {
            var b = DateBufferDays;
            realFrom = from?.AddDays(b);
            realTo = to?.AddDays(b);
        }
        var fromD  = realFrom?.Date;
        var toExcl = realTo?.Date.AddDays(1);

        // مرشحون: كل غير المسلَّمة في الـView (نفس قاعدة الكشف القديم)
        var candidates = await db.VwSalesDeliveryStatuses.AsNoTracking()
            .Where(x => x.DeliveryStatus != "تم التسليم")
            .OrderBy(x => x.TransactionId)
            .Take(500)
            .Select(x => new
            {
                x.TransactionId, x.TransactionDate, x.PartyId, x.PartyName,
                x.DeliveryStatus, x.DeliveryEmployeeName
            })
            .ToListAsync();

        // جدولة البنود (ترمي عند غياب التخزين — يلتقطها الغلاف العام ويرجع للقديم)
        var lines = await LoadLineSchedulesAsync(db, candidates.Select(c => c.TransactionId).ToList());
        var linesByTx = lines.GroupBy(l => l.TransactionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var partyIds = candidates.Select(c => c.PartyId).Distinct().ToList();
        var parties = await db.Parties.AsNoTracking()
            .Where(p => partyIds.Contains(p.PartyId))
            .Select(p => new { p.PartyId, p.Phone, p.Address })
            .ToDictionaryAsync(p => p.PartyId);

        var sheet = new List<DailyDeliverySheetRowDto>();
        foreach (var c in candidates)
        {
            if (!linesByTx.TryGetValue(c.TransactionId, out var txLines)) continue;

            // اكتملت بالأحداث وإن تأخر إسقاط الـView — لا مكان لها في الكشف
            var pending = txLines.Where(l => l.RemainingQty > QtyEpsilon).ToList();
            if (pending.Count == 0) continue;

            var hasEvents = txLines.Any(l => l.DeliveredQty > QtyEpsilon);
            var isReturned = c.DeliveryStatus == DeliveryEffectiveStatuses.Returned;

            foreach (var grp in pending.GroupBy(l => l.EffectiveDate?.Date))
            {
                var realDate = grp.Key;
                if (fromD.HasValue || toExcl.HasValue)
                {
                    if (!realDate.HasValue) continue;
                    if (fromD.HasValue  && realDate.Value <  fromD.Value)  continue;
                    if (toExcl.HasValue && realDate.Value >= toExcl.Value) continue;
                }

                var display = DeliveryScheduleResolver.DisplayDate(
                    realDate, c.TransactionDate, seeReal, DateBufferDays);
                var grpRemaining = grp.Sum(l => l.RemainingQty);
                var overdue = !isReturned
                    && DeliveryScheduleResolver.IsLineOverdue(display, grpRemaining);

                var status = isReturned
                    ? DeliveryEffectiveStatuses.Returned
                    : overdue
                        ? DeliveryEffectiveStatuses.Overdue
                        : hasEvents
                            ? DeliveryEffectiveStatuses.Partial
                            : DeliveryEffectiveStatuses.Pending;

                sheet.Add(new DailyDeliverySheetRowDto
                {
                    TransactionId = c.TransactionId,
                    PartyName = c.PartyName,
                    Phone = parties.TryGetValue(c.PartyId, out var pt) ? pt.Phone : null,
                    Address = parties.TryGetValue(c.PartyId, out var pa) ? pa.Address : null,
                    DueDate = display,
                    DaysRemaining = display.HasValue
                        ? (display.Value.Date - DateTime.Today).Days : (int?)null,
                    DeliveryStatus = status,
                    DeliveryEmployeeName = c.DeliveryEmployeeName,
                    IsPartial = hasEvents && !isReturned,
                    TotalInvoicedQty = grp.Sum(l => l.InvoicedQty),
                    TotalRemainingQty = grpRemaining,
                    DueLinesSummary = string.Join("، ",
                        grp.Select(l => $"{l.ProductName} × {l.RemainingQty:0.###}")),
                    DueLineCount = grp.Count()
                });
            }
        }

        return sheet
            .OrderBy(r => r.DueDate ?? DateTime.MaxValue)
            .ThenBy(r => r.TransactionId)
            .Take(200)
            .ToList();
    }

    /// <summary>المسار القديم للكشف (بتاريخ الفاتورة) — احتياطي لغياب تخزين الميزة فقط.</summary>
    private async Task<List<DailyDeliverySheetRowDto>> GetDailyDeliverySheetLegacyAsync(DateTime? from, DateTime? to)
    {
        using var db = await _factory.CreateDbContextAsync();

        // ⭐ فلتر الكشف بتاريخ الاستحقاق — للمخفيين يُزاح +الهامش ليتطابق مع التواريخ المعروضة لهم
        var seeReal = CurrentUserSeesRealDate;
        if (!seeReal)
        {
            var b = DateBufferDays;
            from = from?.AddDays(b);
            to = to?.AddDays(b);
        }

        var q = db.VwSalesDeliveryStatuses.AsNoTracking()
            .Where(x => x.DeliveryStatus != "تم التسليم");

        if (from.HasValue) q = q.Where(x => x.DueDate >= from.Value);
        if (to.HasValue)   q = q.Where(x => x.DueDate < to.Value.AddDays(1));

        var rows = await q
            .OrderBy(x => x.DueDate ?? DateTime.MaxValue)
            .ThenBy(x => x.TransactionId)
            .Take(500)
            .Select(x => new
            {
                x.TransactionId, x.TransactionDate, x.PartyId, x.PartyName, x.DueDate,
                x.DaysRemaining, x.DeliveryStatus, x.DeliveryEmployeeName
            })
            .ToListAsync();

        // ⭐ التسليم الجزئي: الكشف يضم كل فاتورة متبقّيها > 0 — الجزئية تدخل
        // بعلامتها ومتبقّيها، والمكتملة بالأحداث تخرج وإن تأخر إسقاط الـView.
        var progress = await SafeProgressBatchAsync(rows.Select(r => r.TransactionId).ToList());

        var partyIds = rows.Select(r => r.PartyId).Distinct().ToList();
        var parties = await db.Parties.AsNoTracking()
            .Where(p => partyIds.Contains(p.PartyId))
            .Select(p => new { p.PartyId, p.Phone, p.Address })
            .ToDictionaryAsync(p => p.PartyId);

        var sheet = new List<DailyDeliverySheetRowDto>();
        foreach (var r in rows)
        {
            var due = r.DueDate;
            var days = r.DaysRemaining;
            var status = r.DeliveryStatus;
            if (!seeReal)
                (due, days, status) = MaskValues(due, r.TransactionDate, days, status);

            var isPartial = false;
            decimal invoicedQty = 0, remainingQty = 0;
            if (progress.TryGetValue(r.TransactionId, out var p))
            {
                invoicedQty = p.TotalInvoicedQty;
                remainingQty = p.TotalRemainingQty;

                if (status != DeliveryEffectiveStatuses.Returned && p.HasEvents)
                {
                    if (remainingQty <= QtyEpsilon)
                        continue;   // اكتملت بالأحداث — لا مكان لها في كشف غير المسلَّمة
                    isPartial = true;
                    if (status != DeliveryEffectiveStatuses.Overdue)
                        status = DeliveryEffectiveStatuses.Partial;
                }
            }

            sheet.Add(new DailyDeliverySheetRowDto
            {
                TransactionId = r.TransactionId,
                PartyName = r.PartyName,
                Phone = parties.TryGetValue(r.PartyId, out var pt) ? pt.Phone : null,
                Address = parties.TryGetValue(r.PartyId, out var pa) ? pa.Address : null,
                DueDate = due,
                DaysRemaining = days,
                DeliveryStatus = status,
                DeliveryEmployeeName = r.DeliveryEmployeeName,
                IsPartial = isPartial,
                TotalInvoicedQty = invoicedQty,
                TotalRemainingQty = remainingQty
            });

            if (sheet.Count >= 200) break;
        }

        return sheet;
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
            await _notify.NotifyRoleAsync(title, message, SystemRoles.GeneralManager, dto.UserName,
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

    // ════════════════════════════════════════════════════════
    //        طلبات تعديل مواعيد التسليم (تعديل / تقسيم)
    // ════════════════════════════════════════════════════════

    /// <summary>اسم عرض الفاتورة في الرسائل: المرجع إن وجد وإلا رقمها.</summary>
    private static string InvoiceLabel(Transaction t)
        => !string.IsNullOrWhiteSpace(t.ReferenceNumber) ? t.ReferenceNumber! : $"#{t.TransactionId}";

    private static ScheduleChangeRequestDto MapScheduleRequest(
        DeliveryScheduleChangeRequest r, IReadOnlyDictionary<int, string> nameByDetail) => new()
        {
            Id = r.Id,
            TransactionId = r.TransactionId,
            RequesterUserName = r.RequesterUserName,
            RequesterName = r.RequesterName,
            Reason = r.Reason,
            ProposedDueDate = r.ProposedDueDate,
            Status = r.Status,
            CreatedAt = r.CreatedAt,
            DecidedBy = r.DecidedBy,
            DecidedAt = r.DecidedAt,
            DecisionNote = r.DecisionNote,
            Lines = r.Lines.OrderBy(l => l.Id).Select(l => new ScheduleChangeRequestLineDto
            {
                TransactionDetailId = l.TransactionDetailId,
                ProductName = nameByDetail.TryGetValue(l.TransactionDetailId, out var pn) ? pn : "",
                ProposedDate = l.ProposedDate,
                ProposedIsCustom = l.ProposedIsCustom
            }).ToList()
        };

    public async Task<(bool Success, string Message)> CreateScheduleChangeRequestAsync(
        CreateScheduleChangeRequestDto dto)
    {
        // 🔒 الخدمة هي مصدر الحقيقة: مسئول التسليمات ومدير الإنتاج فقط
        var httpUser = _http.HttpContext?.User;
        if (httpUser == null || !DeliveryPermissions.CanRequestScheduleChange(httpUser))
            return (false, "طلب تعديل مواعيد التسليم متاح لمسئول التسليمات ومدير الإنتاج فقط.");

        if (dto == null || dto.TransactionId <= 0)
            return (false, "بيانات الطلب غير مكتملة.");
        if (string.IsNullOrWhiteSpace(dto.Reason))
            return (false, "سبب طلب تعديل المواعيد إلزامي.");

        // سطر واحد لكل بند (الأخير يغلب عند التكرار)
        var inputLines = (dto.Lines ?? new List<ScheduleChangeLineInputDto>())
            .GroupBy(l => l.TransactionDetailId)
            .Select(g => g.Last())
            .ToList();
        if (!dto.ProposedDueDate.HasValue && inputLines.Count == 0)
            return (false, "برجاء إدخال تعديل واحد على الأقل: تاريخ كلي جديد للفاتورة أو موعد جديد لبند.");

        var requester = httpUser.Identity?.Name;
        if (string.IsNullOrWhiteSpace(requester)) requester = dto.RequesterUserName;
        if (string.IsNullOrWhiteSpace(requester)) requester = "system";

        using var db = await _factory.CreateDbContextAsync();
        try
        {
            var t = await db.Transactions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TransactionId == dto.TransactionId);
            if (t == null) return (false, "الفاتورة غير موجودة.");
            if (t.InvoiceStatus == InvoiceStatuses.Cancelled)
                return (false, "لا يمكن طلب تعديل مواعيد لفاتورة ملغية.");
            if (t.TransactionType != TransactionTypes.Sale)
                return (false, "طلبات تعديل المواعيد متاحة لفواتير البيع فقط.");
            if (t.IsDelivered == true)
                return (false, "الفاتورة مكتملة التسليم — لا حاجة لتعديل مواعيدها.");

            // منع تكرار الطلب المعلّق لنفس الفاتورة
            var hasPending = await db.DeliveryScheduleChangeRequests.AsNoTracking()
                .AnyAsync(r => r.TransactionId == dto.TransactionId
                            && r.Status == DeliveryScheduleRequestStatuses.Pending);
            if (hasPending)
                return (false, "يوجد طلب تعديل مواعيد معلّق بالفعل لهذه الفاتورة — بانتظار قرار المدير أو مدير الحسابات.");

            // البنود المقترحة يجب أن تخص هذه الفاتورة
            var details = await db.TransactionDetails.AsNoTracking()
                .Where(d => d.TransactionId == dto.TransactionId)
                .Select(d => new { d.DetailId, ProductName = d.Product != null ? d.Product.ProductName : "" })
                .ToListAsync();
            var detailIds = details.Select(d => d.DetailId).ToHashSet();
            if (inputLines.Any(l => !detailIds.Contains(l.TransactionDetailId)))
                return (false, "أحد البنود المقترحة لا يخص هذه الفاتورة.");

            // اكتمال الفاتورة وبنود مكتملة: المفتوح (مفوتر − مقبول − مُسوّى) من أحداث
            // التسليم هو الحقيقة — بند مفتوحه صفر لا يُقترح له موعد جديد
            var progress = await SafeProgressAsync(dto.TransactionId);
            if (progress != null)
            {
                if (progress.HasEvents && progress.TotalRemainingQty <= QtyEpsilon)
                    return (false, "الفاتورة مكتملة التسليم — لا حاجة لتعديل مواعيدها.");
                var openByDetail = progress.Lines.ToDictionary(l => l.TransactionDetailId, l => l.RemainingQty);
                var nameByDetail = details.ToDictionary(d => d.DetailId, d => d.ProductName);
                foreach (var l in inputLines)
                    if (openByDetail.TryGetValue(l.TransactionDetailId, out var open) && open <= QtyEpsilon)
                        return (false, $"البند «{nameByDetail[l.TransactionDetailId]}» اكتمل تسليمه — لا يمكن تعديل موعده.");
            }

            var (_, requesterFullName) = await GetEmployeeByUserNameAsync(requester);

            var request = new DeliveryScheduleChangeRequest
            {
                TransactionId = dto.TransactionId,
                RequesterUserName = requester,
                RequesterName = requesterFullName,
                Reason = dto.Reason.Trim(),
                ProposedDueDate = dto.ProposedDueDate?.Date,
                Status = DeliveryScheduleRequestStatuses.Pending,
                CreatedAt = DateTime.Now,
                Lines = inputLines.Select(l =>
                {
                    var (nd, nc) = DeliveryScheduleResolver.Normalize(l.ProposedDate, l.ProposedIsCustom);
                    return new DeliveryScheduleChangeRequestLine
                    {
                        TransactionDetailId = l.TransactionDetailId,
                        ProposedDate = nd,
                        ProposedIsCustom = nc
                    };
                }).ToList()
            };
            db.DeliveryScheduleChangeRequests.Add(request);
            await db.SaveChangesAsync();

            await _audit.LogAsync<object>("Transactions", ActionScheduleChangeRequested,
                dto.TransactionId.ToString(), null,
                new
                {
                    RequestId = request.Id,
                    Reason = request.Reason,
                    ProposedDueDate = request.ProposedDueDate,
                    Lines = request.Lines.Select(l => new { l.TransactionDetailId, l.ProposedDate, l.ProposedIsCustom }).ToList()
                }, requester);

            var v = await db.VwSalesDeliveryStatuses.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TransactionId == dto.TransactionId);
            var partyName = v?.PartyName ?? $"#{dto.TransactionId}";
            var msg = $"طلب تعديل مواعيد تسليم الفاتورة {InvoiceLabel(t)} — العميل {partyName}. " +
                      $"السبب: {request.Reason} — مقدّم الطلب: {requesterFullName ?? requester}.";

            await _notify.NotifyRoleAsync("طلب تعديل مواعيد تسليم", msg, "Admin",
                requester, formName: "sales-delivery-status", relatedTable: "Transactions", relatedId: dto.TransactionId);
            await _notify.NotifyRoleAsync("طلب تعديل مواعيد تسليم", msg, "AccountManager",
                requester, formName: "sales-delivery-status", relatedTable: "Transactions", relatedId: dto.TransactionId);
            await _notify.NotifyRoleAsync("طلب تعديل مواعيد تسليم", msg, "GeneralManager",
                requester, formName: "sales-delivery-status", relatedTable: "Transactions", relatedId: dto.TransactionId);

            _logger.LogInformation("Schedule change requested for {TxId} by {User} (request {ReqId})",
                dto.TransactionId, requester, request.Id);
            return (true, "تم إرسال طلب تعديل المواعيد للمدير العام ومدير الحسابات — سيصلك إشعار عند البتّ فيه.");
        }
        catch (Exception ex) when (IsMissingTablesError(ex))
        {
            return (false, MissingScheduleRequestsMessage);
        }
    }

    public async Task<List<ScheduleChangeRequestDto>> GetScheduleChangeRequestsAsync(int transactionId)
    {
        using var db = await _factory.CreateDbContextAsync();
        try
        {
            var requests = await db.DeliveryScheduleChangeRequests.AsNoTracking()
                .Where(r => r.TransactionId == transactionId)
                .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
                .Include(r => r.Lines)
                .ToListAsync();
            if (requests.Count == 0) return new List<ScheduleChangeRequestDto>();

            var nameByDetail = await db.TransactionDetails.AsNoTracking()
                .Where(d => d.TransactionId == transactionId)
                .Select(d => new { d.DetailId, ProductName = d.Product != null ? d.Product.ProductName : "" })
                .ToDictionaryAsync(d => d.DetailId, d => d.ProductName);

            return requests.Select(r => MapScheduleRequest(r, nameByDetail)).ToList();
        }
        catch (Exception ex) when (IsMissingTablesError(ex))
        {
            _logger.LogWarning("Schedule-change request tables are missing — history skipped until SQL script runs.");
            return new List<ScheduleChangeRequestDto>();
        }
    }

    public async Task<Dictionary<int, ScheduleChangePendingStateDto>> GetPendingScheduleRequestStatesAsync(
        List<int> transactionIds)
    {
        var result = new Dictionary<int, ScheduleChangePendingStateDto>();
        if (transactionIds == null || transactionIds.Count == 0) return result;

        using var db = await _factory.CreateDbContextAsync();
        try
        {
            var rows = await db.DeliveryScheduleChangeRequests.AsNoTracking()
                .Where(r => transactionIds.Contains(r.TransactionId)
                         && r.Status == DeliveryScheduleRequestStatuses.Pending)
                .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
                .Select(r => new { r.Id, r.TransactionId, r.RequesterUserName, r.RequesterName, r.CreatedAt })
                .ToListAsync();

            foreach (var row in rows)
            {
                if (result.ContainsKey(row.TransactionId)) continue;   // الأحدث أولًا — أول صف هو المعلّق الفعّال
                result[row.TransactionId] = new ScheduleChangePendingStateDto
                {
                    TransactionId = row.TransactionId,
                    HasPendingRequest = true,
                    RequestId = row.Id,
                    RequestedBy = row.RequesterName ?? row.RequesterUserName,
                    RequestedAt = row.CreatedAt
                };
            }
            return result;
        }
        catch (Exception ex) when (IsMissingTablesError(ex))
        {
            _logger.LogWarning("Schedule-change request tables are missing — pending states skipped until SQL script runs.");
            return result;
        }
    }

    public async Task<ScheduleChangeApprovalDto?> GetScheduleChangeApprovalDataAsync(int requestId)
    {
        // 🔒 شاشة الاعتماد تعرض التواريخ الحقيقية — للمُعتمِدين فقط (المدير ومدير الحسابات،
        //    وكلاهما من مستثنيي الإخفاء أصلًا)؛ لا تحويل بين مخفي وحقيقي في أي اتجاه.
        var httpUser = _http.HttpContext?.User;
        if (httpUser == null || !DeliveryPermissions.CanDecideScheduleChange(httpUser))
            return null;

        using var db = await _factory.CreateDbContextAsync();
        try
        {
            var r = await db.DeliveryScheduleChangeRequests.AsNoTracking()
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == requestId);
            if (r == null) return null;

            var t = await db.Transactions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TransactionId == r.TransactionId);
            if (t == null) return null;

            var partyName = await db.Parties.AsNoTracking()
                .Where(p => p.PartyId == t.PartyId)
                .Select(p => p.PartyName)
                .FirstOrDefaultAsync() ?? $"#{t.PartyId}";

            var details = await db.TransactionDetails.AsNoTracking()
                .Where(d => d.TransactionId == r.TransactionId)
                .Select(d => new
                {
                    d.DetailId, d.Quantity, d.DeliveryDate, d.DeliveryDateIsCustom,
                    ProductName = d.Product != null ? d.Product.ProductName : ""
                })
                .ToListAsync();

            var nameByDetail = details.ToDictionary(d => d.DetailId, d => d.ProductName);

            var progress = await SafeProgressAsync(r.TransactionId);
            var openByDetail = progress?.Lines.ToDictionary(l => l.TransactionDetailId, l => l.RemainingQty)
                               ?? new Dictionary<int, decimal>();

            return new ScheduleChangeApprovalDto
            {
                Request = MapScheduleRequest(r, nameByDetail),
                PartyName = partyName,
                ReferenceNumber = t.ReferenceNumber,
                CurrentDueDate = t.DueDate,
                CurrentLines = details.Select(d => new ScheduleChangeCurrentLineDto
                {
                    TransactionDetailId = d.DetailId,
                    ProductName = d.ProductName,
                    // الموعد الحقيقي كما هو مخزّن — بلا أي إخفاء عرض
                    CurrentDate = DeliveryScheduleResolver.EffectiveDate(d.DeliveryDate, d.DeliveryDateIsCustom, t.DueDate),
                    CurrentIsCustom = d.DeliveryDateIsCustom && d.DeliveryDate.HasValue,
                    RemainingQty = openByDetail.TryGetValue(d.DetailId, out var open) ? open : d.Quantity
                }).ToList()
            };
        }
        catch (Exception ex) when (IsMissingTablesError(ex))
        {
            _logger.LogWarning("Schedule-change request tables are missing — approval data skipped until SQL script runs.");
            return null;
        }
    }

    public async Task<(bool Success, string Message)> DecideScheduleChangeRequestAsync(
        int requestId, bool approve, List<ScheduleChangeFinalLineDto> finalLines,
        DateTime? finalDueDate, string? note, string deciderUser)
    {
        // 🔒 الخدمة هي مصدر الحقيقة: المدير ومدير الحسابات فقط
        var httpUser = _http.HttpContext?.User;
        if (httpUser == null || !DeliveryPermissions.CanDecideScheduleChange(httpUser))
            return (false, "البتّ في طلبات تعديل المواعيد متاح للمدير ومدير الحسابات فقط.");

        var decider = !string.IsNullOrWhiteSpace(deciderUser)
            ? deciderUser
            : httpUser.Identity?.Name ?? "system";

        if (!approve && string.IsNullOrWhiteSpace(note))
            return (false, "سبب الرفض إلزامي عند رفض طلب تعديل المواعيد.");

        using var db = await _factory.CreateDbContextAsync();
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var req = await db.DeliveryScheduleChangeRequests
                .Include(r => r.Lines)
                .FirstOrDefaultAsync(r => r.Id == requestId);
            if (req == null) return (false, "الطلب غير موجود.");
            if (req.Status != DeliveryScheduleRequestStatuses.Pending)
                return (false, "هذا الطلب سبق البتّ فيه — لا يمكن اعتماده أو رفضه مرة أخرى.");

            var invoice = await db.Transactions
                .FirstOrDefaultAsync(t => t.TransactionId == req.TransactionId);
            if (invoice == null) return (false, "الفاتورة غير موجودة.");
            if (invoice.InvoiceStatus == InvoiceStatuses.Cancelled)
                return (false, "الفاتورة أُلغيت بعد تقديم الطلب — لا يمكن البتّ في الطلب الآن؛ يمكن لصاحبه سحبه.");

            if (!approve)
            {
                req.Status = DeliveryScheduleRequestStatuses.Rejected;
                req.DecidedBy = decider;
                req.DecidedAt = DateTime.Now;
                req.DecisionNote = note!.Trim();
                await db.SaveChangesAsync();
                await tx.CommitAsync();

                await _audit.LogAsync<object>("Transactions", ActionScheduleChangeRejected,
                    invoice.TransactionId.ToString(), null,
                    new { RequestId = req.Id, Reason = req.DecisionNote }, decider);

                await _notify.AddAsync("رُفض طلب تعديل مواعيد تسليم",
                    $"رُفض طلبك لتعديل مواعيد الفاتورة {InvoiceLabel(invoice)} بواسطة {decider}. سبب الرفض: {req.DecisionNote}",
                    req.RequesterUserName, decider,
                    formName: "sales-delivery-status", relatedTable: "Transactions", relatedId: invoice.TransactionId);

                _logger.LogInformation("Schedule change request {ReqId} rejected by {User}", requestId, decider);
                return (true, "تم رفض طلب تعديل المواعيد وإشعار صاحبه بسبب الرفض.");
            }

            // ─── الاعتماد: القيم النهائية للمُعتمِد هي ما يُكتب (تُبنى من المقترح
            //     في الشاشة ويمكنه تعديلها) — تُطبق كلها في هذه المعاملة الواحدة ───
            var finals = (finalLines ?? new List<ScheduleChangeFinalLineDto>())
                .GroupBy(l => l.TransactionDetailId)
                .Select(g => g.Last())
                .ToList();

            var proposalIds = req.Lines.Select(l => l.TransactionDetailId).ToHashSet();
            if (finals.Any(l => !proposalIds.Contains(l.TransactionDetailId)))
                return (false, "أحد بنود القرار لم يكن ضمن بنود الطلب الأصلي — أعد فتح الطلب.");
            if (!finalDueDate.HasValue && finals.Count == 0)
                return (false, "لا توجد قيم نهائية لتطبيقها — حدّد تاريخًا كليًا أو بندًا واحدًا على الأقل.");

            var detailRows = await db.TransactionDetails
                .Where(d => d.TransactionId == req.TransactionId)
                .ToListAsync();
            var detailById = detailRows.ToDictionary(d => d.DetailId);
            if (finals.Any(l => !detailById.ContainsKey(l.TransactionDetailId)))
                return (false, "أحد بنود الطلب لم يعد ضمن بنود الفاتورة الحالية — لا يمكن تطبيق القرار.");

            var headerBefore = invoice.DueDate;
            var lineChanges = new List<(int DetailId, DateTime? BeforeDate, bool BeforeCustom, DateTime? AfterDate, bool AfterCustom)>();

            if (finalDueDate.HasValue)
                invoice.DueDate = finalDueDate.Value.Date;

            foreach (var f in finals)
            {
                var d = detailById[f.TransactionDetailId];
                var (nd, nc) = DeliveryScheduleResolver.Normalize(f.FinalDate, f.FinalIsCustom);
                if (d.DeliveryDate == nd && d.DeliveryDateIsCustom == nc) continue;
                lineChanges.Add((d.DetailId, d.DeliveryDate, d.DeliveryDateIsCustom, nd, nc));
                d.DeliveryDate = nd;
                d.DeliveryDateIsCustom = nc;
            }

            req.Status = DeliveryScheduleRequestStatuses.Approved;
            req.DecidedBy = decider;
            req.DecidedAt = DateTime.Now;
            req.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            await db.SaveChangesAsync();
            await tx.CommitAsync();

            await _audit.LogAsync<object>("Transactions", ActionScheduleChangeApproved,
                invoice.TransactionId.ToString(),
                new
                {
                    DueDate = headerBefore,
                    Lines = lineChanges.Select(c => new { c.DetailId, Date = c.BeforeDate, IsCustom = c.BeforeCustom }).ToList()
                },
                new
                {
                    RequestId = req.Id,
                    DueDate = invoice.DueDate,
                    Lines = lineChanges.Select(c => new { c.DetailId, Date = c.AfterDate, IsCustom = c.AfterCustom }).ToList()
                }, decider);

            await _notify.AddAsync("اعتُمد طلب تعديل مواعيد تسليم",
                $"اعتُمد طلبك لتعديل مواعيد الفاتورة {InvoiceLabel(invoice)} بواسطة {decider} وطُبّقت المواعيد النهائية على الفاتورة." +
                (string.IsNullOrWhiteSpace(req.DecisionNote) ? "" : $" ملاحظة: {req.DecisionNote}"),
                req.RequesterUserName, decider,
                formName: "sales-delivery-status", relatedTable: "Transactions", relatedId: invoice.TransactionId);

            // إشعار مسئول مبيعات الفاتورة إن كان شخصًا آخر غير الطالب والمُعتمِد
            if (invoice.EmpId.HasValue)
            {
                var salesUser = await db.Users.AsNoTracking()
                    .Where(u => u.EmployeeId == invoice.EmpId.Value && u.IsActive == true)
                    .Select(u => u.Username)
                    .FirstOrDefaultAsync();
                if (!string.IsNullOrWhiteSpace(salesUser)
                    && !string.Equals(salesUser, req.RequesterUserName, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(salesUser, decider, StringComparison.OrdinalIgnoreCase))
                {
                    await _notify.AddAsync("تم تعديل مواعيد تسليم فاتورة",
                        $"اعتُمد طلب تعديل مواعيد الفاتورة {InvoiceLabel(invoice)} وطُبّقت المواعيد النهائية بواسطة {decider}. برجاء مراجعة جدولة الفاتورة.",
                        salesUser!, decider,
                        formName: "sales-delivery-status", relatedTable: "Transactions", relatedId: invoice.TransactionId);
                }
            }

            _logger.LogInformation("Schedule change request {ReqId} approved and applied by {User}", requestId, decider);
            return (true, "تم اعتماد الطلب وتطبيق المواعيد النهائية على الفاتورة وبنودها.");
        }
        catch (Exception ex) when (IsMissingTablesError(ex))
        {
            return (false, MissingScheduleRequestsMessage);
        }
    }

    public async Task<(bool Success, string Message)> WithdrawScheduleChangeRequestAsync(
        int requestId, string userName)
    {
        using var db = await _factory.CreateDbContextAsync();
        try
        {
            var req = await db.DeliveryScheduleChangeRequests
                .FirstOrDefaultAsync(r => r.Id == requestId);
            if (req == null) return (false, "الطلب غير موجود.");
            if (!string.Equals(req.RequesterUserName, userName, StringComparison.OrdinalIgnoreCase))
                return (false, "سحب الطلب متاح لصاحبه فقط.");
            if (req.Status != DeliveryScheduleRequestStatuses.Pending)
                return (false, "لا يمكن سحب طلب سبق البتّ فيه.");

            req.Status = DeliveryScheduleRequestStatuses.Withdrawn;
            await db.SaveChangesAsync();

            await _audit.LogAsync<object>("Transactions", ActionScheduleChangeWithdrawn,
                req.TransactionId.ToString(), null, new { RequestId = req.Id }, userName);

            _logger.LogInformation("Schedule change request {ReqId} withdrawn by {User}", requestId, userName);
            return (true, "تم سحب طلب تعديل المواعيد.");
        }
        catch (Exception ex) when (IsMissingTablesError(ex))
        {
            return (false, MissingScheduleRequestsMessage);
        }
    }
}