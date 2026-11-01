using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace COCOBOLOERPNEW.Services;

// ════════════════════════════════════════════════════════════
//  لوحة القيادة التنفيذية — كل الأرقام من مصادر حقيقية:
//  Transactions · Payments · Expenses · AuditLogs · Attendances
//  CrmTasks · LeadsCRMs · Complaints · PriceChangeRequests · StockLevels
//
//  ⭐ مرحلة 1 (صحة واتساق):
//  - المشتريات = مشتريات موردين حقيقية فقط (استبعاد المرآة MirrorOf:)
//    والمرتجعات مطروحة من المبيعات والمشتريات (الصافي هو العنوان).
//  - صافي الربح والمصروفات من قائمة الدخل نفسها
//    (FinancialReportsService.GetIncomeStatementAsync) — نفس مسار الكود،
//    فلا يمكن أن يختلف رقم اللوحة عن قائمة الدخل لنفس الفترة.
//  - «تحصيلات الفترة» = دفعات Payments الفعلية بتاريخ الدفعة على فواتير بيع.
//  - «صافي حركة الخزينة» = قبض − صرف من CashboxTransactions داخل الفترة.
//  - المقارنات متماثلة: الفترات الجارية تُقارن بنفس الأيام المنقضية
//    من الفترة السابقة ومن نفس الفترة العام الماضي.
//  - كل قسم يُحسب داخل try/catch مستقل: فشل قسم لا يُسقط اللوحة.
//
//  ⭐ مرحلة 2 (اكتمال الصورة): بلوكات عروض الأسعار والمخزون وأوامر
//  التشغيل والتسليمات والتسويق وB2B والموارد البشرية — كلها من
//  خدمات/تعريفات النظام القائمة (قيمة المخزون من IStockService،
//  التسويق من IMarketingDashboardService، التسليمات من نفس View
//  شاشتها، أوامر التشغيل بنفس قاعدة شاشتها وزكي).
//
//  ⭐ مرحلة 3 (تجربة الرئيس + أداء):
//  - فلتر فرع اختياري يعمّ الأقسام: إسناد الفاتورة للفرع بنفس قاعدة
//    قائمة الدخل (موظف المنشئ أولًا ثم مخزن احتياطيًا) — والربحية
//    نفسها تُجلب من قائمة الدخل بفلتر الفرع. أقسام بلا إسناد فرع في
//    بياناتها (التسويق/B2B/الشكاوى/لوحة المدير العام) تُعلَّم صراحةً
//    في BranchIgnoredSections بدل تجاهل الفلتر بصمت.
//  - فترة مخصصة (من/إلى) بنفس منطق المقارنات المتماثلة.
//  - كاش قصير 60 ثانية للـ DTO بمفتاح (فترة/نطاق/فرع/نطاق مستخدم)،
//    والتحديث اليدوي يتجاوزه ويكتب القيمة الطازجة مكانه.
//  - تجميع على SQL بدل الجلب للذاكرة: ترند 12 شهرًا (GroupBy شهري)،
//    نشاط التدقيق (تجميع يومي لكل حساب + آخر نشاط)، ديون الاستثناءات،
//    أذونات التسليم المعلقة (آخر إجراء لكل فاتورة)، والفرص المفتوحة.
// ════════════════════════════════════════════════════════════
public class ExecutiveCockpitService : IExecutiveCockpitService
{
    private const string MirrorPrefix = "MirrorOf:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly IDbContextFactory<db24804Context> _factory;
    private readonly IFinancialReportsService _financial;
    private readonly IStockService _stock;
    private readonly IMarketingDashboardService _marketing;
    private readonly ISalesDeliveryStatusService _deliveryStatus;
    private readonly CacheService _cache;
    private readonly ILogger<ExecutiveCockpitService> _logger;

    public ExecutiveCockpitService(
        IDbContextFactory<db24804Context> factory,
        IFinancialReportsService financial,
        IStockService stock,
        IMarketingDashboardService marketing,
        ISalesDeliveryStatusService deliveryStatus,
        CacheService cache,
        ILogger<ExecutiveCockpitService> logger)
    {
        _factory = factory;
        _financial = financial;
        _stock = stock;
        _marketing = marketing;
        _deliveryStatus = deliveryStatus;
        _cache = cache;
        _logger = logger;
    }

    // ─── ترجمات لسجل التدقيق ────────────────────────────────
    //    الخريطتان مأخوذتان من حصاد فعلي لكل استدعاءات IAuditService.LogAsync
    //    في الخدمات والمكونات (أسماء الجداول والإجراءات كما تُكتَب حرفيًا).
    //    البحث مُطبَّع: بلا حساسية لحالة الأحرف وبتجاهل الشرطات السفلية
    //    (CRM_Tasks ≡ CrmTasks). أي إجراء غير مُسجَّل لا يظهر خامًا أبدًا —
    //    انظر FallbackActionAr.
    private static readonly Dictionary<string, string> TableAr = new()
    {
        ["Transactions"] = "الفواتير",
        ["TransactionDetails"] = "بنود الفواتير",
        ["Products"] = "المنتجات",
        ["StockLevels"] = "المخزون",
        ["StockTransactions"] = "حركات المخزون",
        ["Warehouses"] = "المخازن",
        ["Branches"] = "الفروع",
        ["Parties"] = "العملاء/الموردين",
        ["Employees"] = "الموظفين",
        ["CrmTasks"] = "المهام",
        ["LeadsCRM"] = "العملاء المحتملين",
        ["LeadsCRMs"] = "العملاء المحتملين",
        ["LeadInteractions"] = "تفاعلات العملاء المحتملين",
        ["SalesOpportunities"] = "الفرص البيعية",
        ["Quotations"] = "عروض الأسعار",
        ["Complaints"] = "الشكاوى",
        ["Expenses"] = "المصروفات",
        ["Payments"] = "المدفوعات",
        ["Users"] = "المستخدمين",
        ["Attendances"] = "الحضور",
        ["Attendance"] = "الحضور",
        ["BiometricLog"] = "سجلات البصمة",
        ["DailyExemption"] = "الإعفاءات اليومية",
        ["AdditionalCharges"] = "الرسوم الإضافية",
        ["DeliveryMaintenanceItems"] = "صيانة التسليمات",
        ["EmployeeLoans"] = "سلف الموظفين",
        ["LoanInstallments"] = "أقساط السلف",
        ["CompanyInfo"] = "بيانات الشركة",
        ["CompanyLocation"] = "مواقع الشركة"
    };

    private static readonly Dictionary<string, string> ActionAr = new()
    {
        // عمليات عامة
        ["Insert"] = "إضافة",
        ["Add"] = "إضافة",
        ["Create"] = "إنشاء",
        ["Update"] = "تعديل",
        ["Edit"] = "تعديل",
        ["Delete"] = "حذف",
        ["PermanentDelete"] = "حذف نهائي",
        ["Cancel"] = "إلغاء",
        ["Approve"] = "اعتماد",
        ["Reject"] = "رفض",
        ["Complete"] = "إتمام",
        ["Receive"] = "استلام",
        ["Apply"] = "تطبيق",
        ["Import"] = "استيراد",
        ["Process"] = "معالجة",
        ["Convert"] = "تحويل",
        // الفواتير والتسليم
        ["UpdateDeliveryStatus"] = "تحديث تسليم",
        ["DeliveryEventRecorded"] = "تسجيل تسليمة",
        ["DeliveryEventVoided"] = "إلغاء تسليمة",
        ["DeliveryPermissionGranted"] = "منح إذن تسليم",
        ["DeliveryPermissionRequest"] = "طلب إذن تسليم",
        ["MaintenanceStatusChanged"] = "تغيير حالة صيانة",
        ["MaintenanceReclassified"] = "إعادة تصنيف عيب",
        ["MaintenanceClosed"] = "إغلاق بند صيانة",
        ["ModificationChargeCreated"] = "رسوم تعديل: إنشاء",
        ["ModificationChargeApproved"] = "رسوم تعديل: اعتماد",
        ["ModificationChargeRejected"] = "رسوم تعديل: رفض",
        ["ModificationChargeCollected"] = "رسوم تعديل: تحصيل",
        ["EditRequest"] = "طلب تعديل",
        ["EditApprove"] = "اعتماد طلب تعديل",
        ["EditReject"] = "رفض طلب تعديل",
        // عروض الأسعار
        ["ConvertToInvoice"] = "تحويل لفاتورة",
        ["CancelUnlink"] = "إلغاء وفك الارتباط بالفاتورة",
        // مهام المدير العام والموظفين والعملاء المحتملين (TaskService)
        ["GeneralManagerTaskAssignOpportunity"] = "إسناد مهمة على فرصة",
        ["GeneralManagerTaskStart"] = "بدء مهمة فرصة",
        ["GeneralManagerTaskComplete"] = "إتمام مهمة فرصة",
        ["GeneralEmployeeTaskAssign"] = "إسناد مهمة لموظف",
        ["GeneralEmployeeTaskStart"] = "بدء مهمة موظف",
        ["GeneralEmployeeTaskComplete"] = "إتمام مهمة موظف",
        ["LeadTaskAssign"] = "إسناد مهمة عميل محتمل",
        ["LeadTaskStart"] = "بدء مهمة عميل محتمل",
        ["LeadTaskComplete"] = "إتمام مهمة عميل محتمل",
        // الاسترداد (RecoveryService)
        ["Recovery/AutoAssign"] = "إسناد تلقائي لفرصة مستردة",
        ["Recovery/Revive"] = "إحياء فرصة مستردة",
        ["Recovery/RejectFinal"] = "رفض نهائي لاسترداد فرصة",
        // السلف والأقساط
        ["PayrollDeduct"] = "خصم قسط من المرتبات",
        ["OffPayrollAllocation"] = "تسوية قسط خارج المرتبات",
        ["Skip"] = "تخطي قسط",
        ["Split"] = "تقسيم قسط",
        // متنوعات
        ["NonRefundable"] = "تعليم كغير قابل للاسترداد",
        ["ChangePassword"] = "تغيير كلمة المرور"
    };

    // مفاتيح مُطبَّعة (أحرف صغيرة وبلا شرطات سفلية) لبحث لا يتأثر بصيغة الكتابة
    private static string NormKey(string? s)
        => string.IsNullOrWhiteSpace(s) ? "" : s.Replace("_", "").Trim().ToLowerInvariant();

    private static readonly Dictionary<string, string> TableArN
        = TableAr.ToDictionary(kv => NormKey(kv.Key), kv => kv.Value);
    private static readonly Dictionary<string, string> ActionArN
        = ActionAr.ToDictionary(kv => NormKey(kv.Key), kv => kv.Value);

    // احتياطي آمن لإجراء غير مُسجَّل في الخريطة: نلتقط فعلًا معروفًا داخل
    // الاسم (Assign/Update/Delete…) ونعرضه عربيًا، وإلا «إجراء» — ولا يظهر
    // الاسم البرمجي الخام للمستخدم أبدًا. الترتيب مقصود: الأطول/الأخص أولًا.
    private static readonly (string Token, string Ar)[] ActionVerbHints =
    {
        ("permanentdelete", "حذف نهائي"),
        ("assign", "إسناد"),
        ("approve", "اعتماد"),
        ("reject", "رفض"),
        ("delete", "حذف"),
        ("cancel", "إلغاء"),
        ("convert", "تحويل"),
        ("complete", "إتمام"),
        ("insert", "إضافة"),
        ("create", "إنشاء"),
        ("update", "تعديل"),
        ("edit", "تعديل"),
        ("add", "إضافة"),
        ("start", "بدء"),
        ("import", "استيراد"),
        ("process", "معالجة"),
        ("apply", "تطبيق"),
        ("receive", "استلام"),
        ("revive", "إحياء"),
        ("deduct", "خصم"),
        ("split", "تقسيم"),
        ("skip", "تخطي")
    };

    private static string FallbackActionAr(string? actionType)
    {
        var key = NormKey(actionType);
        if (key.Length == 0) return "إجراء";
        foreach (var (token, ar) in ActionVerbHints)
            if (key.Contains(token)) return ar;
        return "إجراء";
    }

    private static string Describe(string? tableName, string? actionType)
    {
        var act = actionType != null && ActionArN.TryGetValue(NormKey(actionType), out var a)
            ? a : FallbackActionAr(actionType);
        var tb = tableName != null && TableArN.TryGetValue(NormKey(tableName), out var t) ? t : null;
        // جدول غير معروف → نكتفي بعبارة الإجراء ولا نعرض اسم الجدول الخام
        return tb == null ? act : $"{act} — {tb}";
    }

    // ─── فترات ──────────────────────────────────────────────
    private static (DateTime From, DateTime To) RangeOf(string period)
    {
        var today = DateTime.Today;
        switch (period)
        {
            case "today":
                return (today, today);
            case "week":
                var ws = today.AddDays(-(((int)today.DayOfWeek - (int)DayOfWeek.Saturday) + 7) % 7);
                return (ws, ws.AddDays(6));
            case "quarter":
                var qm = ((today.Month - 1) / 3) * 3 + 1;
                var qs = new DateTime(today.Year, qm, 1);
                return (qs, qs.AddMonths(3).AddDays(-1));
            case "year":
                return (new DateTime(today.Year, 1, 1), new DateTime(today.Year, 12, 31));
            default: // month
                var ms = new DateTime(today.Year, today.Month, 1);
                return (ms, ms.AddMonths(1).AddDays(-1));
        }
    }

    private static string PeriodLabelAr(string period) => period switch
    {
        "today" => "اليوم",
        "week" => "هذا الأسبوع",
        "quarter" => "ربع السنة",
        "year" => "السنة",
        _ => "هذا الشهر"
    };

