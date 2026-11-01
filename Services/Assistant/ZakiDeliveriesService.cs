using System.Security.Claims;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using COCOBOLOERPNEW.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using static COCOBOLOERPNEW.Services.Assistant.ZakiInvoiceQueries;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// أدوات التسليمات في زكي — مصدر البيانات هو نفس مصدر شاشة متابعة التسليمات
/// (الـ View: VwSalesDeliveryStatus) وبنفس قواعده بالظبط:
/// - الصلاحية: DeliveryPermissions.CanView يعاد فحصها عند التنفيذ، والمبالغ بس لـ CanSeeAmounts.
/// - إخفاء تاريخ الاستحقاق: غير المستثنين (CanSeeRealDeliveryDate) يشوفون الموعد منقوصًا
///   هامش الأيام (Delivery:DateBufferDays)، وكل التصنيفات (متأخرة/جدول/ملخص) بتتحسب
///   على التاريخ المعروض لهم — زي الشاشة، والإخفاء للعرض فقط ولا يُكتب في قاعدة البيانات.
/// - مفيش أرقام تليفونات في أي نتيجة (سياسة زكي العامة).
///
/// ليه بنستعلم من الـ View مباشرة بدل استدعاء SalesDeliveryStatusService؟
/// لأن خدمة الشاشة بتقرأ المستخدم الحالي من IHttpContextAccessor، بينما أدوات زكي
/// بتتنفذ بـ ClaimsPrincipal صريح (وأحيانًا اصطناعي: اختبارات الأدوار والملخص الصباحي).
/// فبنطبّق نفس دلالات الإخفاء هنا على الـ View نفسه — مصدر بيانات واحد وقواعد واحدة.
///
/// ⭐ التكافؤ مع التسليم الجزئي: الحالة المعروضة لم تعد من نص الـ View وحده —
/// تُدمج فوقها حالة أحداث التسليم بنفس اشتقاق الشاشة حرفيًا (ApplyEffectiveStatuses):
/// المرتجع نهائي، والمكتمل بالأحداث يتجاوز النص، والجزئي يُعرض «جزئي» إلا إن كان
/// متأخرًا على موعده المعروض. التقدم يُحمَّل مجمّعًا من جداول الأحداث (LoadProgress).
/// </summary>
public class ZakiDeliveriesService
{
    public const string GetDeliveryStatus = "get_delivery_status";
    public const string GetDeliveriesSchedule = "get_deliveries_schedule";
    public const string GetOverdueDeliveries = "get_overdue_deliveries";
    public const string GetDeliveryLoads = "get_delivery_loads";
    public const string GetTodayDeliveriesSummary = "get_today_deliveries_summary";
    public const string GetDeliveryPerformance = "get_delivery_performance";

    private static readonly string[] ToolNames =
    {
        GetDeliveryStatus, GetDeliveriesSchedule, GetOverdueDeliveries,
        GetDeliveryLoads, GetTodayDeliveriesSummary, GetDeliveryPerformance
    };

    // روابط المصدر (Phase 2): سطر «المصدر» في آخر نتيجة كل أداة — البرومبت بيأمر الموديل ينقله حرفيًا
    private const string DeliveriesScreenSource = "[شاشة متابعة التسليمات](/sales-delivery-status)";

    private static string InvoiceSource(int transactionId, string refr) =>
        $"[الفاتورة {refr}](/sales/invoices/{transactionId})";

    private const string StatusPending = "جارى";
    private const string StatusPartial = "جزئي";
    private const string StatusDelivered = "تم التسليم";
    private const string StatusReturned = "مرتجع";
    private const string StatusOverdue = "متأخر";

    // نفس مسامحة خدمة الشاشة عند اختبار «المتبقي = صفر»
    private const decimal QtyEpsilon = 0.0005m;

    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly ILogger<ZakiDeliveriesService> _logger;
    private readonly int _dateBufferDays;

