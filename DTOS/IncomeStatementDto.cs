namespace COCOBOLOERPNEW.DTOs;

// ============================
// قائمة الدخل الكاملة
// ============================
public class IncomeStatementDto
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public string PeriodLabel { get; set; } = "";

    // ────────────── الإيرادات ──────────────
    public decimal TotalRevenue { get; set; }              // إجمالي المبيعات
    public decimal NetRevenue { get; set; }                // الإيرادات بعد الخصومات
    public int InvoicesCount { get; set; }
    public decimal AverageInvoiceValue { get; set; }

    // ────────────── تكلفة المبيعات (COGS) ──────────────
    public decimal CostOfGoodsSold { get; set; }           // تكلفة الشراء للأصناف المباعة

    // ────────────── الربح الإجمالي ──────────────
    public decimal GrossProfit { get; set; }               // = الإيرادات - تكلفة المبيعات
    public decimal GrossProfitMargin { get; set; }         // النسبة %

    // ────────────── المصروفات التشغيلية ──────────────
    public decimal TotalOperatingExpenses { get; set; }
    public List<ExpenseGroupBreakdownDto> ExpensesByGroup { get; set; } = new();

    // ────────────── صافي الربح ──────────────
    public decimal NetProfit { get; set; }                 // = الربح الإجمالي - المصروفات
    public decimal NetProfitMargin { get; set; }           // النسبة %
    public string ProfitStatus { get; set; } = "";         // "ربح" / "خسارة" / "تعادل"

    // ────────────── التحليل والمقارنات ──────────────
    public IncomeComparisonDto? PreviousPeriod { get; set; }
    public IncomeComparisonDto? PreviousYear { get; set; }

    // ────────────── نقطة التعادل ──────────────
    public BreakEvenAnalysisDto BreakEvenAnalysis { get; set; } = new();

    // ────────────── الأهداف والتوصيات الذكية ──────────────
    public ProfitTargetDto ProfitTarget { get; set; } = new();
    public List<SmartRecommendationDto> Recommendations { get; set; } = new();

    // ────────────── معلومات إضافية ──────────────
    public List<TopExpenseDto> TopExpenses { get; set; } = new();
    public List<MonthlyTrendDto> MonthlyTrend { get; set; } = new();
}

// ============================
// تفصيل المصروفات بالمجموعة
// ============================
public class ExpenseGroupBreakdownDto
{
    public int GroupId { get; set; }
    public string GroupName { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal Percentage { get; set; }    // % من إجمالي المصروفات
    public int Count { get; set; }
    public string? Color { get; set; }
}

// ============================
// أعلى المصروفات
// ============================
public class TopExpenseDto
{
    public int ExpenseId { get; set; }
    public string ExpenseName { get; set; } = "";
    public string? GroupName { get; set; }
    public decimal Amount { get; set; }
    public decimal PercentageOfTotal { get; set; }
    public DateTime Date { get; set; }
}

// ============================
// المقارنات
// ============================
public class IncomeComparisonDto
{
    public string Label { get; set; } = "";        // "الشهر السابق" أو "نفس الشهر السنة السابقة"
    public decimal Revenue { get; set; }
    public decimal Expenses { get; set; }
    public decimal NetProfit { get; set; }
    public decimal NetProfitMargin { get; set; }

    public decimal RevenueChange { get; set; }     // % التغيير
    public decimal ExpensesChange { get; set; }
    public decimal NetProfitChange { get; set; }

