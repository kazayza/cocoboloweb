using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class FinancialReportsService : IFinancialReportsService
{
    private readonly db24804Context _db;

    public FinancialReportsService(db24804Context db)
    {
        _db = db;
    }

    // ============================================================
    //  ⭐ قائمة الدخل الكاملة (الـ Method الرئيسية)
    // ============================================================
    public async Task<IncomeStatementDto> GetIncomeStatementAsync(IncomeStatementFilterDto filter)
    {
        var dto = new IncomeStatementDto
        {
            FromDate = filter.FromDate.Date,
            ToDate = filter.ToDate.Date.AddDays(1).AddTicks(-1),
            PeriodLabel = BuildPeriodLabel(filter)
        };

        // ────────────── 1. حساب الإيرادات ──────────────
        await CalculateRevenueAsync(dto);

        // ────────────── 2. حساب تكلفة المبيعات (COGS) ──────────────
        await CalculateCogsAsync(dto);

        // ────────────── 3. الربح الإجمالي ──────────────
        dto.GrossProfit = dto.NetRevenue - dto.CostOfGoodsSold;
        dto.GrossProfitMargin = dto.NetRevenue == 0 ? 0
            : Math.Round((dto.GrossProfit / dto.NetRevenue) * 100, 2);

        // ────────────── 4. المصروفات التشغيلية ──────────────
        await CalculateExpensesAsync(dto);

        // ────────────── 5. صافي الربح ──────────────
        dto.NetProfit = dto.GrossProfit - dto.TotalOperatingExpenses;
        dto.NetProfitMargin = dto.NetRevenue == 0 ? 0
            : Math.Round((dto.NetProfit / dto.NetRevenue) * 100, 2);

        dto.ProfitStatus = dto.NetProfit > 0 ? "ربح"
                         : dto.NetProfit < 0 ? "خسارة" : "تعادل";

        // ────────────── 6. نقطة التعادل ──────────────
        CalculateBreakEven(dto);

        // ────────────── 7. الهدف الديناميكي ──────────────
        CalculateProfitTarget(dto);

        // ────────────── 8. أعلى المصروفات ──────────────
        await GetTopExpensesAsync(dto);

        // ────────────── 9. المقارنات (اختياري) ──────────────
        if (filter.IncludeComparison)
        {
            await GetPreviousPeriodComparisonAsync(dto, filter);
        }

        // ────────────── 10. الترند الشهري (اختياري) ──────────────
        if (filter.IncludeMonthlyTrend)
        {
            await GetMonthlyTrendAsync(dto);
        }

        // ────────────── 11. التوصيات الذكية ──────────────
        GenerateSmartRecommendations(dto);

        return dto;
    }

    public async Task<decimal> GetNetProfitMarginAsync(DateTime from, DateTime to)
    {
        var filter = new IncomeStatementFilterDto
        {
            FromDate = from, ToDate = to,
            IncludeComparison = false,
            IncludeMonthlyTrend = false
        };
        var stmt = await GetIncomeStatementAsync(filter);
        return stmt.NetProfitMargin;
    }

    // ============================================================
    //  حسابات الإيرادات
    // ============================================================
    private async Task CalculateRevenueAsync(IncomeStatementDto dto)
    {
        var sales = await _db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Sale
                && t.InvoiceStatus != "Cancelled"
                && t.TransactionDate >= dto.FromDate
                && t.TransactionDate <= dto.ToDate)
            .Select(t => new
            {
                t.GrandTotal,
                t.NetTotalAmount,
                t.DiscountAmount
            })
            .ToListAsync();

        dto.TotalRevenue = sales.Sum(s => s.GrandTotal);
        dto.NetRevenue = sales.Sum(s => s.NetTotalAmount ?? s.GrandTotal);
        dto.InvoicesCount = sales.Count;
        dto.AverageInvoiceValue = dto.InvoicesCount == 0 ? 0
            : Math.Round(dto.TotalRevenue / dto.InvoicesCount, 2);
    }

    // ============================================================
    //  حساب تكلفة المبيعات (COGS)
    // ============================================================
    private async Task CalculateCogsAsync(IncomeStatementDto dto)
    {
        // جمع تكلفة المنتجات المباعة في الفترة
        var cogs = await (
            from t in _db.Transactions.AsNoTracking()
            join d in _db.TransactionDetails.AsNoTracking() on t.TransactionId equals d.TransactionId
            join p in _db.Products.AsNoTracking() on d.ProductId equals p.ProductId
            where t.TransactionType == TransactionTypes.Sale
                && t.InvoiceStatus != "Cancelled"
                && t.TransactionDate >= dto.FromDate
                && t.TransactionDate <= dto.ToDate
            select new
            {
                Quantity = d.Quantity,
                Price = p.PurchasePrice ?? 0
            }
        ).ToListAsync();

        dto.CostOfGoodsSold = cogs.Sum(c => c.Quantity * c.Price);
    }

    // ============================================================
    //  حساب المصروفات التشغيلية
    // ============================================================
    private async Task CalculateExpensesAsync(IncomeStatementDto dto)
    {
        // ⭐ ملاحظة مهمة: للمصروفات المقدمة، نحسب كل شهر فرعي على حدة
        // (الأشهر الفرعية تمثل المصروف الشهري الفعلي)

        var expenses = await _db.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDate >= dto.FromDate
                && e.ExpenseDate <= dto.ToDate
                // نأخذ:
                // - المصروفات العادية (مش مقدمة)
                // - الأشهر الفرعية للمصروف المقدم (هي اللي بتمثل المصروف الفعلي)
                && (
                    (e.IsAdvance != true)
                    || (e.AdvanceParentExpenseId.HasValue) // الأشهر الفرعية
                ))
            .Select(e => new
            {
                e.ExpenseId,
                e.Amount,
                e.ExpenseGroupId
            })
            .ToListAsync();

        dto.TotalOperatingExpenses = expenses.Sum(e => e.Amount);

        // تجميع بالـ ExpenseGroup
        var groupTotals = expenses
            .GroupBy(e => e.ExpenseGroupId)
            .Select(g => new { GroupId = g.Key, Total = g.Sum(x => x.Amount), Count = g.Count() })
            .ToList();

        if (groupTotals.Any())
        {
            var groupIds = groupTotals.Select(g => g.GroupId).ToList();
            var groups = await _db.ExpenseGroups.AsNoTracking()
                .Where(g => groupIds.Contains(g.ExpenseGroupId))
                .ToDictionaryAsync(g => g.ExpenseGroupId, g => g.ExpenseGroupName);

            var totalForPct = dto.TotalOperatingExpenses == 0 ? 1 : dto.TotalOperatingExpenses;

            dto.ExpensesByGroup = groupTotals
                .Select((g, idx) => new ExpenseGroupBreakdownDto
                {
                    GroupId = g.GroupId,
                    GroupName = groups.GetValueOrDefault(g.GroupId, "غير محدد"),
                    Amount = g.Total,
                    Percentage = Math.Round((g.Total / totalForPct) * 100, 1),
                    Count = g.Count,
                    Color = GetColorForIndex(idx)
                })
                .OrderByDescending(g => g.Amount)
                .ToList();
        }
    }

    // ============================================================
    //  أعلى المصروفات
    // ============================================================
    private async Task GetTopExpensesAsync(IncomeStatementDto dto)
    {
        var topExpenses = await (
            from e in _db.Expenses.AsNoTracking()
            where e.ExpenseDate >= dto.FromDate
                && e.ExpenseDate <= dto.ToDate
                && ((e.IsAdvance != true) || (e.AdvanceParentExpenseId.HasValue))
            orderby e.Amount descending
            select new { e.ExpenseId, e.ExpenseName, e.Amount, e.ExpenseDate, e.ExpenseGroupId }
        ).Take(10).ToListAsync();

        var groupIds = topExpenses.Select(e => e.ExpenseGroupId).Distinct().ToList();
        var groups = await _db.ExpenseGroups.AsNoTracking()
            .Where(g => groupIds.Contains(g.ExpenseGroupId))
            .ToDictionaryAsync(g => g.ExpenseGroupId, g => g.ExpenseGroupName);

        var totalForPct = dto.TotalOperatingExpenses == 0 ? 1 : dto.TotalOperatingExpenses;

        dto.TopExpenses = topExpenses.Select(e => new TopExpenseDto
        {
            ExpenseId = e.ExpenseId,
            ExpenseName = e.ExpenseName,
            GroupName = groups.GetValueOrDefault(e.ExpenseGroupId),
            Amount = e.Amount,
            PercentageOfTotal = Math.Round((e.Amount / totalForPct) * 100, 1),
            Date = e.ExpenseDate
        }).ToList();
    }

    // ============================================================
    //  ⭐ نقطة التعادل (Break-Even)
    // ============================================================
    private void CalculateBreakEven(IncomeStatementDto dto)
    {
        var be = dto.BreakEvenAnalysis;
        be.FixedExpenses = dto.TotalOperatingExpenses;
        be.GrossProfitMargin = dto.GrossProfitMargin;
        be.CurrentRevenue = dto.NetRevenue;

        if (dto.GrossProfitMargin > 0)
        {
            // نقطة التعادل = المصروفات الثابتة / نسبة الربح الإجمالي
            be.BreakEvenRevenue = Math.Round(be.FixedExpenses / (be.GrossProfitMargin / 100m), 2);
        }
        else
        {
            be.BreakEvenRevenue = be.FixedExpenses; // غير قابل للحساب الدقيق
        }

        be.RevenueGap = be.CurrentRevenue - be.BreakEvenRevenue;

        if (be.BreakEvenRevenue > 0)
        {
            be.SafetyMargin = Math.Round(((be.CurrentRevenue - be.BreakEvenRevenue) / be.BreakEvenRevenue) * 100, 1);
        }

        // تحديد الحالة
        if (be.CurrentRevenue >= be.BreakEvenRevenue * 1.5m)
        {
            be.Status = "آمن جداً";
            be.StatusIcon = "🟢";
            be.Description = $"إيراداتك أعلى من نقطة التعادل بمقدار {Math.Abs(be.RevenueGap):N2} ج (+{be.SafetyMargin}%) — وضع ممتاز!";
        }
        else if (be.CurrentRevenue >= be.BreakEvenRevenue)
        {
            be.Status = "آمن";
            be.StatusIcon = "🟢";
            be.Description = $"تجاوزت نقطة التعادل بـ {Math.Abs(be.RevenueGap):N2} ج. هامش الأمان: {be.SafetyMargin}%";
        }
        else if (be.CurrentRevenue >= be.BreakEvenRevenue * 0.8m)
        {
            be.Status = "حرج";
            be.StatusIcon = "🟡";
            be.Description = $"تحتاج {Math.Abs(be.RevenueGap):N2} ج إيراد إضافي للوصول لنقطة التعادل";
        }
        else
        {
            be.Status = "تحت التعادل";
            be.StatusIcon = "🔴";
            be.Description = $"خطر! إيراداتك أقل من نقطة التعادل بمقدار {Math.Abs(be.RevenueGap):N2} ج";
        }
    }

    // ============================================================
    //  ⭐ الهدف الديناميكي (10% → 20% → 30%)
    // ============================================================
    private void CalculateProfitTarget(IncomeStatementDto dto)
    {
        var t = dto.ProfitTarget;
        t.CurrentMargin = dto.NetProfitMargin;

        // تحديد الهدف الحالي والقادم
        // النظام: لو حقق 10% → الهدف الحالي 10، القادم 20
        // لو حقق 23% → الهدف الحالي 20، القادم 30
        // لو حقق 0% أو سالب → الهدف الحالي 10، القادم 20
        if (t.CurrentMargin <= 0)
        {
            t.CurrentTargetPercentage = 10;
            t.NextTargetPercentage = 20;
            t.Status = "تحت الهدف";
            t.Icon = "🔴";
            t.Message = "العمل بخسارة - أولوية: الوصول لنقطة التعادل ثم تحقيق ربح 10%";
        }
        else
        {
            // اوجد أعلى هدف محقق
            var achievedTier = (int)Math.Floor(t.CurrentMargin / 10) * 10;
            if (achievedTier == 0)
            {
                t.CurrentTargetPercentage = 10;
                t.NextTargetPercentage = 20;
                t.Status = "قريب من الهدف";
                t.Icon = "🟡";
                t.Message = $"اقتربت من تحقيق ربح 10%. الفجوة: {(10 - t.CurrentMargin):N1}%";
            }
            else
            {
                t.CurrentTargetPercentage = achievedTier;
                t.NextTargetPercentage = achievedTier + 10;

                if (t.CurrentMargin >= achievedTier + 5)
                {
                    t.Status = "متجاوز الهدف";
                    t.Icon = "🌟";
                    t.Message = $"رائع! حققت {t.CurrentMargin:N1}% — أعلى من هدف {achievedTier}%. اطمح للوصول لـ {achievedTier + 10}%";
                }
                else
                {
                    t.Status = "محقق الهدف";
                    t.Icon = "🟢";
                    t.Message = $"ممتاز! حققت {t.CurrentMargin:N1}% (هدف {achievedTier}%). اطمح للوصول لـ {achievedTier + 10}%";
                }
            }
        }

        // حساب المبالغ المستهدفة
        t.TargetAmount = Math.Round(dto.NetRevenue * (t.CurrentTargetPercentage / 100m), 2);
        t.NextTargetAmount = Math.Round(dto.NetRevenue * (t.NextTargetPercentage / 100m), 2);

        // % التحقيق
        if (t.TargetAmount > 0)
        {
            t.AchievementPercentage = Math.Round((dto.NetProfit / t.TargetAmount) * 100, 1);
            if (t.AchievementPercentage < 0) t.AchievementPercentage = 0;
            if (t.AchievementPercentage > 100) t.AchievementPercentage = 100;
        }

        // الفجوات للأهداف
        t.GapToTarget = t.TargetAmount - dto.NetProfit;
        t.GapToNextTarget = t.NextTargetAmount - dto.NetProfit;

        // اقتراحات للوصول للهدف القادم
        if (t.GapToNextTarget > 0)
        {
            // كم إيراد إضافي مع نفس نسبة الربح الإجمالي
            if (dto.GrossProfitMargin > 0)
            {
                t.RevenueIncreaseNeeded = Math.Round(t.GapToNextTarget / (dto.GrossProfitMargin / 100m), 2);
            }
            t.ExpensesReductionNeeded = t.GapToNextTarget;
        }
    }

    // ============================================================
    //  المقارنات (الفترة السابقة + السنة السابقة)
    // ============================================================
    private async Task GetPreviousPeriodComparisonAsync(IncomeStatementDto dto, IncomeStatementFilterDto filter)
    {
        var periodLength = (dto.ToDate.Date - dto.FromDate.Date).Days + 1;

        // الفترة السابقة (نفس المدة قبل بداية الفترة الحالية)
        var prevFrom = dto.FromDate.AddDays(-periodLength);
        var prevTo = dto.FromDate.AddDays(-1).AddDays(1).AddTicks(-1);

        var prev = await GetSummaryAsync(prevFrom, prevTo);
        if (prev != null)
        {
            dto.PreviousPeriod = BuildComparison("الفترة السابقة", dto, prev);
        }

        // نفس الفترة من السنة السابقة
        var prevYearFrom = dto.FromDate.AddYears(-1);
        var prevYearTo = dto.ToDate.AddYears(-1);
        var prevYear = await GetSummaryAsync(prevYearFrom, prevYearTo);
if (prevYear.HasValue && prevYear.Value.Revenue > 0)
{
    dto.PreviousYear = BuildComparison("نفس الفترة العام السابق", dto, prevYear);
}
    }

    private async Task<(decimal Revenue, decimal Cogs, decimal Expenses)?> GetSummaryAsync(
    DateTime fromDate, DateTime toDate)
{
    // إيرادات
    var rev = await _db.Transactions.AsNoTracking()
        .Where(t => t.TransactionType == TransactionTypes.Sale
            && t.InvoiceStatus != "Cancelled"
            && t.TransactionDate >= fromDate
            && t.TransactionDate <= toDate)
        .SumAsync(t => (decimal?)(t.NetTotalAmount ?? t.GrandTotal)) ?? 0;

    // COGS
    var cogs = await (
        from t in _db.Transactions.AsNoTracking()
        join d in _db.TransactionDetails.AsNoTracking() on t.TransactionId equals d.TransactionId
        join p in _db.Products.AsNoTracking() on d.ProductId equals p.ProductId
        where t.TransactionType == TransactionTypes.Sale
            && t.InvoiceStatus != "Cancelled"
            && t.TransactionDate >= fromDate
            && t.TransactionDate <= toDate
        select d.Quantity * (p.PurchasePrice ?? 0)
    ).SumAsync(x => (decimal?)x) ?? 0;

    // المصروفات
    var exp = await _db.Expenses.AsNoTracking()
        .Where(e => e.ExpenseDate >= fromDate
            && e.ExpenseDate <= toDate
            && ((e.IsAdvance != true) || (e.AdvanceParentExpenseId.HasValue)))
        .SumAsync(e => (decimal?)e.Amount) ?? 0;

    return (rev, cogs, exp);
}

    private IncomeComparisonDto BuildComparison(string label, IncomeStatementDto current,
        (decimal Revenue, decimal Cogs, decimal Expenses)? prev)
    {
        if (prev == null) return new() { Label = label };

        var prevNetProfit = prev.Value.Revenue - prev.Value.Cogs - prev.Value.Expenses;
        var prevMargin = prev.Value.Revenue == 0 ? 0
            : Math.Round((prevNetProfit / prev.Value.Revenue) * 100, 2);

        var revChange = prev.Value.Revenue == 0 ? 0
            : Math.Round(((current.NetRevenue - prev.Value.Revenue) / prev.Value.Revenue) * 100, 1);
        var expChange = prev.Value.Expenses == 0 ? 0
            : Math.Round(((current.TotalOperatingExpenses - prev.Value.Expenses) / prev.Value.Expenses) * 100, 1);
        var profitChange = prevNetProfit == 0 ? 0
            : Math.Round(((current.NetProfit - prevNetProfit) / Math.Abs(prevNetProfit)) * 100, 1);

        var trend = profitChange > 5 ? "تحسن"
                  : profitChange < -5 ? "تراجع" : "ثابت";

        return new IncomeComparisonDto
        {
            Label = label,
            Revenue = prev.Value.Revenue,
            Expenses = prev.Value.Expenses,
            NetProfit = prevNetProfit,
            NetProfitMargin = prevMargin,
            RevenueChange = revChange,
            ExpensesChange = expChange,
            NetProfitChange = profitChange,
            Trend = trend
        };
    }

    // ============================================================
    //  الترند الشهري (آخر 12 شهر)
    // ============================================================
    private async Task GetMonthlyTrendAsync(IncomeStatementDto dto)
    {
        var endDate = DateTime.Today;
        var startDate = endDate.AddMonths(-11);
        startDate = new DateTime(startDate.Year, startDate.Month, 1);

        var months = new List<MonthlyTrendDto>();
        var current = startDate;

        while (current <= endDate)
        {
            var monthEnd = current.AddMonths(1).AddDays(-1);

            var summary = await GetSummaryAsync(current, monthEnd.Date.AddDays(1).AddTicks(-1));
            if (summary != null)
            {
                var profit = summary.Value.Revenue - summary.Value.Cogs - summary.Value.Expenses;
                var margin = summary.Value.Revenue == 0 ? 0
                    : Math.Round((profit / summary.Value.Revenue) * 100, 1);

                months.Add(new MonthlyTrendDto
                {
                    Month = current,
                    MonthLabel = current.ToString("yyyy/MM"),
                    Revenue = summary.Value.Revenue,
                    Cogs = summary.Value.Cogs,
                    Expenses = summary.Value.Expenses,
                    NetProfit = profit,
                    NetProfitMargin = margin
                });
            }

            current = current.AddMonths(1);
        }

        dto.MonthlyTrend = months;
    }

    // ============================================================
    //  ⭐ التوصيات الذكية
    // ============================================================
    private void GenerateSmartRecommendations(IncomeStatementDto dto)
    {
        var recs = new List<SmartRecommendationDto>();

        // 1. تحليل الخسارة
        if (dto.NetProfit < 0)
        {
            recs.Add(new SmartRecommendationDto
            {
                Type = RecommendationTypes.Critical,
                Icon = "🔴",
                Title = "العمل بخسارة!",
                Description = $"خسارة {Math.Abs(dto.NetProfit):N2} ج هذه الفترة. " +
                              $"يجب اتخاذ إجراءات فورية لتقليل المصروفات أو زيادة المبيعات.",
                Priority = 1,
                Color = "#dc2626"
            });
        }

        // 2. تحليل نقطة التعادل
        if (!dto.BreakEvenAnalysis.AboveBreakEven)
        {
            recs.Add(new SmartRecommendationDto
            {
                Type = RecommendationTypes.Critical,
                Icon = "⚠️",
                Title = "تحت نقطة التعادل",
                Description = $"تحتاج {Math.Abs(dto.BreakEvenAnalysis.RevenueGap):N2} ج إيراد إضافي للوصول لنقطة التعادل.",
                Priority = 2,
                Color = "#dc2626"
            });
        }
        else if (dto.BreakEvenAnalysis.SafetyMargin < 20)
        {
            recs.Add(new SmartRecommendationDto
            {
                Type = RecommendationTypes.Warning,
                Icon = "🟡",
                Title = "هامش الأمان منخفض",
                Description = $"هامش الأمان فقط {dto.BreakEvenAnalysis.SafetyMargin}%. حاول زيادته لـ 30% على الأقل.",
                Priority = 3,
                Color = "#f59e0b"
            });
        }

        // 3. توصيات الهدف
        if (dto.NetProfitMargin < 10 && dto.NetProfit > 0)
        {
            recs.Add(new SmartRecommendationDto
            {
                Type = RecommendationTypes.Warning,
                Icon = "🎯",
                Title = "نسبة الربح أقل من 10%",
                Description = $"حالياً: {dto.NetProfitMargin:N1}%. " +
                              $"للوصول لـ 10% تحتاج إما زيادة الإيراد بـ {dto.ProfitTarget.RevenueIncreaseNeeded:N2} ج " +
                              $"أو تقليل المصروفات بـ {dto.ProfitTarget.GapToTarget:N2} ج",
                Priority = 4,
                Color = "#f59e0b"
            });
        }
        else if (dto.NetProfitMargin >= 10 && dto.NetProfitMargin < 20)
        {
            recs.Add(new SmartRecommendationDto
            {
                Type = RecommendationTypes.Success,
                Icon = "🟢",
                Title = "نسبة ربح ممتازة - اطمح للأعلى",
                Description = $"حققت {dto.NetProfitMargin:N1}%. " +
                              $"للوصول لـ 20% تحتاج زيادة إيراد بـ {dto.ProfitTarget.RevenueIncreaseNeeded:N2} ج",
                Priority = 5,
                Color = "#10b981"
            });
        }
        else if (dto.NetProfitMargin >= 20)
        {
            recs.Add(new SmartRecommendationDto
            {
                Type = RecommendationTypes.Success,
                Icon = "🌟",
                Title = "أداء استثنائي!",
                Description = $"نسبة ربح {dto.NetProfitMargin:N1}% — وضع ممتاز! " +
                              $"الهدف القادم: {dto.ProfitTarget.NextTargetPercentage}%. " +
                              $"فكر في توسيع النشاط أو الاستثمار في النمو.",
                Priority = 6,
                Color = "#10b981"
            });
        }

        // 4. تحليل المصروفات الكبيرة
        if (dto.ExpensesByGroup.Any())
        {
            var biggestGroup = dto.ExpensesByGroup.First();
            if (biggestGroup.Percentage > 40)
            {
                recs.Add(new SmartRecommendationDto
                {
                    Type = RecommendationTypes.Warning,
                    Icon = "📊",
                    Title = $"مصروفات {biggestGroup.GroupName} مرتفعة",
                    Description = $"تمثل {biggestGroup.Percentage}% من إجمالي المصروفات " +
                                  $"({biggestGroup.Amount:N2} ج). راجع إمكانية التخفيض.",
                    Priority = 7,
                    Color = "#f59e0b"
                });
            }
        }

        // 5. تحليل المقارنة
        if (dto.PreviousPeriod != null)
        {
            if (dto.PreviousPeriod.RevenueChange < -10)
            {
                recs.Add(new SmartRecommendationDto
                {
                    Type = RecommendationTypes.Critical,
                    Icon = "📉",
                    Title = "تراجع في الإيرادات",
                    Description = $"الإيرادات تراجعت بـ {Math.Abs(dto.PreviousPeriod.RevenueChange):N1}% " +
                                  $"مقارنة بالفترة السابقة. ابحث عن الأسباب.",
                    Priority = 8,
                    Color = "#dc2626"
                });
            }
            else if (dto.PreviousPeriod.RevenueChange > 20)
            {
                recs.Add(new SmartRecommendationDto
                {
                    Type = RecommendationTypes.Success,
                    Icon = "📈",
                    Title = "نمو رائع في الإيرادات",
                    Description = $"الإيرادات نمت بـ +{dto.PreviousPeriod.RevenueChange:N1}% " +
                                  $"مقارنة بالفترة السابقة. استمر!",
                    Priority = 9,
                    Color = "#10b981"
                });
            }

            if (dto.PreviousPeriod.ExpensesChange > 20)
            {
                recs.Add(new SmartRecommendationDto
                {
                    Type = RecommendationTypes.Warning,
                    Icon = "💸",
                    Title = "زيادة في المصروفات",
                    Description = $"المصروفات زادت بـ +{dto.PreviousPeriod.ExpensesChange:N1}% " +
                                  $"عن الفترة السابقة. راجع المصروفات الجديدة.",
                    Priority = 10,
                    Color = "#f59e0b"
                });
            }
        }

        // 6. هامش الربح الإجمالي
        if (dto.GrossProfitMargin < 30 && dto.NetRevenue > 0)
        {
            recs.Add(new SmartRecommendationDto
            {
                Type = RecommendationTypes.Warning,
                Icon = "💰",
                Title = "هامش الربح الإجمالي منخفض",
                Description = $"الربح الإجمالي {dto.GrossProfitMargin:N1}% — تكلفة المبيعات مرتفعة. " +
                              $"فكر في رفع الأسعار أو تقليل تكلفة الشراء.",
                Priority = 11,
                Color = "#f59e0b"
            });
        }

        // 7. لو أداء ممتاز عام
        if (dto.NetProfit > 0 && dto.NetProfitMargin >= 15 && dto.BreakEvenAnalysis.SafetyMargin > 30)
        {
            recs.Add(new SmartRecommendationDto
            {
                Type = RecommendationTypes.Info,
                Icon = "💡",
                Title = "نصيحة: استثمر في النمو",
                Description = "أداؤك المالي قوي. فكر في الاستثمار في التسويق، التوسع، " +
                              "أو إضافة منتجات جديدة لزيادة الإيرادات.",
                Priority = 12,
                Color = "#3b82f6"
            });
        }

        dto.Recommendations = recs.OrderBy(r => r.Priority).ToList();
    }

    // ============================================================
    //  Helpers
    // ============================================================
    private string BuildPeriodLabel(IncomeStatementFilterDto filter)
    {
        return filter.PeriodType switch
        {
            "Today" => $"اليوم ({DateTime.Today:yyyy/MM/dd})",
            "Week" => $"هذا الأسبوع ({filter.FromDate:yyyy/MM/dd} - {filter.ToDate:yyyy/MM/dd})",
            "Month" => $"شهر {filter.FromDate:yyyy/MM}",
            "Quarter" => $"الربع ({filter.FromDate:yyyy/MM/dd} - {filter.ToDate:yyyy/MM/dd})",
            "Year" => $"سنة {filter.FromDate.Year}",
            _ => $"من {filter.FromDate:yyyy/MM/dd} إلى {filter.ToDate:yyyy/MM/dd}"
        };
    }

    private string GetColorForIndex(int index)
    {
        var colors = new[]
        {
            "#3b82f6", "#10b981", "#f59e0b", "#ef4444", "#8b5cf6",
            "#ec4899", "#06b6d4", "#84cc16", "#d4af37", "#6366f1"
        };
        return colors[index % colors.Length];
    }
}