    // ⭐ نطاقات متماثلة للمقارنة (أ6): الفترة الحالية تُقصّ عند اليوم،
    //    والسابقة/السنوية تأخذان نفس عدد الأيام المنقضية من بدايتهما.
    private sealed record Ranges(
        DateTime From, DateTime ToEff,
        DateTime PrevFrom, DateTime PrevTo,
        DateTime YearFrom, DateTime YearTo,
        string Label);

    private static Ranges ComputeRanges(string period, DateTime? customFrom = null, DateTime? customTo = null)
    {
        // ⭐ مرحلة 3: فترة مخصصة (من/إلى) — نفس منطق التماثل: السابقة هي
        //    نفس الطول مباشرة قبل البداية، والسنوية نفس الأيام المنقضية.
        if (period == "custom" && customFrom.HasValue && customTo.HasValue)
        {
            var from = customFrom.Value.Date;
            var to = customTo.Value.Date;
            if (to < from) (from, to) = (to, from);

            var todayC = DateTime.Today;
            var toEffC = to < todayC ? to : todayC;
            if (toEffC < from) toEffC = from;

            var naturalLen = (to - from).Days + 1;
            var elapsedC = (toEffC - from).Days + 1;

            var prevFromC = from.AddDays(-naturalLen);
            var prevToC = prevFromC.AddDays(elapsedC - 1);

            var yearFromC = from.AddYears(-1);
            var yearNaturalEndC = yearFromC.AddDays(naturalLen - 1);
            var yearToC = yearFromC.AddDays(elapsedC - 1);
            if (yearToC > yearNaturalEndC) yearToC = yearNaturalEndC;

            var labelC = $"فترة مخصصة {from:yyyy/MM/dd} — {to:yyyy/MM/dd}";
            return new Ranges(from, toEffC, prevFromC, prevToC, yearFromC, yearToC, labelC);
        }

        var (fromN, toN) = RangeOf(period);
        var today = DateTime.Today;
        var toEff = toN < today ? toN : today;
        var elapsed = Math.Max((toEff - fromN).Days + 1, 1);

        DateTime Shift(DateTime f, int sign) => period switch
        {
            "today" => f.AddDays(sign),
            "week" => f.AddDays(7 * sign),
            "quarter" => f.AddMonths(3 * sign),
            "year" => f.AddYears(sign),
            _ => f.AddMonths(sign)
        };

        var prevFrom = Shift(fromN, -1);
        var prevNaturalEnd = Shift(prevFrom, +1).AddDays(-1);
        var prevTo = prevFrom.AddDays(elapsed - 1);
        if (prevTo > prevNaturalEnd) prevTo = prevNaturalEnd;

        var yearFrom = fromN.AddYears(-1);
        var yearNaturalEnd = Shift(yearFrom, +1).AddDays(-1);
        var yearTo = yearFrom.AddDays(elapsed - 1);
        if (yearTo > yearNaturalEnd) yearTo = yearNaturalEnd;

        return new Ranges(fromN, toEff, prevFrom, prevTo, yearFrom, yearTo, PeriodLabelAr(period));
    }

    // ─── ⭐ مرحلة 3: نطاق الفرع ──────────────────────────────
    //    نفس إسناد قائمة الدخل: فاتورة الفرع = منشئها موظف الفرع،
    //    أو (المنشئ بلا فرع/بلا موظف) مخزنها من مخازن الفرع.
    private sealed record CockpitBranchScope(
        int? BranchId, string BranchName,
        List<int> EmployeeIds, List<int> WarehouseIds, List<int> CashBoxIds)
    {
        public static readonly CockpitBranchScope All = new(null, "", new(), new(), new());
        public bool IsScoped => BranchId.HasValue;
    }

    private async Task<CockpitBranchScope> BranchScopeAsync(int? branchId)
    {
        if (!branchId.HasValue) return CockpitBranchScope.All;

        await using var db = await _factory.CreateDbContextAsync();
        var employeeIds = await db.Employees.AsNoTracking()
            .Where(e => e.BranchId == branchId.Value)
            .Select(e => e.EmployeeId).ToListAsync();
        var warehouseIds = await db.Warehouses.AsNoTracking()
            .Where(w => w.BranchId == branchId.Value)
            .Select(w => w.WarehouseId).ToListAsync();
        var cashBoxIds = await db.CashBoxes.AsNoTracking()
            .Where(c => c.BranchId == branchId.Value)
            .Select(c => c.CashBoxId).ToListAsync();
        var name = await db.Branches.AsNoTracking()
            .Where(b => b.BranchId == branchId.Value)
            .Select(b => b.BranchNameAr).FirstOrDefaultAsync();

        return new CockpitBranchScope(branchId, name ?? "", employeeIds, warehouseIds, cashBoxIds);
    }

    // تطبيق نطاق الفرع على استعلام فواتير (نفس قاعدة FinancialReportsService)
    private static IQueryable<Transaction> ScopeTx(
        IQueryable<Transaction> query, db24804Context db, CockpitBranchScope scope)
    {
        if (!scope.IsScoped) return query;
        var empIds = scope.EmployeeIds;
        var whIds = scope.WarehouseIds;
        return query.Where(t =>
            (t.EmpId != null && empIds.Contains(t.EmpId.Value))
            || ((t.EmpId == null || !db.Employees.Any(e => e.EmployeeId == t.EmpId.Value && e.BranchId != null))
                && whIds.Contains(t.WarehouseId)));
    }

    // نفس الإسناد لعروض الأسعار (EmpId للبائع + WarehouseId)
    private static IQueryable<Quotation> ScopeQuotations(
        IQueryable<Quotation> query, db24804Context db, CockpitBranchScope scope)
    {
        if (!scope.IsScoped) return query;
        var empIds = scope.EmployeeIds;
        var whIds = scope.WarehouseIds;
        return query.Where(q =>
            (q.EmpId != null && empIds.Contains(q.EmpId.Value))
            || ((q.EmpId == null || !db.Employees.Any(e => e.EmployeeId == q.EmpId.Value && e.BranchId != null))
                && q.WarehouseId != null && whIds.Contains(q.WarehouseId.Value)));
    }

    // ─── أرقام الفترة الأساسية (بلا قائمة الدخل) ─────────────
    private sealed class PeriodFigures
    {
        public decimal SalesGross, SaleReturns;
        public decimal PurchasesGross, PurchaseReturns;
        public decimal Collections;
        public decimal CashIn, CashOut;
        public decimal SalesNet => SalesGross - SaleReturns;
        public decimal PurchasesNet => PurchasesGross - PurchaseReturns;
        public decimal CashNet => CashIn - CashOut;
    }

    private async Task<PeriodFigures> FiguresAsync(DateTime from, DateTime to, CockpitBranchScope scope)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var fromD = from.Date;
        var toEnd = to.Date.AddDays(1).AddTicks(-1);

