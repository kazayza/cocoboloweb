using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class CashFlowService : ICashFlowService
{
    private readonly db24804Context _db;

    public CashFlowService(db24804Context db)
    {
        _db = db;
    }

    // ============================================================
    //  ⭐ الـ Method الرئيسية
    // ============================================================
    public async Task<CashFlowStatementDto> GetCashFlowStatementAsync(CashFlowFilterDto filter)
    {
        var dto = new CashFlowStatementDto
        {
            FromDate = filter.FromDate.Date,
            ToDate = filter.ToDate.Date.AddDays(1).AddTicks(-1),
            PeriodLabel = BuildPeriodLabel(filter)
        };

        // 1. الرصيد الافتتاحي (قبل الفترة)
        await CalculateOpeningBalanceAsync(dto, filter);

        // 2. التدفقات في الفترة (داخلة وخارجة)
        await CalculateFlowsAsync(dto, filter);

        // 3. الرصيد الختامي
        dto.ClosingBalance = dto.OpeningBalance + dto.TotalInflows - dto.TotalOutflows;

        // 4. تفصيل بحسب الخزينة
        await CalculateByCashBoxAsync(dto, filter);

        // 5. الترند اليومي
        await CalculateDailyTrendAsync(dto, filter);

        // 6. الترند الشهري (آخر 12 شهر)
        if (filter.IncludeMonthlyTrend)
        {
            await CalculateMonthlyTrendAsync(dto);
        }

        // 7. أكبر الحركات
        await GetTopFlowsAsync(dto, filter);

        // 8. تحليل السيولة
        await CalculateLiquidityAsync(dto);

        // 9. التوقعات (لو مفعّلة)
        if (filter.IncludeForecast)
        {
            await CalculateForecastAsync(dto);
        }

        // 10. التوصيات والتنبيهات
        GenerateSmartAlerts(dto);

        return dto;
    }

    // ============================================================
    //  1. الرصيد الافتتاحي
    // ============================================================
    private async Task CalculateOpeningBalanceAsync(CashFlowStatementDto dto, CashFlowFilterDto filter)
    {
        var query = _db.CashboxTransactions.AsNoTracking()
            .Where(t => t.TransactionDate < dto.FromDate);

        if (filter.CashBoxId.HasValue)
            query = query.Where(t => t.CashBoxId == filter.CashBoxId.Value);

        var inflows = await query.Where(t => t.TransactionType == "قبض" || t.TransactionType == "In")
            .SumAsync(t => (decimal?)t.Amount) ?? 0;
        var outflows = await query.Where(t => t.TransactionType == "صرف" || t.TransactionType == "Out")
            .SumAsync(t => (decimal?)t.Amount) ?? 0;

        // أيضاً نضيف الرصيد الافتتاحي للخزن
        var boxesQuery = _db.CashBoxes.AsNoTracking().AsQueryable();
        if (filter.CashBoxId.HasValue)
            boxesQuery = boxesQuery.Where(c => c.CashBoxId == filter.CashBoxId.Value);

        // الـ OpeningBalance من الخزينة بيتسجل كـ Transaction نوع "OpeningBalance"
        // فبالتالي محسوب ضمن inflows فوق

        dto.OpeningBalance = inflows - outflows;
    }

    // ============================================================
    //  2. التدفقات (داخلة وخارجة)
    // ============================================================
    private async Task CalculateFlowsAsync(CashFlowStatementDto dto, CashFlowFilterDto filter)
    {
        var query = _db.CashboxTransactions.AsNoTracking()
            .Where(t => t.TransactionDate >= dto.FromDate && t.TransactionDate <= dto.ToDate);

        if (filter.CashBoxId.HasValue)
            query = query.Where(t => t.CashBoxId == filter.CashBoxId.Value);

        // جمع كل الحركات بالـ ReferenceType
        var grouped = await query
            .GroupBy(t => new { t.ReferenceType, t.TransactionType })
            .Select(g => new
            {
                RefType = g.Key.ReferenceType ?? "Other",
                TransType = g.Key.TransactionType,
                Total = g.Sum(x => x.Amount),
                Count = g.Count()
            })
            .ToListAsync();

        var inflows = new List<CashFlowCategoryDto>();
        var outflows = new List<CashFlowCategoryDto>();

        foreach (var g in grouped)
{
    var isInflow = g.TransType == "قبض" || g.TransType == "In";
    
    // ✅ Default value مع الأسماء بشكل صريح
    (string NameAr, string Color, string Icon, string Group) defaultCat = 
        ("أخرى", "#94a3b8", "MoreHoriz", isInflow ? "Inflow" : "Outflow");
    
    var category = CashFlowCategories.Categories.GetValueOrDefault(g.RefType, defaultCat);

    var dto2 = new CashFlowCategoryDto
    {
        ReferenceType = g.RefType,
        CategoryName = category.NameAr,
        Amount = g.Total,
        Count = g.Count,
        Color = category.Color,
        Icon = category.Icon
    };

            if (isInflow)
                inflows.Add(dto2);
            else
                outflows.Add(dto2);
        }

        dto.TotalInflows = inflows.Sum(i => i.Amount);
        dto.TotalOutflows = outflows.Sum(o => o.Amount);

        // حساب النسب
        var inflowsTotal = dto.TotalInflows == 0 ? 1 : dto.TotalInflows;
        var outflowsTotal = dto.TotalOutflows == 0 ? 1 : dto.TotalOutflows;

        foreach (var i in inflows)
            i.Percentage = Math.Round((i.Amount / inflowsTotal) * 100, 1);
        foreach (var o in outflows)
            o.Percentage = Math.Round((o.Amount / outflowsTotal) * 100, 1);

        dto.Inflows = inflows.OrderByDescending(i => i.Amount).ToList();
        dto.Outflows = outflows.OrderByDescending(o => o.Amount).ToList();
    }

    // ============================================================
    //  3. تفصيل بحسب الخزينة
    // ============================================================
    private async Task CalculateByCashBoxAsync(CashFlowStatementDto dto, CashFlowFilterDto filter)
    {
        var boxesQuery = _db.CashBoxes.AsNoTracking().AsQueryable();
        if (filter.CashBoxId.HasValue)
            boxesQuery = boxesQuery.Where(c => c.CashBoxId == filter.CashBoxId.Value);

        var boxes = await boxesQuery.ToListAsync();

        foreach (var box in boxes)
        {
            // الرصيد الافتتاحي للخزينة (قبل الفترة)
            var beforeIn = await _db.CashboxTransactions.AsNoTracking()
                .Where(t => t.CashBoxId == box.CashBoxId
                    && (t.TransactionType == "قبض" || t.TransactionType == "In")
                    && t.TransactionDate < dto.FromDate)
                .SumAsync(t => (decimal?)t.Amount) ?? 0;
            var beforeOut = await _db.CashboxTransactions.AsNoTracking()
                .Where(t => t.CashBoxId == box.CashBoxId
                    && (t.TransactionType == "صرف" || t.TransactionType == "Out")
                    && t.TransactionDate < dto.FromDate)
                .SumAsync(t => (decimal?)t.Amount) ?? 0;
            var openingBal = beforeIn - beforeOut;

            // التدفقات في الفترة
            var periodIn = await _db.CashboxTransactions.AsNoTracking()
                .Where(t => t.CashBoxId == box.CashBoxId
                    && (t.TransactionType == "قبض" || t.TransactionType == "In")
                    && t.TransactionDate >= dto.FromDate
                    && t.TransactionDate <= dto.ToDate)
                .SumAsync(t => (decimal?)t.Amount) ?? 0;
            var periodOut = await _db.CashboxTransactions.AsNoTracking()
                .Where(t => t.CashBoxId == box.CashBoxId
                    && (t.TransactionType == "صرف" || t.TransactionType == "Out")
                    && t.TransactionDate >= dto.FromDate
                    && t.TransactionDate <= dto.ToDate)
                .SumAsync(t => (decimal?)t.Amount) ?? 0;

            dto.ByCashBox.Add(new CashBoxFlowDto
            {
                CashBoxId = box.CashBoxId,
                CashBoxName = box.CashBoxName,
                Color = box.Color ?? "#94a3b8",
                OpeningBalance = openingBal,
                Inflows = periodIn,
                Outflows = periodOut,
                IsActive = box.IsActive
            });
        }

        dto.ByCashBox = dto.ByCashBox
            .OrderByDescending(b => b.IsActive)
            .ThenByDescending(b => b.ClosingBalance)
            .ToList();
    }

    // ============================================================
    //  4. الترند اليومي
    // ============================================================
    private async Task CalculateDailyTrendAsync(CashFlowStatementDto dto, CashFlowFilterDto filter)
    {
        var query = _db.CashboxTransactions.AsNoTracking()
            .Where(t => t.TransactionDate >= dto.FromDate && t.TransactionDate <= dto.ToDate);

        if (filter.CashBoxId.HasValue)
            query = query.Where(t => t.CashBoxId == filter.CashBoxId.Value);

        var dailyData = await query
            .GroupBy(t => t.TransactionDate.Date)
            .Select(g => new
            {
                Date = g.Key,
                In = g.Where(x => x.TransactionType == "قبض" || x.TransactionType == "In").Sum(x => (decimal?)x.Amount) ?? 0,
                Out = g.Where(x => x.TransactionType == "صرف" || x.TransactionType == "Out").Sum(x => (decimal?)x.Amount) ?? 0
            })
            .ToListAsync();

        // بناء كل يوم في الفترة
        var totalDays = (dto.ToDate.Date - dto.FromDate.Date).Days + 1;
        var maxDays = Math.Min(totalDays, 60); // حد أقصى 60 يوم للأداء

        decimal runningBalance = dto.OpeningBalance;
        var trend = new List<DailyFlowDto>();

        for (int i = 0; i < maxDays; i++)
        {
            var date = dto.FromDate.AddDays(i);
            var data = dailyData.FirstOrDefault(d => d.Date == date.Date);
            var inflow = data?.In ?? 0;
            var outflow = data?.Out ?? 0;
            runningBalance += (inflow - outflow);

            trend.Add(new DailyFlowDto
            {
                Date = date,
                Inflows = inflow,
                Outflows = outflow,
                RunningBalance = runningBalance
            });
        }

        dto.DailyTrend = trend;
    }

    // ============================================================
    //  5. الترند الشهري (12 شهر)
    // ============================================================
    private async Task CalculateMonthlyTrendAsync(CashFlowStatementDto dto)
    {
        var endDate = DateTime.Today;
        var startDate = endDate.AddMonths(-11);
        startDate = new DateTime(startDate.Year, startDate.Month, 1);

        var months = new List<MonthlyFlowDto>();
        var current = startDate;

        while (current <= endDate)
        {
            var monthEnd = current.AddMonths(1).AddDays(-1).Date.AddDays(1).AddTicks(-1);

            var inflows = await _db.CashboxTransactions.AsNoTracking()
                .Where(t => (t.TransactionType == "قبض" || t.TransactionType == "In")
                    && t.TransactionDate >= current
                    && t.TransactionDate <= monthEnd)
                .SumAsync(t => (decimal?)t.Amount) ?? 0;

            var outflows = await _db.CashboxTransactions.AsNoTracking()
                .Where(t => (t.TransactionType == "صرف" || t.TransactionType == "Out")
                    && t.TransactionDate >= current
                    && t.TransactionDate <= monthEnd)
                .SumAsync(t => (decimal?)t.Amount) ?? 0;

            months.Add(new MonthlyFlowDto
            {
                Month = current,
                MonthLabel = current.ToString("yyyy/MM"),
                Inflows = inflows,
                Outflows = outflows
            });

            current = current.AddMonths(1);
        }

        dto.MonthlyTrend = months;
    }

    // ============================================================
    //  6. أكبر الحركات
    // ============================================================
    private async Task GetTopFlowsAsync(CashFlowStatementDto dto, CashFlowFilterDto filter)
    {
        var query = _db.CashboxTransactions.AsNoTracking()
            .Where(t => t.TransactionDate >= dto.FromDate && t.TransactionDate <= dto.ToDate);

        if (filter.CashBoxId.HasValue)
            query = query.Where(t => t.CashBoxId == filter.CashBoxId.Value);

        // أعلى 5 داخلة
        var topIn = await query
            .Where(t => t.TransactionType == "قبض" || t.TransactionType == "In")
            .OrderByDescending(t => t.Amount)
            .Take(5)
            .Select(t => new
            {
                t.TransactionDate,
                t.Notes,
                t.CashBoxId,
                t.ReferenceType,
                t.Amount,
                t.ReferenceId,
                t.PaymentId
            })
            .ToListAsync();

        // أعلى 5 خارجة
        var topOut = await query
            .Where(t => t.TransactionType == "صرف" || t.TransactionType == "Out")
            .OrderByDescending(t => t.Amount)
            .Take(5)
            .Select(t => new
            {
                t.TransactionDate,
                t.Notes,
                t.CashBoxId,
                t.ReferenceType,
                t.Amount,
                t.ReferenceId,
                t.PaymentId
            })
            .ToListAsync();

        // الخزن للتسمية
        var allCashBoxIds = topIn.Select(t => t.CashBoxId).Concat(topOut.Select(t => t.CashBoxId)).Distinct().ToList();
        var cashBoxes = await _db.CashBoxes.AsNoTracking()
            .Where(c => allCashBoxIds.Contains(c.CashBoxId))
            .ToDictionaryAsync(c => c.CashBoxId, c => c.CashBoxName);

        dto.TopInflows = topIn.Select(t => new TopFlowDto
        {
            Date = t.TransactionDate,
            Description = t.Notes ?? "—",
            CashBoxName = cashBoxes.GetValueOrDefault(t.CashBoxId, "-"),
            ReferenceType = t.ReferenceType,
            ReferenceTypeAr = CashBoxRefTypes.All.GetValueOrDefault(t.ReferenceType ?? "", t.ReferenceType ?? "-"),
            Amount = t.Amount
        }).ToList();

        dto.TopOutflows = topOut.Select(t => new TopFlowDto
        {
            Date = t.TransactionDate,
            Description = t.Notes ?? "—",
            CashBoxName = cashBoxes.GetValueOrDefault(t.CashBoxId, "-"),
            ReferenceType = t.ReferenceType,
            ReferenceTypeAr = CashBoxRefTypes.All.GetValueOrDefault(t.ReferenceType ?? "", t.ReferenceType ?? "-"),
            Amount = t.Amount
        }).ToList();
    }

    // ============================================================
    //  7. ⭐ تحليل السيولة
    // ============================================================
    private async Task CalculateLiquidityAsync(CashFlowStatementDto dto)
    {
        var l = dto.LiquidityAnalysis;
        l.CurrentBalance = dto.ClosingBalance;

        // متوسط المصروفات الشهرية (آخر 3 شهور)
        var threeMonthsAgo = DateTime.Today.AddMonths(-3);
        var totalOutflowsLast3 = await _db.CashboxTransactions.AsNoTracking()
            .Where(t => (t.TransactionType == "صرف" || t.TransactionType == "Out")
                && t.TransactionDate >= threeMonthsAgo)
            .SumAsync(t => (decimal?)t.Amount) ?? 0;

        l.AverageMonthlyOutflow = Math.Round(totalOutflowsLast3 / 3m, 2);

        // نسبة السيولة = الرصيد الحالي ÷ متوسط المصروفات الشهرية
        if (l.AverageMonthlyOutflow > 0)
        {
            l.LiquidityRatio = Math.Round(l.CurrentBalance / l.AverageMonthlyOutflow, 1);
        }

        // تحديد الحالة
        if (l.LiquidityRatio >= 6)
        {
            l.Status = "ممتاز";
            l.Icon = "🟢";
            l.Description = $"رصيدك يكفي لـ {l.LiquidityRatio} شهر بدون أي إيراد جديد. وضع سيولة قوي جداً.";
        }
        else if (l.LiquidityRatio >= 3)
        {
            l.Status = "جيد";
            l.Icon = "🟢";
            l.Description = $"رصيدك يكفي لـ {l.LiquidityRatio} شهر. سيولة جيدة ومستقرة.";
        }
        else if (l.LiquidityRatio >= 1)
        {
            l.Status = "متوسط";
            l.Icon = "🟡";
            l.Description = $"رصيدك يكفي لـ {l.LiquidityRatio} شهر فقط. حاول زيادة السيولة.";
        }
        else if (l.LiquidityRatio > 0)
        {
            l.Status = "حرج";
            l.Icon = "🟠";
            l.Description = $"تنبيه! رصيدك يكفي فقط لـ {l.LiquidityRatio} شهر. يجب تحسين التدفق فوراً.";
        }
        else
        {
            l.Status = "خطر";
            l.Icon = "🔴";
            l.Description = "خطر شديد! الرصيد سالب أو غير كافي. أوقف المصروفات غير الضرورية.";
        }
    }

    // ============================================================
    //  8. ⭐ التوقعات
    // ============================================================
    private async Task CalculateForecastAsync(CashFlowStatementDto dto)
    {
        var f = dto.Forecast;

        // متوسط آخر 3 شهور
        var threeMonthsAgo = DateTime.Today.AddMonths(-3);
        var endLastMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddDays(-1);

        var totalIn3 = await _db.CashboxTransactions.AsNoTracking()
            .Where(t => (t.TransactionType == "قبض" || t.TransactionType == "In")
                && t.TransactionDate >= threeMonthsAgo
                && t.TransactionDate <= endLastMonth)
            .SumAsync(t => (decimal?)t.Amount) ?? 0;

        var totalOut3 = await _db.CashboxTransactions.AsNoTracking()
            .Where(t => (t.TransactionType == "صرف" || t.TransactionType == "Out")
                && t.TransactionDate >= threeMonthsAgo
                && t.TransactionDate <= endLastMonth)
            .SumAsync(t => (decimal?)t.Amount) ?? 0;

        f.AverageMonthlyInflow = Math.Round(totalIn3 / 3m, 2);
        f.AverageMonthlyOutflow = Math.Round(totalOut3 / 3m, 2);
        f.AverageMonthlyNet = f.AverageMonthlyInflow - f.AverageMonthlyOutflow;

        // التوقع للشهر القادم (نفس المتوسط)
        f.NextMonthExpectedInflow = f.AverageMonthlyInflow;
        f.NextMonthExpectedOutflow = f.AverageMonthlyOutflow;
        f.NextMonthExpectedNet = f.AverageMonthlyNet;
        f.NextMonthExpectedBalance = dto.ClosingBalance + f.AverageMonthlyNet;

        // حساب الـ Trend (مقارنة آخر شهر مع الشهر اللي قبله)
        var lastMonthStart = new DateTime(endLastMonth.Year, endLastMonth.Month, 1);
        var lastMonthIn = await _db.CashboxTransactions.AsNoTracking()
            .Where(t => (t.TransactionType == "قبض" || t.TransactionType == "In")
                && t.TransactionDate >= lastMonthStart
                && t.TransactionDate <= endLastMonth.Date.AddDays(1).AddTicks(-1))
            .SumAsync(t => (decimal?)t.Amount) ?? 0;

        var twoMonthsAgoStart = lastMonthStart.AddMonths(-1);
        var twoMonthsAgoEnd = lastMonthStart.AddDays(-1);
        var twoMonthsIn = await _db.CashboxTransactions.AsNoTracking()
            .Where(t => (t.TransactionType == "قبض" || t.TransactionType == "In")
                && t.TransactionDate >= twoMonthsAgoStart
                && t.TransactionDate <= twoMonthsAgoEnd.Date.AddDays(1).AddTicks(-1))
            .SumAsync(t => (decimal?)t.Amount) ?? 0;

        if (twoMonthsIn > 0)
        {
            f.TrendPercentage = Math.Round(((lastMonthIn - twoMonthsIn) / twoMonthsIn) * 100, 1);
        }

        if (f.TrendPercentage > 5)
        {
            f.Trend = "نمو";
            f.TrendIcon = "📈";
            f.Description = $"الإيرادات في نمو بمعدل {f.TrendPercentage}% شهرياً. توقع الشهر القادم: {f.NextMonthExpectedInflow:N2} ج";
        }
        else if (f.TrendPercentage < -5)
        {
            f.Trend = "تراجع";
            f.TrendIcon = "📉";
            f.Description = $"الإيرادات تتراجع بمعدل {Math.Abs(f.TrendPercentage)}% شهرياً. ابحث عن الأسباب.";
        }
        else
        {
            f.Trend = "ثابت";
            f.TrendIcon = "➡️";
            f.Description = $"الإيرادات مستقرة. التوقع للشهر القادم: {f.NextMonthExpectedInflow:N2} ج";
        }
    }

    // ============================================================
    //  9. ⭐ التوصيات والتنبيهات الذكية
    // ============================================================
    private void GenerateSmartAlerts(CashFlowStatementDto dto)
    {
        var alerts = new List<CashFlowAlertDto>();

        // 1. صافي التدفق سالب
        if (dto.NetCashFlow < 0)
        {
            alerts.Add(new CashFlowAlertDto
            {
                Type = "Critical",
                Icon = "🔴",
                Title = "صافي التدفق سالب",
                Description = $"المصروفات أعلى من الإيرادات بـ {Math.Abs(dto.NetCashFlow):N2} ج. " +
                              $"يجب اتخاذ إجراءات فورية.",
                Priority = 1,
                Color = "#dc2626"
            });
        }
        else if (dto.NetCashFlow > 0)
        {
            alerts.Add(new CashFlowAlertDto
            {
                Type = "Success",
                Icon = "🟢",
                Title = "صافي التدفق موجب",
                Description = $"التدفق النقدي إيجابي بـ +{dto.NetCashFlow:N2} ج. وضع جيد.",
                Priority = 9,
                Color = "#10b981"
            });
        }

        // 2. تحليل السيولة
        if (dto.LiquidityAnalysis.LiquidityRatio < 1)
        {
            alerts.Add(new CashFlowAlertDto
            {
                Type = "Critical",
                Icon = "⚠️",
                Title = "سيولة منخفضة جداً",
                Description = $"رصيدك يكفي فقط لـ {dto.LiquidityAnalysis.LiquidityRatio} شهر. " +
                              "ابحث عن تمويل أو زيادة الإيرادات فوراً.",
                Priority = 2,
                Color = "#dc2626"
            });
        }
        else if (dto.LiquidityAnalysis.LiquidityRatio < 2)
        {
            alerts.Add(new CashFlowAlertDto
            {
                Type = "Warning",
                Icon = "🟡",
                Title = "سيولة متوسطة",
                Description = $"رصيدك يكفي لـ {dto.LiquidityAnalysis.LiquidityRatio} شهر. حاول زيادة الاحتياطي.",
                Priority = 3,
                Color = "#f59e0b"
            });
        }

        // 3. تحليل الترند (التوقعات)
        if (dto.Forecast.Trend == "تراجع")
        {
            alerts.Add(new CashFlowAlertDto
            {
                Type = "Warning",
                Icon = "📉",
                Title = "تراجع في الإيرادات",
                Description = $"الإيرادات تتراجع بمعدل {Math.Abs(dto.Forecast.TrendPercentage)}% شهرياً. " +
                              "تحقق من أسباب الانخفاض.",
                Priority = 4,
                Color = "#f59e0b"
            });
        }
        else if (dto.Forecast.Trend == "نمو")
        {
            alerts.Add(new CashFlowAlertDto
            {
                Type = "Success",
                Icon = "📈",
                Title = "نمو في الإيرادات",
                Description = $"الإيرادات تنمو بمعدل +{dto.Forecast.TrendPercentage}% شهرياً. استمر!",
                Priority = 7,
                Color = "#10b981"
            });
        }

        // 4. توقع الشهر القادم
        if (dto.Forecast.NextMonthExpectedNet < 0)
        {
            alerts.Add(new CashFlowAlertDto
            {
                Type = "Warning",
                Icon = "🔮",
                Title = "توقع شهر قادم سلبي",
                Description = $"بناءً على المتوسط، يُتوقع أن يكون التدفق الشهر القادم: {dto.Forecast.NextMonthExpectedNet:N2} ج. " +
                              "خطط للإجراءات اللازمة.",
                Priority = 5,
                Color = "#f59e0b"
            });
        }

        // 5. مصروفات أعلى من الإيرادات
        if (dto.TotalOutflows > dto.TotalInflows && dto.TotalInflows > 0)
        {
            var ratio = Math.Round((dto.TotalOutflows / dto.TotalInflows) * 100, 1);
            alerts.Add(new CashFlowAlertDto
            {
                Type = "Warning",
                Icon = "💸",
                Title = "المصروفات تتجاوز الإيرادات",
                Description = $"المصروفات {ratio}% من الإيرادات. حاول تقليلها لـ 80% أو أقل.",
                Priority = 6,
                Color = "#f59e0b"
            });
        }

        // 6. أكبر مصدر صرف
        if (dto.Outflows.Any())
        {
            var biggest = dto.Outflows.First();
            if (biggest.Percentage > 50)
            {
                alerts.Add(new CashFlowAlertDto
                {
                    Type = "Info",
                    Icon = "📊",
                    Title = $"{biggest.CategoryName} يمثل {biggest.Percentage}% من المصروفات",
                    Description = $"بقيمة {biggest.Amount:N2} ج. راجع إمكانية التحسين أو التوزيع.",
                    Priority = 8,
                    Color = "#3b82f6"
                });
            }
        }

        // 7. سيولة ممتازة
        if (dto.LiquidityAnalysis.LiquidityRatio >= 6 && dto.NetCashFlow > 0)
        {
            alerts.Add(new CashFlowAlertDto
            {
                Type = "Info",
                Icon = "💡",
                Title = "نصيحة: استثمر السيولة الفائضة",
                Description = "لديك سيولة جيدة. فكر في الاستثمار، التوسع، أو سداد الالتزامات لتحسين العائد.",
                Priority = 10,
                Color = "#3b82f6"
            });
        }

        dto.Alerts = alerts.OrderBy(a => a.Priority).ToList();
    }

    // ============================================================
    //  Helper
    // ============================================================
    private string BuildPeriodLabel(CashFlowFilterDto filter)
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
}
