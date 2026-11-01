namespace COCOBOLOERPNEW.DTOs;

// ════════════════════════════════════════════════════════════
//  لوحة القيادة التنفيذية — Executive Cockpit DTOs
// ════════════════════════════════════════════════════════════

public class ExecutiveKpiDto
{
    public decimal Value { get; set; }
    public decimal PrevValue { get; set; }
    public decimal YearValue { get; set; }
    public decimal PrevPct => PrevValue == 0 ? 0 : Math.Round((Value - PrevValue) / Math.Abs(PrevValue) * 100, 1);
    public decimal YearPct => YearValue == 0 ? 0 : Math.Round((Value - YearValue) / Math.Abs(YearValue) * 100, 1);

    // ⭐ مقارنة ذات معنى؟ — أساس صفر (فترة سابقة/سنة بلا قيمة) = لا مقارنة،
    //    والعرض يكون «—» بدل 0% المضللة (مراجعة أ6).
    public bool HasPrevComparison => PrevValue != 0;
    public bool HasYearComparison => YearValue != 0;
    public decimal? PrevPctOrNull => HasPrevComparison ? PrevPct : null;
    public decimal? YearPctOrNull => HasYearComparison ? YearPct : null;
}

// ════════════════════════════════════════════════════════════
//  أعمار الذمم والمستحقات (إضافة 1) — 5 خانات لكل اتجاه
//  الأساس: (DueDate ?? TransactionDate) مقابل اليوم، على الرصيد
//  المفتوح (GrandTotal − PaidAmount) للفواتير غير الملغاة.
//  مجموع الخانات = إجمالي الرصيد المفتوح بالضرورة.
// ════════════════════════════════════════════════════════════
public class ExecutiveAgingBucketDto
{
    public string Label { get; set; } = "";
    public decimal Amount { get; set; }
    public int Count { get; set; }
}

public class ExecutiveTopDebtorDto
{
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
}

public class ExecutiveAgingDto
{
    public List<ExecutiveAgingBucketDto> Receivables { get; set; } = new();
    public List<ExecutiveAgingBucketDto> Payables { get; set; } = new();
    public decimal TotalReceivables { get; set; }
    public decimal TotalPayables { get; set; }
    public List<ExecutiveTopDebtorDto> TopDebtors { get; set; } = new();
}

