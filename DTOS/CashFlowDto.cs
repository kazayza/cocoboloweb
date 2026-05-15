namespace COCOBOLOERPNEW.DTOs;

// ============================
// قائمة التدفقات النقدية الكاملة
// ============================
public class CashFlowStatementDto
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public string PeriodLabel { get; set; } = "";

    // ────────────── الأرصدة ──────────────
    public decimal OpeningBalance { get; set; }     // رصيد بداية الفترة
    public decimal ClosingBalance { get; set; }     // رصيد نهاية الفترة
    public decimal NetCashFlow => ClosingBalance - OpeningBalance;

    // ────────────── التدفقات الداخلة ──────────────
    public decimal TotalInflows { get; set; }
    public List<CashFlowCategoryDto> Inflows { get; set; } = new();

    // ────────────── التدفقات الخارجة ──────────────
    public decimal TotalOutflows { get; set; }
    public List<CashFlowCategoryDto> Outflows { get; set; } = new();

    // ────────────── تفصيل بحسب الخزينة ──────────────
    public List<CashBoxFlowDto> ByCashBox { get; set; } = new();

    // ────────────── الترند اليومي ──────────────
    public List<DailyFlowDto> DailyTrend { get; set; } = new();

    // ────────────── الترند الشهري ──────────────
    public List<MonthlyFlowDto> MonthlyTrend { get; set; } = new();

    // ────────────── مؤشر السيولة ──────────────
    public LiquidityAnalysisDto LiquidityAnalysis { get; set; } = new();

    // ────────────── التوقعات ──────────────
    public CashFlowForecastDto Forecast { get; set; } = new();

    // ────────────── التوصيات الذكية ──────────────
    public List<CashFlowAlertDto> Alerts { get; set; } = new();

    // ────────────── أكبر الحركات ──────────────
    public List<TopFlowDto> TopInflows { get; set; } = new();
    public List<TopFlowDto> TopOutflows { get; set; } = new();
}

// ============================
// تفصيل تصنيف التدفق
// ============================
public class CashFlowCategoryDto
{
    public string ReferenceType { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal Percentage { get; set; }
    public int Count { get; set; }
    public string? Color { get; set; }
    public string? Icon { get; set; }
}

// ============================
// تدفق حسب الخزينة
// ============================
public class CashBoxFlowDto
{
    public int CashBoxId { get; set; }
    public string CashBoxName { get; set; } = "";
    public string? Color { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal Inflows { get; set; }
    public decimal Outflows { get; set; }
    public decimal NetFlow => Inflows - Outflows;
    public decimal ClosingBalance => OpeningBalance + NetFlow;
    public bool IsActive { get; set; }
}

// ============================
// تدفق يومي
// ============================
public class DailyFlowDto
{
    public DateTime Date { get; set; }
    public decimal Inflows { get; set; }
    public decimal Outflows { get; set; }
    public decimal NetFlow => Inflows - Outflows;
    public decimal RunningBalance { get; set; }
}

// ============================
// تدفق شهري
// ============================
public class MonthlyFlowDto
{
    public DateTime Month { get; set; }
    public string MonthLabel { get; set; } = "";
    public decimal Inflows { get; set; }
    public decimal Outflows { get; set; }
    public decimal NetFlow => Inflows - Outflows;
}

// ============================
// تحليل السيولة
// ============================
public class LiquidityAnalysisDto
{
    public decimal CurrentBalance { get; set; }
    public decimal AverageMonthlyOutflow { get; set; }
    public decimal LiquidityRatio { get; set; }     // كم شهر بدون أي إيراد
    public string Status { get; set; } = "";        // "ممتاز" / "جيد" / "حرج" / "خطر"
    public string Icon { get; set; } = "🟢";
    public string Description { get; set; } = "";
    public decimal MonthsCoverage => LiquidityRatio;
}

// ============================
// التوقعات
// ============================
public class CashFlowForecastDto
{
    public decimal AverageMonthlyInflow { get; set; }
    public decimal AverageMonthlyOutflow { get; set; }
    public decimal AverageMonthlyNet { get; set; }

    public decimal NextMonthExpectedInflow { get; set; }
    public decimal NextMonthExpectedOutflow { get; set; }
    public decimal NextMonthExpectedNet { get; set; }
    public decimal NextMonthExpectedBalance { get; set; }

    public string Trend { get; set; } = "";        // "نمو" / "تراجع" / "ثابت"
    public string TrendIcon { get; set; } = "📊";
    public decimal TrendPercentage { get; set; }   // % تغيير شهري
    public string Description { get; set; } = "";
}

// ============================
// تنبيهات السيولة
// ============================
public class CashFlowAlertDto
{
    public string Type { get; set; } = "";          // Critical / Warning / Success / Info
    public string Icon { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int Priority { get; set; }
    public string Color { get; set; } = "#3b82f6";
}

// ============================
// أكبر الحركات
// ============================
public class TopFlowDto
{
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
    public string CashBoxName { get; set; } = "";
    public string? ReferenceType { get; set; }
    public string? ReferenceTypeAr { get; set; }
    public decimal Amount { get; set; }
    public string? SourceUrl { get; set; }
}

// ============================
// فلتر التقرير
// ============================
public class CashFlowFilterDto
{
    public DateTime FromDate { get; set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    public DateTime ToDate { get; set; } = DateTime.Today;
    public string PeriodType { get; set; } = "Month";
    public int? CashBoxId { get; set; }            // فلترة بخزينة معينة (اختياري)
    public bool IncludeForecast { get; set; } = true;
    public bool IncludeMonthlyTrend { get; set; } = true;
}

// ============================
// تصنيفات (للـ UI)
// ============================
public static class CashFlowCategories
{
    public static readonly Dictionary<string, (string NameAr, string Color, string Icon, string Group)> Categories = new()
    {
        // الداخلة
        { CashBoxRefTypes.SaleInvoice,    ("تحصيل من عملاء",    "#10b981", "Receipt",        "Inflow") },
        { CashBoxRefTypes.AdvanceCharge,  ("رسوم معاينة",       "#84cc16", "Savings",        "Inflow") },
        { CashBoxRefTypes.ManualReceipt,  ("سندات قبض يدوية",   "#22c55e", "AddCard",        "Inflow") },
        { CashBoxRefTypes.OpeningBalance, ("رصيد افتتاحي",      "#6366f1", "Stars",          "Inflow") },
        { CashBoxRefTypes.TransferIn,     ("تحويلات داخلة",     "#06b6d4", "CallReceived",   "Both") },

        // الخارجة
        { CashBoxRefTypes.PurchaseInvoice,("سداد للموردين",     "#ef4444", "ShoppingCart",   "Outflow") },
        { CashBoxRefTypes.Expense,        ("مصروفات تشغيلية",   "#f59e0b", "MoneyOff",       "Outflow") },
        { CashBoxRefTypes.Payroll,        ("رواتب",             "#8b5cf6", "Payments",       "Outflow") },
        { CashBoxRefTypes.ManualPayment,  ("سندات صرف يدوية",   "#dc2626", "RemoveCircle",   "Outflow") },
        { CashBoxRefTypes.TransferOut,    ("تحويلات خارجة",     "#06b6d4", "CallMade",       "Both") },

        // القروض (في الاتجاهين)
        { CashBoxRefTypes.Loan,           ("قروض",              "#3b82f6", "AccountBalance", "Both") },
    };
}