        var salesGross = await ScopeTx(db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Sale
                     && (t.InvoiceStatus == null || t.InvoiceStatus != InvoiceStatuses.Cancelled)
                     && t.TransactionDate >= fromD && t.TransactionDate <= toEnd), db, scope)
            .SumAsync(t => (decimal?)t.GrandTotal) ?? 0m;

        var saleReturns = await ScopeTx(db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.SaleReturn
                     && (t.InvoiceStatus == null || t.InvoiceStatus != InvoiceStatuses.Cancelled)
                     && t.TransactionDate >= fromD && t.TransactionDate <= toEnd), db, scope)
            .SumAsync(t => (decimal?)t.GrandTotal) ?? 0m;

        // ⭐ المشتريات الحقيقية فقط: استبعاد فواتير المرآة الداخلية
        //    (ReferenceType = "MirrorOf:" + رقم فاتورة البيع — نفس صيغة
        //    FinancialReportsService وInvoiceService)
        var purchasesGross = await ScopeTx(db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Purchase
                     && (t.InvoiceStatus == null || t.InvoiceStatus != InvoiceStatuses.Cancelled)
                     && (t.ReferenceType == null || !t.ReferenceType.StartsWith(MirrorPrefix))
                     && t.TransactionDate >= fromD && t.TransactionDate <= toEnd), db, scope)
            .SumAsync(t => (decimal?)t.GrandTotal) ?? 0m;

        var purchaseReturns = await ScopeTx(db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.PurchaseReturn
                     && (t.InvoiceStatus == null || t.InvoiceStatus != InvoiceStatuses.Cancelled)
                     && t.TransactionDate >= fromD && t.TransactionDate <= toEnd), db, scope)
            .SumAsync(t => (decimal?)t.GrandTotal) ?? 0m;

        // ⭐ تحصيلات الفترة الفعلية: دفعات Payments بتاريخ الدفعة (PaymentDate)
        //    على فواتير بيع — المصدر المؤرّخ الحقيقي للتحصيل في النظام
        //    (PaymentService يسجل كل تحصيل كصف Payment + حركة خزينة).
        var saleTx = ScopeTx(db.Transactions.AsNoTracking(), db, scope);
        var collections = await db.Payments.AsNoTracking()
            .Join(saleTx, p => p.TransactionId, t => t.TransactionId, (p, t) => new { p, t })
            .Where(x => x.t.TransactionType == TransactionTypes.Sale
                     && x.p.PaymentDate >= fromD && x.p.PaymentDate <= toEnd)
            .SumAsync(x => (decimal?)x.p.Amount) ?? 0m;

        // حركة الخزينة النقدية داخل الفترة (نفس أنواع CashFlowService) —
        // بفلتر الفرع: حركات خزن الفرع فقط (CashBox.BranchId)
        var cashQuery = db.CashboxTransactions.AsNoTracking()
            .Where(t => t.TransactionDate >= fromD && t.TransactionDate <= toEnd);
        if (scope.IsScoped)
        {
            var boxIds = scope.CashBoxIds;
            cashQuery = cashQuery.Where(t => boxIds.Contains(t.CashBoxId));
        }

        var cashIn = await cashQuery
            .Where(t => t.TransactionType == "قبض" || t.TransactionType == "In")
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;

        var cashOut = await cashQuery
            .Where(t => t.TransactionType == "صرف" || t.TransactionType == "Out")
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;

        return new PeriodFigures
        {
            SalesGross = salesGross,
            SaleReturns = saleReturns,
            PurchasesGross = purchasesGross,
            PurchaseReturns = purchaseReturns,
            Collections = collections,
            CashIn = cashIn,
            CashOut = cashOut
        };
    }

    // ⭐ قائمة الدخل لنفس الفترة — نفس مسار الكود الذي تقرأه شاشة قائمة الدخل
    //    (مرحلة 3: بفلتر الفرع عند اختياره — نفس إسناد الفرع داخل الخدمة)
    private async Task<IncomeStatementDto> StatementAsync(DateTime from, DateTime to, int? branchId)
        => await _financial.GetIncomeStatementAsync(new IncomeStatementFilterDto
        {
            FromDate = from.Date,
            ToDate = to.Date,
            IncludeComparison = false,
            IncludeMonthlyTrend = false,
            BranchId = branchId
        });

    private static ExecutiveKpiDto Kpi(decimal cur, decimal prev, decimal year)
        => new() { Value = cur, PrevValue = prev, YearValue = year };

    // ─── فشل جزئي متسامح: قسم ساقط يُسجَّل ولا يُسقط اللوحة ──
    private async Task<T> SafeAsync<T>(string section, List<string> failed, Func<Task<T>> work, T fallback)
    {
        try { return await work(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExecutiveCockpit: section {Section} failed", section);
            if (!failed.Contains(section)) failed.Add(section);
            return fallback;
        }
    }

    private sealed record Figs3(PeriodFigures Cur, PeriodFigures Prev, PeriodFigures Year);
    private sealed record Stmt3(IncomeStatementDto? Cur, IncomeStatementDto? Prev, IncomeStatementDto? Year);
    private sealed record TrendResult(List<ExecutiveTrendPointDto> Trend, decimal Forecast, decimal LastMonthSales, decimal ForecastPct);
    private sealed record TeamResult(List<ExecutiveEmployeeRowDto> Employees, ExecutiveGmDto Gm, int Dormant);
    private sealed record ExceptionsResult(ExecutiveExceptionsDto Exceptions, int Escalated);
    private sealed record PendingResult(int InvoiceEdits, int ClosureApprovals, int DeliveryPermits, int PriceRequests);
    private sealed record PipelineResult(decimal CashBalance, int Open, decimal Value, int Stalled, int B2b, List<ExecutiveBranchSaleDto> BranchSales);

    // ════════════════════════════════════════════════════════
    //  ⭐ مرحلة 3: نقطة الدخول العامة — كاش قصير (60 ث) بمفتاح
    //     (الفترة + النطاق الفعلي + الفرع + نطاق المستخدم). التحديث
    //     اليدوي (bypassCache) يحسب طازجًا ويكتب النتيجة في الكاش.
    //     استدعاء زكي القديم GetCockpitAsync(period) يعمل كما هو
    //     (كل الفروع، بلا فترة مخصصة) ويستفيد من نفس الكاش.
    // ════════════════════════════════════════════════════════
    public async Task<ExecutiveCockpitDto> GetCockpitAsync(
        string period,
        int? branchId = null,
        DateTime? customFrom = null,
        DateTime? customTo = null,
        bool bypassCache = false,
        string userScope = "shared")
    {
        if (period is not ("today" or "week" or "month" or "quarter" or "year" or "custom"))
            period = "month";
        if (period == "custom" && (!customFrom.HasValue || !customTo.HasValue))
            period = "month";

        var ranges = ComputeRanges(period, customFrom, customTo);
        var key = $"exec_cockpit:{period}:{ranges.From:yyyyMMdd}:{ranges.ToEff:yyyyMMdd}" +
                  $":b{(branchId.HasValue ? branchId.Value.ToString() : "all")}:{userScope}";

        if (!bypassCache)
            return await _cache.GetOrCreateAsync(
                key, () => ComputeCockpitAsync(period, ranges, branchId), CacheTtl);

        var fresh = await ComputeCockpitAsync(period, ranges, branchId);
        _cache.Set(key, fresh, CacheTtl);
        return fresh;
    }

    private async Task<ExecutiveCockpitDto> ComputeCockpitAsync(string period, Ranges ranges, int? branchId)
    {
        var failed = new List<string>();
        var scope = await BranchScopeAsync(branchId);

        // ── 1) المؤشرات الأساسية: الفترة / السابقة / نفس الفترة السنة الماضية ──
        var figs = await SafeAsync("kpis", failed, async () => new Figs3(
            await FiguresAsync(ranges.From, ranges.ToEff, scope),
            await FiguresAsync(ranges.PrevFrom, ranges.PrevTo, scope),
            await FiguresAsync(ranges.YearFrom, ranges.YearTo, scope)),
            new Figs3(new PeriodFigures(), new PeriodFigures(), new PeriodFigures()));

        // ── 1ب) الربحية من قائمة الدخل (مصروفات تشغيل شاملة الرواتب + صافي الربح) ──
        var stmts = await SafeAsync("profit", failed, async () => new Stmt3(
            await StatementAsync(ranges.From, ranges.ToEff, branchId),
            await StatementAsync(ranges.PrevFrom, ranges.PrevTo, branchId),
            await StatementAsync(ranges.YearFrom, ranges.YearTo, branchId)),
            new Stmt3(null, null, null));

        // ── 2) اتجاه 12 شهر + توقع إقفال الشهر ──
        var trendRes = await SafeAsync("trend", failed, () => TrendAsync(scope),
            new TrendResult(new List<ExecutiveTrendPointDto>(), 0, 0, 0));

        // ── 3) الفريق + لوحة المدير العام ──
        var team = await SafeAsync("team", failed, () => TeamAsync(ranges, scope),
            new TeamResult(new List<ExecutiveEmployeeRowDto>(), new ExecutiveGmDto(), 0));

        // ── 4) الاستثناءات ──
        var exc = await SafeAsync("exceptions", failed, () => ExceptionsAsync(scope),
            new ExceptionsResult(new ExecutiveExceptionsDto(), 0));

        // ── 5) بنود بانتظار قرار (أ8) ──
        var pending = await SafeAsync("pending", failed, () => PendingAsync(scope),
            new PendingResult(0, 0, 0, exc.Exceptions.PendingPrice));

        // ── 6) التشغيل: مصنع / Leads ──
        var ops = await SafeAsync("ops", failed, () => OpsAsync(scope), new ExecutiveOpsDto());

        // ── 7) خزينة (رصيد) · بايبلاين الفرص · B2B · مبيعات الفروع ──
        var pipe = await SafeAsync("pipeline", failed, () => PipelineAsync(ranges, scope),
            new PipelineResult(0, 0, 0, 0, 0, new List<ExecutiveBranchSaleDto>()));

        // ── 8) أعمار الذمم والمستحقات ──
        var aging = await SafeAsync("aging", failed, () => AgingAsync(scope), new ExecutiveAgingDto());

        // ── ⭐ مرحلة 2: بلوكات التغطية ──
        // ── 9) عروض الأسعار ──
        var quotations = await SafeAsync("quotations", failed, () => QuotationsAsync(ranges, scope), new ExecutiveQuotationsDto());

        // ── 10) المخزون (لقطة حالية) ──
        var inventory = await SafeAsync("inventory", failed, () => InventoryAsync(branchId, scope), new ExecutiveInventoryDto());

        // ── 11) أوامر التشغيل والإنتاج ──
        var jobOrders = await SafeAsync("joborders", failed, () => JobOrdersAsync(ranges, scope), new ExecutiveJobOrdersDto());

        // ── 12) التسليمات التشغيلي ──
        var deliveries = await SafeAsync("deliveries", failed, () => DeliveriesAsync(ranges, scope), new ExecutiveDeliveriesDto());

        // ── 12.5) التكليفات العامة ──
        //     ⚠️ التكليفات بلا بُعد فرع في النظام — أرقامها إجمالي الشركة دائمًا
        //        (تُعلَّم في BranchIgnoredSections عند اختيار فرع).
        var generalTasks = await SafeAsync("generaltasks", failed, () => GeneralTasksAsync(ranges), new ExecutiveGeneralTasksDto());

        // ── 13) التسويق المصغر (من خدمة لوحة التسويق نفسها) ──
        //     ⚠️ خدمة التسويق بلا بُعد فرع — أرقامها إجمالي الشركة دائمًا
        //        (تُعلَّم في BranchIgnoredSections عند اختيار فرع).
        var marketing = await SafeAsync("marketing", failed, () => MarketingAsync(ranges), new ExecutiveMarketingDto());

        // ── 14) بوابة B2B موسّع ──
        //     ⚠️ طلبات البوابة بلا إسناد فرع في النظام — إجمالي الشركة دائمًا.
        var b2bBlock = await SafeAsync("b2b", failed, () => B2bAsync(ranges), new ExecutiveB2bBlockDto());

        // ── 15) الموارد البشرية التشغيلي ──
        var hr = await SafeAsync("hr", failed, () => HrAsync(ranges, scope), new ExecutiveHrDto());

        // ── تجميع القرارات المعلقة ──
        var priceForDecisions = failed.Contains("pending") ? exc.Exceptions.PendingPrice : pending.PriceRequests;
        var decisions = priceForDecisions + exc.Escalated + team.Dormant
                      + pending.InvoiceEdits + pending.ClosureApprovals + pending.DeliveryPermits;

        team.Gm.PendingPrice = exc.Exceptions.PendingPrice;
        team.Gm.EscalatedComplaints = exc.Escalated;

        // ── القصة ──
        var cur = figs.Cur;
        var prev = figs.Prev;
        var salesPct = prev.SalesNet == 0 ? (decimal?)null
            : Math.Round((cur.SalesNet - prev.SalesNet) / Math.Abs(prev.SalesNet) * 100, 1);
        var story = failed.Contains("kpis")
            ? $"{ranges.Label}: تعذّر تحميل المؤشرات المالية لهذه الفترة."
            : $"{ranges.Label}{(scope.IsScoped ? $" — فرع {scope.BranchName}" : "")}: المبيعات (صافي بعد المرتجعات) {cur.SalesNet:N0} ج" +
              (salesPct.HasValue ? $" ({(salesPct.Value >= 0 ? "+" : "")}{salesPct.Value}% عن الفترة السابقة)" : "") +
              $" — تحصيلات الفترة {cur.Collections:N0} ج — " +
              $"الفريق: {team.Employees.Count(x => x.ActionsToday > 0)} نشط من {team.Employees.Count}" +
              (team.Dormant > 0 ? $" — {team.Dormant} موظف ساكن يحتاج قرارًا" : "") +
              (decisions > 0 ? $" — {decisions} بند في انتظارك." : " — لا بنود معلقة.");

        // ── قائمة الدخل المصغرة (من نفس استدعاء قائمة الدخل) ──
        var miniPl = new ExecutiveMiniPlDto();
        if (stmts.Cur != null)
        {
            var s = stmts.Cur;
            miniPl = new ExecutiveMiniPlDto
            {
                Loaded = true,
                NetRevenue = s.NetRevenue,
                Cogs = s.CostOfGoodsSold,
                GrossProfit = s.GrossProfit,
                GrossProfitMargin = s.GrossProfitMargin,
                OperatingExpenses = s.TotalOperatingExpenses,
                PayrollExpense = s.PayrollExpense,
                NetProfit = s.NetProfit,
                NetProfitMargin = s.NetProfitMargin,
                TargetMarginPct = s.ProfitTarget.CurrentTargetPercentage,
                NextTargetMarginPct = s.ProfitTarget.NextTargetPercentage,
                TargetAchievementPct = s.ProfitTarget.AchievementPercentage,
                TargetStatus = s.ProfitTarget.Status,
                TargetMessage = s.ProfitTarget.Message
            };
        }

        return new ExecutiveCockpitDto
        {
            Sales = Kpi(cur.SalesNet, prev.SalesNet, figs.Year.SalesNet),
            Collected = Kpi(cur.Collections, prev.Collections, figs.Year.Collections),
            Purchases = Kpi(cur.PurchasesNet, prev.PurchasesNet, figs.Year.PurchasesNet),
            Expenses = Kpi(stmts.Cur?.TotalOperatingExpenses ?? 0, stmts.Prev?.TotalOperatingExpenses ?? 0, stmts.Year?.TotalOperatingExpenses ?? 0),
            Net = Kpi(stmts.Cur?.NetProfit ?? 0, stmts.Prev?.NetProfit ?? 0, stmts.Year?.NetProfit ?? 0),
            CashNet = Kpi(cur.CashNet, prev.CashNet, figs.Year.CashNet),
            SaleReturns = cur.SaleReturns,
            PurchaseReturns = cur.PurchaseReturns,
            Trend = trendRes.Trend,
            Employees = team.Employees,
            Gm = team.Gm,
            Exceptions = exc.Exceptions,
            Ops = ops,
            CashBalance = pipe.CashBalance,
            PipelineOpen = pipe.Open,
            PipelineValue = pipe.Value,
            PipelineStalled = pipe.Stalled,
            B2bPending = pipe.B2b,
            BranchSales = pipe.BranchSales,
            Aging = aging,
            MiniPl = miniPl,
            Quotations = quotations,
            Inventory = inventory,
            JobOrders = jobOrders,
            Deliveries = deliveries,
            GeneralTasks = generalTasks,
            Marketing = marketing,
            B2bBlock = b2bBlock,
            Hr = hr,
            PendingInvoiceEdits = pending.InvoiceEdits,
            PendingClosureApprovals = pending.ClosureApprovals,
            PendingDeliveryPermits = pending.DeliveryPermits,
            PeriodLabel = ranges.Label,
            PeriodKey = period,
            RangeFrom = ranges.From,
            RangeTo = ranges.ToEff,
            BranchId = scope.BranchId,
            BranchName = scope.BranchName,
            BranchIgnoredSections = scope.IsScoped
                ? new List<string> { "marketing", "b2b", "gm", "complaints", "generaltasks" }
                : new List<string>(),
            Story = story,
            DecisionsPending = decisions,
            DormantCount = team.Dormant,
            ForecastValue = trendRes.Forecast,
            LastMonthSales = trendRes.LastMonthSales,
            ForecastVsLastPct = trendRes.ForecastPct,
            FailedSections = failed,
            GeneratedAt = DateTime.Now
        };
    }

    // ─── اتجاه 12 شهر (مبيعات/مشتريات صافية + تحصيلات فعلية) ──
    //    ⭐ مرحلة 3: التجميع الشهري على SQL (GroupBy) — قبل ذلك كانت
    //       كل فواتير ودفعات 12 شهرًا تُجلب للذاكرة وتُجمّع في الكود.
    //       الناتج الآن ≤ 48 صفًا مجمعًا للفواتير و≤ 12 للدفعات.
    private async Task<TrendResult> TrendAsync(CockpitBranchScope scope)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var today = DateTime.Today;
        var trendStart = new DateTime(today.Year, today.Month, 1).AddMonths(-11);

        var txAgg = await ScopeTx(db.Transactions.AsNoTracking()
            .Where(t => (t.TransactionType == TransactionTypes.Sale
                      || t.TransactionType == TransactionTypes.SaleReturn
                      || t.TransactionType == TransactionTypes.Purchase
                      || t.TransactionType == TransactionTypes.PurchaseReturn)
                     && (t.InvoiceStatus == null || t.InvoiceStatus != InvoiceStatuses.Cancelled)
                     && t.TransactionDate >= trendStart
                     && (t.TransactionType != TransactionTypes.Purchase
                         || t.ReferenceType == null || !t.ReferenceType.StartsWith(MirrorPrefix))), db, scope)
            .GroupBy(t => new { t.TransactionDate.Year, t.TransactionDate.Month, t.TransactionType })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.TransactionType, Total = g.Sum(x => x.GrandTotal) })
            .ToListAsync();

        var saleTx = ScopeTx(db.Transactions.AsNoTracking(), db, scope);
        var payAgg = await db.Payments.AsNoTracking()
            .Join(saleTx, p => p.TransactionId, t => t.TransactionId, (p, t) => new { p, t })
            .Where(x => x.t.TransactionType == TransactionTypes.Sale && x.p.PaymentDate >= trendStart)
            .GroupBy(x => new { x.p.PaymentDate.Year, x.p.PaymentDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(x => x.p.Amount) })
            .ToListAsync();

        decimal TxMonth(int year, int month, string type)
            => txAgg.Where(x => x.Year == year && x.Month == month && x.TransactionType == type)
                    .Sum(x => x.Total);
        decimal NetSalesMonth(DateTime m)
            => TxMonth(m.Year, m.Month, TransactionTypes.Sale) - TxMonth(m.Year, m.Month, TransactionTypes.SaleReturn);
        decimal NetPurchasesMonth(DateTime m)
            => TxMonth(m.Year, m.Month, TransactionTypes.Purchase) - TxMonth(m.Year, m.Month, TransactionTypes.PurchaseReturn);

        var trend = new List<ExecutiveTrendPointDto>();
        for (int i = 0; i < 12; i++)
        {
            var mStart = trendStart.AddMonths(i);
            trend.Add(new ExecutiveTrendPointDto
            {
                Label = mStart.ToString("MM/yyyy"),
                Sales = NetSalesMonth(mStart),
                Collected = payAgg.Where(p => p.Year == mStart.Year && p.Month == mStart.Month).Sum(p => p.Total),
                Purchases = NetPurchasesMonth(mStart)
            });
        }

        // توقع إقفال الشهر الحالي (على المبيعات الصافية)
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthSales = NetSalesMonth(monthStart);
        var lastMonthSales = NetSalesMonth(monthStart.AddMonths(-1));
        var dayOfMonth = Math.Max(today.Day, 1);
        var forecast = Math.Round(monthSales / dayOfMonth * DateTime.DaysInMonth(today.Year, today.Month), 0);
        var forecastPct = lastMonthSales == 0 ? 0 : Math.Round((forecast - lastMonthSales) / lastMonthSales * 100, 1);

        return new TrendResult(trend, forecast, lastMonthSales, forecastPct);
    }

    // ─── الفريق + لوحة المدير العام ──────────────────────────
    private async Task<TeamResult> TeamAsync(Ranges ranges, CockpitBranchScope scope)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);

        // بفلتر الفرع: جدول الفريق لموظفي الفرع فقط
        // (المدير العام نفسه يُبحث عنه على مستوى الشركة — انظر أسفل).
        // ⭐ نشطون فقط (قرار المالك): الموقوف/المستقيل/بالإجازة لا يظهرون
        //    في جدول الفريق ولا في العدّادات المشتقة منه (القصة/الخاملون/
        //    فريق المدير العام). لوحة الشرف لا تتغير — كانت تستبعد صفوف
        //    «off» (غير النشطين) أصلًا فعضويتها كما هي.
        var empQuery = db.Employees.AsNoTracking()
            .Where(e => e.EndDate == null && e.Status == EmployeeStatuses.Active);
        if (scope.IsScoped) empQuery = empQuery.Where(e => e.BranchId == scope.BranchId);
        var employees = await empQuery
            .OrderBy(e => e.FullName)
            .ToListAsync();

        var users = await db.Users.AsNoTracking()
            .Where(u => u.EmployeeId != null)
            .Select(u => new { u.Username, u.EmployeeId })
            .ToListAsync();
        var loginByEmp = users.Where(u => u.EmployeeId.HasValue)
            .GroupBy(u => u.EmployeeId!.Value)
            .ToDictionary(g => g.Key, g => g.First().Username);

        var auditFrom = today.AddDays(-34);
        var logins = loginByEmp.Values.Distinct().ToList();

        // ⭐ مرحلة 3: تجميع التدقيق على SQL — عدّ يومي لكل حساب + آخر
        //    نشاط داخل نفس نافذة الـ 35 يومًا، بدل جلب كل أحداث النافذة
        //    للذاكرة ومسح القائمة كاملة لكل موظف (نفس الدلالات تمامًا).
        var dailyByLogin = new Dictionary<string, Dictionary<DateTime, int>>();
        var lastByLogin = new Dictionary<string, DateTime>();
        if (logins.Count > 0)
        {
            var daily = await db.AuditLogs.AsNoTracking()
                .Where(a => a.ActionDate != null && a.ActionDate >= auditFrom
                         && a.LoginName != null && logins.Contains(a.LoginName))
                .GroupBy(a => new { a.LoginName, Day = a.ActionDate!.Value.Date })
                .Select(g => new { g.Key.LoginName, g.Key.Day, Count = g.Count() })
                .ToListAsync();
            foreach (var row in daily)
            {
                if (row.LoginName == null) continue;
                if (!dailyByLogin.TryGetValue(row.LoginName, out var byDay))
                    dailyByLogin[row.LoginName] = byDay = new Dictionary<DateTime, int>();
                byDay[row.Day] = row.Count;
            }

            var lasts = await db.AuditLogs.AsNoTracking()
                .Where(a => a.ActionDate != null && a.ActionDate >= auditFrom
                         && a.LoginName != null && logins.Contains(a.LoginName))
                .GroupBy(a => a.LoginName)
                .Select(g => new { LoginName = g.Key, Last = g.Max(a => a.ActionDate) })
                .ToListAsync();
            foreach (var row in lasts)
                if (row.LoginName != null && row.Last.HasValue)
                    lastByLogin[row.LoginName] = row.Last.Value;
        }

        var attToday = await db.Attendances.AsNoTracking()
            .Where(a => a.LogDate == today)
            .Select(a => new { a.BiometricCode, a.LateMinutes })
            .ToListAsync();
        var attByBio = attToday.GroupBy(a => a.BiometricCode)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.LateMinutes).First());

        var salesByEmp = await ScopeTx(db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Sale
                     && (t.InvoiceStatus == null || t.InvoiceStatus != InvoiceStatuses.Cancelled)
                     && t.TransactionDate >= monthStart && t.EmpId != null), db, scope)
            .GroupBy(t => t.EmpId!.Value)
            .Select(g => new { EmpId = g.Key, Total = g.Sum(x => x.GrandTotal), Paid = g.Sum(x => x.PaidAmount) })
            .ToListAsync();
        var salesDict = salesByEmp.ToDictionary(x => x.EmpId, x => x);

        var tasksQuery = db.CrmTasks.AsNoTracking().Where(t => t.Status == "Pending");
        if (scope.IsScoped)
        {
            var empIds = scope.EmployeeIds;
            tasksQuery = tasksQuery.Where(t => empIds.Contains(t.AssignedTo));
        }
        var pendingTasks = await tasksQuery
            .Select(t => new { t.AssignedTo, t.DueDate })
            .ToListAsync();

        var empRows = new List<ExecutiveEmployeeRowDto>();
        foreach (var e in employees)
        {
            loginByEmp.TryGetValue(e.EmployeeId, out var login);
            Dictionary<DateTime, int>? myDays = null;
            if (login != null) dailyByLogin.TryGetValue(login, out myDays);

            var heat = new List<int>();
            for (int d = 6; d >= 0; d--)
            {
                var day = today.AddDays(-d);
                heat.Add(myDays != null && myDays.TryGetValue(day, out var hc) ? hc : 0);
            }

            var actionsToday = myDays != null && myDays.TryGetValue(today, out var ac) ? ac : 0;
            var actionsMonth = myDays?.Where(kv => kv.Key >= today.AddDays(-29)).Sum(kv => kv.Value) ?? 0;
            DateTime? last = login != null && lastByLogin.TryGetValue(login, out var lv) ? lv : null;

            string attText = "بلا تسجيل";
            // المعفى من البصمة أولًا وقبل أي بحث في البصمة: حضوره يدوي
            // بطبيعته فلا تُقرأ خانته «بلا تسجيل» كتقصير.
            if (e.IsPermanentlyExempt == true) attText = "معفى من البصمة";
            else if (e.Status == EmployeeStatuses.Suspended) attText = "موقوف";
            else if (e.Status == EmployeeStatuses.OnLeave) attText = "بالإجازة";
            else if (e.Status == EmployeeStatuses.Resigned) attText = "مستقيل";
            else if (e.BioEmployeeId.HasValue && attByBio.TryGetValue(e.BioEmployeeId.Value, out var att))
                attText = (att.LateMinutes ?? 0) > 15 ? "متأخر" : "حاضر";

            salesDict.TryGetValue(e.EmployeeId, out var sl);
            var monthSalesEmp = sl?.Total ?? 0m;
            var monthCollEmp = sl?.Paid ?? 0m;
            var jt = e.JobTitle ?? "";
            var isSales = monthSalesEmp > 0 || jt.Contains("مبيعات") || jt.Contains("مندوب");

            var myTasks = pendingTasks.Where(t => t.AssignedTo == e.EmployeeId).ToList();

            var hrOff = e.Status != EmployeeStatuses.Active;
            var hasAccount = login != null;
            string status;
            if (hrOff) status = "off";
            else if (!hasAccount) status = "gray";
            else
            {
                var daysIdle = last == null ? 999 : (DateTime.Now - last.Value).TotalDays;
                status = daysIdle >= 3 ? "red" : actionsToday == 0 ? "yellow" : "green";
            }

            empRows.Add(new ExecutiveEmployeeRowDto
            {
                HrStatus = string.IsNullOrWhiteSpace(e.Status) ? "—" : e.Status,
                HasAccount = hasAccount,
                EmployeeId = e.EmployeeId,
                FullName = e.FullName,
                JobTitle = string.IsNullOrWhiteSpace(jt) ? "—" : jt,
                Attendance = attText,
                ActionsToday = actionsToday,
                ActionsMonth = actionsMonth,
                Heat7 = heat,
                LastActivity = last,
                LastActivityText = last == null ? "لا يوجد نشاط" : HumanizeWhen(last.Value),
                MonthSales = monthSalesEmp,
                MonthCollection = monthCollEmp,
                TasksPending = myTasks.Count,
                TasksOverdue = myTasks.Count(t => t.DueDate < DateTime.Now),
                Status = status,
                IsSales = isSales
            });
        }

        // لوحة المدير العام — ⚠️ بلا فلتر فرع بطبيعتها (منصب على مستوى
        // الشركة): عند اختيار فرع يُبحث عنه في كل الشركة لا في قائمة
        // الفرع، و«فريقه في سطر» وحده يتبع الفرع المختار (من empRows).
        var hrOffIds = empRows.Where(x => x.Status == "off").Select(x => x.EmployeeId).ToHashSet();
        var gm = employees.FirstOrDefault(x => (x.JobTitle ?? "").Contains("مدير عام"))
              ?? employees.FirstOrDefault(x => (x.JobTitle ?? "").Contains("مدير عامًا"));
        // فلتر «نشط فقط» قد يُخرج المدير العام من القائمة أعلاه (فرعًا أو
        // شركة) — فيُستكمل البحث عنه على مستوى الشركة كما كان يحدث أصلًا
        // عند تفعيل فلتر الفرع، حتى لا تختفي لوحته لمجرد حالته.
        if (gm == null)
        {
            gm = await db.Employees.AsNoTracking()
                .Where(x => x.EndDate == null && x.JobTitle != null && x.JobTitle.Contains("مدير عام"))
                .OrderBy(x => x.FullName)
                .FirstOrDefaultAsync();
        }
        var sysTeam = empRows.Where(x => !hrOffIds.Contains(x.EmployeeId) && x.HasAccount).ToList();
        var gmDto = new ExecutiveGmDto
        {
            TeamSalesMonth = empRows.Sum(x => x.MonthSales),
            TeamActiveToday = sysTeam.Count(x => x.ActionsToday > 0),
            TeamTotal = sysTeam.Count
        };
        if (gm != null)
        {
            loginByEmp.TryGetValue(gm.EmployeeId, out var gmLogin);
            gmDto.Found = true;
            gmDto.FullName = gm.FullName;
            if (gmLogin != null)
            {
                Dictionary<DateTime, int>? gmDays = null;
                if (gmLogin != null) dailyByLogin.TryGetValue(gmLogin, out gmDays);
                gmDto.ActionsWeek = gmDays?.Where(kv => kv.Key >= today.AddDays(-6)).Sum(kv => kv.Value) ?? 0;
                gmDto.LastActivityText = lastByLogin.TryGetValue(gmLogin, out var gmLast)
                    ? HumanizeWhen(gmLast) : "لا يوجد نشاط";

                var recent = await db.AuditLogs.AsNoTracking()
                    .Where(a => a.LoginName == gmLogin)
                    .OrderByDescending(a => a.ActionDate)
                    .Take(6)
                    .Select(a => new { a.ActionDate, a.TableName, a.ActionType })
                    .ToListAsync();
                gmDto.Recent = recent.Select(a => new ExecutiveGmActionDto
                {
                    When = a.ActionDate.HasValue ? HumanizeWhen(a.ActionDate.Value) : "—",
                    What = Describe(a.TableName, a.ActionType)
                }).ToList();
            }
        }

        var dormant = empRows.Count(x => x.Status == "red");
        return new TeamResult(empRows, gmDto, dormant);
    }

    // طلبات الأسعار المعلقة — بفلتر الفرع تُسنَد لفرع الموظف الطالب
    // (RequestedBy = اسم مستخدم ← Users ← Employee.BranchId عبر قائمة
    // موظفي الفرع في النطاق).
    private static IQueryable<PriceChangeRequest> ScopePriceRequests(
        IQueryable<PriceChangeRequest> query, db24804Context db, CockpitBranchScope scope)
    {
        if (!scope.IsScoped) return query;
        var empIds = scope.EmployeeIds;
        return query.Where(r => db.Users.Any(u => u.Username == r.RequestedBy
                                               && u.EmployeeId != null
                                               && empIds.Contains(u.EmployeeId.Value)));
    }

    // ─── الاستثناءات ─────────────────────────────────────────
    private async Task<ExceptionsResult> ExceptionsAsync(CockpitBranchScope scope)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var today = DateTime.Today;
        var cutoff30 = today.AddDays(-30);

        // ⭐ مرحلة 3: عدّ وجمع على SQL بدل جلب كل الفواتير المفتوحة للذاكرة
        var debtQuery = ScopeTx(db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Sale
                     && (t.InvoiceStatus == null || t.InvoiceStatus != InvoiceStatuses.Cancelled)
                     && t.GrandTotal > t.PaidAmount
                     && (t.DueDate ?? t.TransactionDate) < cutoff30), db, scope);
        var debts30 = await debtQuery.CountAsync();
        var debts30Amount = await debtQuery.SumAsync(t => (decimal?)(t.GrandTotal - t.PaidAmount)) ?? 0m;

        var lateDeliveries = await ScopeTx(db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Sale
                     && (t.InvoiceStatus == null || t.InvoiceStatus != InvoiceStatuses.Cancelled)
                     && t.IsDelivered != true
                     && t.DueDate != null && t.DueDate < today), db, scope)
            .CountAsync();

        var pendingPrice = await ScopePriceRequests(db.PriceChangeRequests.AsNoTracking()
            .Where(r => r.Status == "Pending"), db, scope)
            .CountAsync();

        var negQuery = db.StockLevels.AsNoTracking().Where(s => s.Quantity < 0);
        if (scope.IsScoped)
        {
            var whIds = scope.WarehouseIds;
            negQuery = negQuery.Where(s => whIds.Contains(s.WarehouseId));
        }
        var negativeStock = await negQuery
            .Select(s => s.ProductId).Distinct().CountAsync();

        var leadsQuery = db.LeadsCRMs.AsNoTracking()
            .Where(l => (l.LeadStatus == "جديد" || l.LeadStatus == "تم الإسناد")
                     && l.CreatedAt < today.AddDays(-10));
        if (scope.IsScoped)
        {
            var empIds = scope.EmployeeIds;
            leadsQuery = leadsQuery.Where(l => l.AssignedEmployeeId != null
                                            && empIds.Contains(l.AssignedEmployeeId.Value));
        }
        var stalledLeads = await leadsQuery.CountAsync();

        // الشكاوى: لا إسناد فرع لها في النظام (لا فرع ولا موظف مسؤول على
        // السجل) — عدّادات على مستوى الشركة دائمًا، بعدّ SQL مباشر بدل
        // جلب كل الحالات للذاكرة (مغلقة = 4/5 حسب ComplaintStatus.IsClosed).
        var openComplaints = await db.Complaints.AsNoTracking()
            .CountAsync(c => c.Status == null
                          || (c.Status != ComplaintStatus.Resolved && c.Status != ComplaintStatus.Rejected));
        var escalated = await db.Complaints.AsNoTracking()
            .CountAsync(c => c.Status == ComplaintStatus.Escalated);

        return new ExceptionsResult(new ExecutiveExceptionsDto
        {
            Debts30 = debts30,
            Debts30Amount = debts30Amount,
            LateDeliveries = lateDeliveries,
            PendingPrice = pendingPrice,
            NegativeStock = negativeStock,
            StalledLeads = stalledLeads,
            OpenComplaints = openComplaints
        }, escalated);
    }

    // ─── بنود بانتظار قرار (أ8) ──────────────────────────────
    private async Task<PendingResult> PendingAsync(CockpitBranchScope scope)
    {
        await using var db = await _factory.CreateDbContextAsync();

        // طلبات تعديل فواتير معلقة (Transactions.EditStatus == Pending)
        var invoiceEdits = await ScopeTx(db.Transactions.AsNoTracking()
            .Where(t => t.EditStatus == InvoiceEditStatuses.Pending), db, scope)
            .CountAsync();

        // طلبات اعتماد إغلاق فرص معلقة — بفلتر الفرع: عبر فرع طالبها
        var closureQuery = db.OpportunityClosureApprovalRequests.AsNoTracking()
            .Where(r => r.Status == OpportunityClosureApprovalStatuses.Pending);
        if (scope.IsScoped)
        {
            var empIds = scope.EmployeeIds;
            closureQuery = closureQuery.Where(r => db.Users.Any(u => u.Username == r.RequestedBy
                                                                  && u.EmployeeId != null
                                                                  && empIds.Contains(u.EmployeeId.Value)));
        }
        var closureApprovals = await closureQuery.CountAsync();

        var priceRequests = await ScopePriceRequests(db.PriceChangeRequests.AsNoTracking()
            .Where(r => r.Status == "Pending"), db, scope)
            .CountAsync();

        // أذونات التسليم: لا كيان اعتماد مستقل — الطلب يُسجَّل في AuditLogs
        // (DeliveryPermissionRequest) والمعلق هو ما كان آخر حركة له طلبًا
        // بلا منح لاحق (نفس منطق SalesDeliveryStatusService/ZakiApprovalsService).
        // ⭐ مرحلة 3: «آخر إجراء لكل فاتورة» يُحسب على SQL (Max AuditId لكل
        //    مفتاح) بدل جلب سجل الأذونات كاملًا عبر كل التاريخ للذاكرة.
        var lastAuditIds = await db.AuditLogs.AsNoTracking()
            .Where(a => a.TableName == "Transactions" && a.PrimaryKeyValue != null
                     && (a.ActionType == "DeliveryPermissionRequest" || a.ActionType == "DeliveryPermissionGranted"))
            .GroupBy(a => a.PrimaryKeyValue)
            .Select(g => g.Max(a => a.AuditId))
            .ToListAsync();

        var pendingPermitKeys = lastAuditIds.Count == 0
            ? new List<string>()
            : await db.AuditLogs.AsNoTracking()
                .Where(a => lastAuditIds.Contains(a.AuditId)
                         && a.ActionType == "DeliveryPermissionRequest")
                .Select(a => a.PrimaryKeyValue!)
                .ToListAsync();

        int deliveryPermits;
        var permitTxIds = pendingPermitKeys
            .Select(k => int.TryParse(k, out var id) ? id : -1)
            .Where(id => id > 0).Distinct().ToList();
        if (!scope.IsScoped || permitTxIds.Count == 0)
        {
            // بلا فرع (أو مفاتيح غير رقمية): العدّ بالمفاتيح المميزة كما كان
            deliveryPermits = pendingPermitKeys.Distinct().Count();
        }
        else
        {
            deliveryPermits = await ScopeTx(db.Transactions.AsNoTracking()
                .Where(t => permitTxIds.Contains(t.TransactionId)), db, scope)
                .CountAsync();
        }

        return new PendingResult(invoiceEdits, closureApprovals, deliveryPermits, priceRequests);
    }

    // ─── التشغيل: مصنع / Leads جديدة ─────────────────────────
    private async Task<ExecutiveOpsDto> OpsAsync(CockpitBranchScope scope)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);

        var scopedTx = ScopeTx(db.Transactions.AsNoTracking(), db, scope);
        var factoryOpen = await scopedTx
            .Join(db.Parties.AsNoTracking(), t => t.PartyId, p => p.PartyId, (t, p) => new { t, p })
            .CountAsync(x => x.p.PartyName == "المصنع"
                          && x.t.TransactionType == TransactionTypes.Purchase
                          && (x.t.InvoiceStatus == null || x.t.InvoiceStatus != InvoiceStatuses.Cancelled)
                          && x.t.IsDelivered != true);

        var factoryMonth = await scopedTx
            .Join(db.Parties.AsNoTracking(), t => t.PartyId, p => p.PartyId, (t, p) => new { t, p })
            .CountAsync(x => x.p.PartyName == "المصنع"
                          && x.t.TransactionType == TransactionTypes.Purchase
                          && (x.t.InvoiceStatus == null || x.t.InvoiceStatus != InvoiceStatuses.Cancelled)
                          && x.t.DeliveredAt != null && x.t.DeliveredAt >= monthStart);

        var leadsQuery = db.LeadsCRMs.AsNoTracking().Where(l => l.CreatedAt >= monthStart);
        if (scope.IsScoped)
        {
            var empIds = scope.EmployeeIds;
            leadsQuery = leadsQuery.Where(l => l.AssignedEmployeeId != null
                                            && empIds.Contains(l.AssignedEmployeeId.Value));
        }
        var newLeads = await leadsQuery.CountAsync();

        // الشكاوى بلا إسناد فرع — عدّ شركة كاملة على SQL (انظر الاستثناءات)
        var openComplaints = await db.Complaints.AsNoTracking()
            .CountAsync(c => c.Status == null
                          || (c.Status != ComplaintStatus.Resolved && c.Status != ComplaintStatus.Rejected));

        return new ExecutiveOpsDto
        {
            FactoryOpen = factoryOpen,
            FactoryMonth = factoryMonth,
            OpenComplaints = openComplaints,
            NewLeads = newLeads
        };
    }

    // ─── رصيد الخزائن · الفرص · B2B · مبيعات الفروع ───────────
    private async Task<PipelineResult> PipelineAsync(Ranges ranges, CockpitBranchScope scope)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var today = DateTime.Today;

        // رصيد الخزن عبر كل التاريخ — بأنواع القبض/الصرف العربية والإنجليزية
        // (نفس قبول CashFlowService؛ الحركات الإنجليزية كانت تُفوَّت قبلًا).
        // بفلتر الفرع: خزن الفرع وحركاتها فقط.
        var boxesQuery = db.CashBoxes.AsNoTracking();
        var movementsQuery = db.CashboxTransactions.AsNoTracking();
        if (scope.IsScoped)
        {
            var boxIds = scope.CashBoxIds;
            boxesQuery = boxesQuery.Where(c => boxIds.Contains(c.CashBoxId));
            movementsQuery = movementsQuery.Where(t => boxIds.Contains(t.CashBoxId));
        }
        var cashOpening = await boxesQuery.SumAsync(c => (decimal?)c.OpeningBalance) ?? 0m;
        var cashIn = await movementsQuery
            .Where(t => t.TransactionType == "قبض" || t.TransactionType == "In")
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;
        var cashOut = await movementsQuery
            .Where(t => t.TransactionType == "صرف" || t.TransactionType == "Out")
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;
        var cashBalance = cashOpening + cashIn - cashOut;

        // ⭐ مرحلة 3: الفرص المفتوحة بتجميع واحد على SQL (عدد/قيمة/متعثرة)
        //    بدل جلب كل الفرص المفتوحة للذاكرة — وبفلتر الفرع عبر موظف الفرصة.
        var oppQuery = db.SalesOpportunities.AsNoTracking().Where(o => o.ClosedAt == null);
        if (scope.IsScoped)
        {
            var empIds = scope.EmployeeIds;
            oppQuery = oppQuery.Where(o => o.EmployeeId != null && empIds.Contains(o.EmployeeId.Value));
        }
        var oppAgg = await oppQuery
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Value = g.Sum(o => o.ExpectedValue ?? 0m),
                Stalled = g.Count(o => o.NextFollowUpDate != null && o.NextFollowUpDate < today)
            })
            .FirstOrDefaultAsync();

        // طلبات B2B: بلا إسناد فرع — إجمالي الشركة دائمًا (BranchIgnoredSections)
        var b2bPending = await db.B2BRequests.AsNoTracking()
            .CountAsync(r => r.Status == B2BRequestStatuses.New || r.Status == B2BRequestStatuses.UnderReview);

        var fromD = ranges.From.Date;
        var toEnd = ranges.ToEff.Date.AddDays(1).AddTicks(-1);
        var branchSalesQuery = db.Transactions.AsNoTracking()
            .Join(db.Warehouses.AsNoTracking(), t => t.WarehouseId, w => w.WarehouseId, (t, w) => new { t, w })
            .Join(db.Branches.AsNoTracking(), x => x.w.BranchId, b => (int?)b.BranchId, (x, b) => new { x.t, b })
            .Where(x => x.t.TransactionType == TransactionTypes.Sale
                     && (x.t.InvoiceStatus == null || x.t.InvoiceStatus != InvoiceStatuses.Cancelled)
                     && x.t.TransactionDate >= fromD && x.t.TransactionDate <= toEnd);
        if (scope.IsScoped)
        {
            var empIds = scope.EmployeeIds;
            var whIds = scope.WarehouseIds;
            branchSalesQuery = branchSalesQuery.Where(x =>
                (x.t.EmpId != null && empIds.Contains(x.t.EmpId.Value))
                || ((x.t.EmpId == null || !db.Employees.Any(e => e.EmployeeId == x.t.EmpId.Value && e.BranchId != null))
                    && whIds.Contains(x.t.WarehouseId)));
        }
        var branchSales = await branchSalesQuery
            .GroupBy(x => x.b.BranchNameAr)
            .Select(g => new ExecutiveBranchSaleDto { Name = g.Key, Value = g.Sum(x => x.t.GrandTotal) })
            .ToListAsync();
        branchSales = branchSales.OrderByDescending(b => b.Value).ToList();

        return new PipelineResult(
            cashBalance,
            oppAgg?.Count ?? 0,
            oppAgg?.Value ?? 0m,
            oppAgg?.Stalled ?? 0,
            b2bPending,
            branchSales);
    }

    // ─── أعمار الذمم والمستحقات (إضافة 1) ────────────────────
    private sealed record AgingRow(int PartyId, decimal Remaining, DateTime EffDate);

    private static readonly string[] AgingLabels = { "حالي", "1–30 يوم", "31–60 يوم", "61–90 يوم", "+90 يوم" };

    private static List<ExecutiveAgingBucketDto> Bucketize(List<AgingRow> rows, DateTime today)
    {
        var buckets = AgingLabels.Select(l => new ExecutiveAgingBucketDto { Label = l }).ToList();
        foreach (var r in rows)
        {
            var age = (today - r.EffDate.Date).Days;
            var idx = age <= 0 ? 0 : age <= 30 ? 1 : age <= 60 ? 2 : age <= 90 ? 3 : 4;
            buckets[idx].Amount += r.Remaining;
            buckets[idx].Count++;
        }
        return buckets;
    }

    private async Task<ExecutiveAgingDto> AgingAsync(CockpitBranchScope scope)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var today = DateTime.Today;

        var recv = await ScopeTx(db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Sale
                     && (t.InvoiceStatus == null || t.InvoiceStatus != InvoiceStatuses.Cancelled)
                     && t.GrandTotal > t.PaidAmount), db, scope)
            .Select(t => new AgingRow(t.PartyId, t.GrandTotal - t.PaidAmount, t.DueDate ?? t.TransactionDate))
            .ToListAsync();

        // مستحقات الموردين: فواتير شراء حقيقية فقط (المرآة ليست التزامًا لمورد)
        var pay = await ScopeTx(db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Purchase
                     && (t.InvoiceStatus == null || t.InvoiceStatus != InvoiceStatuses.Cancelled)
                     && (t.ReferenceType == null || !t.ReferenceType.StartsWith(MirrorPrefix))
                     && t.GrandTotal > t.PaidAmount), db, scope)
            .Select(t => new AgingRow(t.PartyId, t.GrandTotal - t.PaidAmount, t.DueDate ?? t.TransactionDate))
            .ToListAsync();

        var dto = new ExecutiveAgingDto
        {
            Receivables = Bucketize(recv, today),
            Payables = Bucketize(pay, today),
            TotalReceivables = recv.Sum(r => r.Remaining),
            TotalPayables = pay.Sum(r => r.Remaining)
        };

        var top = recv.GroupBy(r => r.PartyId)
            .Select(g => new { PartyId = g.Key, Amount = g.Sum(x => x.Remaining) })
            .OrderByDescending(x => x.Amount)
            .Take(3)
            .ToList();
        if (top.Count > 0)
        {
            var topIds = top.Select(t => t.PartyId).ToList();
            var names = await db.Parties.AsNoTracking()
                .Where(p => topIds.Contains(p.PartyId))
                .ToDictionaryAsync(p => p.PartyId, p => p.PartyName);
            dto.TopDebtors = top.Select(t => new ExecutiveTopDebtorDto
            {
                Name = names.GetValueOrDefault(t.PartyId, "—"),
                Amount = t.Amount
            }).ToList();
        }

        return dto;
    }

    // ════════════════════════════════════════════════════════
    //  ⭐ مرحلة 2 — بلوكات التغطية
    // ════════════════════════════════════════════════════════

    // ─── عروض الأسعار (إضافة 3) ──────────────────────────────
    private async Task<ExecutiveQuotationsDto> QuotationsAsync(Ranges ranges, CockpitBranchScope scope)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var today = DateTime.Today;
        var fromD = ranges.From.Date;
        var toEnd = ranges.ToEff.Date.AddDays(1).AddTicks(-1);

        // عروض الفترة (بتاريخ العرض) بحالتها الحالية
        var periodRows = await ScopeQuotations(db.Quotations.AsNoTracking()
            .Where(q => q.QuotationDate >= fromD && q.QuotationDate <= toEnd), db, scope)
            .Select(q => new { q.Status, q.InvoiceId, Value = q.GrandTotal ?? q.TotalAmount })
            .ToListAsync();

        var dto = new ExecutiveQuotationsDto
        {
            PeriodCount = periodRows.Count,
            PeriodValue = periodRows.Sum(x => x.Value)
        };

        foreach (var (status, label) in QuotationStatuses.All)
        {
            var rows = periodRows.Where(x => x.Status == status).ToList();
            dto.ByStatus.Add(new ExecutiveQuotationStatusDto
            {
                Label = label,
                Count = rows.Count,
                Value = rows.Sum(x => x.Value)
            });
        }

        // التحويل: مُحوَّلة = الحالة Converted أو مرتبطة بفاتورة (Batch 2 يضبطهما معًا)
        var converted = periodRows.Count(x => x.Status == QuotationStatuses.Converted || x.InvoiceId != null);
        var rejected = periodRows.Count(x => x.Status == QuotationStatuses.Rejected);
        var expired = periodRows.Count(x => x.Status == QuotationStatuses.Expired);
        var decided = converted + rejected + expired;
        dto.ConvertedCount = converted;
        dto.HasDecided = decided > 0;
        dto.ConversionRate = decided == 0 ? 0 : Math.Round(converted * 100m / decided, 1);

        // لقطات حالية: بانتظار الرد / مقبولة لم تُحوَّل / شارفت على الانتهاء
        var sentRows = await ScopeQuotations(db.Quotations.AsNoTracking()
            .Where(q => q.Status == QuotationStatuses.Sent), db, scope)
            .Select(q => new { q.ValidUntil, Value = q.GrandTotal ?? q.TotalAmount })
            .ToListAsync();
        dto.SentCount = sentRows.Count;
        dto.SentValue = sentRows.Sum(x => x.Value);
        dto.ExpiringSoonCount = sentRows.Count(x => x.ValidUntil != null
                                                && x.ValidUntil.Value.Date >= today
                                                && x.ValidUntil.Value.Date <= today.AddDays(7));

        var acceptedRows = await ScopeQuotations(db.Quotations.AsNoTracking()
            .Where(q => q.Status == QuotationStatuses.Accepted && q.InvoiceId == null), db, scope)
            .Select(q => new { Value = q.GrandTotal ?? q.TotalAmount })
            .ToListAsync();
        dto.AcceptedNotConvertedCount = acceptedRows.Count;
        dto.AcceptedNotConvertedValue = acceptedRows.Sum(x => x.Value);

        return dto;
    }

    // ─── المخزون (إضافة 4) ───────────────────────────────────
    private async Task<ExecutiveInventoryDto> InventoryAsync(int? branchId, CockpitBranchScope scope)
    {
        // القيمة من نفس مسار شاشة «قيمة المخزون» وزكي (كمية × تكلفة الشراء) —
        // بفلتر الفرع تمرَّر للخدمة نفسها (باراميترها الأول BranchId)
        var (rows, summary) = await _stock.GetStockValueAsync(branchId, null, null);

        var dto = new ExecutiveInventoryDto
        {
            TotalCostValue = summary.TotalCostValue,
            TotalSaleValue = summary.TotalSaleValue,
            ProductsCount = summary.TotalProducts,
            TotalQuantity = summary.TotalQuantity
        };

        // تجميع لكل صنف عبر المخازن (نفس صفوف القيمة)
        var perProduct = rows.GroupBy(r => r.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Name = g.First().ProductName,
                Qty = g.Sum(x => x.Quantity),
                Cost = g.Sum(x => x.CostValue)
            })
            .ToList();

        dto.TopValueProducts = perProduct
            .OrderByDescending(x => x.Cost)
            .Take(5)
            .Select(x => new ExecutiveNameValueDto { Name = x.Name, Value = x.Cost })
            .ToList();

        // تفصيل لكل مخزن: تجميع لنفس صفوف القيمة أعلاه (كل صف = صنف × مخزن)
        // — مجموع CostValue للمخازن يساوي إجمالي الكارت بالبناء، وبلا أي
        // استعلام إضافي. محتوًى بفشل مستقل: أي خطأ هنا يترك الإجماليات سليمة.
        try
        {
            var totalCost = dto.TotalCostValue;
            dto.Warehouses = rows
                .GroupBy(r => new { r.WarehouseId, r.WarehouseName })
                .Select(g => new ExecutiveWarehouseValueDto
                {
                    WarehouseId = g.Key.WarehouseId,
                    WarehouseName = g.Key.WarehouseName,
                    CostValue = g.Sum(x => x.CostValue),
                    ProductsCount = g.Select(x => x.ProductId).Distinct().Count(),
                    SharePct = totalCost == 0 ? 0m
                        : Math.Round(g.Sum(x => x.CostValue) * 100m / totalCost, 1)
                })
                .OrderByDescending(w => w.CostValue)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ExecutiveCockpit: per-warehouse inventory breakdown failed");
            dto.Warehouses = new();
        }

        await using var db = await _factory.CreateDbContextAsync();

        // نافد/منخفض: من أرصدة StockLevels مجمعة لكل صنف (تعريف زكي:
        // لا حد إعادة طلب مخزّن — نافد ≤ 0 ومنخفض 1..5)
        var levelsQuery = db.StockLevels.AsNoTracking();
        if (scope.IsScoped)
        {
            var whIds = scope.WarehouseIds;
            levelsQuery = levelsQuery.Where(s => whIds.Contains(s.WarehouseId));
        }
        var totals = await levelsQuery
            .GroupBy(s => s.ProductId)
            .Select(g => new { ProductId = g.Key, Total = g.Sum(x => x.Quantity) })
            .ToListAsync();
        dto.OutOfStockCount = totals.Count(x => x.Total <= 0);
        dto.LowStockCount = totals.Count(x => x.Total >= 1 && x.Total <= 5);

        // راكد: رصيد موجب وآخر حركة مخزون قبل 90 يومًا (أو بلا حركة أصلًا) —
        // بفلتر الفرع: آخر حركة داخل مخازن الفرع
        var cutoff = DateTime.Today.AddDays(-90);
        var movesQuery = db.StockTransactions.AsNoTracking();
        if (scope.IsScoped)
        {
            var whIds = scope.WarehouseIds;
            movesQuery = movesQuery.Where(t => whIds.Contains(t.WarehouseId));
        }
        var lastMoves = await movesQuery
            .GroupBy(t => t.ProductId)
            .Select(g => new { ProductId = g.Key, Last = g.Max(x => x.TransactionDate ?? x.CreatedAt) })
            .ToListAsync();
        var lastByProduct = lastMoves.ToDictionary(x => x.ProductId, x => x.Last);
        var dead = perProduct
            .Where(p => p.Qty > 0
                     && (!lastByProduct.TryGetValue(p.ProductId, out var last)
                         || last == null || last.Value.Date < cutoff))
            .ToList();
        dto.DeadStockCount = dead.Count;
        dto.DeadStockValue = dead.Sum(x => x.Cost);

        return dto;
    }

    // ─── أوامر التشغيل والإنتاج (إضافة 5) ────────────────────
    private async Task<ExecutiveJobOrdersDto> JobOrdersAsync(Ranges ranges, CockpitBranchScope scope)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var today = DateTime.Today;
        var fromD = ranges.From.Date;
        var toEnd = ranges.ToEff.Date.AddDays(1).AddTicks(-1);

        // نفس قاعدة الشاشة وزكي: بيع + شراء حقيقي (بلا مرآة)، غير ملغاة
        var baseQuery = ScopeTx(db.Transactions.AsNoTracking()
            .Where(t => (t.InvoiceStatus == null || t.InvoiceStatus != InvoiceStatuses.Cancelled)
                     && (t.TransactionType == TransactionTypes.Sale
                         || (t.TransactionType == TransactionTypes.Purchase
                             && (t.ReferenceType == null || !t.ReferenceType.StartsWith(MirrorPrefix))))), db, scope);

        var open = baseQuery.Where(t => t.IsDelivered != true);

        var dto = new ExecutiveJobOrdersDto
        {
            OpenCount = await open.CountAsync(),
            OpenSale = await open.CountAsync(t => t.TransactionType == TransactionTypes.Sale),
            OpenPurchase = await open.CountAsync(t => t.TransactionType == TransactionTypes.Purchase)
        };

        var overdueRows = await open
            .Where(t => t.DueDate != null && t.DueDate < today)
            .Select(t => new { t.GrandTotal })
            .ToListAsync();
        dto.OverdueCount = overdueRows.Count;
        dto.OverdueValue = overdueRows.Sum(x => x.GrandTotal);

        dto.WithAlternativesCount = await open
            .CountAsync(t => db.TransactionDetails.Any(d => d.TransactionId == t.TransactionId
                                                         && d.SelectedAlternativeId != null));

        dto.DeliveredInPeriod = await baseQuery
            .CountAsync(t => t.IsDelivered == true
                          && t.DeliveredAt != null && t.DeliveredAt >= fromD && t.DeliveredAt <= toEnd);

        return dto;
    }

    /// <summary>خطأ نقص تخزين التسليم الجزئي (جداول الأحداث 208 / أعمدة الجدولة 207) قبل تشغيل سكربتاته.</summary>
    private static bool IsMissingDeliveryStorage(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
            if (e is Microsoft.Data.SqlClient.SqlException sql && (sql.Number == 208 || sql.Number == 207))
                return true;
        return false;
    }

    // ─── التسليمات التشغيلي (إضافة 6) ────────────────────────
    // ⭐ التكافؤ مع شاشة متابعة التسليمات: الاكتمال والحالة الفعّالة من أحداث
    // التسليم (نفس اشتقاق خدمة الشاشة عبر GetDeliveryProgressBatchAsync) لا من
    // نص الـView وحده. الالتزام الرئيسي = التزام الإكمال (تاريخ حدث الإكمال ≤
    // الاستحقاق) بنفس صيغة ما قبل الأحداث حتى تبقى الأرقام قابلة للمقارنة،
    // والثانوي = دقة الدفعات (أحداث الفترة في/قبل مواعيد بنودها المجدولة).
    // جمهور اللوحة مستثنى من الإخفاء أصلًا فكل التواريخ هنا حقيقية = المعروضة له.
    /// <summary>
    /// التكليفات العامة: لقطة حالية للمفتوح/المتأخر + منجزات الفترة
    /// (مكتمل بتاريخ الإتمام، ملغي بتاريخ الإلغاء) + أكثر 3 موظفين تأخرًا.
    /// بلا بُعد فرع — إجمالي الشركة.
    /// </summary>
    private async Task<ExecutiveGeneralTasksDto> GeneralTasksAsync(Ranges ranges)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var today = DateTime.Today;
        var fromD = ranges.From.Date;
        var toEnd = ranges.ToEff.Date.AddDays(1).AddTicks(-1);

        var general = db.CrmTasks.AsNoTracking()
            .Where(t => t.IsActive && t.TaskScope == "General");

        var open = general.Where(t => t.Status == "Pending" || t.Status == "In Progress");

        var dto = new ExecutiveGeneralTasksDto
        {
            OpenTotal = await open.CountAsync(),
            OverdueTotal = await open.CountAsync(t => t.DueDate < today),
            InProgressTotal = await general.CountAsync(t => t.Status == "In Progress"),
            ReturnedPendingTotal = await general.CountAsync(t => t.Status == "Pending" && t.ReturnedAt != null),
            CompletedInPeriod = await general.CountAsync(t =>
                t.Status == "Completed" && t.CompletedDate != null && t.CompletedDate >= fromD && t.CompletedDate <= toEnd),
            CancelledInPeriod = await general.CountAsync(t =>
                t.Status == "Cancelled" && t.CancelledAt != null && t.CancelledAt >= fromD && t.CancelledAt <= toEnd)
        };

        var top = await open
            .Where(t => t.DueDate < today)
            .GroupBy(t => t.AssignedTo)
            .Select(g => new { EmployeeId = g.Key, Overdue = g.Count() })
            .OrderByDescending(x => x.Overdue)
            .Take(3)
            .ToListAsync();

        if (top.Count > 0)
        {
            var ids = top.Select(x => x.EmployeeId).ToList();
            var names = await db.Employees.AsNoTracking()
                .Where(e => ids.Contains(e.EmployeeId))
                .Select(e => new { e.EmployeeId, e.FullName })
                .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName);
            var openByEmp = await open
                .Where(t => ids.Contains(t.AssignedTo))
                .GroupBy(t => t.AssignedTo)
                .Select(g => new { EmployeeId = g.Key, Open = g.Count() })
                .ToDictionaryAsync(x => x.EmployeeId, x => x.Open);

            dto.TopOverdueEmployees = top.Select(x => new ExecutiveTaskLoadRowDto
            {
                EmployeeName = names.TryGetValue(x.EmployeeId, out var n) ? n : $"موظف #{x.EmployeeId}",
                Overdue = x.Overdue,
                Open = openByEmp.TryGetValue(x.EmployeeId, out var o) ? o : 0
            }).ToList();
        }

        return dto;
    }

    private async Task<ExecutiveDeliveriesDto> DeliveriesAsync(Ranges ranges, CockpitBranchScope scope)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var today = DateTime.Today;
        var fromD = ranges.From.Date;
        var toEnd = ranges.ToEff.Date.AddDays(1).AddTicks(-1);

        const string delivered = "تم التسليم";
        const string returned = "مرتجع";
        const decimal qtyEpsilon = 0.0005m;

        // ⚠️ بفلتر الفرع: الـ View بلا عمود مخزن — الإسناد ببائع الفاتورة
        //    (EmpId) فقط، فتُستبعد فواتير بلا بائع عند اختيار فرع.
        var viewQuery = db.VwSalesDeliveryStatuses.AsNoTracking();
        if (scope.IsScoped)
        {
            var empIds = scope.EmployeeIds;
            viewQuery = viewQuery.Where(x => x.EmpId != null && empIds.Contains(x.EmpId.Value));
        }

        // مخطط الفترة: الموعد (DueDate الحقيقي) داخل الفترة — من نفس View الشاشة
        var planned = await viewQuery
            .Where(x => x.DueDate != null && x.DueDate >= fromD && x.DueDate <= toEnd)
            .Select(x => new { x.TransactionId, x.DueDate, x.DeliveryStatus, x.DeliveredAt })
            .ToListAsync();

        // لقطة حالية: مفتوحة (غير مسلَّمة/مرتجعة في الـView) بموعد — تُدقَّق بالأحداث أدناه
        var openRows = await viewQuery
            .Where(x => x.DueDate != null
                     && x.DeliveryStatus != delivered && x.DeliveryStatus != returned)
            .Select(x => new { x.TransactionId, x.DueDate, x.GrandTotal })
            .ToListAsync();

        // ═══ طبقة الأحداث (تدهور آمن: غياب التخزين يعيد أرقام الـView كما كانت) ═══
        var progress = new Dictionary<int, DeliveryProgressSummaryDto>();
        var completionDates = new Dictionary<int, DateTime>();
        var eventsOk = false;
        var partialOpenNow = 0;
        var batchCountable = 0;
        var batchOnTime = 0;
        // نتائج التسليم (القسم 11) — تُصفَّر كلها إن غاب تخزين النتائج
        var ocEventsRemarks = 0;
        decimal ocManufQty = 0, ocHandlingQty = 0;
        var ocBacklogRecords = 0;
        decimal ocBacklogQty = 0;
        int? ocOldestAge = null;
        var ocPendingCharges = 0;
        decimal ocApprovedUncollected = 0, ocRepairCost = 0;
        try
        {
            var ids = planned.Select(p => p.TransactionId)
                .Concat(openRows.Select(o => o.TransactionId)).Distinct().ToList();
            if (ids.Count > 0)
                progress = await _deliveryStatus.GetDeliveryProgressBatchAsync(ids);

            // تاريخ الإكمال: حدث الإكمال المُعلَّم CompletedInvoice (الأخير زمنيًا)،
            // وإلا آخر حدث غير ملغى — نفس قاعدة كاتب إسقاط IsDelivered/DeliveredAt
            var completedIds = progress
                .Where(kv => kv.Value.EffectiveStatus == DeliveryEffectiveStatuses.Delivered)
                .Select(kv => kv.Key).ToList();
            if (completedIds.Count > 0)
            {
                var evs = await db.InvoiceDeliveries.AsNoTracking()
                    .Where(d => completedIds.Contains(d.TransactionId) && !d.IsVoided)
                    .Select(d => new { d.TransactionId, d.DeliveryDate, d.DeliveryId, d.CompletedInvoice })
                    .ToListAsync();
                foreach (var g in evs.GroupBy(e => e.TransactionId))
                {
                    var completing = g.Where(e => e.CompletedInvoice)
                                      .OrderBy(e => e.DeliveryDate).ThenBy(e => e.DeliveryId)
                                      .LastOrDefault()
                                  ?? g.OrderBy(e => e.DeliveryDate).ThenBy(e => e.DeliveryId).Last();
                    completionDates[g.Key] = completing.DeliveryDate;
                }
            }

            // كل فواتير النطاق (للقطة «جزئي مفتوح الآن» ودقة الدفعات بنفس إسناد الفرع)
            var scopedIds = (await viewQuery.Select(x => x.TransactionId).ToListAsync()).ToHashSet();
            var returnedIds = (await viewQuery
                .Where(x => x.DeliveryStatus == returned)
                .Select(x => x.TransactionId).ToListAsync()).ToHashSet();

            // جزئي مفتوح الآن: لها أحداث ومتبقٍّ > 0 وغير مرتجعة — أيًا كان موعدها
            var withEvents = await db.InvoiceDeliveries.AsNoTracking()
                .Where(d => !d.IsVoided)
                .Select(d => d.TransactionId).Distinct().ToListAsync();
            var partialCandidates = withEvents.Where(scopedIds.Contains).ToList();
            if (partialCandidates.Count > 0)
            {
                var snap = await _deliveryStatus.GetDeliveryProgressBatchAsync(partialCandidates);
                partialOpenNow = snap.Count(kv =>
                    kv.Value.EffectiveStatus == DeliveryEffectiveStatuses.Partial
                    && !returnedIds.Contains(kv.Key));
            }

            // دقة الدفعات: أحداث الفترة (ضمن النطاق) — الحدث منضبط إن خرج كل بند
            // فيه في/قبل موعده المجدول الفعّال (تاريخه الخاص أو استحقاق فاتورته)
            var periodEvents = await db.InvoiceDeliveries.AsNoTracking()
                .Where(d => !d.IsVoided && d.DeliveryDate >= fromD && d.DeliveryDate <= toEnd
                         && scopedIds.Contains(d.TransactionId))
                .Select(d => new { d.DeliveryId, d.TransactionId, d.DeliveryDate })
                .ToListAsync();
            if (periodEvents.Count > 0)
            {
                var evIds = periodEvents.Select(e => e.DeliveryId).ToList();
                var evItems = await db.InvoiceDeliveryItems.AsNoTracking()
                    .Where(i => evIds.Contains(i.DeliveryId))
                    .Select(i => new { i.DeliveryId, i.TransactionDetailId })
                    .ToListAsync();
                var detailIds = evItems.Select(i => i.TransactionDetailId).Distinct().ToList();
                var lines = detailIds.Count == 0
                    ? new List<TransactionDetail>()
                    : await db.TransactionDetails.AsNoTracking()
                        .Where(d => detailIds.Contains(d.DetailId))
                        .ToListAsync();
                var lineById = lines.ToDictionary(l => l.DetailId);
                var txIds = periodEvents.Select(e => e.TransactionId).Distinct().ToList();
                var dueByTx = (await db.Transactions.AsNoTracking()
                        .Where(t => txIds.Contains(t.TransactionId))
                        .Select(t => new { t.TransactionId, t.DueDate })
                        .ToListAsync())
                    .ToDictionary(t => t.TransactionId, t => t.DueDate);
                var itemsByEvent = evItems.GroupBy(i => i.DeliveryId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var e in periodEvents)
                {
                    if (!itemsByEvent.TryGetValue(e.DeliveryId, out var its) || its.Count == 0)
                        continue;
                    var ok = true;
                    var countable = true;
                    foreach (var it in its)
                    {
                        // بند لم يعد موجودًا أو بلا موعد فعّال: الحدث لا يُحتسب أصلًا
                        if (!lineById.TryGetValue(it.TransactionDetailId, out var line)) { countable = false; break; }
                        var eff = DeliveryScheduleResolver.EffectiveDate(
                            line.DeliveryDate, line.DeliveryDateIsCustom,
                            dueByTx.TryGetValue(e.TransactionId, out var dd) ? dd : null);
                        if (eff == null) { countable = false; break; }
                        if (e.DeliveryDate.Date > eff.Value.Date) { ok = false; break; }
                    }
                    if (!countable) continue;
                    batchCountable++;
                    if (ok) batchOnTime++;
                }
            }

            // ═══ نتائج التسليم (القسم 11) — كتلة فرعية آمنة مستقلة:
            //     غياب تخزين النتائج يُبقي أرقامها صفرًا ولا يُسقط أرقام الأحداث ═══
            try
            {
                if (periodEvents.Count > 0)
                {
                    var ocEvIds = periodEvents.Select(e => e.DeliveryId).ToList();
                    var ocItems = await db.InvoiceDeliveryItems.AsNoTracking()
                        .Where(i => ocEvIds.Contains(i.DeliveryId))
                        .Select(i => new
                        {
                            i.DeliveryId,
                            i.ReturnedManufacturingQty,
                            i.ReturnedHandlingQty,
                            i.ReturnedModificationQty
                        })
                        .ToListAsync();
                    ocEventsRemarks = ocItems
                        .Where(i => i.ReturnedManufacturingQty + i.ReturnedHandlingQty
                                  + i.ReturnedModificationQty > qtyEpsilon)
                        .Select(i => i.DeliveryId).Distinct().Count();
                    ocManufQty = ocItems.Sum(i => i.ReturnedManufacturingQty);
                    ocHandlingQty = ocItems.Sum(i => i.ReturnedHandlingQty);
                }

                var inLoop = DeliveryMaintenanceStatuses.InLoop;
                var backlog = await db.DeliveryMaintenanceItems.AsNoTracking()
                    .Where(m => !m.IsVoided && inLoop.Contains(m.Status)
                             && scopedIds.Contains(m.TransactionId))
                    .Select(m => new { m.Quantity, m.CreatedAt })
                    .ToListAsync();
                ocBacklogRecords = backlog.Count;
                ocBacklogQty = backlog.Sum(m => m.Quantity);
                if (backlog.Count > 0)
                    ocOldestAge = (today - backlog.Min(m => m.CreatedAt).Date).Days;

                ocRepairCost = await db.DeliveryMaintenanceItems.AsNoTracking()
                    .Where(m => !m.IsVoided && m.ClosedAt != null
                             && m.ClosedAt >= fromD && m.ClosedAt <= toEnd
                             && m.RepairCost != null && scopedIds.Contains(m.TransactionId))
                    .SumAsync(m => m.RepairCost ?? 0m);

                // رسوم التعديل تُسنَد للفرع عبر فاتورتها (scopedIds) مثل بقية البلوك
                ocPendingCharges = await db.AdditionalCharges.AsNoTracking()
                    .CountAsync(c => c.ChargeType == ChargeTypes.Modification
                                  && c.SourceDeliveryId != null
                                  && c.Status == ChargeStatuses.PendingApproval
                                  && c.TransactionId != null && scopedIds.Contains(c.TransactionId.Value));
                ocApprovedUncollected = await db.AdditionalCharges.AsNoTracking()
                    .Where(c => c.ChargeType == ChargeTypes.Modification
                             && c.SourceDeliveryId != null
                             && c.Status == ChargeStatuses.Approved
                             && c.TransactionId != null && scopedIds.Contains(c.TransactionId.Value))
                    .SumAsync(c => c.ChargeAmount ?? 0m);
            }
            catch (Exception ex) when (IsMissingDeliveryStorage(ex))
            {
                _logger.LogWarning("Cockpit deliveries: outcomes storage missing — outcomes figures stay zero.");
            }

            eventsOk = true;
        }
        catch (Exception ex) when (IsMissingDeliveryStorage(ex))
        {
            _logger.LogWarning("Cockpit deliveries: partial-delivery storage missing — view-only figures.");
            eventsOk = false;
            progress = new Dictionary<int, DeliveryProgressSummaryDto>();
            completionDates = new Dictionary<int, DateTime>();
            partialOpenNow = 0;
            batchCountable = 0;
            batchOnTime = 0;
        }

        bool HasProgress(int id, out DeliveryProgressSummaryDto p)
        {
            if (eventsOk && progress.TryGetValue(id, out var found)) { p = found; return true; }
            p = null!; return false;
        }
        bool IsCompleted(int id, string viewStatus) =>
            HasProgress(id, out var p)
                ? p.EffectiveStatus == DeliveryEffectiveStatuses.Delivered
                : viewStatus == delivered;
        DateTime? CompletionOf(int id, DateTime? viewDeliveredAt) =>
            completionDates.TryGetValue(id, out var cd) ? cd : viewDeliveredAt;

        var deliveredRows = planned.Where(x => IsCompleted(x.TransactionId, x.DeliveryStatus)).ToList();
        var onTime = deliveredRows.Count(x =>
        {
            var c = CompletionOf(x.TransactionId, x.DeliveredAt);
            return c.HasValue && c.Value.Date <= x.DueDate!.Value.Date;
        });

        var dto = new ExecutiveDeliveriesDto
        {
            Planned = planned.Count,
            Delivered = deliveredRows.Count,
            OnTime = onTime,
            Late = deliveredRows.Count - onTime,
            Pending = planned.Count(x => !IsCompleted(x.TransactionId, x.DeliveryStatus)
                                       && x.DeliveryStatus != returned),
            ReturnedInPeriod = planned.Count(x => x.DeliveryStatus == returned),
            NotDeliveredInPeriod = planned.Count(x =>
                x.DeliveryStatus != returned
                && (HasProgress(x.TransactionId, out var pp)
                        ? !pp.HasEvents
                        : x.DeliveryStatus != delivered)),
            PartialInPeriod = planned.Count(x =>
                HasProgress(x.TransactionId, out var pp)
                && pp.EffectiveStatus == DeliveryEffectiveStatuses.Partial),
            PartialOpenNow = partialOpenNow,
            BatchEventsInPeriod = batchCountable,
            BatchOnTimeEvents = batchOnTime,
            HasBatchEvents = batchCountable > 0,
            BatchPunctualityPct = batchCountable == 0 ? 0
                : Math.Round(batchOnTime * 100m / batchCountable, 1)
        };
        dto.HasDelivered = dto.Delivered > 0;
        dto.OnTimePct = dto.Delivered == 0 ? 0 : Math.Round(onTime * 100m / dto.Delivered, 1);
        dto.EventsWithRemarksInPeriod = ocEventsRemarks;
        dto.ManufacturingDefectQtyInPeriod = ocManufQty;
        dto.HandlingDamageQtyInPeriod = ocHandlingQty;
        dto.MaintenanceBacklogRecords = ocBacklogRecords;
        dto.MaintenanceBacklogQty = ocBacklogQty;
        dto.MaintenanceOldestAgeDays = ocOldestAge;
        dto.PendingModificationChargesCount = ocPendingCharges;
        dto.ApprovedUncollectedModificationAmount = ocApprovedUncollected;
        dto.RepairCostInPeriod = ocRepairCost;

        // لقطة حالية: متأخرة الآن = متبقٍّ > 0 (من الأحداث) وموعدها فات — نفس تعريف الشاشة
        // (صفوف openRows مضمونة من الاستعلام أنها غير مسلَّمة/مرتجعة في الـView،
        //  فالاكتمال يُفحص من الأحداث فقط)
        bool OpenNotCompleted(int id) =>
            !HasProgress(id, out var p)
            || p.EffectiveStatus != DeliveryEffectiveStatuses.Delivered;
        var overdue = openRows.Where(x => x.DueDate!.Value.Date < today
            && OpenNotCompleted(x.TransactionId)
            && (!HasProgress(x.TransactionId, out var op) || op.TotalRemainingQty > qtyEpsilon)).ToList();
        dto.OverdueNow = overdue.Count;
        dto.OverdueAmount = overdue.Sum(x => x.GrandTotal);
        dto.DueToday = openRows.Count(x => x.DueDate!.Value.Date == today
            && OpenNotCompleted(x.TransactionId));
        dto.DueTomorrow = openRows.Count(x => x.DueDate!.Value.Date == today.AddDays(1)
            && OpenNotCompleted(x.TransactionId));

        return dto;
    }

    // ─── التسويق المصغر (إضافة 8) ────────────────────────────
    private async Task<ExecutiveMarketingDto> MarketingAsync(Ranges ranges)
    {
        // نفس خدمة لوحة التسويق ولنفس الفترة — لا حسابات موازية
        var m = await _marketing.GetDashboardAsync(ranges.From.Date, ranges.ToEff.Date, "all");

        var total = m.Channels.FirstOrDefault(c => c.IsTotal);
        var channels = m.Channels.Where(c => !c.IsTotal).ToList();
        var leads = total?.Leads ?? channels.Sum(c => c.Leads);
        var customers = total?.Customers ?? channels.Sum(c => c.Customers);
        var spend = total?.Spend ?? channels.Sum(c => c.Spend);
        var cpl = total?.Cpl ?? (leads > 0 ? spend / leads : 0m);

        var topSource = channels.OrderByDescending(c => c.Leads).FirstOrDefault();
        var topCampaign = m.TopCampaigns.FirstOrDefault();

        return new ExecutiveMarketingDto
        {
            Leads = leads,
            Customers = customers,
            ConversionPct = leads == 0 ? 0 : Math.Round(customers * 100m / leads, 1),
            Spend = spend,
            CostPerLead = cpl,
            TopSourceName = topSource?.Name ?? "",
            TopSourceLeads = topSource?.Leads ?? 0,
            TopCampaignName = topCampaign?.Name ?? "",
            TopCampaignLeads = topCampaign?.Leads ?? 0,
            TopCampaignDeals = topCampaign?.Deals ?? 0
        };
    }

    // ─── بوابة B2B موسّع (إضافة 9) ───────────────────────────
    private async Task<ExecutiveB2bBlockDto> B2bAsync(Ranges ranges)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var fromD = ranges.From.Date;
        var toEnd = ranges.ToEff.Date.AddDays(1).AddTicks(-1);

        var periodRequests = await db.B2BRequests.AsNoTracking()
            .Where(r => r.CreatedAt >= fromD && r.CreatedAt <= toEnd)
            .Select(r => new { r.Status, r.RelatedInvoiceId })
            .ToListAsync();

        var dto = new ExecutiveB2bBlockDto
        {
            RequestsInPeriod = periodRequests.Count,
            ConvertedInPeriod = periodRequests.Count(r => r.Status == B2BRequestStatuses.Converted)
        };

        // قيمة المحوّلة: مجموع فواتير البيع المرتبطة بطلبات الفترة المحوّلة
        var invoiceIds = periodRequests
            .Where(r => r.Status == B2BRequestStatuses.Converted && r.RelatedInvoiceId != null)
            .Select(r => r.RelatedInvoiceId!.Value)
            .Distinct()
            .ToList();
        if (invoiceIds.Count > 0)
        {
            dto.ConvertedValue = await db.Transactions.AsNoTracking()
                .Where(t => invoiceIds.Contains(t.TransactionId))
                .SumAsync(t => (decimal?)t.GrandTotal) ?? 0m;
        }

        dto.PendingNow = await db.B2BRequests.AsNoTracking()
            .CountAsync(r => r.Status == B2BRequestStatuses.New || r.Status == B2BRequestStatuses.UnderReview);

        var users = await db.B2BPortalUsers.AsNoTracking()
            .Select(u => new { u.PartyId, u.IsActive })
            .ToListAsync();
        dto.Companies = users.Select(u => u.PartyId).Distinct().Count();
        dto.ActiveUsers = users.Count(u => u.IsActive);
        dto.InactiveUsers = users.Count(u => !u.IsActive);

        return dto;
    }

    // ─── الموارد البشرية التشغيلي (إضافة 7) ──────────────────
    private async Task<ExecutiveHrDto> HrAsync(Ranges ranges, CockpitBranchScope scope)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var today = DateTime.Today;
        var fromD = ranges.From.Date;
        var toEnd = ranges.ToEff.Date.AddDays(1).AddTicks(-1);

        // بفلتر الفرع: موظفو الفرع فقط (حضورهم عبر أكواد بصمتهم)
        var empHrQuery = db.Employees.AsNoTracking().Where(e => e.EndDate == null);
        if (scope.IsScoped) empHrQuery = empHrQuery.Where(e => e.BranchId == scope.BranchId);
        var employees = await empHrQuery
            .Select(e => new { e.Status, e.BioEmployeeId, e.IsPermanentlyExempt })
            .ToListAsync();
        var active = employees.Where(e => e.Status == EmployeeStatuses.Active).ToList();

        var dto = new ExecutiveHrDto
        {
            ActiveEmployees = active.Count,
            OnLeaveCount = employees.Count(e => e.Status == EmployeeStatuses.OnLeave),
            // المعفون من البصمة نهائيًا (من النشطين): حضورهم يُرصد يدويًا
            // (نمط المرتبات) — يُعرض عددهم ولا يدخلون أساس الحضور أدناه.
            ExemptCount = active.Count(e => e.IsPermanentlyExempt == true)
        };

        // حضور اليوم (نفس عتبة التأخير في قسم الفريق: +15 دقيقة)
        // ⭐ أساس الحضور = نشط + له كود بصمة + غير معفى من البصمة؛ المعفى
        //    لا يُحتسب حاضرًا ولا غائبًا حتى لو وُجد له سجل بصمة اليوم.
        var attTodayAll = await db.Attendances.AsNoTracking()
            .Where(a => a.LogDate == today)
            .Select(a => new { a.BiometricCode, a.LateMinutes })
            .ToListAsync();
        var bioSet = scope.IsScoped
            ? active.Where(e => e.BioEmployeeId.HasValue && e.IsPermanentlyExempt != true).Select(e => e.BioEmployeeId!.Value).ToHashSet()
            : null;
        var exemptCodes = active
            .Where(e => e.BioEmployeeId.HasValue && e.IsPermanentlyExempt == true)
            .Select(e => e.BioEmployeeId!.Value).ToHashSet();
        var attToday = bioSet == null
            ? attTodayAll.Where(a => !exemptCodes.Contains(a.BiometricCode)).ToList()
            : attTodayAll.Where(a => bioSet.Contains(a.BiometricCode)).ToList();
        dto.PresentToday = attToday.Select(a => a.BiometricCode).Distinct().Count();
        dto.LateToday = attToday.GroupBy(a => a.BiometricCode)
            .Count(g => g.Max(x => x.LateMinutes ?? 0) > 15);
        var withBio = active.Count(e => e.BioEmployeeId.HasValue && e.IsPermanentlyExempt != true);
        dto.AbsentToday = Math.Max(withBio - dto.PresentToday, 0);
        dto.AttendancePct = withBio == 0 ? 0 : Math.Round(dto.PresentToday * 100m / withBio, 1);

        // رواتب الشهر الحالي (Payrolls بصيغة شهر "yyyy-MM") — بالفرع عبر موظف الراتب
        var month = today.ToString("yyyy-MM");
        dto.PayrollMonthLabel = month;
        var payrollQuery = db.Payrolls.AsNoTracking().Where(p => p.PayrollMonth == month);
        if (scope.IsScoped) payrollQuery = payrollQuery.Where(p => p.Employee.BranchId == scope.BranchId);
        dto.PayrollMonthCost = await payrollQuery
            .SumAsync(p => (decimal?)(p.NetSalary ?? (p.BasicSalary + (p.Allowances ?? 0) - (p.Deductions ?? 0)))) ?? 0m;

        // السلف: نشطة الآن (عدد ومتبقٍّ) + جديدة في الفترة — بالفرع عبر الموظف
        var activeLoansQuery = db.EmployeeLoans.AsNoTracking().Where(l => l.Status == "Active");
        if (scope.IsScoped) activeLoansQuery = activeLoansQuery.Where(l => l.Employee.BranchId == scope.BranchId);
        var activeLoans = await activeLoansQuery
            .Select(l => new { l.RemainingAmount })
            .ToListAsync();
        dto.ActiveLoansCount = activeLoans.Count;
        dto.ActiveLoansRemaining = activeLoans.Sum(l => l.RemainingAmount);

        var newLoansQuery = db.EmployeeLoans.AsNoTracking()
            .Where(l => l.Status != "Cancelled" && l.LoanDate >= fromD && l.LoanDate <= toEnd);
        if (scope.IsScoped) newLoansQuery = newLoansQuery.Where(l => l.Employee.BranchId == scope.BranchId);
        var newLoans = await newLoansQuery
            .Select(l => new { l.LoanAmount })
            .ToListAsync();
        dto.NewLoansInPeriod = newLoans.Count;
        dto.NewLoansAmountInPeriod = newLoans.Sum(l => l.LoanAmount);

        return dto;
    }

    // ════════════════════════════════════════════════════════
    public async Task<List<ExecutiveTimelineRowDto>> GetEmployeeTimelineAsync(int employeeId)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var login = await db.Users.AsNoTracking()
            .Where(u => u.EmployeeId == employeeId)
            .Select(u => u.Username)
            .FirstOrDefaultAsync();

        var rows = new List<ExecutiveTimelineRowDto>();
        if (login == null) return rows;

        var audits = await db.AuditLogs.AsNoTracking()
            .Where(a => a.LoginName == login && a.ActionDate != null)
            .OrderByDescending(a => a.ActionDate)
            .Take(20)
            .Select(a => new { a.ActionDate, a.TableName, a.ActionType })
            .ToListAsync();

        rows.AddRange(audits.Select(a => new ExecutiveTimelineRowDto
        {
            When = a.ActionDate ?? DateTime.MinValue,
            What = Describe(a.TableName, a.ActionType),
            Kind = "audit"
        }));

        var invoices = await db.Transactions.AsNoTracking()
            .Where(t => t.CreatedBy == login)
            .OrderByDescending(t => t.TransactionDate)
            .Take(10)
            .Select(t => new { t.TransactionDate, t.TransactionType, t.ReferenceNumber, t.GrandTotal })
            .ToListAsync();

        rows.AddRange(invoices.Select(t => new ExecutiveTimelineRowDto
        {
            When = t.TransactionDate,
            What = $"{(t.TransactionType == TransactionTypes.Sale ? "فاتورة بيع" : "فاتورة شراء")} {t.ReferenceNumber} — {t.GrandTotal:N0} ج",
            Kind = "invoice"
        }));

        return rows.OrderByDescending(r => r.When).Take(25).ToList();
    }

    // ─── «منذ 5 دقائق» ──────────────────────────────────────
    private static string HumanizeWhen(DateTime when)
    {
        var ts = DateTime.Now - when;
        if (ts.TotalMinutes < 1) return "الآن";
        if (ts.TotalMinutes < 60) return $"منذ {(int)ts.TotalMinutes} دقيقة";
        if (ts.TotalHours < 24) return $"منذ {(int)ts.TotalHours} ساعة";
        var days = (int)ts.TotalDays;
        if (days == 1) return "منذ يوم";
        if (days < 30) return $"منذ {days} أيام";
        return when.ToString("yyyy/MM/dd");
    }
}