// ════════════════════════════════════════════════════════════
//  قائمة دخل مصغرة (إضافة 2) — كل الأرقام من نفس استدعاء
//  FinancialReportsService.GetIncomeStatementAsync الذي يغذي
//  كارت «صافي الربح»، فلا يمكن أن تختلف عن قائمة الدخل.
// ════════════════════════════════════════════════════════════
public class ExecutiveMiniPlDto
{
    public bool Loaded { get; set; }
    public decimal NetRevenue { get; set; }
    public decimal Cogs { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal GrossProfitMargin { get; set; }
    public decimal OperatingExpenses { get; set; }   // شاملة الرواتب (تعريف قائمة الدخل)
    public decimal PayrollExpense { get; set; }
    public decimal NetProfit { get; set; }
    public decimal NetProfitMargin { get; set; }

    // هدف هامش الربح القائم في قائمة الدخل (ProfitTarget)
    public decimal TargetMarginPct { get; set; }
    public decimal NextTargetMarginPct { get; set; }
    public decimal TargetAchievementPct { get; set; }
    public string TargetStatus { get; set; } = "";
    public string TargetMessage { get; set; } = "";
}

public class ExecutiveTrendPointDto
{
    public string Label { get; set; } = "";
    public decimal Sales { get; set; }
    public decimal Collected { get; set; }
    public decimal Purchases { get; set; }
}

public class ExecutiveEmployeeRowDto
{
    public int EmployeeId { get; set; }
    public string FullName { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string Attendance { get; set; } = "";
    public int ActionsToday { get; set; }
    public int ActionsMonth { get; set; }
    public List<int> Heat7 { get; set; } = new();
    public DateTime? LastActivity { get; set; }
    public string LastActivityText { get; set; } = "";
    public decimal MonthSales { get; set; }
    public decimal MonthCollection { get; set; }
    public int TasksPending { get; set; }
    public int TasksOverdue { get; set; }
    public string Status { get; set; } = "green";   // green | yellow | red | off (موقوف/إجازة) | gray (بلا حساب نظام)
    public bool IsSales { get; set; }
    public string HrStatus { get; set; } = "";      // نشط / موقوف / بالإجازة / مستقيل
    public bool HasAccount { get; set; }
}

public class ExecutiveBranchSaleDto
{
    public string Name { get; set; } = "";
    public decimal Value { get; set; }
}

public class ExecutiveGmActionDto
{
    public string When { get; set; } = "";
    public string What { get; set; } = "";
}

public class ExecutiveGmDto
{
    public bool Found { get; set; }
    public string FullName { get; set; } = "";
    public string LastActivityText { get; set; } = "";
    public int ActionsWeek { get; set; }
    public List<ExecutiveGmActionDto> Recent { get; set; } = new();
    public int PendingPrice { get; set; }
    public int EscalatedComplaints { get; set; }
    public decimal TeamSalesMonth { get; set; }
    public int TeamActiveToday { get; set; }
    public int TeamTotal { get; set; }
}

public class ExecutiveExceptionsDto
{
    public int Debts30 { get; set; }
    public decimal Debts30Amount { get; set; }
    public int LateDeliveries { get; set; }
    public int PendingPrice { get; set; }
    public int NegativeStock { get; set; }
    public int StalledLeads { get; set; }
    public int OpenComplaints { get; set; }
}

public class ExecutiveOpsDto
{
    public int FactoryOpen { get; set; }
    public int FactoryMonth { get; set; }
    public int OpenComplaints { get; set; }
    public int NewLeads { get; set; }
}

public class ExecutiveCockpitDto
{
    // Sales/Purchases: القيمة هي الصافي بعد مرتجعات الفترة (انظر SaleReturns/PurchaseReturns)
    // Collected: تحصيلات الفترة الفعلية من جدول Payments بتاريخ الدفعة
    // Expenses/Net: من قائمة الدخل نفسها (FinancialReportsService) — Net = صافي الربح الاستحقاقي
    // CashNet: صافي حركة الخزينة النقدي (قبض − صرف) بحركات CashboxTransactions داخل الفترة
    public ExecutiveKpiDto Sales { get; set; } = new();
    public ExecutiveKpiDto Collected { get; set; } = new();
    public ExecutiveKpiDto Purchases { get; set; } = new();
    public ExecutiveKpiDto Expenses { get; set; } = new();
    public ExecutiveKpiDto Net { get; set; } = new();
    public ExecutiveKpiDto CashNet { get; set; } = new();

    // مرتجعات الفترة (تُعرض كسطر توضيحي تحت كارتَي المبيعات/المشتريات)
    public decimal SaleReturns { get; set; }
    public decimal PurchaseReturns { get; set; }

    // أعمار الذمم/المستحقات + قائمة دخل مصغرة
    public ExecutiveAgingDto Aging { get; set; } = new();
    public ExecutiveMiniPlDto MiniPl { get; set; } = new();

    // ⭐ مرحلة 2: بلوكات التغطية (عروض أسعار/مخزون/أوامر تشغيل/تسليمات/تسويق/B2B/موارد بشرية)
    public ExecutiveQuotationsDto Quotations { get; set; } = new();
    public ExecutiveInventoryDto Inventory { get; set; } = new();
    public ExecutiveJobOrdersDto JobOrders { get; set; } = new();
    public ExecutiveDeliveriesDto Deliveries { get; set; } = new();
    public ExecutiveGeneralTasksDto GeneralTasks { get; set; } = new();
    public ExecutiveMarketingDto Marketing { get; set; } = new();
    public ExecutiveB2bBlockDto B2bBlock { get; set; } = new();
    public ExecutiveHrDto Hr { get; set; } = new();

    // بنود بانتظار قرار (إكمال أ8) — تُضاف إلى DecisionsPending والدايلوج
    public int PendingInvoiceEdits { get; set; }
    public int PendingClosureApprovals { get; set; }
    public int PendingDeliveryPermits { get; set; }

    // أقسام تعذّر تحميلها (فشل جزئي متسامح) — الصفحة تعرض مكانها تنبيهًا خفيفًا
    public List<string> FailedSections { get; set; } = new();

    public List<ExecutiveTrendPointDto> Trend { get; set; } = new();
    public List<ExecutiveEmployeeRowDto> Employees { get; set; } = new();

    public ExecutiveGmDto Gm { get; set; } = new();
    public ExecutiveExceptionsDto Exceptions { get; set; } = new();
    public ExecutiveOpsDto Ops { get; set; } = new();

