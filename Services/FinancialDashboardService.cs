using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class FinancialDashboardService : IFinancialDashboardService
{
    private readonly db24804Context _db;
    private readonly IFinancialReportsService _income;
    private readonly ICashFlowService _cashFlow;

    public FinancialDashboardService(
        db24804Context db,
        IFinancialReportsService income,
        ICashFlowService cashFlow)
    {
        _db = db;
        _income = income;
        _cashFlow = cashFlow;
    }

    // ============================================================
    //  ⭐ الـ Method الرئيسية
    // ============================================================
    public async Task<FinancialDashboardDto> GetDashboardAsync(FinancialDashboardFilterDto filter)
    {
        var dto = new FinancialDashboardDto
        {
            FromDate = filter.FromDate.Date,
            ToDate = filter.ToDate.Date.AddDays(1).AddTicks(-1),
            PeriodLabel = BuildPeriodLabel(filter)
        };

        // 1. جلب قائمة الدخل
        var income = await _income.GetIncomeStatementAsync(new IncomeStatementFilterDto
        {
            FromDate = dto.FromDate,
            ToDate = dto.ToDate,
            IncludeComparison = true,
            IncludeMonthlyTrend = false
        });

        // 2. جلب التدفقات النقدية
        var cashFlow = await _cashFlow.GetCashFlowStatementAsync(new CashFlowFilterDto
        {
            FromDate = dto.FromDate,
            ToDate = dto.ToDate,
            IncludeForecast = true,
            IncludeMonthlyTrend = false
        });

        // 3. ملخصات
        BuildIncomeSummary(dto, income);
        BuildCashFlowSummary(dto, cashFlow);

        // 4. KPIs الرئيسية
        BuildMainKpis(dto, income, cashFlow);

        // 5. المؤشرات النسبية
        await BuildRatiosAsync(dto, income, cashFlow);

        // 6. مؤشر الصحة المالية
        BuildHealthScore(dto, income, cashFlow);

        // 7. المقارنات
        BuildComparisons(dto, income);

        // 8. الترند الموحّد (12 شهر)
        await BuildUnifiedMonthlyTrendAsync(dto);

        // 9. أعلى المنتجات
        await GetTopProductsAsync(dto);

        // 10. أعلى العملاء
        await GetTopCustomersAsync(dto);

        // 11. تصنيفات المصروفات
        BuildExpenseCategories(dto, income);

        // 12. ملخص الذمم
        await BuildReceivablesSummaryAsync(dto);

        // 13. توحيد التنبيهات من كل المصادر
        ConsolidateAlerts(dto, income, cashFlow);

        // 14. التوصيات الاستراتيجية
        GenerateStrategicRecommendations(dto, income, cashFlow);

        return dto;
    }

    // ============================================================
    //  ملخصات
    // ============================================================
    private void BuildIncomeSummary(FinancialDashboardDto dto, IncomeStatementDto income)
    {
        dto.IncomeSummary = new IncomeSummaryDto
        {
            Revenue = income.NetRevenue,
            Cogs = income.CostOfGoodsSold,
            GrossProfit = income.GrossProfit,
            OperatingExpenses = income.TotalOperatingExpenses,
            NetProfit = income.NetProfit,
            NetProfitMargin = income.NetProfitMargin,
            ProfitStatus = income.ProfitStatus,
            TargetPercentage = income.ProfitTarget.CurrentTargetPercentage,
            AchievedPercentage = income.ProfitTarget.AchievementPercentage,
            BreakEvenRevenue = income.BreakEvenAnalysis.BreakEvenRevenue,
            AboveBreakEven = income.BreakEvenAnalysis.AboveBreakEven
        };
    }

    private void BuildCashFlowSummary(FinancialDashboardDto dto, CashFlowStatementDto cf)
    {
        dto.CashFlowSummary = new CashFlowSummaryDto
        {
            CurrentBalance = cf.ClosingBalance,
            Inflows = cf.TotalInflows,
            Outflows = cf.TotalOutflows,
            NetCashFlow = cf.NetCashFlow,
            LiquidityRatio = cf.LiquidityAnalysis.LiquidityRatio,
            LiquidityStatus = cf.LiquidityAnalysis.Status,
            NextMonthForecast = cf.Forecast.NextMonthExpectedNet,
            Trend = cf.Forecast.Trend
        };
    }

    // ============================================================
    //  KPIs الرئيسية
    // ============================================================
    private void BuildMainKpis(FinancialDashboardDto dto, IncomeStatementDto income, CashFlowStatementDto cf)
    {
        // 1. الإيرادات
        dto.MainKpis.Add(new KpiCardDto
        {
            Title = "إجمالي الإيرادات",
            Subtitle = $"{income.InvoicesCount} فاتورة",
            Value = income.NetRevenue,
            Format = "money",
            Icon = "TrendingUp",
            Color = "#10b981",
            ChangePercentage = income.PreviousPeriod?.RevenueChange,
            ChangeLabel = income.PreviousPeriod != null ? "vs الفترة السابقة" : "",
            TrendIcon = GetTrendIcon(income.PreviousPeriod?.RevenueChange ?? 0),
            Url = "/reports/income-statement"
        });

        // 2. صافي الربح
        dto.MainKpis.Add(new KpiCardDto
        {
            Title = "صافي الربح",
            Subtitle = $"{income.NetProfitMargin:N1}% هامش ربح",
            Value = income.NetProfit,
            Format = "money",
            Icon = income.NetProfit >= 0 ? "AccountBalance" : "Warning",
            Color = income.NetProfit >= 0 ? "#10b981" : "#ef4444",
            ChangePercentage = income.PreviousPeriod?.NetProfitChange,
            ChangeLabel = income.PreviousPeriod != null ? "vs الفترة السابقة" : "",
            TrendIcon = GetTrendIcon(income.PreviousPeriod?.NetProfitChange ?? 0),
            Url = "/reports/income-statement"
        });

        // 3. المصروفات
        dto.MainKpis.Add(new KpiCardDto
        {
            Title = "إجمالي المصروفات",
            Subtitle = $"{income.ExpensesByGroup.Count} مجموعة",
            Value = income.TotalOperatingExpenses,
            Format = "money",
            Icon = "MoneyOff",
            Color = "#f59e0b",
            ChangePercentage = income.PreviousPeriod?.ExpensesChange,
            ChangeLabel = income.PreviousPeriod != null ? "vs الفترة السابقة" : "",
            TrendIcon = GetTrendIcon(income.PreviousPeriod?.ExpensesChange ?? 0, inverse: true),
            Url = "/expenses"
        });

        // 4. الرصيد النقدي
        dto.MainKpis.Add(new KpiCardDto
        {
            Title = "الرصيد النقدي",
            Subtitle = $"{cf.LiquidityAnalysis.LiquidityRatio} شهر سيولة",
            Value = cf.ClosingBalance,
            Format = "money",
            Icon = "AccountBalanceWallet",
            Color = "#06b6d4",
            Url = "/cashbox/dashboard"
        });

        // 5. صافي التدفق النقدي
        dto.MainKpis.Add(new KpiCardDto
        {
            Title = "صافي التدفق",
            Subtitle = "في الفترة",
            Value = cf.NetCashFlow,
            Format = "money",
            Icon = cf.NetCashFlow >= 0 ? "TrendingUp" : "TrendingDown",
            Color = cf.NetCashFlow >= 0 ? "#10b981" : "#ef4444",
            Url = "/reports/cash-flow"
        });

        // 6. هامش الربح الإجمالي
        dto.MainKpis.Add(new KpiCardDto
        {
            Title = "هامش الربح الإجمالي",
            Subtitle = $"تكلفة: {income.CostOfGoodsSold:N0} ج",
            Value = income.GrossProfitMargin,
            Format = "percent",
            Icon = "PieChart",
            Color = income.GrossProfitMargin >= 30 ? "#10b981" : "#f59e0b",
            Url = "/reports/income-statement"
        });
    }

    // ============================================================
    //  المؤشرات النسبية
    // ============================================================
    private async Task BuildRatiosAsync(FinancialDashboardDto dto, IncomeStatementDto income, CashFlowStatementDto cf)
    {
        var r = dto.Ratios;

        // ربحية
        r.GrossProfitMargin = income.GrossProfitMargin;
        r.NetProfitMargin = income.NetProfitMargin;
        r.OperatingExpenseRatio = income.NetRevenue == 0 ? 0
            : Math.Round((income.TotalOperatingExpenses / income.NetRevenue) * 100, 1);

        // سيولة
        r.CashRatio = cf.LiquidityAnalysis.LiquidityRatio;

        // نسبة سريعة (Cash + Receivables / Monthly Outflows)
        var receivables = await GetTotalCustomerReceivablesAsync();
        if (cf.LiquidityAnalysis.AverageMonthlyOutflow > 0)
        {
            r.QuickRatio = Math.Round((cf.ClosingBalance + receivables) / cf.LiquidityAnalysis.AverageMonthlyOutflow, 1);
        }
        r.CurrentRatio = r.QuickRatio;

        // كفاءة
        r.RevenuePerInvoice = income.AverageInvoiceValue;

        // نسبة التحصيل
        var totalInvoiced = await _db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Sale
                && t.InvoiceStatus != "Cancelled"
                && t.TransactionDate >= dto.FromDate
                && t.TransactionDate <= dto.ToDate)
            .SumAsync(t => (decimal?)t.GrandTotal) ?? 0;
        var totalCollected = await _db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Sale
                && t.InvoiceStatus != "Cancelled"
                && t.TransactionDate >= dto.FromDate
                && t.TransactionDate <= dto.ToDate)
            .SumAsync(t => (decimal?)t.PaidAmount) ?? 0;
        r.CollectionRate = totalInvoiced == 0 ? 0
            : Math.Round((totalCollected / totalInvoiced) * 100, 1);

        // معدلات النمو
        r.RevenueGrowthRate = income.PreviousPeriod?.RevenueChange ?? 0;
        r.ExpenseGrowthRate = income.PreviousPeriod?.ExpensesChange ?? 0;
    }

    private async Task<decimal> GetTotalCustomerReceivablesAsync()
    {
        return await _db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Sale
                && t.InvoiceStatus != "Cancelled"
                && t.GrandTotal > t.PaidAmount)
            .SumAsync(t => (decimal?)(t.GrandTotal - t.PaidAmount)) ?? 0;
    }

    // ============================================================
    //  ⭐ مؤشر الصحة المالية الكلية (0-100)
    // ============================================================
    private void BuildHealthScore(FinancialDashboardDto dto, IncomeStatementDto income, CashFlowStatementDto cf)
    {
        var hs = dto.HealthScore;
        var factors = new List<HealthFactorDto>();

        // 1. الربحية (25 درجة)
        int profitabilityScore;
        if (income.NetProfitMargin >= 20) profitabilityScore = 25;
        else if (income.NetProfitMargin >= 15) profitabilityScore = 22;
        else if (income.NetProfitMargin >= 10) profitabilityScore = 18;
        else if (income.NetProfitMargin >= 5) profitabilityScore = 12;
        else if (income.NetProfitMargin > 0) profitabilityScore = 6;
        else profitabilityScore = 0;

        factors.Add(new HealthFactorDto
        {
            Name = "الربحية",
            Score = profitabilityScore,
            MaxScore = 25,
            Description = $"هامش صافي الربح: {income.NetProfitMargin:N1}%",
            Color = GetScoreColor(profitabilityScore, 25),
            Icon = "TrendingUp"
        });

        // 2. السيولة (25 درجة)
        int liquidityScore;
        if (cf.LiquidityAnalysis.LiquidityRatio >= 6) liquidityScore = 25;
        else if (cf.LiquidityAnalysis.LiquidityRatio >= 3) liquidityScore = 20;
        else if (cf.LiquidityAnalysis.LiquidityRatio >= 1) liquidityScore = 12;
        else if (cf.LiquidityAnalysis.LiquidityRatio > 0) liquidityScore = 5;
        else liquidityScore = 0;

        factors.Add(new HealthFactorDto
        {
            Name = "السيولة",
            Score = liquidityScore,
            MaxScore = 25,
            Description = $"تغطية {cf.LiquidityAnalysis.LiquidityRatio} شهر",
            Color = GetScoreColor(liquidityScore, 25),
            Icon = "WaterDrop"
        });

        // 3. النمو (20 درجة)
        int growthScore = 10; // متعادل افتراضياً
        if (income.PreviousPeriod != null)
        {
            if (income.PreviousPeriod.RevenueChange >= 20) growthScore = 20;
            else if (income.PreviousPeriod.RevenueChange >= 10) growthScore = 16;
            else if (income.PreviousPeriod.RevenueChange >= 5) growthScore = 13;
            else if (income.PreviousPeriod.RevenueChange >= 0) growthScore = 10;
            else if (income.PreviousPeriod.RevenueChange >= -10) growthScore = 5;
            else growthScore = 0;
        }

        factors.Add(new HealthFactorDto
        {
            Name = "النمو",
            Score = growthScore,
            MaxScore = 20,
            Description = income.PreviousPeriod != null
                ? $"تغير الإيرادات: {(income.PreviousPeriod.RevenueChange >= 0 ? "+" : "")}{income.PreviousPeriod.RevenueChange:N1}%"
                : "لا توجد بيانات للمقارنة",
            Color = GetScoreColor(growthScore, 20),
            Icon = "ShowChart"
        });

        // 4. الكفاءة (15 درجة)
        int efficiencyScore;
        var expenseRatio = dto.Ratios.OperatingExpenseRatio;
        if (expenseRatio <= 30) efficiencyScore = 15;
        else if (expenseRatio <= 50) efficiencyScore = 12;
        else if (expenseRatio <= 70) efficiencyScore = 8;
        else if (expenseRatio <= 90) efficiencyScore = 4;
        else efficiencyScore = 0;

        factors.Add(new HealthFactorDto
        {
            Name = "الكفاءة التشغيلية",
            Score = efficiencyScore,
            MaxScore = 15,
            Description = $"نسبة المصروفات: {expenseRatio:N1}%",
            Color = GetScoreColor(efficiencyScore, 15),
            Icon = "Speed"
        });

        // 5. التحصيل (15 درجة)
        int collectionScore;
        var collectionRate = dto.Ratios.CollectionRate;
        if (collectionRate >= 90) collectionScore = 15;
        else if (collectionRate >= 75) collectionScore = 12;
        else if (collectionRate >= 60) collectionScore = 8;
        else if (collectionRate >= 40) collectionScore = 4;
        else collectionScore = 0;

        factors.Add(new HealthFactorDto
        {
            Name = "كفاءة التحصيل",
            Score = collectionScore,
            MaxScore = 15,
            Description = $"نسبة التحصيل: {collectionRate:N1}%",
            Color = GetScoreColor(collectionScore, 15),
            Icon = "Payments"
        });

        // الإجمالي
        hs.Factors = factors;
        hs.OverallScore = factors.Sum(f => f.Score);

        // Grade & Status
        if (hs.OverallScore >= 90)
        {
            hs.Grade = "A+";
            hs.Status = "ممتاز";
            hs.Color = "#10b981";
            hs.Description = "🌟 الصحة المالية ممتازة! استمر على هذا المستوى وفكر في التوسع.";
        }
        else if (hs.OverallScore >= 80)
        {
            hs.Grade = "A";
            hs.Status = "ممتاز";
            hs.Color = "#10b981";
            hs.Description = "🟢 الصحة المالية قوية وفي المسار الصحيح.";
        }
        else if (hs.OverallScore >= 70)
        {
            hs.Grade = "B+";
            hs.Status = "جيد جداً";
            hs.Color = "#22c55e";
            hs.Description = "🟢 وضع جيد جداً مع مجال للتحسين في بعض المؤشرات.";
        }
        else if (hs.OverallScore >= 60)
        {
            hs.Grade = "B";
            hs.Status = "جيد";
            hs.Color = "#84cc16";
            hs.Description = "🟡 وضع جيد، لكن يحتاج لتحسين بعض المؤشرات للوصول للمستوى الممتاز.";
        }
        else if (hs.OverallScore >= 50)
        {
            hs.Grade = "C";
            hs.Status = "متوسط";
            hs.Color = "#f59e0b";
            hs.Description = "🟡 وضع متوسط - يحتاج عمل على تحسين الربحية والسيولة.";
        }
        else if (hs.OverallScore >= 35)
        {
            hs.Grade = "D";
            hs.Status = "ضعيف";
            hs.Color = "#fb923c";
            hs.Description = "🟠 وضع ضعيف - يحتاج تدخل سريع لتحسين الأداء المالي.";
        }
        else
        {
            hs.Grade = "F";
            hs.Status = "حرج";
            hs.Color = "#ef4444";
            hs.Description = "🔴 وضع حرج! يجب اتخاذ إجراءات فورية لتحسين الصحة المالية.";
        }
    }

    private string GetScoreColor(int score, int maxScore)
    {
        var pct = (double)score / maxScore * 100;
        if (pct >= 80) return "#10b981";
        if (pct >= 60) return "#84cc16";
        if (pct >= 40) return "#f59e0b";
        if (pct >= 20) return "#fb923c";
        return "#ef4444";
    }

    // ============================================================
    //  المقارنات
    // ============================================================
    private void BuildComparisons(FinancialDashboardDto dto, IncomeStatementDto income)
    {
        if (income.PreviousPeriod != null)
        {
            dto.VsPreviousPeriod = MapComparison(income.PreviousPeriod);
        }
        if (income.PreviousYear != null)
        {
            dto.VsLastYear = MapComparison(income.PreviousYear);
        }
    }

    private PeriodComparisonDto MapComparison(IncomeComparisonDto src)
    {
        var trend = src.NetProfitChange > 5 ? "نمو"
                  : src.NetProfitChange < -5 ? "تراجع" : "ثابت";

        return new PeriodComparisonDto
        {
            Label = src.Label,
            Revenue = src.Revenue,
            Expenses = src.Expenses,
            NetProfit = src.NetProfit,
            RevenueChange = src.RevenueChange,
            ExpensesChange = src.ExpensesChange,
            NetProfitChange = src.NetProfitChange,
            OverallTrend = trend,
            TrendIcon = trend switch { "نمو" => "📈", "تراجع" => "📉", _ => "➡️" }
        };
    }

    // ============================================================
    //  الترند الموحّد (12 شهر)
    // ============================================================
    private async Task BuildUnifiedMonthlyTrendAsync(FinancialDashboardDto dto)
    {
        var endDate = DateTime.Today;
        var startDate = endDate.AddMonths(-11);
        startDate = new DateTime(startDate.Year, startDate.Month, 1);

        var months = new List<UnifiedMonthlyDto>();
        var current = startDate;
        decimal runningBalance = 0;

        // Opening balance قبل البداية
        runningBalance = await _db.CashboxTransactions.AsNoTracking()
            .Where(t => t.TransactionDate < startDate
                && (t.TransactionType == "قبض" || t.TransactionType == "In"))
            .SumAsync(t => (decimal?)t.Amount) ?? 0;
        runningBalance -= await _db.CashboxTransactions.AsNoTracking()
            .Where(t => t.TransactionDate < startDate
                && (t.TransactionType == "صرف" || t.TransactionType == "Out"))
            .SumAsync(t => (decimal?)t.Amount) ?? 0;

        while (current <= endDate)
        {
            var monthEnd = current.AddMonths(1).AddDays(-1).Date.AddDays(1).AddTicks(-1);

            // إيرادات
            var revenue = await _db.Transactions.AsNoTracking()
                .Where(t => t.TransactionType == TransactionTypes.Sale
                    && t.InvoiceStatus != "Cancelled"
                    && t.TransactionDate >= current && t.TransactionDate <= monthEnd)
                .SumAsync(t => (decimal?)(t.NetTotalAmount ?? t.GrandTotal)) ?? 0;

            // COGS
            var cogs = await (
                from t in _db.Transactions.AsNoTracking()
                join d in _db.TransactionDetails.AsNoTracking() on t.TransactionId equals d.TransactionId
                join p in _db.Products.AsNoTracking() on d.ProductId equals p.ProductId
                where t.TransactionType == TransactionTypes.Sale
                    && t.InvoiceStatus != "Cancelled"
                    && t.TransactionDate >= current && t.TransactionDate <= monthEnd
                select d.Quantity * (p.PurchasePrice ?? 0)
            ).SumAsync(x => (decimal?)x) ?? 0;

            // مصروفات
            var expenses = await _db.Expenses.AsNoTracking()
                .Where(e => e.ExpenseDate >= current && e.ExpenseDate <= monthEnd
                    && ((e.IsAdvance != true) || (e.AdvanceParentExpenseId.HasValue)))
                .SumAsync(e => (decimal?)e.Amount) ?? 0;

            // تدفقات نقدية
            var cashIn = await _db.CashboxTransactions.AsNoTracking()
                .Where(t => (t.TransactionType == "قبض" || t.TransactionType == "In")
                    && t.TransactionDate >= current && t.TransactionDate <= monthEnd)
                .SumAsync(t => (decimal?)t.Amount) ?? 0;
            var cashOut = await _db.CashboxTransactions.AsNoTracking()
                .Where(t => (t.TransactionType == "صرف" || t.TransactionType == "Out")
                    && t.TransactionDate >= current && t.TransactionDate <= monthEnd)
                .SumAsync(t => (decimal?)t.Amount) ?? 0;

            var netCashFlow = cashIn - cashOut;
            runningBalance += netCashFlow;
            var netProfit = revenue - cogs - expenses;

            months.Add(new UnifiedMonthlyDto
            {
                Month = current,
                MonthLabel = current.ToString("yyyy/MM"),
                Revenue = revenue,
                Cogs = cogs,
                Expenses = expenses,
                NetProfit = netProfit,
                NetProfitMargin = revenue == 0 ? 0 : Math.Round((netProfit / revenue) * 100, 1),
                CashInflows = cashIn,
                CashOutflows = cashOut,
                NetCashFlow = netCashFlow,
                CashBalance = runningBalance
            });

            current = current.AddMonths(1);
        }

        dto.Last12Months = months;
    }

    // ============================================================
    //  أعلى المنتجات
    // ============================================================
    private async Task GetTopProductsAsync(FinancialDashboardDto dto)
    {
        var data = await (
            from t in _db.Transactions.AsNoTracking()
            join d in _db.TransactionDetails.AsNoTracking() on t.TransactionId equals d.TransactionId
            join p in _db.Products.AsNoTracking() on d.ProductId equals p.ProductId
            where t.TransactionType == TransactionTypes.Sale
                && t.InvoiceStatus != "Cancelled"
                && t.TransactionDate >= dto.FromDate
                && t.TransactionDate <= dto.ToDate
            group new { d, p, t } by new { p.ProductId, p.ProductName } into g
            select new
            {
                ProductId = g.Key.ProductId,
                ProductName = g.Key.ProductName,
                QuantitySold = g.Sum(x => (int)x.d.Quantity),
                Revenue = g.Sum(x => x.d.Quantity * x.d.UnitPrice),
                Cogs = g.Sum(x => x.d.Quantity * (x.p.PurchasePrice ?? 0)),
                OrdersCount = g.Select(x => x.t.TransactionId).Distinct().Count()
            }
        ).OrderByDescending(x => x.Revenue).Take(10).ToListAsync();

        dto.TopProducts = data.Select(d => new TopProductDto
        {
            ProductId = d.ProductId,
            ProductName = d.ProductName,
            QuantitySold = d.QuantitySold,
            Revenue = d.Revenue,
            Cogs = d.Cogs,
            Profit = d.Revenue - d.Cogs,
            ProfitMargin = d.Revenue == 0 ? 0 : Math.Round(((d.Revenue - d.Cogs) / d.Revenue) * 100, 1),
            OrdersCount = d.OrdersCount
        }).ToList();
    }

    // ============================================================
    //  أعلى العملاء
    // ============================================================
    private async Task GetTopCustomersAsync(FinancialDashboardDto dto)
    {
        var data = await (
            from t in _db.Transactions.AsNoTracking()
            join p in _db.Parties.AsNoTracking() on t.PartyId equals p.PartyId
            where t.TransactionType == TransactionTypes.Sale
                && t.InvoiceStatus != "Cancelled"
                && t.TransactionDate >= dto.FromDate
                && t.TransactionDate <= dto.ToDate
            group new { t, p } by new { p.PartyId, p.PartyName, p.Phone } into g
            select new
            {
                PartyId = g.Key.PartyId,
                CustomerName = g.Key.PartyName,
                Phone = g.Key.Phone,
                InvoicesCount = g.Count(),
                TotalRevenue = g.Sum(x => x.t.GrandTotal),
                TotalPaid = g.Sum(x => x.t.PaidAmount)
            }
        ).OrderByDescending(x => x.TotalRevenue).Take(10).ToListAsync();

        dto.TopCustomers = data.Select(d => new TopCustomerDto
        {
            PartyId = d.PartyId,
            CustomerName = d.CustomerName,
            Phone = d.Phone,
            InvoicesCount = d.InvoicesCount,
            TotalRevenue = d.TotalRevenue,
            TotalPaid = d.TotalPaid,
            Remaining = d.TotalRevenue - d.TotalPaid,
            AverageInvoiceValue = d.InvoicesCount == 0 ? 0
                : Math.Round(d.TotalRevenue / d.InvoicesCount, 2)
        }).ToList();
    }

    // ============================================================
    //  تصنيفات المصروفات
    // ============================================================
    private void BuildExpenseCategories(FinancialDashboardDto dto, IncomeStatementDto income)
    {
        dto.TopExpenseCategories = income.ExpensesByGroup
            .Take(8)
            .Select(g => new ExpenseCategoryDto
            {
                GroupName = g.GroupName,
                Amount = g.Amount,
                Percentage = g.Percentage,
                Color = g.Color ?? "#94a3b8"
            }).ToList();
    }

    // ============================================================
    //  ملخص الذمم
    // ============================================================
    private async Task BuildReceivablesSummaryAsync(FinancialDashboardDto dto)
    {
        var r = dto.Receivables;

        // مستحقات على العملاء (فواتير لم تُسدد)
        var customerData = await _db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Sale
                && t.InvoiceStatus != "Cancelled"
                && t.GrandTotal > t.PaidAmount)
            .Select(t => new { t.GrandTotal, t.PaidAmount, t.PartyId, t.DueDate })
            .ToListAsync();

        r.CustomerReceivables = customerData.Sum(c => c.GrandTotal - c.PaidAmount);
        r.CustomersWithDebtCount = customerData.Select(c => c.PartyId).Distinct().Count();
        r.OverdueInvoicesCount = customerData
            .Count(c => c.DueDate.HasValue && c.DueDate.Value < DateTime.Today);

        // مستحقات للموردين (فواتير شراء لم تُسدد - باستثناء الـ Mirror)
        r.SupplierPayables = await _db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Purchase
                && t.InvoiceStatus != "Cancelled"
                && t.PartyId != SystemConstants.DefaultSupplierId
                && t.GrandTotal > t.PaidAmount)
            .SumAsync(t => (decimal?)(t.GrandTotal - t.PaidAmount)) ?? 0;

        // الذمم الشخصية
        var personalAccounts = await _db.PersonalAccounts.AsNoTracking().ToListAsync();
        decimal credit = 0, debit = 0;

        foreach (var acc in personalAccounts)
        {
            var loansIn = await _db.CashboxTransactions.AsNoTracking()
                .Where(t => t.ReferenceType == CashBoxRefTypes.Loan
                    && t.ReferenceId == acc.PersonalAccountId
                    && (t.TransactionType == "قبض" || t.TransactionType == "In"))
                .SumAsync(t => (decimal?)t.Amount) ?? 0;
            var loansOut = await _db.CashboxTransactions.AsNoTracking()
                .Where(t => t.ReferenceType == CashBoxRefTypes.Loan
                    && t.ReferenceId == acc.PersonalAccountId
                    && (t.TransactionType == "صرف" || t.TransactionType == "Out"))
                .SumAsync(t => (decimal?)t.Amount) ?? 0;

            var openingDebit = acc.OpeningType == "Debit" ? acc.OpeningBalance : 0;
            var openingCredit = acc.OpeningType == "Credit" ? acc.OpeningBalance : 0;
            var balance = (openingCredit - openingDebit) + (loansIn - loansOut);

            if (balance > 0) credit += balance;
            else if (balance < 0) debit += Math.Abs(balance);
        }

        r.PersonalAccountsCredit = credit;
        r.PersonalAccountsDebit = debit;
    }

    // ============================================================
    //  ⭐ توحيد التنبيهات من كل المصادر
    // ============================================================
    private void ConsolidateAlerts(FinancialDashboardDto dto, IncomeStatementDto income, CashFlowStatementDto cf)
    {
        var alerts = new List<DashboardAlertDto>();

        // من قائمة الدخل
        foreach (var rec in income.Recommendations.Take(5))
        {
            alerts.Add(new DashboardAlertDto
            {
                Type = rec.Type,
                Icon = rec.Icon,
                Title = rec.Title,
                Description = rec.Description,
                ActionUrl = "/reports/income-statement",
                ActionLabel = "عرض القائمة الكاملة",
                Priority = rec.Priority,
                Color = rec.Color,
                Category = "Income"
            });
        }

        // من التدفقات النقدية
        foreach (var alert in cf.Alerts.Take(5))
        {
            alerts.Add(new DashboardAlertDto
            {
                Type = alert.Type,
                Icon = alert.Icon,
                Title = alert.Title,
                Description = alert.Description,
                ActionUrl = "/reports/cash-flow",
                ActionLabel = "عرض التدفقات",
                Priority = alert.Priority + 100, // أقل أولوية من قائمة الدخل
                Color = alert.Color,
                Category = "CashFlow"
            });
        }

        // تنبيهات الذمم
        if (dto.Receivables.OverdueInvoicesCount > 0)
        {
            alerts.Add(new DashboardAlertDto
            {
                Type = "Warning",
                Icon = "🚨",
                Title = $"{dto.Receivables.OverdueInvoicesCount} فاتورة متأخرة",
                Description = $"يوجد {dto.Receivables.OverdueInvoicesCount} فاتورة تجاوزت تاريخ الاستحقاق. تابع التحصيل.",
                ActionUrl = "/sales/invoices",
                ActionLabel = "عرض الفواتير",
                Priority = 50,
                Color = "#f59e0b",
                Category = "Receivables"
            });
        }

        if (dto.Receivables.CustomerReceivables > dto.CashFlowSummary.CurrentBalance)
        {
            alerts.Add(new DashboardAlertDto
            {
                Type = "Info",
                Icon = "💰",
                Title = "مستحقات العملاء أعلى من الرصيد النقدي",
                Description = $"لديك {dto.Receivables.CustomerReceivables:N2} ج مستحقات. تركيز على التحصيل سيحسّن السيولة.",
                ActionUrl = "/sales/invoices",
                ActionLabel = "عرض المستحقات",
                Priority = 60,
                Color = "#3b82f6",
                Category = "Receivables"
            });
        }

        dto.Alerts = alerts.OrderBy(a => a.Priority).Take(10).ToList();
    }

    // ============================================================
    //  ⭐ التوصيات الاستراتيجية
    // ============================================================
    private void GenerateStrategicRecommendations(FinancialDashboardDto dto, IncomeStatementDto income, CashFlowStatementDto cf)
    {
        var recs = new List<StrategicRecommendationDto>();

        // 1. تحسين الربحية
        if (income.NetProfitMargin < 10 && income.NetRevenue > 0)
        {
            recs.Add(new StrategicRecommendationDto
            {
                Title = "تحسين هامش الربح",
                Description = $"هامش الربح الحالي {income.NetProfitMargin:N1}% أقل من الهدف 10%. " +
                              "ركّز على تقليل تكلفة المبيعات أو زيادة الأسعار.",
                Impact = "High",
                Difficulty = "Medium",
                ExpectedOutcome = "زيادة صافي الربح بـ 30-50%",
                Icon = "📈",
                Color = "#10b981",
                ActionSteps = new List<string>
                {
                    "راجع أسعار الشراء مع الموردين الحاليين",
                    "ابحث عن موردين بديلين بأسعار أفضل",
                    "ارفع أسعار البيع للمنتجات الأقل حساسية للسعر",
                    "قلل المصروفات التشغيلية غير الضرورية"
                }
            });
        }

        // 2. زيادة السيولة
        if (cf.LiquidityAnalysis.LiquidityRatio < 3)
        {
            recs.Add(new StrategicRecommendationDto
            {
                Title = "تعزيز السيولة النقدية",
                Description = $"السيولة الحالية تكفي {cf.LiquidityAnalysis.LiquidityRatio} شهر فقط. " +
                              "اهدف للوصول لـ 3-6 شهور كحد آمن.",
                Impact = "High",
                Difficulty = "Medium",
                ExpectedOutcome = "حماية الشركة من المخاطر المالية",
                Icon = "💧",
                Color = "#06b6d4",
                ActionSteps = new List<string>
                {
                    "فعّل سياسة تحصيل أسرع للفواتير",
                    "تفاوض مع الموردين على فترات سداد أطول",
                    "أجّل المصروفات الاستثمارية غير الضرورية",
                    "فكر في خط ائتمان احتياطي من البنك"
                }
            });
        }

        // 3. تنويع مصادر الدخل
        if (dto.TopProducts.Any() && dto.TopProducts.First().Revenue > income.NetRevenue * 0.5m)
        {
            recs.Add(new StrategicRecommendationDto
            {
                Title = "تنويع مصادر الإيرادات",
                Description = $"المنتج الرئيسي يمثل أكثر من 50% من الإيرادات. " +
                              "الاعتماد على منتج واحد يشكّل مخاطرة.",
                Impact = "Medium",
                Difficulty = "Hard",
                ExpectedOutcome = "تقليل المخاطر وزيادة الإيرادات بـ 20-40%",
                Icon = "🎯",
                Color = "#8b5cf6",
                ActionSteps = new List<string>
                {
                    "حدّد المنتجات الأكثر طلباً ولكن أقل ترويجاً",
                    "طوّر منتجات جديدة مكملة",
                    "وسّع قاعدة العملاء جغرافياً أو في قطاعات جديدة",
                    "ادرس فرص الشراكات والتعاون"
                }
            });
        }

        // 4. تحسين التحصيل
        if (dto.Ratios.CollectionRate < 75 && dto.Receivables.CustomerReceivables > 0)
        {
            recs.Add(new StrategicRecommendationDto
            {
                Title = "تحسين كفاءة التحصيل",
                Description = $"نسبة التحصيل {dto.Ratios.CollectionRate:N1}% منخفضة. " +
                              $"لديك {dto.Receivables.CustomerReceivables:N2} ج مستحقات معلقة.",
                Impact = "High",
                Difficulty = "Easy",
                ExpectedOutcome = "تحرير سيولة فورية",
                Icon = "💸",
                Color = "#f59e0b",
                ActionSteps = new List<string>
                {
                    "اتصل بكل العملاء أصحاب الفواتير المتأخرة",
                    "اطلب دفعات مقدمة على الطلبات الكبيرة",
                    "اعرض خصم 2% للسداد المبكر",
                    "حدّد سقف ائتمان لكل عميل حسب تاريخه"
                }
            });
        }

        // 5. خفض المصروفات
        if (dto.TopExpenseCategories.Any() && dto.TopExpenseCategories.First().Percentage > 40)
        {
            var biggest = dto.TopExpenseCategories.First();
            recs.Add(new StrategicRecommendationDto
            {
                Title = $"مراجعة مصروفات {biggest.GroupName}",
                Description = $"تمثل {biggest.Percentage}% من إجمالي المصروفات " +
                              $"({biggest.Amount:N2} ج). فرصة كبيرة للتوفير.",
                Impact = "Medium",
                Difficulty = "Medium",
                ExpectedOutcome = "توفير 10-25% من هذا البند",
                Icon = "📊",
                Color = "#ef4444",
                ActionSteps = new List<string>
                {
                    "حلل تفاصيل المصروفات في هذه المجموعة",
                    "ابحث عن بنود غير ضرورية",
                    "تفاوض على عقود أفضل",
                    "ضع حد أقصى شهري للإنفاق"
                }
            });
        }

        // 6. الاستثمار في النمو (لو الوضع ممتاز)
        if (income.NetProfitMargin >= 15 && cf.LiquidityAnalysis.LiquidityRatio >= 3
            && income.PreviousPeriod?.RevenueChange > 10)
        {
            recs.Add(new StrategicRecommendationDto
            {
                Title = "وقت الاستثمار في النمو 🚀",
                Description = "أداء مالي ممتاز - الوقت مناسب للاستثمار في توسيع النشاط.",
                Impact = "High",
                Difficulty = "Hard",
                ExpectedOutcome = "نمو 50-100% خلال سنة",
                Icon = "🚀",
                Color = "#10b981",
                ActionSteps = new List<string>
                {
                    "زد الاستثمار في التسويق والإعلان",
                    "وظّف موظفين إضافيين للمبيعات",
                    "افتح فرع جديد أو وسّع المنطقة الجغرافية",
                    "استثمر في تطوير المنتجات الجديدة"
                }
            });
        }

        dto.StrategicRecommendations = recs;
    }

    // ============================================================
    //  Helpers
    // ============================================================
    private string GetTrendIcon(decimal change, bool inverse = false)
    {
        if (Math.Abs(change) < 1) return "➡️";
        var positive = inverse ? change < 0 : change > 0;
        return positive ? "📈" : "📉";
    }

    private string BuildPeriodLabel(FinancialDashboardFilterDto filter)
    {
        return filter.PeriodType switch
        {
            "Today" => $"اليوم ({DateTime.Today:yyyy/MM/dd})",
            "Week" => $"هذا الأسبوع",
            "Month" => $"شهر {filter.FromDate:yyyy/MM}",
            "Quarter" => "هذا الربع",
            "Year" => $"سنة {filter.FromDate.Year}",
            _ => $"من {filter.FromDate:yyyy/MM/dd} إلى {filter.ToDate:yyyy/MM/dd}"
        };
    }
}