    public string Trend { get; set; } = "";        // "تحسن" / "تراجع" / "ثابت"
}

// ============================
// نقطة التعادل (Break-Even Point)
// ============================
public class BreakEvenAnalysisDto
{
    public decimal FixedExpenses { get; set; }              // المصروفات الثابتة (التشغيلية)
    public decimal GrossProfitMargin { get; set; }          // نسبة الربح الإجمالي %
    public decimal BreakEvenRevenue { get; set; }           // الإيراد المطلوب للتعادل
    public decimal CurrentRevenue { get; set; }             // الإيراد الفعلي
    public decimal RevenueGap { get; set; }                 // الفرق
    public decimal SafetyMargin { get; set; }               // هامش الأمان %
    public string Status { get; set; } = "";                // "آمن" / "حرج" / "تحت التعادل"
    public string StatusIcon { get; set; } = "🟢";
    public string Description { get; set; } = "";

    public bool AboveBreakEven => CurrentRevenue >= BreakEvenRevenue;
}

// ============================
// الهدف الديناميكي (10% → 20% → 30%)
// ============================
public class ProfitTargetDto
{
    public decimal CurrentMargin { get; set; }              // النسبة الحالية المحققة
    public decimal CurrentTargetPercentage { get; set; }    // الهدف الحالي (10/20/30...)
    public decimal NextTargetPercentage { get; set; }       // الهدف القادم
    public decimal TargetAmount { get; set; }               // الربح المطلوب للهدف الحالي
    public decimal NextTargetAmount { get; set; }           // الربح المطلوب للهدف القادم
    public decimal AchievementPercentage { get; set; }      // % التحقيق من الهدف
    public string Status { get; set; } = "";                // "محقق" / "قريب" / "بعيد"
    public string Icon { get; set; } = "🎯";
    public string Message { get; set; } = "";

    // الفجوة للوصول للهدف
    public decimal GapToTarget { get; set; }
    public decimal GapToNextTarget { get; set; }

    // اقتراحات للوصول
    public decimal RevenueIncreaseNeeded { get; set; }      // زيادة الإيرادات المطلوبة
    public decimal ExpensesReductionNeeded { get; set; }    // تقليل المصروفات المطلوب
}

// ============================
// التوصيات الذكية
// ============================
public class SmartRecommendationDto
{
    public string Type { get; set; } = "";          // "Critical" / "Warning" / "Success" / "Info"
    public string Icon { get; set; } = "";          // 🔴 🟡 🟢 💡
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? ActionText { get; set; }
    public string? ActionUrl { get; set; }
    public int Priority { get; set; }                // 1 = أعلى أولوية
    public string Color { get; set; } = "#3b82f6";
}

// ============================
// الترند الشهري (للرسم البياني)
// ============================
public class MonthlyTrendDto
{
    public DateTime Month { get; set; }
    public string MonthLabel { get; set; } = "";
    public decimal Revenue { get; set; }
    public decimal Cogs { get; set; }
    public decimal Expenses { get; set; }
    public decimal NetProfit { get; set; }
    public decimal NetProfitMargin { get; set; }
}

// ============================
// فلتر التقرير
// ============================
public class IncomeStatementFilterDto
{
    public DateTime FromDate { get; set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    public DateTime ToDate { get; set; } = DateTime.Today;
    public string PeriodType { get; set; } = "Custom";   // Today / Week / Month / Quarter / Year / Custom
    public bool IncludeComparison { get; set; } = true;
    public bool IncludeMonthlyTrend { get; set; } = true;
}

// ============================
// ثوابت
// ============================
public static class IncomePeriodTypes
{
    public const string Today = "Today";
    public const string Week = "Week";
    public const string Month = "Month";
    public const string Quarter = "Quarter";
    public const string Year = "Year";
    public const string Custom = "Custom";

    public static readonly Dictionary<string, string> All = new()
    {
        { Today,   "اليوم" },
        { Week,    "هذا الأسبوع" },
        { Month,   "هذا الشهر" },
        { Quarter, "هذا الربع" },
        { Year,    "هذه السنة" },
        { Custom,  "فترة مخصصة" }
    };
}

public static class RecommendationTypes
{
    public const string Critical = "Critical";
    public const string Warning = "Warning";
    public const string Success = "Success";
    public const string Info = "Info";
}