    public string PeriodLabel { get; set; } = "";
    // ⭐ مرحلة 3: سياق التوليد (فترة/نطاق فعلي/فرع) — يغذي نسخة الطباعة واللقطات
    public string PeriodKey { get; set; } = "";
    public DateTime RangeFrom { get; set; }
    public DateTime RangeTo { get; set; }
    public int? BranchId { get; set; }
    public string BranchName { get; set; } = "";
    // أقسام لا تستطيع احترام فلتر الفرع (بياناتها بلا إسناد فرع) عند اختيار
    // فرع — مفاتيح: marketing | b2b | gm | complaints. فارغة مع «كل الفروع».
    public List<string> BranchIgnoredSections { get; set; } = new();
    public string Story { get; set; } = "";
    public int DecisionsPending { get; set; }
    public int DormantCount { get; set; }

    // مصادر إضافية: خزينة · بايبلاين الفرص · B2B · الفروع
    public decimal CashBalance { get; set; }
    public int PipelineOpen { get; set; }
    public decimal PipelineValue { get; set; }
    public int PipelineStalled { get; set; }
    public int B2bPending { get; set; }
    public List<ExecutiveBranchSaleDto> BranchSales { get; set; } = new();

    // توقع إقفال الشهر الحالي
    public decimal ForecastValue { get; set; }
    public decimal LastMonthSales { get; set; }
    public decimal ForecastVsLastPct { get; set; }

