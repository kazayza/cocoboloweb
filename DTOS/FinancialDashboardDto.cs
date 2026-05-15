namespace COCOBOLOERPNEW.DTOs;

// ============================
// Dashboard المالي الشامل
// ============================
public class FinancialDashboardDto
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public string PeriodLabel { get; set; } = "";

    // ────────────── KPIs الرئيسية ──────────────
    public List<KpiCardDto> MainKpis { get; set; } = new();

    // ────────────── الصحة المالية الكلية ──────────────
    public FinancialHealthScoreDto HealthScore { get; set; } = new();

    // ────────────── المؤشرات النسبية ──────────────
    public FinancialRatiosDto Ratios { get; set; } = new();

    // ────────────── ملخص قائمة الدخل ──────────────
    public IncomeSummaryDto IncomeSummary { get; set; } = new();

    // ────────────── ملخص التدفقات النقدية ──────────────
    public CashFlowSummaryDto CashFlowSummary { get; set; } = new();

    // ────────────── المقارنات ──────────────
    public PeriodComparisonDto? VsPreviousPeriod { get; set; }
    public PeriodComparisonDto? VsLastYear { get; set; }

    // ────────────── الترند الشهري الموحّد ──────────────
    public List<UnifiedMonthlyDto> Last12Months { get; set; } = new();

    // ────────────── المنتجات والعملاء ──────────────
    public List<TopProductDto> TopProducts { get; set; } = new();
    public List<TopCustomerDto> TopCustomers { get; set; } = new();

    // ────────────── المصروفات ──────────────
    public List<ExpenseCategoryDto> TopExpenseCategories { get; set; } = new();

    // ────────────── الذمم ──────────────
    public ReceivablesSummaryDto Receivables { get; set; } = new();

    // ────────────── التنبيهات والتوصيات الموحّدة ──────────────
    public List<DashboardAlertDto> Alerts { get; set; } = new();
    public List<StrategicRecommendationDto> StrategicRecommendations { get; set; } = new();
}

// ============================
// KPI Card
// ============================
public class KpiCardDto
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public decimal Value { get; set; }
    public string Format { get; set; } = "money";   // money / percent / number
    public string Suffix { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Color { get; set; } = "";
    public decimal? ChangePercentage { get; set; }
    public string ChangeLabel { get; set; } = "";
    public string TrendIcon { get; set; } = "";
    public string Url { get; set; } = "";

    public string FormattedValue => Format switch
    {
        "money" => $"{Value:N2} ج",
        "percent" => $"{Value:N1}%",
        "number" => Value.ToString("N0"),
        _ => Value.ToString("N2")
    };
}

// ============================
// مؤشر الصحة المالية الكلية (0-100)
// ============================
public class FinancialHealthScoreDto
{
    public int OverallScore { get; set; }              // 0-100
    public string Grade { get; set; } = "";            // A+, A, B, C, D, F
    public string Status { get; set; } = "";           // ممتاز / جيد / متوسط / ضعيف / حرج
    public string Color { get; set; } = "";
    public string Description { get; set; } = "";

    public List<HealthFactorDto> Factors { get; set; } = new();
}

public class HealthFactorDto
{
    public string Name { get; set; } = "";
    public int Score { get; set; }                     // 0-100
    public int MaxScore { get; set; } = 100;
    public string Description { get; set; } = "";
    public string Color { get; set; } = "";
    public string Icon { get; set; } = "";
}

// ============================
// المؤشرات المالية النسبية
// ============================
public class FinancialRatiosDto
{
    // ربحية
    public decimal GrossProfitMargin { get; set; }     // الربح الإجمالي %
    public decimal NetProfitMargin { get; set; }       // صافي الربح %
    public decimal OperatingExpenseRatio { get; set; } // مصروفات تشغيلية / إيرادات

    // سيولة
    public decimal CurrentRatio { get; set; }          // السيولة (الأصول/الالتزامات) - مبسط
    public decimal CashRatio { get; set; }             // النقدية / المتوسط الشهري للمصروفات
    public decimal QuickRatio { get; set; }            // نسبة السرعة

    // كفاءة
    public decimal RevenuePerInvoice { get; set; }     // متوسط قيمة الفاتورة
    public decimal CollectionRate { get; set; }        // نسبة التحصيل %
    public decimal ExpenseGrowthRate { get; set; }     // معدل نمو المصروفات
    public decimal RevenueGrowthRate { get; set; }     // معدل نمو الإيرادات
}

