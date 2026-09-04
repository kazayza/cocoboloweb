using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;

namespace COCOBOLOERPNEW.Services;

public class ExpenseService : IExpenseService
{
    private readonly db24804Context _db;
    private readonly IAuditService _audit;

    public ExpenseService(db24804Context db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    private IQueryable<Expense> BuildRecognizedExpensesQuery(int? branchId = null)
    {
        var query = _db.Expenses.AsNoTracking()
            .Where(e => (e.IsAdvance != true) || e.AdvanceParentExpenseId.HasValue);

        if (branchId.HasValue)
            query = query.Where(e => e.BranchId == branchId.Value);

        return query;
    }

    private IQueryable<Expense> BuildAdvanceHeadersQuery(int? branchId = null)
    {
        var query = _db.Expenses.AsNoTracking()
            .Where(e => e.IsAdvance == true
                && e.AdvanceParentExpenseId == null
                && e.AdvanceMonthIndex == 0);

        if (branchId.HasValue)
            query = query.Where(e => e.BranchId == branchId.Value);

        return query;
    }

    // ============================================================
    //  ⭐ Expenses List - محسّن للسرعة (5x أسرع)
    // ============================================================

    // ⭐ تطبيق كل فلاتر المصروفات (مشترك بين اللسته والإحصائيات والتقارير)
    //    لأي تعديل جوه الفلاتر هنا يتأثر في كل الأماكن تلقائيًا
    private async Task<IQueryable<Expense>> ApplyExpenseFiltersAsync(
        IQueryable<Expense> query, ExpenseFilterDto filter)
    {
        if (filter.BranchId.HasValue)
            query = query.Where(e => e.BranchId == filter.BranchId.Value);

        // ⭐ فلتر المجموعة الرئيسية مستقل ويشمل كل المستويات التابعة لها
        if (filter.ParentGroupId.HasValue)
        {
            var allowedGroupIds = await GetExpenseGroupDescendantIdsAsync(filter.ParentGroupId.Value);
            allowedGroupIds.Add(filter.ParentGroupId.Value);

            query = query.Where(e => allowedGroupIds.Contains(e.ExpenseGroupId));
        }

        // ⭐ فلتر النوع المباشر مستقل عن اختيار المجموعة الرئيسية
        if (filter.ExpenseGroupId.HasValue)
            query = query.Where(e => e.ExpenseGroupId == filter.ExpenseGroupId.Value);

        // ⭐ بحث نصي يشمل: اسم المصروف + الملاحظات + المستفيد + نوع المصروف + المجموعة/المسار الكامل
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();

            var groupMap = await _db.ExpenseGroups.AsNoTracking()
                .Select(g => new { g.ExpenseGroupId, g.ExpenseGroupName, g.ParentGroupId })
                .ToDictionaryAsync(
                    g => g.ExpenseGroupId,
                    g => (Name: g.ExpenseGroupName, ParentId: g.ParentGroupId));

            var matchingGroupIds = groupMap
                .Where(g => (BuildGroupPathFromDict(g.Key, groupMap) ?? g.Value.Name)
                    .Contains(s, StringComparison.OrdinalIgnoreCase))
                .Select(g => g.Key)
                .ToHashSet();

            query = query.Where(e => e.ExpenseName.Contains(s)
                || (e.Notes != null && e.Notes.Contains(s))
                || (e.Torecipient != null && e.Torecipient.Contains(s))
                || matchingGroupIds.Contains(e.ExpenseGroupId));
        }

        if (filter.CashBoxId.HasValue)
            query = query.Where(e => e.CashBoxId == filter.CashBoxId.Value);
        if (filter.DateFrom.HasValue)
            query = query.Where(e => e.ExpenseDate >= filter.DateFrom.Value.Date);
        if (filter.DateTo.HasValue)
            query = query.Where(e => e.ExpenseDate < filter.DateTo.Value.Date.AddDays(1)); // ⭐ حل مشكلة التاريخ هنا
        if (filter.AmountFrom.HasValue)
            query = query.Where(e => e.Amount >= filter.AmountFrom.Value);
        if (filter.AmountTo.HasValue)
            query = query.Where(e => e.Amount <= filter.AmountTo.Value);
        if (filter.IsAdvance.HasValue)
            query = query.Where(e => e.IsAdvance == filter.IsAdvance.Value);

        if (filter.OnlyParents == true)
            query = query.Where(e => e.AdvanceParentExpenseId == null);
        else if (filter.ExcludeAdvanceHeaders)
            query = query.Where(e => !(e.IsAdvance == true
                && e.AdvanceParentExpenseId == null
                && e.AdvanceMonthIndex == 0));

        return query;
    }

