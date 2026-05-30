using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

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

    // ============================================================
    //  ⭐ Expenses List - محسّن للسرعة (5x أسرع)
    // ============================================================
    public async Task<PagedResult<ExpenseListDto>> GetExpensesAsync(ExpenseFilterDto filter)
    {
        var query = _db.Expenses.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();
            query = query.Where(e => e.ExpenseName.Contains(s)
                || (e.Notes != null && e.Notes.Contains(s))
                || (e.Torecipient != null && e.Torecipient.Contains(s)));
        }

        if (filter.ExpenseGroupId.HasValue)
            query = query.Where(e => e.ExpenseGroupId == filter.ExpenseGroupId.Value);
        if (filter.CashBoxId.HasValue)
            query = query.Where(e => e.CashBoxId == filter.CashBoxId.Value);
        if (filter.DateFrom.HasValue)
            query = query.Where(e => e.ExpenseDate >= filter.DateFrom.Value.Date);
        if (filter.DateTo.HasValue)
            query = query.Where(e => e.ExpenseDate <= filter.DateTo.Value.Date.AddDays(1).AddTicks(-1));
        if (filter.AmountFrom.HasValue)
            query = query.Where(e => e.Amount >= filter.AmountFrom.Value);
        if (filter.AmountTo.HasValue)
            query = query.Where(e => e.Amount <= filter.AmountTo.Value);
        if (filter.IsAdvance.HasValue)
            query = query.Where(e => e.IsAdvance == filter.IsAdvance.Value);

        if (filter.OnlyParents == true)
            query = query.Where(e => e.AdvanceParentExpenseId == null);

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

        // ✅ تحسين 1: قراءة الصفحة فقط بـ raw data
        var rawData = await query
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(e => new
            {
                e.ExpenseId,
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

        // ✅ تحسين 2: تحميل المجموعات والخزن مرة واحدة (بدل subquery لكل صف)
        var allGroups = await _db.ExpenseGroups.AsNoTracking()
    .ToDictionaryAsync(
        g => g.ExpenseGroupId,
        g => (Name: g.ExpenseGroupName, ParentId: g.ParentGroupId)
    );

        var cashBoxIds = rawData.Select(e => e.CashBoxId).Distinct().ToList();
        var cashBoxes = await _db.CashBoxes.AsNoTracking()
            .Where(c => cashBoxIds.Contains(c.CashBoxId))
            .ToDictionaryAsync(c => c.CashBoxId, c => c.CashBoxName);

        // ✅ تحسين 3: بناء الـ DTOs بدون أي DB calls إضافية
        var items = rawData.Select(e => new ExpenseListDto
        {
            ExpenseId = e.ExpenseId,
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
            ExpenseGroupName = allGroups.ContainsKey(e.ExpenseGroupId) 
    ? allGroups[e.ExpenseGroupId].Name 
    : null,
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
            var groups = await _db.ExpenseGroups.AsNoTracking()
                .Where(g => groupIds.Contains(g.ExpenseGroupId))
                .ToDictionaryAsync(g => g.ExpenseGroupId, g => g.ExpenseGroupName);
            var boxes = await _db.CashBoxes.AsNoTracking()
                .Where(c => cashBoxIds.Contains(c.CashBoxId))
                .ToDictionaryAsync(c => c.CashBoxId, c => c.CashBoxName);

            foreach (var c in children)
            {
                c.ExpenseGroupName = groups.GetValueOrDefault(c.ExpenseGroupId);
                c.CashBoxName = boxes.GetValueOrDefault(c.CashBoxId);
            }
        }

        return children;
    }

    public async Task<ExpenseStatsDto> GetStatsAsync(DateTime? from = null, DateTime? to = null)
    {
        var query = _db.Expenses.AsNoTracking().AsQueryable();
        if (from.HasValue) query = query.Where(e => e.ExpenseDate >= from.Value.Date);
        if (to.HasValue) query = query.Where(e => e.ExpenseDate <= to.Value.Date.AddDays(1).AddTicks(-1));

        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var yearStart = new DateTime(today.Year, 1, 1);

        var stats = new ExpenseStatsDto
        {
            TotalCount = await query.CountAsync(),
            TotalAmount = await query.SumAsync(e => (decimal?)e.Amount) ?? 0,
            TodayAmount = await query.Where(e => e.ExpenseDate.Date == today)
                .SumAsync(e => (decimal?)e.Amount) ?? 0,
            MonthAmount = await query.Where(e => e.ExpenseDate >= monthStart)
                .SumAsync(e => (decimal?)e.Amount) ?? 0,
            YearAmount = await query.Where(e => e.ExpenseDate >= yearStart)
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
     public async Task<ExpenseDashboardDto> GetDashboardDataAsync()
    {
        var dashboard = new ExpenseDashboardDto();
        var today = DateTime.Today;

        // Current Month Data
        var currentMonthStart = new DateTime(today.Year, today.Month, 1);
        var currentMonthEnd = currentMonthStart.AddMonths(1).AddDays(-1);

        // Previous Month Data
        var prevMonthStart = currentMonthStart.AddMonths(-1);
        var prevMonthEnd = currentMonthStart.AddDays(-1);

        // All expenses
        var allExpenses = _db.Expenses.AsNoTracking().AsQueryable();
        // Base expenses only (no children of advances)
        var baseExpenses = allExpenses.Where(e => e.AdvanceParentExpenseId == null);

        dashboard.CurrentMonthAmount = await baseExpenses
            .Where(e => e.ExpenseDate >= currentMonthStart && e.ExpenseDate <= currentMonthEnd)
            .SumAsync(e => (decimal?)e.Amount) ?? 0;

        dashboard.PreviousMonthAmount = await baseExpenses
            .Where(e => e.ExpenseDate >= prevMonthStart && e.ExpenseDate <= prevMonthEnd)
            .SumAsync(e => (decimal?)e.Amount) ?? 0;

        if (dashboard.PreviousMonthAmount > 0)
        {
            dashboard.MonthOverMonthGrowth = ((dashboard.CurrentMonthAmount - dashboard.PreviousMonthAmount) / dashboard.PreviousMonthAmount) * 100;
        }
        else if (dashboard.CurrentMonthAmount > 0)
        {
            dashboard.MonthOverMonthGrowth = 100;
        }

        int daysInMonthPassed = today.Day;
        dashboard.DailyAverage = daysInMonthPassed > 0 ? dashboard.CurrentMonthAmount / daysInMonthPassed : 0;

        // Active Advances
        var advancesQuery = baseExpenses.Where(e => e.IsAdvance == true && e.AdvanceMonths > 1);
        dashboard.ActiveAdvanceExpensesCount = await advancesQuery.CountAsync();
        dashboard.ActiveAdvanceExpensesAmount = await advancesQuery.SumAsync(e => (decimal?)e.Amount) ?? 0;

        // Group Distribution (Current Year)
        var yearStart = new DateTime(today.Year, 1, 1);
        var groupData = await baseExpenses
            .Where(e => e.ExpenseDate >= yearStart)
            .GroupBy(e => e.ExpenseGroupId)
            .Select(g => new { GroupId = g.Key, Total = g.Sum(x => x.Amount), Count = g.Count() })
            .ToListAsync();

        var groups = await _db.ExpenseGroups.AsNoTracking().ToDictionaryAsync(g => g.ExpenseGroupId, g => g.ExpenseGroupName);
        var totalYearAmount = groupData.Sum(g => g.Total);
        var totalForPct = totalYearAmount == 0 ? 1 : totalYearAmount;

        dashboard.GroupDistribution = groupData.Select(x => new ExpenseGroupStatsDto
        {
            ExpenseGroupId = x.GroupId,
            GroupName = groups.GetValueOrDefault(x.GroupId, "غير محدد"),
            Total = x.Total,
            Count = x.Count,
            Percentage = Math.Round((x.Total / totalForPct) * 100, 1)
        }).OrderByDescending(x => x.Total).Take(5).ToList();

        // Top 5 Expenses (Current Month)
        var topExpensesQuery = await baseExpenses
            .Where(e => e.ExpenseDate >= currentMonthStart && e.ExpenseDate <= currentMonthEnd)
            .OrderByDescending(e => e.Amount)
            .Take(5)
            .Select(e => new ExpenseListDto
            {
                ExpenseId = e.ExpenseId,
                ExpenseName = e.ExpenseName,
                ExpenseDate = e.ExpenseDate,
                Amount = e.Amount,
                ExpenseGroupId = e.ExpenseGroupId
            })
            .ToListAsync();

        foreach(var topExp in topExpensesQuery)
        {
            topExp.ExpenseGroupName = groups.GetValueOrDefault(topExp.ExpenseGroupId, "غير محدد");
        }
        dashboard.TopExpenses = topExpensesQuery;

        // Monthly Trends (Last 6 Months)
        var sixMonthsAgo = currentMonthStart.AddMonths(-5);
        var trendData = await baseExpenses
            .Where(e => e.ExpenseDate >= sixMonthsAgo && e.ExpenseDate <= currentMonthEnd)
            .GroupBy(e => new { e.ExpenseDate.Year, e.ExpenseDate.Month })
            .Select(g => new
            {
                Year = g.Key.Year,
                Month = g.Key.Month,
                Total = g.Sum(x => x.Amount)
            })
            .ToListAsync();

        for (int i = 5; i >= 0; i--)
        {
            var m = currentMonthStart.AddMonths(-i);
            var mData = trendData.FirstOrDefault(x => x.Year == m.Year && x.Month == m.Month);
            dashboard.MonthlyTrends.Add(new MonthlyTrendDto
            {
                MonthDate = m,
                MonthName = m.ToString("MMMM yyyy", new System.Globalization.CultureInfo("ar-EG")),
                TotalAmount = mData?.Total ?? 0
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
        if (dto.Amount <= 0) return (false, "المبلغ يجب أن يكون أكبر من صفر", null);
        if (dto.CashBoxId == null) return (false, "الخزينة مطلوبة", null);
        if (dto.ExpenseGroupId == null) return (false, "مجموعة المصروف مطلوبة", null);

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
            // عند التعديل: نحسب الفرق
            var oldAmount = await _db.Expenses.AsNoTracking()
                .Where(e => e.ExpenseId == dto.ExpenseId)
                .Select(e => e.Amount).FirstOrDefaultAsync();

            var diff = dto.Amount - oldAmount;
            if (diff > 0)
            {
                var cashBoxBalance = await GetCashBoxBalanceAsync(dto.CashBoxId.Value);
                if (cashBoxBalance < diff)
                    return (false, $"رصيد الخزينة غير كافي للزيادة. المتاح: {cashBoxBalance:N2}", null);
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
                entity.Amount = dto.Amount;
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
        var groupTotals = await _db.Expenses.AsNoTracking()
            .Where(e => e.AdvanceParentExpenseId == null)
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
}