    public DateTime GeneratedAt { get; set; } = DateTime.Now;
}

// ════════════════════════════════════════════════════════════
//  ⭐ مرحلة 2 (اكتمال الصورة) — بلوكات التغطية الجديدة
// ════════════════════════════════════════════════════════════

/// <summary>اسم + قيمة (أعلى أصناف المخزون قيمةً وغيرها)</summary>
public class ExecutiveNameValueDto
{
    public string Name { get; set; } = "";
    public decimal Value { get; set; }
}

// ─── عروض الأسعار (إضافة 3) ───────────────────────────────
//  بايبلاين الفترة = عروض أُنشئت في الفترة بحالتها الحالية.
//  نسبة التحويل من «المحسومة» في الفترة (محوّلة ÷ محوّلة+مرفوضة+منتهية).
//  «بانتظار الرد» و«مقبولة لم تُحوَّل» و«شارفت على الانتهاء» لقطات حالية.
public class ExecutiveQuotationStatusDto
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public decimal Value { get; set; }
}

public class ExecutiveQuotationsDto
{
    public List<ExecutiveQuotationStatusDto> ByStatus { get; set; } = new();
    public int PeriodCount { get; set; }
    public decimal PeriodValue { get; set; }
    public int ConvertedCount { get; set; }
    public decimal ConversionRate { get; set; }          // % من المحسومة
    public bool HasDecided { get; set; }
    public int SentCount { get; set; }                    // لقطة: مرسلة بانتظار رد العميل
    public decimal SentValue { get; set; }
    public int AcceptedNotConvertedCount { get; set; }    // لقطة: مقبولة ولم تُحوَّل لفاتورة بعد
    public decimal AcceptedNotConvertedValue { get; set; }
    public int ExpiringSoonCount { get; set; }            // لقطة: مرسلة وتنتهي صلاحيتها خلال 7 أيام
}

// ─── المخزون (إضافة 4) — لقطة حالية بطبيعتها ──────────────
//  القيمة من IStockService.GetStockValueAsync نفسه (كمية × تكلفة
//  الشراء بباقة الأسعار الأساسية) — نفس رقم شاشة قيمة المخزون وزكي.
//  نافد/منخفض من StockLevels (لا حد إعادة طلب مخزّن في النظام:
//  نافد = الإجمالي ≤ 0، منخفض = 1..5 وحدات — مُعلن على الكارت).
//  راكد = رصيد > 0 وبلا أي حركة مخزون منذ 90 يومًا.
public class ExecutiveInventoryDto
{
    public decimal TotalCostValue { get; set; }
    public decimal TotalSaleValue { get; set; }
    public int ProductsCount { get; set; }
    public long TotalQuantity { get; set; }
    public int OutOfStockCount { get; set; }
    public int LowStockCount { get; set; }
    public int DeadStockCount { get; set; }
    public decimal DeadStockValue { get; set; }
    public List<ExecutiveNameValueDto> TopValueProducts { get; set; } = new();
    public List<ExecutiveWarehouseValueDto> Warehouses { get; set; } = new();
}

// تفصيل قيمة المخزون لكل مخزن — تجميع لنفس صفوف GetStockValueAsync
// التي يُبنى منها إجمالي الكارت، فمجموع CostValue هنا يساوي
// TotalCostValue بالبناء (بلا أي استعلام إضافي).
public class ExecutiveWarehouseValueDto
{
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public decimal CostValue { get; set; }
    public decimal SharePct { get; set; }
    public int ProductsCount { get; set; }
}

// ─── أوامر التشغيل والإنتاج (إضافة 5) ─────────────────────
//  نفس قاعدة شاشة أوامر التشغيل وزكي: فواتير بيع + شراء حقيقي
//  (بلا مرآة) وغير ملغاة. المفتوح/المتأخر لقطة حالية (الرئيس يرى
//  التواريخ الحقيقية)، والمُسلَّم يُحسب داخل الفترة المختارة.
public class ExecutiveJobOrdersDto
{
    public int OpenCount { get; set; }
    public int OpenSale { get; set; }
    public int OpenPurchase { get; set; }
    public int OverdueCount { get; set; }
    public decimal OverdueValue { get; set; }
    public int WithAlternativesCount { get; set; }
    public int DeliveredInPeriod { get; set; }
}

// ─── التسليمات التشغيلي (إضافة 6) ─────────────────────────
//  نفس تعريفات شاشة حالة التسليمات وأداة أداء زكي، وبالتواريخ
//  الحقيقية (حساب الرئيس مستثنى من الإخفاء أصلًا): المخطط =
//  موعده (DueDate) داخل الفترة، والالتزام = التسليم الفعلي
//  في/قبل الموعد من بين المُسلَّمة فعليًا فقط.
public class ExecutiveDeliveriesDto
{
    public int Planned { get; set; }
    public int Delivered { get; set; }
    public int OnTime { get; set; }
    public int Late { get; set; }
    public decimal OnTimePct { get; set; }
    public bool HasDelivered { get; set; }
    public int Pending { get; set; }
    public int ReturnedInPeriod { get; set; }
    public int NotDeliveredInPeriod { get; set; }         // من مخطط الفترة: لا أحداث تسليم بعد
    public int PartialInPeriod { get; set; }              // من مخطط الفترة: جزئية حاليًا (من الأحداث)
    public int PartialOpenNow { get; set; }               // لقطة حالية: جزئي مفتوح (أحداث + متبقٍّ > 0)
    public int BatchEventsInPeriod { get; set; }          // دفعات الفترة المحتسبة في دقة الدفعات
    public int BatchOnTimeEvents { get; set; }
    public decimal BatchPunctualityPct { get; set; }
    public bool HasBatchEvents { get; set; }
    public int OverdueNow { get; set; }                   // لقطة حالية
    public decimal OverdueAmount { get; set; }
    public int DueToday { get; set; }
    public int DueTomorrow { get; set; }
    // ─── نتائج التسليم (القسم 11) ───
    public int EventsWithRemarksInPeriod { get; set; }    // أحداث الفترة التي فيها حصص راجعة
    public decimal ManufacturingDefectQtyInPeriod { get; set; }
    public decimal HandlingDamageQtyInPeriod { get; set; }
    public int MaintenanceBacklogRecords { get; set; }    // لقطة حالية: سجلات داخل حلقة الصيانة
    public decimal MaintenanceBacklogQty { get; set; }
    public int? MaintenanceOldestAgeDays { get; set; }    // عمر أقدم سجل مفتوح (يوم)
    public int PendingModificationChargesCount { get; set; }      // بانتظار الاعتماد
    public decimal ApprovedUncollectedModificationAmount { get; set; } // معتمدة غير محصّلة
    public decimal RepairCostInPeriod { get; set; }       // تكلفة إصلاح داخلية أُغلقت في الفترة
}

// ─── التكليفات العامة ────────────────────────────────────
//  لقطة حالية (مفتوح/متأخر/قيد التنفيذ/مرتجع بانتظار قرار) +
//  منجزات الفترة (مكتمل/ملغي بتواريخ الإغلاق) + أكثر 3 موظفين
//  تأخرًا. التكليفات بلا بُعد فرع — إجمالي الشركة دائمًا
//  (تُعلَّم في BranchIgnoredSections عند اختيار فرع).
public class ExecutiveGeneralTasksDto
{
    public int OpenTotal { get; set; }
    public int OverdueTotal { get; set; }
    public int InProgressTotal { get; set; }
    public int ReturnedPendingTotal { get; set; }
    public int CompletedInPeriod { get; set; }
    public int CancelledInPeriod { get; set; }
    public List<ExecutiveTaskLoadRowDto> TopOverdueEmployees { get; set; } = new();
}

public class ExecutiveTaskLoadRowDto
{
    public string EmployeeName { get; set; } = "";
    public int Overdue { get; set; }
    public int Open { get; set; }
}

// ─── التسويق المصغر (إضافة 8) ─────────────────────────────
//  كل الأرقام من IMarketingDashboardService.GetDashboardAsync
//  لنفس الفترة — نفس تعريفات لوحة التسويق بحذافيرها.
public class ExecutiveMarketingDto
{
    public int Leads { get; set; }
    public int Customers { get; set; }
    public decimal ConversionPct { get; set; }
    public decimal Spend { get; set; }
    public decimal CostPerLead { get; set; }
    public string TopSourceName { get; set; } = "";
    public int TopSourceLeads { get; set; }
    public string TopCampaignName { get; set; } = "";
    public int TopCampaignLeads { get; set; }
    public int TopCampaignDeals { get; set; }
}

// ─── بوابة B2B موسّع (إضافة 9) ────────────────────────────
//  طلبات الفترة بحالتها الحالية؛ «قيمة المحوّلة» = مجموع فواتير
//  البيع المرتبطة بطلبات الفترة المحوّلة (لا سعر على بنود الطلب).
public class ExecutiveB2bBlockDto
{
    public int RequestsInPeriod { get; set; }
    public int ConvertedInPeriod { get; set; }
    public decimal ConvertedValue { get; set; }
    public int PendingNow { get; set; }                   // لقطة: جديد + قيد المراجعة
    public int Companies { get; set; }                    // شركات لها حسابات بوابة
    public int ActiveUsers { get; set; }
    public int InactiveUsers { get; set; }                // غير مفعّلة/موقوفة
}

// ─── الموارد البشرية التشغيلي (إضافة 7) ───────────────────
//  الحضور لقطة اليوم، الرواتب شهر حالي (Payrolls)، السلف من
//  EmployeeLoans. تنبيه: لا كيان «طلبات إجازة» ولا «طلبات سلف
//  معلقة» في النظام (السلف تُنشأ نشطة مباشرة) — فلا أرقام لهما.
public class ExecutiveHrDto
{
    public int ActiveEmployees { get; set; }
    public int PresentToday { get; set; }
    public int LateToday { get; set; }
    public int AbsentToday { get; set; }
    public decimal AttendancePct { get; set; }
    public int ExemptCount { get; set; }        // نشطون معفون من البصمة — خارج أساس الحضور (حضورهم يدوي)
    public int OnLeaveCount { get; set; }
    public decimal PayrollMonthCost { get; set; }
    public string PayrollMonthLabel { get; set; } = "";
    public int ActiveLoansCount { get; set; }
    public decimal ActiveLoansRemaining { get; set; }
    public int NewLoansInPeriod { get; set; }
    public decimal NewLoansAmountInPeriod { get; set; }
}

public class ExecutiveTimelineRowDto
{
    public DateTime When { get; set; }
    public string What { get; set; } = "";
    public string Kind { get; set; } = "";   // audit | invoice
}

// ════════════════════════════════════════════════════════════
//  ⭐ مرحلة 3: لقطات اللوحة المحفوظة (dbo.ExecutiveCockpitSnapshots)
//  تُخزَّن كـ JSON كامل للـ DTO عبر ExecutiveCockpitSnapshotService
//  (SQL مباشر — نفس اصطلاح جداول المساعد، بلا كيانات EF).
// ════════════════════════════════════════════════════════════
public class ExecutiveCockpitSnapshotItemDto
{
    public long SnapshotId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string PeriodKey { get; set; } = "";
    public string PeriodLabel { get; set; } = "";
    public int? BranchId { get; set; }
    public string BranchName { get; set; } = "";
    public string CreatedBy { get; set; } = "";
}

public class ExecutiveCockpitSnapshotDetailDto
{
    public ExecutiveCockpitSnapshotItemDto Meta { get; set; } = new();
    public ExecutiveCockpitDto Data { get; set; } = new();
}