    public async Task<PagedResult<ExpenseListDto>> GetExpensesAsync(ExpenseFilterDto filter)
    {
        var query = await ApplyExpenseFiltersAsync(
            _db.Expenses.AsNoTracking().AsQueryable(), filter);

        var totalCount = await query.CountAsync();

        query = filter.SortBy switch
        {
            "Amount" => filter.SortDescending
                ? query.OrderByDescending(e => e.Amount)
                : query.OrderBy(e => e.Amount),
            _ => filter.SortDescending
                ? query.OrderByDescending(e => e.ExpenseDate).ThenByDescending(e => e.ExpenseId)
                : query.OrderBy(e => e.ExpenseDate).ThenBy(e => e.ExpenseId)
        };

        var rawData = await query
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(e => new
            {
                e.ExpenseId,
                e.BranchId,
                e.ExpenseName,
                e.ExpenseDate,
                e.Amount,
                IsAdvance = e.IsAdvance ?? false,
                e.AdvanceMonths,
                e.AdvanceParentExpenseId,
                e.AdvanceMonthIndex,
                e.Notes,
                Recipient = e.Torecipient,
                e.ExpenseGroupId,
                e.CashBoxId,
                e.CreatedBy,
                e.CreatedAt
            })
            .ToListAsync();

        if (!rawData.Any())
        {
            return new PagedResult<ExpenseListDto>
            {
                Items = new List<ExpenseListDto>(),
                TotalCount = totalCount,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize
            };
        }

        var allGroups = await _db.ExpenseGroups.AsNoTracking()
            .ToDictionaryAsync(
                g => g.ExpenseGroupId,
                g => (Name: g.ExpenseGroupName, ParentId: g.ParentGroupId)
            );

        var cashBoxIds = rawData.Select(e => e.CashBoxId).Distinct().ToList();
        var cashBoxes = await _db.CashBoxes.AsNoTracking()
            .Where(c => cashBoxIds.Contains(c.CashBoxId))
            .ToDictionaryAsync(c => c.CashBoxId, c => c.CashBoxName);

        var branchIds = rawData.Where(e => e.BranchId.HasValue).Select(e => e.BranchId!.Value).Distinct().ToList();
        var branches = branchIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Branches.AsNoTracking()
                .Where(b => branchIds.Contains(b.BranchId))
                .ToDictionaryAsync(b => b.BranchId, b => b.BranchNameAr);

        var items = rawData.Select(e => new ExpenseListDto
        {
            ExpenseId = e.ExpenseId,
            BranchId = e.BranchId,
            BranchName = e.BranchId.HasValue ? branches.GetValueOrDefault(e.BranchId.Value) : null,
            ExpenseName = e.ExpenseName,
            ExpenseDate = e.ExpenseDate,
            Amount = e.Amount,
            IsAdvance = e.IsAdvance,
            AdvanceMonths = e.AdvanceMonths,
            AdvanceParentExpenseId = e.AdvanceParentExpenseId,
            AdvanceMonthIndex = e.AdvanceMonthIndex,
            Notes = e.Notes,
            Recipient = e.Recipient,
            ExpenseGroupId = e.ExpenseGroupId,
            ExpenseGroupName = allGroups.ContainsKey(e.ExpenseGroupId) ? allGroups[e.ExpenseGroupId].Name : null,
            FullGroupPath = BuildGroupPathFromDict(e.ExpenseGroupId, allGroups),
            CashBoxId = e.CashBoxId,
            CashBoxName = cashBoxes.GetValueOrDefault(e.CashBoxId),
            CreatedBy = e.CreatedBy,
            CreatedAt = e.CreatedAt
        }).ToList();

        return new PagedResult<ExpenseListDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize
        };
    }

    public async Task<ExpenseFormDto?> GetExpenseForEditAsync(int id)
    {
        var e = await _db.Expenses.AsNoTracking().FirstOrDefaultAsync(x => x.ExpenseId == id);
        if (e == null) return null;

        return new ExpenseFormDto
        {
            ExpenseId = e.ExpenseId,
            BranchId = e.BranchId,
            ExpenseName = e.ExpenseName,
            ExpenseDate = e.ExpenseDate,
            ExpenseGroupId = e.ExpenseGroupId,
            CashBoxId = e.CashBoxId,
            Amount = e.Amount,
            IsAdvance = e.IsAdvance ?? false,
            AdvanceMonths = e.AdvanceMonths ?? 1,
            Notes = e.Notes,
            Recipient = e.Torecipient
        };
    }

    public async Task<List<ExpenseListDto>> GetAdvanceChildrenAsync(int parentExpenseId)
    {
        var children = await _db.Expenses.AsNoTracking()
            .Where(e => e.AdvanceParentExpenseId == parentExpenseId)
            .OrderBy(e => e.AdvanceMonthIndex)
            .Select(e => new ExpenseListDto
            {
                ExpenseId = e.ExpenseId,
                BranchId = e.BranchId,
                ExpenseName = e.ExpenseName,
                ExpenseDate = e.ExpenseDate,
                Amount = e.Amount,
                IsAdvance = e.IsAdvance ?? false,
                AdvanceMonths = e.AdvanceMonths,
                AdvanceParentExpenseId = e.AdvanceParentExpenseId,
                AdvanceMonthIndex = e.AdvanceMonthIndex,
                Notes = e.Notes,
                Recipient = e.Torecipient,
                ExpenseGroupId = e.ExpenseGroupId,
                CashBoxId = e.CashBoxId,
                CreatedBy = e.CreatedBy,
                CreatedAt = e.CreatedAt
            }).ToListAsync();

        // Enrich مرة واحدة
        if (children.Any())
        {
            var groupIds = children.Select(c => c.ExpenseGroupId).Distinct().ToList();
            var cashBoxIds = children.Select(c => c.CashBoxId).Distinct().ToList();
            var branchIds = children.Where(c => c.BranchId.HasValue).Select(c => c.BranchId!.Value).Distinct().ToList();
            var groups = await _db.ExpenseGroups.AsNoTracking()
                .Where(g => groupIds.Contains(g.ExpenseGroupId))
                .ToDictionaryAsync(g => g.ExpenseGroupId, g => g.ExpenseGroupName);
            var boxes = await _db.CashBoxes.AsNoTracking()
                .Where(c => cashBoxIds.Contains(c.CashBoxId))
                .ToDictionaryAsync(c => c.CashBoxId, c => c.CashBoxName);
            var branches = branchIds.Count == 0
                ? new Dictionary<int, string>()
                : await _db.Branches.AsNoTracking()
                    .Where(b => branchIds.Contains(b.BranchId))
                    .ToDictionaryAsync(b => b.BranchId, b => b.BranchNameAr);

            foreach (var c in children)
            {
                c.ExpenseGroupName = groups.GetValueOrDefault(c.ExpenseGroupId);
                c.CashBoxName = boxes.GetValueOrDefault(c.CashBoxId);
                c.BranchName = c.BranchId.HasValue ? branches.GetValueOrDefault(c.BranchId.Value) : null;
            }
        }

        return children;
    }

    // ⭐ الإحصائيات بتحترم الفلتر بالكامل (نفس فلتر الجدول)
    //    الكروت: (اليوم / الشهر / السنة / الإجمالي) بتتحسب جوه نطاق الفلتر الحالي
    public async Task<ExpenseStatsDto> GetStatsAsync(ExpenseFilterDto filter)
    {
        var query = await ApplyExpenseFiltersAsync(
            _db.Expenses.AsNoTracking().AsQueryable(), filter);

        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1);          // أول الشهر الجاي (نهاية مفتوحة)
        var yearStart = new DateTime(today.Year, 1, 1);
        var yearEnd = yearStart.AddYears(1);             // أول السنة الجاية (نهاية مفتوحة)

        // ⚠️ النوافذ لازم تكون مقفولة من الجهتين (بداية + نهاية مفتوحة) عشان سطور
        //    "المصروف المقدم" لشهور قادمة (تواريخها في المستقبل) ماتتسحبش في
        //    كارت الشهر/السنة الحاليين — الشهر = من أول يوم لآخر يوم بس.
        var stats = new ExpenseStatsDto
        {
            TotalCount = await query.CountAsync(),
            TotalAmount = await query.SumAsync(e => (decimal?)e.Amount) ?? 0,
            TodayAmount = await query.Where(e => e.ExpenseDate >= today && e.ExpenseDate < today.AddDays(1))
                .SumAsync(e => (decimal?)e.Amount) ?? 0,
            MonthAmount = await query.Where(e => e.ExpenseDate >= monthStart && e.ExpenseDate < monthEnd)
                .SumAsync(e => (decimal?)e.Amount) ?? 0,
            YearAmount = await query.Where(e => e.ExpenseDate >= yearStart && e.ExpenseDate < yearEnd)
                .SumAsync(e => (decimal?)e.Amount) ?? 0
        };

        var groupData = await query
            .GroupBy(e => e.ExpenseGroupId)
            .Select(g => new
            {
                GroupId = g.Key,
                Total = g.Sum(x => x.Amount),
                Count = g.Count()
            })
            .ToListAsync();

        var groups = await _db.ExpenseGroups.AsNoTracking()
            .ToDictionaryAsync(g => g.ExpenseGroupId, g => g.ExpenseGroupName);

        var totalForPct = stats.TotalAmount == 0 ? 1 : stats.TotalAmount;
        stats.GroupBreakdown = groupData.Select(x => new ExpenseGroupStatsDto
        {
            ExpenseGroupId = x.GroupId,
            GroupName = groups.GetValueOrDefault(x.GroupId, "غير محدد"),
            Total = x.Total,
            Count = x.Count,
            Percentage = Math.Round((x.Total / totalForPct) * 100, 1)
        }).OrderByDescending(x => x.Total).ToList();

        return stats;
    }
     public async Task<ExpenseDashboardDto> GetDashboardDataAsync(int? branchId = null)
{
    var dashboard = new ExpenseDashboardDto();
    var today = DateTime.Today;

    // ── Date Ranges ──
    var currentMonthStart = new DateTime(today.Year, today.Month, 1);
    var currentMonthEnd = currentMonthStart.AddMonths(1).AddDays(-1);
    var prevMonthStart = currentMonthStart.AddMonths(-1);
    var prevMonthEnd = currentMonthStart.AddDays(-1);
    var yearStart = new DateTime(today.Year, 1, 1);

    // المصروفات المعترف بها فعلياً = العادية + أقساط المصروف المقدم
    var recognizedExpenses = BuildRecognizedExpensesQuery(branchId);

    // رؤوس المصروفات المقدمة للمتابعة فقط
    var advanceHeaders = BuildAdvanceHeadersQuery(branchId);

    // ── 1. Current & Previous Month ──
    dashboard.CurrentMonthAmount = await recognizedExpenses
        .Where(e => e.ExpenseDate >= currentMonthStart && e.ExpenseDate <= currentMonthEnd)
        .SumAsync(e => (decimal?)e.Amount) ?? 0;

    dashboard.PreviousMonthAmount = await recognizedExpenses
        .Where(e => e.ExpenseDate >= prevMonthStart && e.ExpenseDate <= prevMonthEnd)
        .SumAsync(e => (decimal?)e.Amount) ?? 0;

    if (dashboard.PreviousMonthAmount > 0)
        dashboard.MonthOverMonthGrowth = Math.Round(
            ((dashboard.CurrentMonthAmount - dashboard.PreviousMonthAmount) 
            / dashboard.PreviousMonthAmount) * 100, 1);
    else if (dashboard.CurrentMonthAmount > 0)
        dashboard.MonthOverMonthGrowth = 100;

    // ── 2. Daily Average ──
    int daysInMonthPassed = today.Day;
    dashboard.DailyAverage = daysInMonthPassed > 0 
        ? Math.Round(dashboard.CurrentMonthAmount / daysInMonthPassed, 2) : 0;

    // ── 3. Active Advances ──
    var advanceHeadersQuery = advanceHeaders.Where(e => e.AdvanceMonths > 1);
    var advanceHeaderIds = await advanceHeadersQuery.Select(e => e.ExpenseId).ToListAsync();
    dashboard.ActiveAdvanceExpensesCount = advanceHeaderIds.Count;
    dashboard.ActiveAdvanceExpensesAmount = advanceHeaderIds.Count == 0
        ? 0m
        : (await _db.Expenses.AsNoTracking()
            .Where(e => e.AdvanceParentExpenseId.HasValue && advanceHeaderIds.Contains(e.AdvanceParentExpenseId.Value))
            .SumAsync(e => (decimal?)e.Amount)) ?? 0m;

    // ── 4. Group Distribution (Current Year - Top 10) ──
    var groupData = await recognizedExpenses
        .Where(e => e.ExpenseDate >= yearStart)
        .GroupBy(e => e.ExpenseGroupId)
        .Select(g => new { GroupId = g.Key, Total = g.Sum(x => x.Amount), Count = g.Count() })
        .ToListAsync();

    var groups = await _db.ExpenseGroups.AsNoTracking()
        .ToDictionaryAsync(g => g.ExpenseGroupId, g => g.ExpenseGroupName);

    var totalYearAmount = groupData.Sum(g => g.Total);
    var totalForPct = totalYearAmount == 0 ? 1 : totalYearAmount;

    dashboard.GroupDistribution = groupData.Select(x => new ExpenseGroupStatsDto
    {
        ExpenseGroupId = x.GroupId,
        GroupName = groups.GetValueOrDefault(x.GroupId, "غير محدد"),
        Total = x.Total,
        Count = x.Count,
        Percentage = Math.Round((x.Total / totalForPct) * 100, 1)
    }).OrderByDescending(x => x.Total).Take(10).ToList();

    // ── 5. Top 5 Expenses (Current Month) ──
    var topExpensesQuery = await recognizedExpenses
        .Where(e => e.ExpenseDate >= currentMonthStart && e.ExpenseDate <= currentMonthEnd)
        .OrderByDescending(e => e.Amount)
        .Take(5)
        .Select(e => new ExpenseListDto
        {
            ExpenseId = e.ExpenseId,
            ExpenseName = e.ExpenseName,
            ExpenseDate = e.ExpenseDate,
            Amount = e.Amount,
            ExpenseGroupId = e.ExpenseGroupId,
            Recipient = e.Torecipient
        })
        .ToListAsync();

    foreach (var topExp in topExpensesQuery)
        topExp.ExpenseGroupName = groups.GetValueOrDefault(topExp.ExpenseGroupId, "غير محدد");
    dashboard.TopExpenses = topExpensesQuery;

    // ── 6. Monthly Trends (Last 12 Months) ──
    var twelveMonthsAgo = currentMonthStart.AddMonths(-11);
    var trendData = await recognizedExpenses
        .Where(e => e.ExpenseDate >= twelveMonthsAgo && e.ExpenseDate <= currentMonthEnd)
        .GroupBy(e => new { e.ExpenseDate.Year, e.ExpenseDate.Month })
        .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(x => x.Amount) })
        .ToListAsync();

    for (int i = 11; i >= 0; i--)
    {
        var m = currentMonthStart.AddMonths(-i);
        var mData = trendData.FirstOrDefault(x => x.Year == m.Year && x.Month == m.Month);
        dashboard.MonthlyTrends.Add(new ExpenseMonthlyTrendDto
        {
            MonthDate = m,
            MonthName = m.ToString("MMM yyyy", new System.Globalization.CultureInfo("ar-EG")),
            TotalAmount = mData?.Total ?? 0
        });
    }

    // ── 7. ⭐ Daily Trend (Current Month) ──
    var dailyData = await recognizedExpenses
        .Where(e => e.ExpenseDate >= currentMonthStart && e.ExpenseDate <= currentMonthEnd)
        .GroupBy(e => e.ExpenseDate.Day)
        .Select(g => new { Day = g.Key, Total = g.Sum(x => x.Amount) })
        .ToListAsync();

    int daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
    for (int d = 1; d <= daysInMonth; d++)
    {
        dashboard.DailyTrend.Add(new ExpenseDailyTrendDto
        {
            Day = d,
            Amount = dailyData.FirstOrDefault(x => x.Day == d)?.Total ?? 0
        });
    }

    // ── 8. ⭐ Variance Analysis (Current Month vs Average of Last 3 Months) ──
    var threeMonthsAgo = currentMonthStart.AddMonths(-3);
    var currentMonthByGroup = await recognizedExpenses
        .Where(e => e.ExpenseDate >= currentMonthStart && e.ExpenseDate <= currentMonthEnd)
        .GroupBy(e => e.ExpenseGroupId)
        .Select(g => new { GroupId = g.Key, Total = g.Sum(x => x.Amount) })
        .ToListAsync();

    var prevThreeMonthsByGroup = await recognizedExpenses
        .Where(e => e.ExpenseDate >= threeMonthsAgo && e.ExpenseDate < currentMonthStart)
        .GroupBy(e => e.ExpenseGroupId)
        .Select(g => new { GroupId = g.Key, Total = g.Sum(x => x.Amount) })
        .ToListAsync();

    var allGroupIds = currentMonthByGroup.Select(x => x.GroupId)
        .Union(prevThreeMonthsByGroup.Select(x => x.GroupId)).Distinct();

    foreach (var gId in allGroupIds)
    {
        var currentAmount = currentMonthByGroup
            .FirstOrDefault(x => x.GroupId == gId)?.Total ?? 0;
        var prevAverage = (prevThreeMonthsByGroup
            .FirstOrDefault(x => x.GroupId == gId)?.Total ?? 0) / 3m;

        var variance = prevAverage > 0
            ? Math.Round(((currentAmount - prevAverage) / prevAverage) * 100, 1)
            : (currentAmount > 0 ? 100m : 0m);

        dashboard.VarianceAnalysis.Add(new ExpenseVarianceDto
        {
            GroupName = groups.GetValueOrDefault(gId, "غير محدد"),
            CurrentMonth = currentAmount,
            PreviousMonthsAverage = Math.Round(prevAverage, 2),
            VariancePercentage = variance
        });
    }

    dashboard.VarianceAnalysis = dashboard.VarianceAnalysis
        .OrderByDescending(x => Math.Abs(x.VariancePercentage)).ToList();

    // ── 9. ⭐ Pareto Analysis (80/20) - Year to Date ──
    var paretoData = groupData.OrderByDescending(x => x.Total).ToList();
    decimal cumulative = 0;
    foreach (var item in paretoData)
    {
        var pct = Math.Round((item.Total / totalForPct) * 100, 1);
        cumulative += pct;
        dashboard.ParetoAnalysis.Add(new ExpenseParetoDto
        {
            ItemName = groups.GetValueOrDefault(item.GroupId, "غير محدد"),
            Amount = item.Total,
            Percentage = pct,
            CumulativePercentage = Math.Round(cumulative, 1)
        });
    }

   
        return dashboard;
    }

    // ============================================================
    //  ⭐ حفظ مصروف (محسّن للتعديل والمصروف المقدم)
    // ============================================================
    public async Task<(bool Success, string Message, int? Id)> SaveExpenseAsync(
        ExpenseFormDto dto, string userName)
    {
        if (string.IsNullOrWhiteSpace(dto.ExpenseName))
            return (false, "اسم المصروف مطلوب", null);
        if (!dto.BranchId.HasValue) return (false, "الفرع مطلوب", null);
        if (dto.Amount <= 0) return (false, "المبلغ يجب أن يكون أكبر من صفر", null);
        if (dto.CashBoxId == null) return (false, "الخزينة مطلوبة", null);
        if (dto.ExpenseGroupId == null) return (false, "مجموعة المصروف مطلوبة", null);

        var selectedCashBox = await _db.CashBoxes.AsNoTracking()
            .Where(c => c.CashBoxId == dto.CashBoxId.Value)
            .Select(c => new { c.CashBoxId, c.BranchId, c.IsActive })
            .FirstOrDefaultAsync();

        if (selectedCashBox == null)
            return (false, "الخزينة المحددة غير موجودة", null);
        if (!selectedCashBox.IsActive)
            return (false, "الخزينة المحددة غير نشطة", null);
        if (!selectedCashBox.BranchId.HasValue)
            return (false, "الخزينة المحددة غير مربوطة بفرع", null);
        if (selectedCashBox.BranchId.Value != dto.BranchId.Value)
            return (false, "لا يمكن اختيار خزينة من فرع مختلف عن فرع المصروف", null);

        var branchExists = await _db.Branches.AsNoTracking()
            .AnyAsync(b => b.BranchId == dto.BranchId.Value && b.IsActive);
        if (!branchExists)
            return (false, "الفرع المحدد غير موجود أو غير نشط", null);

        var isNew = dto.ExpenseId == 0;

        // ⛔ منع تعديل مصروف مقدم بعد الحفظ (سواء أصل أو شهر فرعي)
        if (!isNew)
        {
            var existing = await _db.Expenses.AsNoTracking()
                .FirstOrDefaultAsync(e => e.ExpenseId == dto.ExpenseId);
            if (existing == null) return (false, "المصروف غير موجود", null);

            // الشهر الفرعي ممنوع تعديله
            if (existing.AdvanceParentExpenseId.HasValue)
                return (false, "هذا شهر فرعي من مصروف مقدم - لا يمكن تعديله. عدّل الأصل أو احذفه.", null);

            // الأصل المقدم ممنوع تعديله
            if (existing.AdvanceMonthIndex == 0 && (existing.IsAdvance ?? false))
                return (false, "لا يمكن تعديل مصروف مقدم بعد الحفظ. احذفه وأنشئ واحد جديد.", null);
        }

        // Validation للمصروف المقدم (لا يكون عند التعديل)
        if (dto.IsAdvance && isNew)
        {
            if (dto.AdvanceMonths == null || dto.AdvanceMonths < 1)
                return (false, "عدد الشهور يجب أن يكون على الأقل 1", null);
            if (dto.AdvanceMonths > 60)
                return (false, "الحد الأقصى لعدد الشهور هو 60 شهر", null);
        }

        // ⚠️ التحقق من رصيد الخزينة (للمصروف الجديد + التعديل بمبلغ أكبر)
        if (isNew)
        {
            var cashBoxBalance = await GetCashBoxBalanceAsync(dto.CashBoxId.Value);
            if (cashBoxBalance < dto.Amount)
                return (false, $"رصيد الخزينة غير كافي. المتاح: {cashBoxBalance:N2}", null);
        }
        else
        {
            // عند التعديل: نجيب بيانات المصروف القديم (المبلغ + الخزينة القديمة)
            var existingExpense = await _db.Expenses.AsNoTracking()
                .Where(e => e.ExpenseId == dto.ExpenseId)
                .Select(e => new { e.Amount, e.CashBoxId })
                .FirstOrDefaultAsync()
                ?? throw new Exception("المصروف غير موجود");

            var oldAmount = existingExpense.Amount;
            var cashBoxChanged = existingExpense.CashBoxId != dto.CashBoxId.Value;

            // ⭐ المبلغ الفعلي اللي الخزينة هتدفعه بعد التعديل:
            //  - لو الخزينة اتغيرت عن القديمة: الخزينة الجديدة هتسدّد كامل المبلغ
            //    (لأن الخصم القديم كان على الخزينة التانية وهيتشال منها تلقائياً)
            //  - لو نفس الخزينة: القديم متخصوم منها أصلًا → نحتاج فقط تأمين قيمة الزيادة (الفرق)
            var requiredAmount = cashBoxChanged
                ? dto.Amount
                : Math.Max(dto.Amount - oldAmount, 0m);

            if (requiredAmount > 0)
            {
                var cashBoxBalance = await GetCashBoxBalanceAsync(dto.CashBoxId.Value);
                if (cashBoxBalance < requiredAmount)
                    return (false, "رصيد الخزينة غير كافي" +
                        (cashBoxChanged
                            ? $". الخزينة الجديدة هتدفع المبلغ كامل ({requiredAmount:N2}) والمتاح: {cashBoxBalance:N2}"
                            : $". قيمة الزيادة المطلوبة: {requiredAmount:N2} والمتاح: {cashBoxBalance:N2}"), null);
            }
        }

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            Expense entity;
            object? oldEntity = null;

            if (isNew)
            {
                entity = new Expense
                {
                    CreatedBy = userName,
                    CreatedAt = DateTime.Now,
                    AdvanceMonthIndex = dto.IsAdvance ? 0 : null
                };
                _db.Expenses.Add(entity);
            }
            else
            {
                entity = await _db.Expenses.FindAsync(dto.ExpenseId)
                    ?? throw new Exception("المصروف غير موجود");

                // احفظ نسخة من القديم للـ Audit
                oldEntity = new
                {
                    entity.ExpenseId,
                    entity.BranchId,
                    entity.ExpenseName,
                    entity.ExpenseDate,
                    entity.Amount,
                    entity.ExpenseGroupId,
                    entity.CashBoxId,
                    entity.Notes,
                    entity.Torecipient
                };

                // احذف الـ CashboxTransaction القديم (للمصروف العادي فقط)
                var oldTrans = await _db.CashboxTransactions
                    .Where(t => t.ReferenceType == CashBoxRefTypes.Expense
                        && t.ReferenceId == entity.ExpenseId).ToListAsync();
                _db.CashboxTransactions.RemoveRange(oldTrans);
            }

            entity.BranchId = dto.BranchId.Value;
            entity.ExpenseName = dto.ExpenseName;
            entity.ExpenseDate = dto.ExpenseDate;
            entity.ExpenseGroupId = dto.ExpenseGroupId.Value;
            entity.CashBoxId = dto.CashBoxId.Value;
            entity.Notes = dto.Notes;
            entity.Torecipient = dto.Recipient;

            // ⭐ منطق المصروف المقدم (فقط للجديد)
            if (isNew && dto.IsAdvance && dto.AdvanceMonths > 1)
            {
                entity.IsAdvance = true;
                entity.AdvanceMonths = dto.AdvanceMonths;
                entity.Amount = 0m;
                entity.AdvanceMonthIndex = 0;

                await _db.SaveChangesAsync();

                var monthlyAmount = Math.Round(dto.Amount / dto.AdvanceMonths.Value, 2);
                var totalCalc = monthlyAmount * dto.AdvanceMonths.Value;
                var difference = dto.Amount - totalCalc;

                // اعمل سجلات الأشهر الفرعية
                for (int i = 1; i <= dto.AdvanceMonths.Value; i++)
                {
                    var monthDate = dto.ExpenseDate.AddMonths(i - 1);
                    var amount = i == dto.AdvanceMonths.Value
                        ? monthlyAmount + difference
                        : monthlyAmount;

                    var childExpense = new Expense
                    {
                        ExpenseGroupId = entity.ExpenseGroupId,
                        BranchId = entity.BranchId,
                        ExpenseName = $"{entity.ExpenseName} - شهر {i}/{dto.AdvanceMonths.Value}",
                        ExpenseDate = monthDate,
                        Amount = amount,
                        CashBoxId = entity.CashBoxId,
                        IsAdvance = true,
                        AdvanceMonths = dto.AdvanceMonths,
                        AdvanceParentExpenseId = entity.ExpenseId,
                        AdvanceMonthIndex = i,
                        Notes = $"الشهر رقم {i} من المصروف المقدم",
                        Torecipient = entity.Torecipient,
                        CreatedBy = userName,
                        CreatedAt = DateTime.Now
                    };
                    _db.Expenses.Add(childExpense);
                }
                await _db.SaveChangesAsync();

                // خصم من الخزينة مرة واحدة
                _db.CashboxTransactions.Add(new CashboxTransaction
                {
                    CashBoxId = entity.CashBoxId,
                    TransactionType = "صرف",
                    ReferenceType = CashBoxRefTypes.Expense,
                    ReferenceId = entity.ExpenseId,
                    Amount = dto.Amount,
                    TransactionDate = entity.ExpenseDate,
                    Notes = $"مصروف مقدم: {entity.ExpenseName} ({dto.AdvanceMonths} شهور)",
                    CreatedBy = userName,
                    CreatedAt = DateTime.Now
                });
            }
            else
            {
                // مصروف عادي (جديد أو تعديل)
                entity.Amount = dto.Amount;
                entity.IsAdvance = false;
                entity.AdvanceMonths = null;
                entity.AdvanceMonthIndex = null;
                await _db.SaveChangesAsync();

                _db.CashboxTransactions.Add(new CashboxTransaction
                {
                    CashBoxId = entity.CashBoxId,
                    TransactionType = "صرف",
                    ReferenceType = CashBoxRefTypes.Expense,
                    ReferenceId = entity.ExpenseId,
                    Amount = entity.Amount,
                    TransactionDate = entity.ExpenseDate,
                    Notes = $"مصروف: {entity.ExpenseName}" +
                            (string.IsNullOrEmpty(entity.Torecipient) ? "" : $" - {entity.Torecipient}"),
                    CreatedBy = userName,
                    CreatedAt = DateTime.Now
                });
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            // ✅ Audit مع تفاصيل القديم والجديد
            await _audit.LogAsync<object>("Expenses", isNew ? "Insert" : "Update",
                entity.ExpenseId.ToString(), oldEntity, entity, userName);

            var msg = dto.IsAdvance && dto.AdvanceMonths > 1 && isNew
                ? $"تم تسجيل المصروف المقدم على {dto.AdvanceMonths} شهور وخصم {dto.Amount:N2} من الخزينة"
                : (isNew ? "تم تسجيل المصروف وخصمه من الخزينة" : "تم تحديث المصروف بنجاح");

            return (true, msg, entity.ExpenseId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return (false, $"حدث خطأ: {ex.Message}", null);
        }
    }

    private async Task<decimal> GetCashBoxBalanceAsync(int cashBoxId)
    {
        var totalIn = await _db.CashboxTransactions
            .Where(t => t.CashBoxId == cashBoxId && t.TransactionType == "قبض")
            .SumAsync(t => (decimal?)t.Amount) ?? 0;
        var totalOut = await _db.CashboxTransactions
            .Where(t => t.CashBoxId == cashBoxId && t.TransactionType == "صرف")
            .SumAsync(t => (decimal?)t.Amount) ?? 0;
        return totalIn - totalOut;
    }

    public async Task<(bool Success, string Message)> DeleteExpenseAsync(int id, string userName)
    {
        var expense = await _db.Expenses.FindAsync(id);
        if (expense == null) return (false, "المصروف غير موجود");

        int parentId = expense.AdvanceParentExpenseId ?? expense.ExpenseId;

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var allRelated = await _db.Expenses
                .Where(e => e.ExpenseId == parentId || e.AdvanceParentExpenseId == parentId)
                .ToListAsync();

            var trans = await _db.CashboxTransactions
                .Where(t => t.ReferenceType == CashBoxRefTypes.Expense
                    && t.ReferenceId == parentId).ToListAsync();
            _db.CashboxTransactions.RemoveRange(trans);

            _db.Expenses.RemoveRange(allRelated);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            await _audit.LogAsync<object>("Expenses", "Delete",
                id.ToString(), expense, null, userName);

            var msg = allRelated.Count > 1
                ? $"تم حذف المصروف وكل الـ {allRelated.Count - 1} شهور المرتبطة وردّ المبلغ للخزينة"
                : "تم حذف المصروف وردّ المبلغ للخزينة";

            return (true, msg);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return (false, $"حدث خطأ: {ex.Message}");
        }
    }

    // ============================================================
    //  Expense Groups
    // ============================================================
    public async Task<List<ExpenseGroupDto>> GetExpenseGroupsAsync(bool asTree = false)
    {
        var groups = await _db.ExpenseGroups.AsNoTracking()
            .OrderBy(g => g.ExpenseGroupName).ToListAsync();

        // ✅ تحسين: حساب الإجماليات لكل المجموعات بـ query واحد
        var groupTotals = await BuildRecognizedExpensesQuery()
            .GroupBy(e => e.ExpenseGroupId)
            .Select(g => new { GroupId = g.Key, Total = g.Sum(x => x.Amount), Count = g.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => new { x.Total, x.Count });

        var dtos = groups.Select(g =>
        {
            var totals = groupTotals.GetValueOrDefault(g.ExpenseGroupId);
            var childrenCount = groups.Count(x => x.ParentGroupId == g.ExpenseGroupId);

            return new ExpenseGroupDto
            {
                ExpenseGroupId = g.ExpenseGroupId,
                ExpenseGroupName = g.ExpenseGroupName,
                ParentGroupId = g.ParentGroupId,
                ParentGroupName = g.ParentGroupId.HasValue
                    ? groups.FirstOrDefault(x => x.ExpenseGroupId == g.ParentGroupId.Value)?.ExpenseGroupName
                    : null,
                ChildrenCount = childrenCount,
                ExpensesCount = totals?.Count ?? 0,
                TotalAmount = totals?.Total ?? 0,
                CreatedBy = g.CreatedBy,
                CreatedAt = g.CreatedAt
            };
        }).ToList();

        if (!asTree) return dtos;

        var tree = dtos.Where(d => d.ParentGroupId == null).ToList();
        foreach (var root in tree)
            BuildChildren(root, dtos);
        return tree;
    }

    private void BuildChildren(ExpenseGroupDto parent, List<ExpenseGroupDto> all)
    {
        parent.Children = all.Where(x => x.ParentGroupId == parent.ExpenseGroupId).ToList();
        foreach (var child in parent.Children)
            BuildChildren(child, all);
    }

    private async Task<HashSet<int>> GetExpenseGroupDescendantIdsAsync(int parentGroupId)
    {
        var allGroups = await _db.ExpenseGroups.AsNoTracking()
            .Select(g => new { g.ExpenseGroupId, g.ParentGroupId })
            .ToListAsync();

        var childrenLookup = allGroups
            .Where(g => g.ParentGroupId.HasValue)
            .GroupBy(g => g.ParentGroupId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ExpenseGroupId).ToList());

        var result = new HashSet<int>();
        var stack = new Stack<int>();
        stack.Push(parentGroupId);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!childrenLookup.TryGetValue(current, out var children))
                continue;

            foreach (var childId in children)
            {
                if (result.Add(childId))
                    stack.Push(childId);
            }
        }

        return result;
    }

    public async Task<ExpenseGroupDto?> GetExpenseGroupByIdAsync(int id)
    {
        var groups = await GetExpenseGroupsAsync(false);
        return groups.FirstOrDefault(g => g.ExpenseGroupId == id);
    }

    public async Task<(bool Success, string Message, int? Id)> SaveExpenseGroupAsync(
        ExpenseGroupFormDto dto, string userName)
    {
        if (string.IsNullOrWhiteSpace(dto.ExpenseGroupName))
            return (false, "اسم المجموعة مطلوب", null);

        // ⛔ منع الدورات (Cycles) في شجرة المجموعات قبل أي حفظ
        if (dto.ParentGroupId.HasValue)
        {
            // الأب لا يكون المجموعة نفسها (أثناء التعديل)
            if (dto.ExpenseGroupId != 0 && dto.ParentGroupId.Value == dto.ExpenseGroupId)
                return (false, "لا يمكن جعل المجموعة أبًا لنفسها", null);

            // الأب المحدد يجب أن يكون موجودًا فعلًا
            var parentExists = await _db.ExpenseGroups.AsNoTracking()
                .AnyAsync(g => g.ExpenseGroupId == dto.ParentGroupId.Value);
            if (!parentExists)
                return (false, "المجموعة الأم المحددة غير موجودة", null);

            // أثناء التعديل: لا يصح اختيار مجموعة من أحفاد هذه المجموعة كأب لها
            // (وإلا أصبحت المجموعة أبًا لجدّها → دورة تنهار معها الشجرة)
            if (dto.ExpenseGroupId != 0)
            {
                var descendantIds = await GetExpenseGroupDescendantIdsAsync(dto.ExpenseGroupId);
                if (descendantIds.Contains(dto.ParentGroupId.Value))
                    return (false,
                        "لا يمكن جعل مجموعة فرعية من هذه المجموعة أبًا لها — هذا سينشئ دورة في الشجرة",
                        null);
            }
        }

        try
        {
            var isNew = dto.ExpenseGroupId == 0;
            ExpenseGroup entity;

            if (isNew)
            {
                entity = new ExpenseGroup
                {
                    CreatedBy = userName,
                    CreatedAt = DateTime.Now
                };
                _db.ExpenseGroups.Add(entity);
            }
            else
            {
                entity = await _db.ExpenseGroups.FindAsync(dto.ExpenseGroupId)
                    ?? throw new Exception("المجموعة غير موجودة");
            }

            entity.ExpenseGroupName = dto.ExpenseGroupName;
            entity.ParentGroupId = dto.ParentGroupId;

            await _db.SaveChangesAsync();
            await _audit.LogAsync<object>("ExpenseGroups", isNew ? "Insert" : "Update",
                entity.ExpenseGroupId.ToString(), null, entity, userName);

            return (true, isNew ? "تم إضافة المجموعة" : "تم التحديث", entity.ExpenseGroupId);
        }
        catch (Exception ex)
        {
            return (false, $"حدث خطأ: {ex.Message}", null);
        }
    }

    public async Task<(bool Success, string Message)> DeleteExpenseGroupAsync(int id, string userName)
    {
        var group = await _db.ExpenseGroups.FindAsync(id);
        if (group == null) return (false, "المجموعة غير موجودة");

        var hasExpenses = await _db.Expenses.AnyAsync(e => e.ExpenseGroupId == id);
        if (hasExpenses) return (false, "لا يمكن حذف مجموعة بها مصروفات");

        var hasChildren = await _db.ExpenseGroups.AnyAsync(g => g.ParentGroupId == id);
        if (hasChildren) return (false, "لا يمكن حذف مجموعة بها مجموعات فرعية");

        _db.ExpenseGroups.Remove(group);
        await _db.SaveChangesAsync();
        await _audit.LogAsync<object>("ExpenseGroups", "Delete", id.ToString(), group, null, userName);

        return (true, "تم الحذف");
    }

    // ============================================================
    //  ⭐ Helper: بناء مسار المجموعة من Dictionary (سريع جداً)
    // ============================================================
    private string? BuildGroupPathFromDict(int groupId,
    Dictionary<int, (string Name, int? ParentId)> groups)
{
    if (!groups.ContainsKey(groupId)) return null;

    var path = new List<string>();
    int? currentId = groupId;
    int safety = 10;

    while (currentId.HasValue && safety-- > 0)
    {
        if (!groups.ContainsKey(currentId.Value)) break;
        var g = groups[currentId.Value];
        path.Insert(0, g.Name);
        currentId = g.ParentId;
    }

    return string.Join(" > ", path);
}

    // محتفظ بالنسخة القديمة للـ Backward Compatibility
    private async Task<string?> GetGroupFullPathAsync(int groupId)
    {
        var groups = await _db.ExpenseGroups.AsNoTracking()
            .ToDictionaryAsync(g => g.ExpenseGroupId,
                g => new { g.ExpenseGroupName, g.ParentGroupId });

        if (!groups.ContainsKey(groupId)) return null;

        var path = new List<string>();
        int? currentId = groupId;
        int safety = 10;
        while (currentId.HasValue && safety-- > 0)
        {
            var g = groups[currentId.Value];
            path.Insert(0, g.ExpenseGroupName);
            currentId = g.ParentGroupId;
        }

        return string.Join(" > ", path);
    }
        // ============================================================
    //  ⭐ تصدير إكسيل احترافي (ClosedXML)
    // ============================================================
    public async Task<byte[]> ExportExpensesToExcelAsync(ExpenseFilterDto filter)
    {
        // سحب كل البيانات المطابقة للفلتر
        filter.PageNumber = 1;
        filter.PageSize = int.MaxValue;
        var data = await GetExpensesAsync(filter);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("تقرير المصروفات");
        ws.RightToLeft = true; // اتجاه الشيت عربي

        // تنسيق الهيدر
        var headerRow = ws.Row(1);
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Font.FontColor = XLColor.White;
        headerRow.Style.Fill.BackgroundColor = XLColor.MidnightBlue;
        headerRow.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Cell(1, 1).Value = "التاريخ";
        ws.Cell(1, 2).Value = "الفرع";
        ws.Cell(1, 3).Value = "اسم المصروف / البيان";
        ws.Cell(1, 4).Value = "المجموعة / النوع";
        ws.Cell(1, 5).Value = "الخزينة";
        ws.Cell(1, 6).Value = "المستلم";
        ws.Cell(1, 7).Value = "المبلغ";
        ws.Cell(1, 8).Value = "ملاحظات";

        int row = 2;
        decimal totalAmount = 0;

        foreach (var exp in data.Items)
        {
            ws.Cell(row, 1).Value = exp.ExpenseDate.ToString("yyyy/MM/dd");
            ws.Cell(row, 2).Value = exp.BranchName ?? "غير محدد";
            
            string advanceTag = exp.IsAdvance ? $" (مقدم {exp.AdvanceMonths} أشهر)" : "";
            ws.Cell(row, 3).Value = exp.ExpenseName + advanceTag;
            
            ws.Cell(row, 4).Value = exp.FullGroupPath ?? exp.ExpenseGroupName;
            ws.Cell(row, 5).Value = exp.CashBoxName;
            ws.Cell(row, 6).Value = exp.Recipient;
            
            ws.Cell(row, 7).Value = exp.Amount;
            ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0.00";
            
            ws.Cell(row, 8).Value = exp.Notes;

            totalAmount += exp.Amount;
            row++;
        }

        // سطر الإجمالي
        var totalRow = ws.Row(row);
        totalRow.Style.Font.Bold = true;
        totalRow.Style.Fill.BackgroundColor = XLColor.LightGray;
        
        ws.Cell(row, 6).Value = "الإجمالي الكلي:";
        ws.Cell(row, 7).Value = totalAmount;
        ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(row, 7).Style.Font.FontColor = XLColor.DarkRed;

        // تظبيط عرض الأعمدة تلقائياً
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ============================================================
    //  🆕 التقرير الإجمالي الشهري (Summary)
    // ============================================================
    public async Task<ExpenseSummaryReportDto> GetMonthlySummaryAsync(ExpenseFilterDto filter)
    {
        var query = await ApplyExpenseFiltersAsync(
            _db.Expenses.AsNoTracking().AsQueryable(), filter);

        var grouped = await query
            .GroupBy(e => new { e.ExpenseDate.Year, e.ExpenseDate.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Count = g.Count(),
                Total = g.Sum(x => x.Amount)
            })
            .ToListAsync();

        var byMonth = grouped.ToDictionary(
            x => (x.Year, x.Month),
            x => (x.Count, x.Total));

        // بناء خط زمني متصل (شهور صفرية تُملأ) من بداية الفلتر إلى نهايته
        DateTime start;
        DateTime end;

        if (filter.DateFrom.HasValue)
            start = new DateTime(filter.DateFrom.Value.Year, filter.DateFrom.Value.Month, 1);
        else if (grouped.Any())
            start = new DateTime(grouped.Min(g => g.Year), grouped.Min(g => g.Month), 1);
        else
            start = DateTime.Today;

        if (filter.DateTo.HasValue)
            end = new DateTime(filter.DateTo.Value.Year, filter.DateTo.Value.Month, 1);
        else if (grouped.Any())
            end = new DateTime(grouped.Max(g => g.Year), grouped.Max(g => g.Month), 1);
        else
            end = start;

        var ar = new System.Globalization.CultureInfo("ar-EG");
        var result = new ExpenseSummaryReportDto();

        var monthItems = new List<ExpenseMonthlySummaryDto>();

        for (var dt = start; dt <= end; dt = dt.AddMonths(1))
        {
            var has = byMonth.TryGetValue((dt.Year, dt.Month), out var data);
            monthItems.Add(new ExpenseMonthlySummaryDto
            {
                MonthDate = dt,
                MonthLabel = dt.ToString("MMMM yyyy", ar),
                MonthShort = dt.ToString("MMM", ar),
                Count = has ? data.Count : 0,
                Total = has ? data.Total : 0m
            });
        }

        for (int i = 0; i < monthItems.Count; i++)
        {
            var item = monthItems[i];
            if (i > 0)
            {
                var prev = monthItems[i - 1].Total;
                item.HasPrevious = true;
                item.PreviousTotal = prev;
                item.ChangePercent = prev > 0
                    ? Math.Round(((item.Total - prev) / prev) * 100, 1)
                    : (item.Total > 0 ? 100m : 0m);
            }

            result.Months.Add(item);
            result.TotalCount += item.Count;
            result.GrandTotal += item.Total;
        }

        result.MonthlyAverage = result.Months.Count > 0
            ? Math.Round(result.GrandTotal / result.Months.Count, 2)
            : 0;

        return result;
    }

    // ============================================================
    //  🆕 تصدير التقرير الإجمالي الشهري لإكسيل
    // ============================================================
    public async Task<byte[]> ExportMonthlySummaryToExcelAsync(ExpenseFilterDto filter)
    {
        var data = await GetMonthlySummaryAsync(filter);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("ملخص المصروفات الشهري");
        ws.RightToLeft = true;

        var headerRow = ws.Row(1);
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Font.FontColor = XLColor.White;
        headerRow.Style.Fill.BackgroundColor = XLColor.MidnightBlue;
        headerRow.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Cell(1, 1).Value = "الشهر";
        ws.Cell(1, 2).Value = "عدد العمليات";
        ws.Cell(1, 3).Value = "الإجمالي";
        ws.Cell(1, 4).Value = "التغير عن السابق %";

        int row = 2;
        foreach (var m in data.Months)
        {
            ws.Cell(row, 1).Value = m.MonthLabel;
            ws.Cell(row, 2).Value = m.Count;
            ws.Cell(row, 3).Value = m.Total;
            ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0.00";

            if (m.HasPrevious)
            {
                ws.Cell(row, 4).Value = m.ChangePercent;
                ws.Cell(row, 4).Style.NumberFormat.Format = "0.0";
            }
            else
            {
                ws.Cell(row, 4).Value = "—";
            }

            row++;
        }

        var totalRow = ws.Row(row);
        totalRow.Style.Font.Bold = true;
        totalRow.Style.Fill.BackgroundColor = XLColor.LightGray;

        ws.Cell(row, 1).Value = "الإجمالي";
        ws.Cell(row, 2).Value = data.TotalCount;
        ws.Cell(row, 3).Value = data.GrandTotal;
        ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0.00";

        ws.Cell(row + 1, 1).Value = "متوسط الشهر";
        ws.Cell(row + 1, 3).Value = data.MonthlyAverage;
        ws.Cell(row + 1, 3).Style.NumberFormat.Format = "#,##0.00";

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