// ============================
// ملخص قائمة الدخل
// ============================
public class IncomeSummaryDto
{
    public decimal Revenue { get; set; }
    public decimal Cogs { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal OperatingExpenses { get; set; }
    public decimal NetProfit { get; set; }
    public decimal NetProfitMargin { get; set; }
    public string ProfitStatus { get; set; } = "";
    public decimal TargetPercentage { get; set; }      // الهدف الحالي
    public decimal AchievedPercentage { get; set; }    // % التحقيق
    public decimal BreakEvenRevenue { get; set; }
    public bool AboveBreakEven { get; set; }
}

// ============================
// ملخص التدفقات النقدية
// ============================
public class CashFlowSummaryDto
{
    public decimal CurrentBalance { get; set; }
    public decimal Inflows { get; set; }
    public decimal Outflows { get; set; }
    public decimal NetCashFlow { get; set; }
    public decimal LiquidityRatio { get; set; }        // كم شهر بدون دخل
    public string LiquidityStatus { get; set; } = "";
    public decimal NextMonthForecast { get; set; }
    public string Trend { get; set; } = "";            // نمو / تراجع / ثابت
}

// ============================
// المقارنة بفترة
// ============================
public class PeriodComparisonDto
{
    public string Label { get; set; } = "";
    public decimal Revenue { get; set; }
    public decimal Expenses { get; set; }
    public decimal NetProfit { get; set; }
    public decimal RevenueChange { get; set; }
    public decimal ExpensesChange { get; set; }
    public decimal NetProfitChange { get; set; }
    public string OverallTrend { get; set; } = "";
    public string TrendIcon { get; set; } = "";
}

// ============================
// الترند الشهري الموحّد
// ============================
public class UnifiedMonthlyDto
{
    public DateTime Month { get; set; }
    public string MonthLabel { get; set; } = "";
    public decimal Revenue { get; set; }
    public decimal Cogs { get; set; }
    public decimal Expenses { get; set; }
    public decimal NetProfit { get; set; }
    public decimal NetProfitMargin { get; set; }
    public decimal CashInflows { get; set; }
    public decimal CashOutflows { get; set; }
    public decimal NetCashFlow { get; set; }
    public decimal CashBalance { get; set; }
}

// ============================
// أعلى المنتجات
// ============================
public class TopProductDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public int QuantitySold { get; set; }
    public decimal Revenue { get; set; }
    public decimal Cogs { get; set; }
    public decimal Profit { get; set; }
    public decimal ProfitMargin { get; set; }
    public int OrdersCount { get; set; }
}

// ============================
// أعلى العملاء
// ============================
public class TopCustomerDto
{
    public int PartyId { get; set; }
    public string CustomerName { get; set; } = "";
    public string? Phone { get; set; }
    public int InvoicesCount { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal Remaining { get; set; }
    public decimal AverageInvoiceValue { get; set; }
}

// ============================
// تصنيفات المصروفات
// ============================
public class ExpenseCategoryDto
{
    public string GroupName { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal Percentage { get; set; }
    public string Color { get; set; } = "";
}

// ============================
// ملخص الذمم
// ============================
public class ReceivablesSummaryDto
{
    public decimal CustomerReceivables { get; set; }   // مستحقات على العملاء
    public decimal SupplierPayables { get; set; }      // مستحقات للموردين
    public decimal PersonalAccountsCredit { get; set; } // عليكم (دائن)
    public decimal PersonalAccountsDebit { get; set; }  // لكم (مدين)
    public decimal NetReceivables => CustomerReceivables - SupplierPayables - PersonalAccountsCredit + PersonalAccountsDebit;
    public int CustomersWithDebtCount { get; set; }
    public int OverdueInvoicesCount { get; set; }
}

// ============================
// التنبيهات
// ============================
public class DashboardAlertDto
{
    public string Type { get; set; } = "";          // Critical / Warning / Success / Info
    public string Icon { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? ActionUrl { get; set; }
    public string? ActionLabel { get; set; }
    public int Priority { get; set; }
    public string Color { get; set; } = "";
    public string Category { get; set; } = "";       // Income / CashFlow / Expenses / Receivables
}

// ============================
// التوصيات الاستراتيجية
// ============================
public class StrategicRecommendationDto
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Impact { get; set; } = "";        // High / Medium / Low
    public string Difficulty { get; set; } = "";    // Easy / Medium / Hard
    public string ExpectedOutcome { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Color { get; set; } = "";
    public List<string> ActionSteps { get; set; } = new();
}

// ============================
// فلتر
// ============================
public class FinancialDashboardFilterDto
{
    public DateTime FromDate { get; set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    public DateTime ToDate { get; set; } = DateTime.Today;
    public string PeriodType { get; set; } = "Month";
}