    public ZakiDeliveriesService(
        IDbContextFactory<db24804Context> dbFactory,
        ILogger<ZakiDeliveriesService> logger,
        IConfiguration config)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _dateBufferDays = config.GetValue<int?>("Delivery:DateBufferDays") is int b && b >= 0 ? b : 4;
    }

    public static bool Handles(string toolName) => ToolNames.Contains(toolName);

    public static bool CanView(ClaimsPrincipal user) => DeliveryPermissions.CanView(user);

    /// <summary>
    /// أداة أداء التسليم للقيادة بس: نفس بوابة المشاهدة + أدوار قيادية
    /// (أدمن/مدير حسابات/مدير عام/مدير مبيعات/مسؤول تسليمات). بتتعاد فحصها عند التنفيذ.
    /// </summary>
    public static bool CanViewPerformance(ClaimsPrincipal user) =>
        CanView(user)
        && (user.IsInRole(SystemRoles.Admin)
            || user.IsInRole(SystemRoles.AccountManager)
            || user.IsInRole(SystemRoles.GeneralManager)
            || user.IsInRole(SystemRoles.SalesManager)
            || user.IsInRole(SystemRoles.DeliveryOfficer));

    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user)
    {
        if (!CanView(user)) return Array.Empty<AiToolDefinition>();
        return CanViewPerformance(user)
            ? Definitions.Concat(new[] { PerformanceDefinition }).ToArray()
            : Definitions;
    }

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string name, string argumentsJson, CancellationToken ct = default)
    {
        // إعادة فحص الصلاحية عند التنفيذ (مش بس إخفاء الأداة عن الموديل) — زي كل أدوات زكي
        if (!CanView(user))
            return Denied();

        var args = ParseArgs(argumentsJson);

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            return name switch
            {
                GetDeliveryStatus => await DeliveryStatusAsync(db, user, args, ct),
                GetDeliveriesSchedule => await ScheduleAsync(db, user, args, ct),
                GetOverdueDeliveries => await OverdueAsync(db, user, args, ct),
                GetDeliveryLoads => await LoadsAsync(db, user, ct),
                GetTodayDeliveriesSummary => await TodaySummaryAsync(db, user, ct),
                GetDeliveryPerformance => await PerformanceAsync(db, user, args, ct),
                _ => Message("أداة تسليمات غير معروفة.")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki deliveries: query failed ({Tool})", name);
            return Message("ماقدرتش أجيب بيانات التسليمات دلوقتي، حاول تاني بعد شوية.");
        }
    }

    // ════════════════════════════════════════════════════════════
    // نموذج الصف + حساب قيم العرض (نفس MaskValues في خدمة الشاشة)
    // ════════════════════════════════════════════════════════════
    /// <summary>تقدم أحداث التسليم لفاتورة: المسلَّم/المتبقي وآخر حدث — نفس أرقام خدمة الشاشة.</summary>
    private sealed record ProgressInfo(
        bool HasEvents, bool IsComplete, decimal InvoicedQty, decimal DeliveredQty,
        DateTime? LastEventDate, string? LastEventEmployee,
        decimal SettledQty = 0, decimal InLoopQty = 0)
    {
        /// <summary>المفتوح الحقيقي (القسم 11): مفوتر − مقبول − مسوّى بردّ نهائي.
        /// الكمية داخل الصيانة جزء من المفتوح حتى تُحسم.</summary>
        public decimal RemainingQty => InvoicedQty - DeliveredQty - SettledQty;
    }

    private sealed record DRow(
        int TransactionId, DateTime TransactionDate, int PartyId, string PartyName,
        DateTime? DueDate, string ViewStatus, decimal GrandTotal, decimal PaidAmount,
        int? DeliveryEmployeeId, string? DeliveryEmployeeName, DateTime? DeliveredAt)
    {
        /// <summary>تقدم الأحداث المدمج (يُرفق بـ AttachProgressAsync قبل التحويل للعرض)</summary>
        public ProgressInfo? Progress { get; init; }
    }

    private sealed record DisplayRow(DRow Row, DateTime? ShownDue, string Status)
    {
        public bool IsFinal => Status is StatusDelivered or StatusReturned;

        /// <summary>تاريخ الإكمال الفعّال: تاريخ حدث الإكمال للمكتملة بالأحداث، وإلا إسقاط الـ View</summary>
        public DateTime? DeliveredAt { get; init; }
    }

    private static bool IsFinalStatus(string status) =>
        status is StatusDelivered or StatusReturned;

    /// <summary>خطأ نقص تخزين التسليم الجزئي (208/207) — قبل تشغيل سكربتات SQL تُهمل طبقة الأحداث.</summary>
    private static bool IsMissingDeliveryTables(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
            if (e is Microsoft.Data.SqlClient.SqlException sql && (sql.Number == 208 || sql.Number == 207))
                return true;
        return false;
    }

    private static string Qty(decimal v) => v.ToString("0.###", Culture);

    /// <summary>
    /// يحمّل تقدم أحداث التسليم لمجموعة فواتير بثلاثة استعلامات مجمعة (مفوتر/
    /// مسلَّم/أحداث) — نفس اشتقاق GetDeliveryProgressBatchAsync في خدمة الشاشة.
    /// غياب جداول الأحداث يعيد قاموسًا فارغًا فتعود الأدوات لسلوك ما قبل الميزة.
    /// </summary>
    private static async Task<Dictionary<int, ProgressInfo>> LoadProgressAsync(
        db24804Context db, IEnumerable<int> ids, CancellationToken ct)
    {
        var result = new Dictionary<int, ProgressInfo>();
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return result;

        try
        {
            var invoiced = await db.TransactionDetails.AsNoTracking()
                .Where(d => list.Contains(d.TransactionId))
                .GroupBy(d => d.TransactionId)
                .Select(g => new { TransactionId = g.Key, Total = g.Sum(d => d.Quantity) })
                .ToListAsync(ct);

            // المسلَّم = المقبول فعليًا عند العميل (AcceptedQty) لا المحاوَل (القسم 11)
            var delivered = await (from i in db.InvoiceDeliveryItems.AsNoTracking()
                                   join d in db.InvoiceDeliveries.AsNoTracking()
                                       on i.DeliveryId equals d.DeliveryId
                                   where list.Contains(d.TransactionId) && !d.IsVoided
                                   group i by d.TransactionId into g
                                   select new { TransactionId = g.Key, Total = g.Sum(x => x.AcceptedQty) })
                .ToListAsync(ct);

            // نتائج التسليم: مسوّى بردّ نهائي + داخل حلقة الصيانة (تدهور آمن منفصل)
            var settledByTx = new Dictionary<int, decimal>();
            var inLoopByTx = new Dictionary<int, decimal>();
            try
            {
                var mrows = await db.DeliveryMaintenanceItems.AsNoTracking()
                    .Where(m => list.Contains(m.TransactionId) && !m.IsVoided)
                    .Select(m => new { m.TransactionId, m.Status, m.Quantity })
                    .ToListAsync(ct);
                foreach (var g in mrows.GroupBy(m => m.TransactionId))
                {
                    settledByTx[g.Key] = g
                        .Where(m => m.Status == DeliveryMaintenanceStatuses.ClosedByCredit)
                        .Sum(m => m.Quantity);
                    inLoopByTx[g.Key] = g
                        .Where(m => DeliveryMaintenanceStatuses.InLoop.Contains(m.Status))
                        .Sum(m => m.Quantity);
                }
            }
            catch (Exception ex) when (IsMissingDeliveryTables(ex))
            {
                // تخزين النتائج غير مهيأ — الأرقام الأساسية (أحداث/مقبول) تعمل بدونه
            }

            var events = await db.InvoiceDeliveries.AsNoTracking()
                .Where(d => list.Contains(d.TransactionId) && !d.IsVoided)
                .Select(d => new { d.TransactionId, d.DeliveryDate, d.DeliveryId, d.DeliveryEmployeeName })
                .ToListAsync(ct);

            var deliveredByTx = delivered.ToDictionary(x => x.TransactionId, x => x.Total);
            var eventsByTx = events.GroupBy(e => e.TransactionId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var inv in invoiced)
            {
                deliveredByTx.TryGetValue(inv.TransactionId, out var dq);
                settledByTx.TryGetValue(inv.TransactionId, out var sq);
                inLoopByTx.TryGetValue(inv.TransactionId, out var lq);
                eventsByTx.TryGetValue(inv.TransactionId, out var evs);
                var has = evs is { Count: > 0 };
                var last = has
                    ? evs!.OrderByDescending(e => e.DeliveryDate).ThenByDescending(e => e.DeliveryId).First()
                    : null;
                result[inv.TransactionId] = new ProgressInfo(
                    HasEvents: has,
                    IsComplete: has && inv.Total - dq - sq <= QtyEpsilon,
                    InvoicedQty: inv.Total,
                    DeliveredQty: dq,
                    LastEventDate: last?.DeliveryDate,
                    LastEventEmployee: last?.DeliveryEmployeeName,
                    SettledQty: sq,
                    InLoopQty: lq);
            }
        }
        catch (Exception ex) when (IsMissingDeliveryTables(ex))
        {
            return new Dictionary<int, ProgressInfo>();
        }

        return result;
    }

    /// <summary>
    /// إضافات نتائج التسليم لفاتورة واحدة (القسم 11): نتيجة آخر حدث
    /// (بدون ملاحظات/بملاحظات + التفصيل) وملخص الصيانة المفتوحة بالتصنيف.
    /// تتحلل لِلاشيء عند غياب تخزين النتائج. لا تكاليف إصلاح هنا إطلاقًا.
    /// </summary>
    private static async Task<(string? LastOutcome, string? OpenMaintenance)> LoadOutcomeExtrasAsync(
        db24804Context db, int transactionId, CancellationToken ct)
    {
        try
        {
            var lastEvent = await db.InvoiceDeliveries.AsNoTracking()
                .Where(d => d.TransactionId == transactionId && !d.IsVoided)
                .OrderByDescending(d => d.DeliveryDate).ThenByDescending(d => d.DeliveryId)
                .Select(d => new { d.DeliveryId })
                .FirstOrDefaultAsync(ct);

            string? lastOutcome = null;
            if (lastEvent != null)
            {
                var items = await db.InvoiceDeliveryItems.AsNoTracking()
                    .Where(i => i.DeliveryId == lastEvent.DeliveryId)
                    .Select(i => new
                    {
                        i.ReturnedManufacturingQty,
                        i.ReturnedHandlingQty,
                        i.ReturnedModificationQty
                    })
                    .ToListAsync(ct);
                var manuf = items.Sum(i => i.ReturnedManufacturingQty);
                var handling = items.Sum(i => i.ReturnedHandlingQty);
                var modif = items.Sum(i => i.ReturnedModificationQty);
                if (manuf + handling + modif <= QtyEpsilon)
                {
                    lastOutcome = "بدون ملاحظات — كله اتقبل";
                }
                else
                {
                    var parts = new List<string>();
                    if (manuf > 0) parts.Add($"عيب تصنيع {Qty(manuf)}");
                    if (handling > 0) parts.Add($"تلف مناولة {Qty(handling)}");
                    if (modif > 0) parts.Add($"مطلوب تعديل {Qty(modif)}");
                    lastOutcome = "بملاحظات: " + string.Join(" · ", parts);
                }
            }

            var open = await db.DeliveryMaintenanceItems.AsNoTracking()
                .Where(m => m.TransactionId == transactionId && !m.IsVoided
                         && DeliveryMaintenanceStatuses.InLoop.Contains(m.Status))
                .Select(m => new { m.ReasonClass, m.Quantity })
                .ToListAsync(ct);
            string? openText = null;
            if (open.Count > 0)
            {
                var parts = open.GroupBy(m => m.ReasonClass)
                    .Select(g => $"{DeliveryDispositions.NameOf(g.Key)} {Qty(g.Sum(x => x.Quantity))}")
                    .ToList();
                openText = string.Join(" · ", parts) + $" (الإجمالي {Qty(open.Sum(m => m.Quantity))})";
            }

            return (lastOutcome, openText);
        }
        catch (Exception ex) when (IsMissingDeliveryTables(ex))
        {
            return (null, null);
        }
    }

    /// <summary>
    /// نتائج تسليمات فترة بأكملها (لأداة الأداء): نظافة التسليم وتفصيل العيوب
    /// وتراكم الصيانة الحالي — نفس تعريفات لوحة القيادة. بدون تكاليف إصلاح.
    /// </summary>
    private static async Task<(int Events, int WithRemarks, decimal Manuf, decimal Handling,
        int BacklogRecords, decimal BacklogQty)?> LoadPeriodOutcomesAsync(
        db24804Context db, DateTime start, DateTime end, CancellationToken ct)
    {
        try
        {
            var events = await db.InvoiceDeliveries.AsNoTracking()
                .Where(d => !d.IsVoided && d.DeliveryDate >= start && d.DeliveryDate < end)
                .Select(d => new { d.DeliveryId })
                .ToListAsync(ct);
            if (events.Count == 0)
            {
                var bl0 = await db.DeliveryMaintenanceItems.AsNoTracking()
                    .Where(m => !m.IsVoided && DeliveryMaintenanceStatuses.InLoop.Contains(m.Status))
                    .Select(m => new { m.Quantity })
                    .ToListAsync(ct);
                return (0, 0, 0m, 0m, bl0.Count, bl0.Sum(m => m.Quantity));
            }

            var evIds = events.Select(e => e.DeliveryId).ToList();
            var items = await db.InvoiceDeliveryItems.AsNoTracking()
                .Where(i => evIds.Contains(i.DeliveryId))
                .Select(i => new
                {
                    i.DeliveryId,
                    i.ReturnedManufacturingQty,
                    i.ReturnedHandlingQty,
                    i.ReturnedModificationQty
                })
                .ToListAsync(ct);
            var withRemarks = items
                .Where(i => i.ReturnedManufacturingQty + i.ReturnedHandlingQty
                          + i.ReturnedModificationQty > QtyEpsilon)
                .Select(i => i.DeliveryId).Distinct().Count();

            var backlog = await db.DeliveryMaintenanceItems.AsNoTracking()
                .Where(m => !m.IsVoided && DeliveryMaintenanceStatuses.InLoop.Contains(m.Status))
                .Select(m => new { m.Quantity })
                .ToListAsync(ct);

            return (events.Count, withRemarks,
                items.Sum(i => i.ReturnedManufacturingQty),
                items.Sum(i => i.ReturnedHandlingQty),
                backlog.Count, backlog.Sum(m => m.Quantity));
        }
        catch (Exception ex) when (IsMissingDeliveryTables(ex))
        {
            return null;
        }
    }

    /// <summary>يُرفق تقدم الأحداث بصفوف الـ View (قائمة جديدة — الصفوف الأصلية لا تُعدَّل).</summary>
    private static async Task<List<DRow>> AttachProgressAsync(
        db24804Context db, List<DRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return rows;
        var progress = await LoadProgressAsync(db, rows.Select(r => r.TransactionId), ct);
        if (progress.Count == 0) return rows;
        return rows
            .Select(r => progress.TryGetValue(r.TransactionId, out var p) ? r with { Progress = p } : r)
            .ToList();
    }

    /// <summary>
    /// يحوّل صف الـ View لقيم العرض الخاصة بالمستخدم الحالي:
    /// - التاريخ المعروض: الحقيقي للمستثنين، ومنقوصًا الهامش لغيرهم (ولا يسبق تاريخ الفاتورة).
    /// - الحالة المعروضة: المعلَّقة فقط تتحول «متأخر» لو موعدها المعروض فات —
    ///   المسَلَّمة والمرتجعة نتائج نهائية تحتفظ بحالتها (نفس MaskValues في الشاشة).
    /// - ثم تُدمج حالة الأحداث بنفس قاعدة الشاشة: المرتجع نهائي لا يُمس، والمكتمل
    ///   بالأحداث يتجاوز نص الـ View، والجزئي يُعرض «جزئي» إلا إن كان الصف «متأخر».
    /// </summary>
    private DisplayRow ToDisplay(DRow row, bool seeReal, DateTime today)
    {
        var status = row.ViewStatus;
        DateTime? shown = row.DueDate;

        if (row.DueDate.HasValue)
        {
            var d = row.DueDate.Value;
            if (!seeReal)
            {
                d = d.AddDays(-_dateBufferDays);
                if (d < row.TransactionDate) d = row.TransactionDate;   // المعروض لا يسبق تاريخ الفاتورة
            }
            shown = d;

            if (!IsFinalStatus(status) && d.Date < today)
                status = StatusOverdue;
        }

        var deliveredAt = row.DeliveredAt;
        if (row.Progress is { } p && status != StatusReturned)
        {
            if (p.IsComplete)
            {
                status = StatusDelivered;
                deliveredAt = p.LastEventDate ?? deliveredAt;
            }
            else if (p.HasEvents && status != StatusOverdue)
            {
                status = StatusPartial;
            }
        }

        return new DisplayRow(row, shown, status) { DeliveredAt = deliveredAt };
    }

    private bool SeeReal(ClaimsPrincipal user) => DeliveryPermissions.CanSeeRealDeliveryDate(user);

    /// <summary>إزاحة عتبات الاستعلام للمخفيين (+الهامش) حتى تطابق النتائج التواريخ المعروضة لهم — نفس منطق الشاشة</summary>
    private int Shift(ClaimsPrincipal user) => SeeReal(user) ? 0 : _dateBufferDays;

    private static IQueryable<DRow> Project(IQueryable<VwSalesDeliveryStatus> q) =>
        q.Select(x => new DRow(
            x.TransactionId, x.TransactionDate, x.PartyId, x.PartyName,
            x.DueDate, x.DeliveryStatus, x.GrandTotal, x.PaidAmount,
            x.DeliveryEmployeeId, x.DeliveryEmployeeName, x.DeliveredAt));

    /// <summary>أرقام/مراجع الفواتير من جدول المعاملات (الـ View مافيهوش ReferenceNumber)</summary>
    private static async Task<Dictionary<int, string>> RefsAsync(
        db24804Context db, IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, string>();

        var found = await db.Transactions.AsNoTracking()
            .Where(t => list.Contains(t.TransactionId))
            .Select(t => new { t.TransactionId, t.ReferenceNumber })
            .ToListAsync(ct);

        return found.ToDictionary(
            x => x.TransactionId,
            x => string.IsNullOrWhiteSpace(x.ReferenceNumber) ? $"#{x.TransactionId}" : x.ReferenceNumber!);
    }

    private static string RefOf(int id, IReadOnlyDictionary<int, string> refs) =>
        refs.TryGetValue(id, out var r) ? r : $"#{id}";

    private static string EmployeeOf(DRow row) =>
        !string.IsNullOrWhiteSpace(row.DeliveryEmployeeName)
            ? row.DeliveryEmployeeName!
            : "مش مُسند لمسؤول تسليم";

    /// <summary>
    /// مواعيد تسليم بنود فاتورة واحدة بصيغة عرض المستخدم الحالي: أكثر من موعد
    /// مميز واحد يعني فاتورة مقسّمة على دفعات مجدولة — تُسرد المواعيد كلها بعدد
    /// بنودها (مثال صاحب القرار: بنود على 2-11 وأخرى على 30-11). موعد واحد = null.
    /// </summary>
    private async Task<string?> AppointmentsTextAsync(
        db24804Context db, DRow row, bool seeReal, CancellationToken ct)
    {
        try
        {
            var lines = await db.TransactionDetails.AsNoTracking()
                .Where(d => d.TransactionId == row.TransactionId)
                .Select(d => new { d.DeliveryDate, d.DeliveryDateIsCustom })
                .ToListAsync(ct);
            if (lines.Count == 0) return null;

            var groups = lines
                .GroupBy(l => DeliveryScheduleResolver.DisplayDate(
                    DeliveryScheduleResolver.EffectiveDate(l.DeliveryDate, l.DeliveryDateIsCustom, row.DueDate),
                    row.TransactionDate, seeReal, _dateBufferDays))
                .OrderBy(g => g.Key ?? DateTime.MaxValue)
                .ToList();
            if (groups.Count <= 1) return null;

            return string.Join("، ", groups.Select(g =>
                $"{Date(g.Key)} ({g.Count()} {(g.Count() == 1 ? "بند" : "بنود")})"));
        }
        catch (Exception ex) when (IsMissingDeliveryTables(ex))
        {
            return null;
        }
    }

    // ════════════════════════════════════════════════════════════
    // 1) حالة تسليم فاتورة / تسليمات عميل معلّقة
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> DeliveryStatusAsync(
        db24804Context db, ClaimsPrincipal user, System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var reference = ReadRawString(args, "reference").TrimStart('#').Trim();
        var customer = ReadRawString(args, "customer");
        var limit = ReadInt(args, "limit", 15, 1, 30);
        var today = DateTime.Today;
        var seeReal = SeeReal(user);
        var showAmounts = DeliveryPermissions.CanSeeAmounts(user);

        if (reference.Length > 0)
        {
            int? txId = null;
            var byRef = await db.Transactions.AsNoTracking()
                .Where(t => t.ReferenceNumber == reference)
                .Select(t => t.TransactionId)
                .FirstOrDefaultAsync(ct);
            if (byRef != 0) txId = byRef;
            else if (int.TryParse(reference, out var parsed)) txId = parsed;

            if (txId == null)
                return Message("مالقيتش فاتورة بالمرجع ده.");

            var row = await Project(db.VwSalesDeliveryStatuses.AsNoTracking()
                    .Where(x => x.TransactionId == txId.Value))
                .FirstOrDefaultAsync(ct);

            if (row == null)
                return Message("الفاتورة دي مالهاش بيانات تسليم في نطاق صلاحياتك.");

            row = (await AttachProgressAsync(db, new List<DRow> { row }, ct))[0];
            var d = ToDisplay(row, seeReal, today);
            var refs = await RefsAsync(db, new[] { row.TransactionId }, ct);

            var data = new Dictionary<string, object?>
            {
                ["المرجع"] = RefOf(row.TransactionId, refs),
                ["العميل"] = row.PartyName,
                ["حالة التسليم"] = d.Status,
                ["موعد التسليم"] = Date(d.ShownDue),
                ["تاريخ التسليم الفعلي"] = Date(d.DeliveredAt ?? row.DeliveredAt),
                ["مسؤول التسليم"] = EmployeeOf(row)
            };
            // ⭐ تقدم الأحداث: المسلَّم/المتبقي وآخر تسليمة — كميات لا مبالغ، تظهر لكل من يرى الشاشة
            if (row.Progress is { HasEvents: true } p)
            {
                data["تقدم التسليم"] = $"اتقبل {Qty(p.DeliveredQty)} من {Qty(p.InvoicedQty)}";
                if (p.InLoopQty > 0)
                    data["منها في الصيانة"] = Qty(p.InLoopQty);
                if (p.SettledQty > 0)
                    data["سُوّي بردّ نهائي (مش هيتسلّم)"] = Qty(p.SettledQty);
                if (p.LastEventDate.HasValue)
                    data["آخر تسليمة"] = $"{Date(p.LastEventDate)} — {p.LastEventEmployee ?? EmployeeOf(row)}";

                // نتيجة آخر حدث + الصيانة المفتوحة بالتصنيف (القسم 11)
                var (lastOutcome, openMaintenance) = await LoadOutcomeExtrasAsync(db, row.TransactionId, ct);
                if (lastOutcome != null)
                    data["نتيجة آخر تسليمة"] = lastOutcome;
                if (openMaintenance != null)
                    data["في الصيانة دلوقتي"] = openMaintenance;
            }
            // ⭐ فاتورة مقسّمة على أكثر من موعد: تُذكر مواعيدها كلها لا موعد الفاتورة وحده
            var appointments = await AppointmentsTextAsync(db, row, seeReal, ct);
            if (appointments != null)
                data["مواعيد التسليم"] = appointments;
            if (showAmounts)
            {
                data["الإجمالي"] = Money(row.GrandTotal);
                data["المدفوع"] = Money(row.PaidAmount);
                data["المتبقي"] = Money(row.GrandTotal - row.PaidAmount);
            }
            data["المصدر"] = InvoiceSource(row.TransactionId, RefOf(row.TransactionId, refs));

            var headers = showAmounts
                ? new[] { "المرجع", "العميل", "موعد التسليم", "الحالة", "مسؤول التسليم", "التسليم الفعلي", "المتبقي" }
                : new[] { "المرجع", "العميل", "موعد التسليم", "الحالة", "مسؤول التسليم", "التسليم الفعلي" };

            var cells = new List<string>
            {
                RefOf(row.TransactionId, refs), row.PartyName, Date(d.ShownDue), d.Status,
                EmployeeOf(row), Date(d.DeliveredAt ?? row.DeliveredAt)
            };
            if (showAmounts) cells.Add(Money(row.GrandTotal - row.PaidAmount));

            return new ZakiToolOutcome(
                ToJson(data),
                new ZakiTable("حالة التسليم", headers, new List<IReadOnlyList<string>> { cells }));
        }

        if (customer.Length > 0)
        {
            var parties = await db.Parties.AsNoTracking()
                .Select(p => new { p.PartyId, p.PartyName })
                .ToListAsync(ct);
            var matchedIds = parties
                .Where(p => (p.PartyName ?? "").ContainsArabic(customer))
                .Select(p => p.PartyId)
                .ToList();

            if (matchedIds.Count == 0)
                return Message("مالقيتش عميل بالاسم ده.");

            var all = await Project(db.VwSalesDeliveryStatuses.AsNoTracking()
                    .Where(x => matchedIds.Contains(x.PartyId)))
                .ToListAsync(ct);
            all = await AttachProgressAsync(db, all, ct);

            var display = all.Select(r => ToDisplay(r, seeReal, today)).ToList();
            var pending = display
                .Where(d => !d.IsFinal)
                .OrderBy(d => d.ShownDue == null)
                .ThenBy(d => d.ShownDue)
                .ToList();

            if (pending.Count == 0)
                return Message("العميل ده ماعندوش تسليمات معلّقة — كل فواتيره اتسلّمت أو اترجعت.");

            var shown = pending.Take(limit).ToList();
            var refs = await RefsAsync(db, shown.Select(d => d.Row.TransactionId), ct);

            var headers = showAmounts
                ? new[] { "المرجع", "العميل", "موعد التسليم", "الحالة", "مسؤول التسليم", "المتبقي" }
                : new[] { "المرجع", "العميل", "موعد التسليم", "الحالة", "مسؤول التسليم" };

            var rows = shown.Select(d =>
            {
                var cells = new List<string>
                {
                    RefOf(d.Row.TransactionId, refs), d.Row.PartyName, Date(d.ShownDue), d.Status, EmployeeOf(d.Row)
                };
                if (showAmounts) cells.Add(Money(d.Row.GrandTotal - d.Row.PaidAmount));
                return (IReadOnlyList<string>)cells;
            }).ToList();

            var json = ToJson(new Dictionary<string, object?>
            {
                ["العميل"] = customer,
                ["تسليمات معلّقة"] = pending.Count,
                ["منها متأخرة"] = pending.Count(d => d.Status == StatusOverdue),
                ["منها جزئية"] = pending.Count(d => d.Status == StatusPartial),
                ["اتسلّمت قبل كده"] = display.Count(d => d.Status == StatusDelivered),
                ["التسليمات المعلّقة"] = ToObjects(headers, rows),
                ["ملاحظة"] = pending.Count > rows.Count
                    ? $"الجدول بيعرض أول {rows.Count} من {pending.Count}."
                    : "الجدول بيعرض كل التسليمات المعلّقة.",
                ["المصدر"] = DeliveriesScreenSource
            });

            return new ZakiToolOutcome(json, new ZakiTable($"التسليمات المعلّقة — {customer}", headers, rows));
        }

        return Message("حدد رقم/مرجع الفاتورة أو اسم العميل عشان أجيب حالة التسليم.");
    }

    // ════════════════════════════════════════════════════════════
    // 2) جدول التسليمات لفترة (النهاردة / بكرة / الأسبوع)
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> ScheduleAsync(
        db24804Context db, ClaimsPrincipal user, System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var period = ReadString(args, "period", "today");   // today / tomorrow / week
        var limit = ReadInt(args, "limit", 30, 1, 40);
        var today = DateTime.Today;
        var seeReal = SeeReal(user);
        var shift = Shift(user);
        var showAmounts = DeliveryPermissions.CanSeeAmounts(user);

        var (start, end, label) = period switch
        {
            "tomorrow" => (today.AddDays(1), today.AddDays(2), "بكرة"),
            "week" => (today, today.AddDays(7), "الأسبوع ده (7 أيام)"),
            _ => (today, today.AddDays(1), "النهاردة")
        };

        // الاستعلام على التاريخ الحقيقي بحدود مُزاحة +الهامش للمخفيين — نفس فلاتر الشاشة
        var from = start.AddDays(shift);
        var to = end.AddDays(shift);

        var raw = await Project(db.VwSalesDeliveryStatuses.AsNoTracking()
                .Where(x => x.DueDate.HasValue && x.DueDate.Value >= from && x.DueDate.Value < to))
            .ToListAsync(ct);
        raw = await AttachProgressAsync(db, raw, ct);

        var display = raw
            .Select(r => ToDisplay(r, seeReal, today))
            .Where(d => d.ShownDue.HasValue && d.ShownDue.Value.Date >= start && d.ShownDue.Value.Date < end)
            .OrderBy(d => d.ShownDue)
            .ThenBy(d => d.Row.TransactionId)
            .ToList();

        if (display.Count == 0)
            return Message($"مفيش تسليمات مجدولة {label}.");

        var shown = display.Take(limit).ToList();
        var refs = await RefsAsync(db, shown.Select(d => d.Row.TransactionId), ct);

        var headers = showAmounts
            ? new[] { "اليوم", "المرجع", "العميل", "موعد التسليم", "الحالة", "مسؤول التسليم", "المتبقي" }
            : new[] { "اليوم", "المرجع", "العميل", "موعد التسليم", "الحالة", "مسؤول التسليم" };

        var rows = shown.Select(d =>
        {
            var cells = new List<string>
            {
                Date(d.ShownDue), RefOf(d.Row.TransactionId, refs), d.Row.PartyName,
                Date(d.ShownDue), d.Status, EmployeeOf(d.Row)
            };
            if (showAmounts) cells.Add(Money(d.Row.GrandTotal - d.Row.PaidAmount));
            return (IReadOnlyList<string>)cells;
        }).ToList();

        var days = display
            .GroupBy(d => d.ShownDue!.Value.Date)
            .OrderBy(g => g.Key)
            .Select(g => new Dictionary<string, object?>
            {
                ["اليوم"] = Date(g.Key),
                ["الإجمالي"] = g.Count(),
                ["جارى"] = g.Count(x => x.Status == StatusPending),
                ["جزئي"] = g.Count(x => x.Status == StatusPartial),
                ["متأخر"] = g.Count(x => x.Status == StatusOverdue),
                ["تم التسليم"] = g.Count(x => x.Status == StatusDelivered),
                ["مرتجع"] = g.Count(x => x.Status == StatusReturned)
            })
            .ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفترة"] = label,
            ["إجمالي التسليمات"] = display.Count,
            ["الأيام"] = days,
            ["التسليمات"] = ToObjects(headers, rows),
            ["ملاحظة"] = display.Count > rows.Count
                ? $"الجدول بيعرض أول {rows.Count} من {display.Count}."
                : "الجدول بيعرض كل النتايج.",
            ["المصدر"] = DeliveriesScreenSource
        });

        return new ZakiToolOutcome(json, new ZakiTable($"جدول التسليمات — {label}", headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 3) التسليمات المتأخرة بشرائح التأخير
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> OverdueAsync(
        db24804Context db, ClaimsPrincipal user, System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var limit = ReadInt(args, "limit", 30, 1, 40);
        var today = DateTime.Today;
        var seeReal = SeeReal(user);
        var shift = Shift(user);
        var showAmounts = DeliveryPermissions.CanSeeAmounts(user);

        // مرشحين: موعدهم الحقيقي قبل (النهاردة + الهامش) ولسه مش نهائي — والتصفية الدقيقة على المعروض بالأسفل
        var threshold = today.AddDays(shift);
        var raw = await Project(db.VwSalesDeliveryStatuses.AsNoTracking()
                .Where(x => x.DueDate.HasValue && x.DueDate.Value < threshold
                    && x.DeliveryStatus != StatusDelivered && x.DeliveryStatus != StatusReturned))
            .ToListAsync(ct);
        raw = await AttachProgressAsync(db, raw, ct);

        // التصنيف من الحالة الفعّالة المدمجة: الجزئية المتأخرة داخلة بمتبقّيها،
        // والمكتملة بالأحداث خارجة وإن تأخر نص الـ View عن إسقاطها
        var overdue = raw
            .Select(r => ToDisplay(r, seeReal, today))
            .Where(d => d.Status == StatusOverdue)
            .OrderBy(d => d.ShownDue)
            .ToList();

        if (overdue.Count == 0)
            return Message("مفيش تسليمات متأخرة — كله ماشي في مواعيده.");

        var shown = overdue.Take(limit).ToList();
        var refs = await RefsAsync(db, shown.Select(d => d.Row.TransactionId), ct);

        var headers = showAmounts
            ? new[] { "المرجع", "العميل", "موعد التسليم", "متأخرة من (يوم)", "شريحة التأخير", "مسؤول التسليم", "كمية متبقية", "المتبقي" }
            : new[] { "المرجع", "العميل", "موعد التسليم", "متأخرة من (يوم)", "شريحة التأخير", "مسؤول التسليم", "كمية متبقية" };

        var rows = shown.Select(d =>
        {
            var days = (today - d.ShownDue!.Value.Date).Days;
            var cells = new List<string>
            {
                RefOf(d.Row.TransactionId, refs), d.Row.PartyName, Date(d.ShownDue),
                days.ToString(Culture), BucketLabel(days), EmployeeOf(d.Row),
                d.Row.Progress is { } pq ? Qty(pq.RemainingQty) : "—"
            };
            if (showAmounts) cells.Add(Money(d.Row.GrandTotal - d.Row.PaidAmount));
            return (IReadOnlyList<string>)cells;
        }).ToList();

        var buckets = new[] { "من 1 لـ 3 أيام", "من 4 لـ 7 أيام", "من أسبوع لأسبوعين", "أكتر من أسبوعين" }
            .Select(b => new Dictionary<string, object?>
            {
                ["الشريحة"] = b,
                ["العدد"] = overdue.Count(d => BucketLabel((today - d.ShownDue!.Value.Date).Days) == b)
            })
            .ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["إجمالي التسليمات المتأخرة"] = overdue.Count,
            ["منها جزئية (بدأ تسليمها)"] = overdue.Count(d => d.Row.Progress is { HasEvents: true }),
            ["شرائح التأخير"] = buckets,
            ["المتأخرة"] = ToObjects(headers, rows),
            ["ملاحظة"] = overdue.Count > rows.Count
                ? $"الجدول بيعرض أول {rows.Count} من {overdue.Count} (الأقدم موعدًا أولاً)."
                : "الجدول بيعرض كل النتايج.",
            ["المصدر"] = DeliveriesScreenSource
        });

        return new ZakiToolOutcome(json, new ZakiTable("التسليمات المتأخرة", headers, rows));
    }

    private static string BucketLabel(int daysOverdue) =>
        daysOverdue <= 3 ? "من 1 لـ 3 أيام"
        : daysOverdue <= 7 ? "من 4 لـ 7 أيام"
        : daysOverdue <= 14 ? "من أسبوع لأسبوعين"
        : "أكتر من أسبوعين";

    // ════════════════════════════════════════════════════════════
    // 4) حمل مسؤولي التسليم
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> LoadsAsync(
        db24804Context db, ClaimsPrincipal user, CancellationToken ct)
    {
        var today = DateTime.Today;
        var seeReal = SeeReal(user);

        var pendingRaw = await Project(db.VwSalesDeliveryStatuses.AsNoTracking()
                .Where(x => x.DeliveryStatus != StatusDelivered && x.DeliveryStatus != StatusReturned))
            .ToListAsync(ct);
        pendingRaw = await AttachProgressAsync(db, pendingRaw, ct);

        // ⭐ «اتسلّم النهاردة» = أحداث اليوم نفسها (دفعات) لا فواتير اكتملت فقط —
        // فنشاط يوم جزئي (دفعة من فاتورة لم تكتمل) يظهر في حمل مندوبه ولا يختفي
        var eventsToday = new List<(string Name, bool Completed)>();
        try
        {
            var evs = await db.InvoiceDeliveries.AsNoTracking()
                .Where(d => !d.IsVoided && d.DeliveryDate >= today && d.DeliveryDate < today.AddDays(1))
                .Select(d => new { d.DeliveryEmployeeName, d.CompletedInvoice })
                .ToListAsync(ct);
            eventsToday = evs
                .Select(e => (string.IsNullOrWhiteSpace(e.DeliveryEmployeeName)
                    ? "مش مُسند لمسؤول تسليم" : e.DeliveryEmployeeName!, e.CompletedInvoice))
                .ToList();
        }
        catch (Exception ex) when (IsMissingDeliveryTables(ex)) { }

        if (pendingRaw.Count == 0 && eventsToday.Count == 0)
            return Message("مفيش تسليمات معلّقة ولا اتسلّم حاجة النهاردة.");

        var pending = pendingRaw.Select(r => ToDisplay(r, seeReal, today)).ToList();

        var byEmployee = new Dictionary<string, (int Pending, int Overdue, int DeliveredToday)>();

        void Bump(string name, int p, int o, int d)
        {
            byEmployee.TryGetValue(name, out var v);
            byEmployee[name] = (v.Pending + p, v.Overdue + o, v.DeliveredToday + d);
        }

        foreach (var d in pending)
            Bump(EmployeeOf(d.Row), 1, d.Status == StatusOverdue ? 1 : 0, 0);
        foreach (var e in eventsToday)
            Bump(e.Name, 0, 0, 1);

        var headers = new[] { "مسؤول التسليم", "معلّقة", "متأخرة", "اتسلّمت النهاردة" };
        var rows = byEmployee
            .OrderByDescending(kv => kv.Value.Overdue)
            .ThenByDescending(kv => kv.Value.Pending)
            .Select(kv => (IReadOnlyList<string>)new List<string>
            {
                kv.Key,
                kv.Value.Pending.ToString(Culture),
                kv.Value.Overdue.ToString(Culture),
                kv.Value.DeliveredToday.ToString(Culture)
            })
            .ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["إجمالي المعلّقة"] = pending.Count,
            ["إجمالي المتأخرة"] = pending.Count(d => d.Status == StatusOverdue),
            ["منها جزئية"] = pending.Count(d => d.Status == StatusPartial),
            ["دفعات تسليم النهاردة"] = eventsToday.Count,
            ["فواتير اكتملت النهاردة"] = eventsToday.Count(e => e.Completed),
            ["ملاحظة"] = "«اتسلّمت النهاردة» في الجدول تُعدّ بالدفعات (أحداث التسليم) لكل مسؤول، لا بالفواتير المكتملة فقط.",
            ["حمل المسؤولين"] = ToObjects(headers, rows),
            ["المصدر"] = DeliveriesScreenSource
        });

        return new ZakiToolOutcome(json, new ZakiTable("حمل مسؤولي التسليم", headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 5) ملخص تسليمات النهاردة
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> TodaySummaryAsync(
        db24804Context db, ClaimsPrincipal user, CancellationToken ct)
    {
        var today = DateTime.Today;
        var seeReal = SeeReal(user);
        var shift = Shift(user);
        var showAmounts = DeliveryPermissions.CanSeeAmounts(user);

        // مخطط النهاردة على التاريخ المعروض: الحقيقي في نافذة مُزاحة +الهامش للمخفيين
        var from = today.AddDays(shift);
        var to = from.AddDays(1);
        var plannedRaw = await Project(db.VwSalesDeliveryStatuses.AsNoTracking()
                .Where(x => x.DueDate.HasValue && x.DueDate.Value >= from && x.DueDate.Value < to))
            .ToListAsync(ct);
        plannedRaw = await AttachProgressAsync(db, plannedRaw, ct);

        var planned = plannedRaw
            .Select(r => ToDisplay(r, seeReal, today))
            .Where(d => d.ShownDue.HasValue && d.ShownDue.Value.Date == today)
            .OrderBy(d => d.ShownDue)
            .ToList();

        var deliveredOfPlanned = planned.Count(d => d.Status == StatusDelivered);

        // ⭐ نشاط النهاردة الفعلي من الأحداث: دفعات اليوم + فواتير اكتملت اليوم
        // (عند غياب جداول الأحداث يبقى عدّ الـ View القديم للاكتمالات)
        var todayEvents = await TodayEventCountsAsync(db, today, ct);
        var deliveredTodayTotal = todayEvents?.Completed
            ?? await db.VwSalesDeliveryStatuses.AsNoTracking()
                .CountAsync(x => x.DeliveryStatus == StatusDelivered
                    && x.DeliveredAt.HasValue && x.DeliveredAt.Value >= today && x.DeliveredAt.Value < today.AddDays(1), ct);

        var overdueRaw = await Project(db.VwSalesDeliveryStatuses.AsNoTracking()
                .Where(x => x.DueDate.HasValue && x.DueDate.Value < today.AddDays(shift)
                    && x.DeliveryStatus != StatusDelivered && x.DeliveryStatus != StatusReturned))
            .ToListAsync(ct);
        overdueRaw = await AttachProgressAsync(db, overdueRaw, ct);
        var overdueNow = overdueRaw
            .Select(r => ToDisplay(r, seeReal, today))
            .Count(d => d.Status == StatusOverdue);

        var percent = planned.Count == 0
            ? "—"
            : Math.Round(deliveredOfPlanned * 100.0 / planned.Count).ToString(Culture) + "%";

        var data = new Dictionary<string, object?>
        {
            ["اليوم"] = Date(today),
            ["مخطط تسليمه النهاردة"] = planned.Count,
            ["اتسلّم من المخطط"] = deliveredOfPlanned,
            ["نسبة إنجاز النهاردة"] = percent,
            ["اتسلّم النهاردة إجمالاً"] = deliveredTodayTotal,
            ["متأخرة حتى الآن"] = overdueNow,
            ["المصدر"] = DeliveriesScreenSource
        };
        if (todayEvents is { } te)
            data["دفعات تسليم النهاردة"] = te.Batches;

        if (planned.Count == 0)
            return new ZakiToolOutcome(ToJson(data), null);

        var shown = planned.Take(15).ToList();
        var refs = await RefsAsync(db, shown.Select(d => d.Row.TransactionId), ct);

        var headers = showAmounts
            ? new[] { "المرجع", "العميل", "موعد التسليم", "الحالة", "مسؤول التسليم", "المتبقي" }
            : new[] { "المرجع", "العميل", "موعد التسليم", "الحالة", "مسؤول التسليم" };

        var rows = shown.Select(d =>
        {
            var cells = new List<string>
            {
                RefOf(d.Row.TransactionId, refs), d.Row.PartyName, Date(d.ShownDue), d.Status, EmployeeOf(d.Row)
            };
            if (showAmounts) cells.Add(Money(d.Row.GrandTotal - d.Row.PaidAmount));
            return (IReadOnlyList<string>)cells;
        }).ToList();

        data["تسليمات النهاردة المخططة"] = ToObjects(headers, rows);

        return new ZakiToolOutcome(
            ToJson(data),
            new ZakiTable("تسليمات النهاردة المخططة", headers, rows));
    }

    /// <summary>نشاط اليوم من أحداث التسليم: (عدد الدفعات، عدد الفواتير المكتملة اليوم) — null عند غياب الجداول.</summary>
    private static async Task<(int Batches, int Completed)?> TodayEventCountsAsync(
        db24804Context db, DateTime today, CancellationToken ct)
    {
        try
        {
            var flags = await db.InvoiceDeliveries.AsNoTracking()
                .Where(d => !d.IsVoided && d.DeliveryDate >= today && d.DeliveryDate < today.AddDays(1))
                .Select(d => d.CompletedInvoice)
                .ToListAsync(ct);
            return (flags.Count, flags.Count(c => c));
        }
        catch (Exception ex) when (IsMissingDeliveryTables(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// دقة الدفعات (التعريف الثانوي للالتزام): من أحداث الفترة — الحدث منضبط إن
    /// خرج كل بند فيه في/قبل موعده المجدول «المعروض» لهذا المستخدم (تاريخ البند
    /// الخاص أو استحقاق فاتورته، بنفس إخفاء العرض). حدث بلا موعد قابل للحساب
    /// لبند من بنوده يُستبعد من المقام. null عند غياب تخزين الميزة.
    /// </summary>
    private async Task<(int Total, int OnTime)?> BatchPunctualityAsync(
        db24804Context db, ClaimsPrincipal user, DateTime start, DateTime end, CancellationToken ct)
    {
        try
        {
            var seeReal = SeeReal(user);
            var events = await db.InvoiceDeliveries.AsNoTracking()
                .Where(d => !d.IsVoided && d.DeliveryDate >= start && d.DeliveryDate < end)
                .Select(d => new { d.DeliveryId, d.TransactionId, d.DeliveryDate })
                .ToListAsync(ct);
            if (events.Count == 0) return (0, 0);

            var evIds = events.Select(e => e.DeliveryId).ToList();
            var items = await db.InvoiceDeliveryItems.AsNoTracking()
                .Where(i => evIds.Contains(i.DeliveryId))
                .Select(i => new { i.DeliveryId, i.TransactionDetailId })
                .ToListAsync(ct);
            var detailIds = items.Select(i => i.TransactionDetailId).Distinct().ToList();
            var lines = await db.TransactionDetails.AsNoTracking()
                .Where(d => detailIds.Contains(d.DetailId))
                .Select(d => new { d.DetailId, d.DeliveryDate, d.DeliveryDateIsCustom })
                .ToListAsync(ct);
            var txIds = events.Select(e => e.TransactionId).Distinct().ToList();
            var headers = await db.Transactions.AsNoTracking()
                .Where(t => txIds.Contains(t.TransactionId))
                .Select(t => new { t.TransactionId, t.DueDate, t.TransactionDate })
                .ToListAsync(ct);

            var headerByTx = headers.ToDictionary(h => h.TransactionId);
            var lineById = lines.ToDictionary(l => l.DetailId);
            var itemsByEvent = items.GroupBy(i => i.DeliveryId)
                .ToDictionary(g => g.Key, g => g.ToList());

            int total = 0, onTime = 0;
            foreach (var e in events)
            {
                if (!itemsByEvent.TryGetValue(e.DeliveryId, out var its) || its.Count == 0) continue;
                if (!headerByTx.TryGetValue(e.TransactionId, out var hdr)) continue;

                var ok = true;
                var countable = true;
                foreach (var it in its)
                {
                    if (!lineById.TryGetValue(it.TransactionDetailId, out var line)) { countable = false; break; }
                    var disp = DeliveryScheduleResolver.DisplayDate(
                        DeliveryScheduleResolver.EffectiveDate(
                            line.DeliveryDate, line.DeliveryDateIsCustom, hdr.DueDate),
                        hdr.TransactionDate, seeReal, _dateBufferDays);
                    if (disp == null) { countable = false; break; }
                    if (e.DeliveryDate.Date > disp.Value.Date) { ok = false; break; }
                }
                if (!countable) continue;
                total++;
                if (ok) onTime++;
            }
            return (total, onTime);
        }
        catch (Exception ex) when (IsMissingDeliveryTables(ex))
        {
            return null;
        }
    }

    // ════════════════════════════════════════════════════════════
    // 6) أداء التسليم: الالتزام بالمواعيد (للقيادة بس)
    // ════════════════════════════════════════════════════════════
    /// <summary>
    /// نسبة التسليم في الموعد خلال فترة، واختياريًا لكل مسؤول تسليم.
    /// - الأساس: الفواتير اللي موعدها «المعروض» للمستخدم الحالي داخل الفترة.
    /// - الالتزام الرئيسي (التزام الإكمال) بيتحكم عليه بمنطق المستخدم نفسه: تاريخ حدث
    ///   الإكمال (من أحداث التسليم) مقابل الموعد المعروض له — فالمستثنون بياخدوا
    ///   الحقيقة على التاريخ الحقيقي، والمخفيون على تاريخهم المعروض (اتساق مع باقي الأدوات).
    /// - النسبة من اللي اكتمل فعليًا بس؛ المرتجعة والمعلّقة (وفيها الجزئية) برّه النسبة وبيتعدّوا لوحدهم.
    /// - الثانوي: دقة الدفعات — أحداث الفترة الخارجة في/قبل مواعيد بنودها المعروضة.
    /// </summary>
    private async Task<ZakiToolOutcome> PerformanceAsync(
        db24804Context db, ClaimsPrincipal user, System.Text.Json.JsonElement args, CancellationToken ct)
    {
        // إعادة فحص بوابة القيادة عند التنفيذ — مش بس إخفاء الأداة عن الموديل
        if (!CanViewPerformance(user))
            return Denied();

        var period = ReadString(args, "period", "month");   // month / last_month / days
        var days = ReadInt(args, "days", 30, 1, 180);
        var byEmployee = ReadBool(args, "by_employee", false);
        var today = DateTime.Today;
        var seeReal = SeeReal(user);
        var shift = Shift(user);

        var (start, end, label) = period switch
        {
            "last_month" => (new DateTime(today.Year, today.Month, 1).AddMonths(-1),
                             new DateTime(today.Year, today.Month, 1), "الشهر اللي فات"),
            "days" => (today.AddDays(-(days - 1)), today.AddDays(1), $"آخر {days} يوم"),
            _ => (new DateTime(today.Year, today.Month, 1), today.AddDays(1), "الشهر ده")
        };

        // الاستعلام على التاريخ الحقيقي بنافذة مُزاحة +الهامش للمخفيين، والتصفية الدقيقة على المعروض
        var raw = await Project(db.VwSalesDeliveryStatuses.AsNoTracking()
                .Where(x => x.DueDate.HasValue
                    && x.DueDate.Value >= start.AddDays(shift) && x.DueDate.Value < end.AddDays(shift)))
            .ToListAsync(ct);
        raw = await AttachProgressAsync(db, raw, ct);

        var inPeriod = raw
            .Select(r => ToDisplay(r, seeReal, today))
            .Where(d => d.ShownDue.HasValue && d.ShownDue.Value.Date >= start && d.ShownDue.Value.Date < end)
            .ToList();

        if (inPeriod.Count == 0)
            return Message($"مفيش تسليمات كان موعدها في الفترة دي ({label}).");

        var overall = Summarize(inPeriod);

        var data = new Dictionary<string, object?>
        {
            ["الفترة"] = label,
            ["مخطط تسليمها في الفترة"] = overall.Planned,
            ["اتسلّمت فعليًا"] = overall.Delivered,
            ["اتسلّمت في الموعد أو قبله"] = overall.OnTime,
            ["اتسلّمت متأخر"] = overall.Late,
            ["نسبة الالتزام بالمواعيد"] = overall.Percent,
            ["لسه معلّقة"] = overall.Pending,
            ["منها جزئية"] = inPeriod.Count(d => d.Status == StatusPartial),
            ["منها متأخرة دلوقتي"] = overall.OverdueNow,
            ["مرتجعة"] = overall.Returned,
            ["ملاحظة"] = "النسبة محسوبة على اللي اكتمل فعليًا بس، وبمقارنة تاريخ حدث الإكمال بالموعد المعروض ليك.",
            ["المصدر"] = DeliveriesScreenSource
        };

        // ⭐ الثانوي: دقة الدفعات على أحداث الفترة (بنفس مواعيد العرض لهذا المستخدم)
        var batch = await BatchPunctualityAsync(db, user, start, end, ct);
        if (batch is { Total: > 0 } b)
        {
            data["دفعات الفترة"] = b.Total;
            data["دفعات في موعدها"] = b.OnTime;
            data["دقة الدفعات"] = Math.Round(b.OnTime * 100.0 / b.Total).ToString(Culture) + "%";
        }

        // ⭐ نتائج التسليم في الفترة (القسم 11): نظافة التسليم + العيوب + تراكم الصيانة
        var outcomes = await LoadPeriodOutcomesAsync(db, start, end, ct);
        if (outcomes is { } oc)
        {
            if (oc.Events > 0)
            {
                data["أحداث تسليم في الفترة"] = oc.Events;
                data["منها بملاحظات (رجوع)"] = oc.WithRemarks;
                data["نسبة التسليم النظيف"] =
                    Math.Round((oc.Events - oc.WithRemarks) * 100.0 / oc.Events).ToString(Culture) + "%";
                if (oc.Manuf > 0) data["رجوع عيب تصنيع (كمية)"] = Qty(oc.Manuf);
                if (oc.Handling > 0) data["رجوع تلف مناولة (كمية)"] = Qty(oc.Handling);
            }
            data["لسه في الصيانة دلوقتي"] = oc.BacklogRecords == 0
                ? "لا شيء"
                : $"{oc.BacklogRecords} سجل — كمية {Qty(oc.BacklogQty)}";
        }

        if (!byEmployee)
            return new ZakiToolOutcome(ToJson(data), null);

        var headers = new[] { "مسؤول التسليم", "مخطط", "اتسلّم", "في الموعد", "متأخر", "نسبة الالتزام" };
        var groups = inPeriod
            .GroupBy(d => EmployeeOf(d.Row))
            .Select(g => (Name: g.Key, Stats: Summarize(g.ToList())))
            .OrderByDescending(x => x.Stats.Delivered)
            .ThenByDescending(x => x.Stats.Planned)
            .ToList();

        var rows = groups.Select(x => (IReadOnlyList<string>)new List<string>
        {
            x.Name,
            x.Stats.Planned.ToString(Culture),
            x.Stats.Delivered.ToString(Culture),
            x.Stats.OnTime.ToString(Culture),
            x.Stats.Late.ToString(Culture),
            x.Stats.Percent
        }).ToList();

        data["الأداء حسب مسؤول التسليم"] = ToObjects(headers, rows);

        return new ZakiToolOutcome(
            ToJson(data),
            new ZakiTable($"أداء التسليم حسب المسؤول — {label}", headers, rows));
    }

    private sealed record PerfStats(
        int Planned, int Delivered, int OnTime, int Late, int Pending, int OverdueNow, int Returned)
    {
        public string Percent => Delivered == 0
            ? "—"
            : Math.Round(OnTime * 100.0 / Delivered).ToString(Culture) + "%";
    }

    private static PerfStats Summarize(List<DisplayRow> rows)
    {
        // تاريخ الإكمال الفعّال: حدث الإكمال للمكتملة بالأحداث، وإلا إسقاط الـ View
        var delivered = rows
            .Where(d => d.Status == StatusDelivered && (d.DeliveredAt ?? d.Row.DeliveredAt).HasValue)
            .ToList();
        var onTime = delivered.Count(d => (d.DeliveredAt ?? d.Row.DeliveredAt)!.Value.Date <= d.ShownDue!.Value.Date);

        return new PerfStats(
            Planned: rows.Count,
            Delivered: delivered.Count,
            OnTime: onTime,
            Late: delivered.Count - onTime,
            Pending: rows.Count(d => !d.IsFinal),
            OverdueNow: rows.Count(d => d.Status == StatusOverdue),
            Returned: rows.Count(d => d.Status == StatusReturned));
    }

    // ════════════════════════════════════════════════════════════
    // تعريفات الأدوات (بتتبعت للموديل)
    // ════════════════════════════════════════════════════════════
    private static readonly AiToolDefinition[] Definitions =
    {
        new(GetDeliveryStatus,
            "حالة تسليم فاتورة معينة (برقمها أو مرجعها) أو كل التسليمات المعلّقة لعميل معين: حالة التسليم (ومنها «جزئي» لو بدأ تسليمها على دفعات) وموعد التسليم — وكل مواعيدها لو بنودها مقسّمة على أكثر من موعد — وتقدم التسليم (المقبول/المتبقي) وآخر تسليمة ومسؤول التسليم، ونتيجة آخر تسليمة لو رجع منها حاجة (عيب تصنيع/تلف مناولة/مطلوب تعديل) وإيه اللي لسه في الصيانة منها.",
            new
            {
                type = "object",
                properties = new
                {
                    reference = new { type = "string", description = "رقم الفاتورة أو مرجعها (لو السؤال عن فاتورة معينة)" },
                    customer = new { type = "string", description = "اسم العميل (لو السؤال عن تسليمات عميل)" },
                    limit = new { type = "integer", minimum = 1, maximum = 30 }
                }
            }),
        new(GetDeliveriesSchedule,
            "جدول التسليمات لفترة: النهاردة أو بكرة أو الأسبوع ده، مجمعة باليوم والحالة (جارى/جزئي/متأخر/تم التسليم/مرتجع).",
            new
            {
                type = "object",
                properties = new
                {
                    period = new { type = "string", @enum = new[] { "today", "tomorrow", "week" }, description = "الافتراضي today" },
                    limit = new { type = "integer", minimum = 1, maximum = 40 }
                }
            }),
        new(GetOverdueDeliveries,
            "التسليمات المتأخرة: فات موعد تسليمها ولسه ما اتسلمتش، مجمعة بشرائح التأخير (أيام/أسبوع/أكتر) مع مسؤول التسليم لكل واحدة.",
            new
            {
                type = "object",
                properties = new
                {
                    limit = new { type = "integer", minimum = 1, maximum = 40 }
                }
            }),
        new(GetDeliveryLoads,
            "حمل شغل مسؤولي/مناديب التسليم: لكل مسؤول كام تسليمة معلّقة وكام متأخرة وكام اتسلّم النهاردة.",
            new { type = "object", properties = new { } }),
        new(GetTodayDeliveriesSummary,
            "ملخص تسليمات النهاردة: المخطط تسليمه مقابل اللي اتسلّم فعليًا ونسبة الإنجاز وعدد المتأخرة حتى الآن.",
            new { type = "object", properties = new { } })
    };

    private static readonly AiToolDefinition PerformanceDefinition = new(
        GetDeliveryPerformance,
        "أداء التسليم والالتزام بالمواعيد: نسبة الفواتير اللي اكتمل تسليمها في موعدها أو قبله مقابل المتأخرة خلال فترة (الشهر ده/الشهر اللي فات/آخر عدد أيام)، ودقة الدفعات (أحداث الفترة في مواعيد بنودها)، ونتائج التسليم: نسبة التسليم النظيف (من غير رجوع) وكميات عيوب التصنيع وتلف المناولة وإيه اللي لسه في الصيانة، واختياريًا لكل مسؤول تسليم. للقيادة فقط.",
        new
        {
            type = "object",
            properties = new
            {
                period = new { type = "string", @enum = new[] { "month", "last_month", "days" }, description = "الافتراضي month" },
                days = new { type = "integer", minimum = 1, maximum = 180, description = "عدد الأيام لو period = days (الافتراضي 30)" },
                by_employee = new { type = "boolean", description = "true لعرض الأداء لكل مسؤول تسليم (الافتراضي false)" }
            }
        });
}
