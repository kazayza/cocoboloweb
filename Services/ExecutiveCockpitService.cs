using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

// ════════════════════════════════════════════════════════════
//  لوحة القيادة التنفيذية — كل الأرقام من مصادر حقيقية:
//  Transactions · Expenses · AuditLogs · Attendances · CrmTasks
//  LeadsCRMs · Complaints · PriceChangeRequests · StockLevels
// ════════════════════════════════════════════════════════════
public class ExecutiveCockpitService : IExecutiveCockpitService
{
    private readonly IDbContextFactory<db24804Context> _factory;

    public ExecutiveCockpitService(IDbContextFactory<db24804Context> factory)
        => _factory = factory;

    // ─── ترجمات لسجل التدقيق ────────────────────────────────
    private static readonly Dictionary<string, string> TableAr = new()
    {
        ["Transactions"] = "الفواتير",
        ["TransactionDetails"] = "بنود الفواتير",
        ["Products"] = "المنتجات",
        ["StockLevels"] = "المخزون",
        ["Parties"] = "العملاء/الموردين",
        ["Employees"] = "الموظفين",
        ["CrmTasks"] = "المهام",
        ["LeadsCRMs"] = "العملاء المحتملين",
        ["Complaints"] = "الشكاوى",
        ["Expenses"] = "المصروفات",
        ["Users"] = "المستخدمين",
        ["Attendances"] = "الحضور"
    };

    private static readonly Dictionary<string, string> ActionAr = new()
    {
        ["Insert"] = "إضافة",
        ["Update"] = "تعديل",
        ["Delete"] = "حذف",
        ["UpdateDeliveryStatus"] = "تحديث تسليم",
        ["DeliveryPermissionGranted"] = "منح إذن تسليم",
        ["DeliveryPermissionRequest"] = "طلب إذن تسليم",
        ["Approve"] = "اعتماد",
        ["Reject"] = "رفض",
        ["Complete"] = "إتمام",
        ["Receive"] = "استلام"
    };

    private static string Describe(string? tableName, string? actionType)
    {
        var act = actionType != null && ActionAr.TryGetValue(actionType, out var a) ? a : (actionType ?? "إجراء");
        var tb = tableName != null && TableAr.TryGetValue(tableName, out var t) ? t : (tableName ?? "");
        return string.IsNullOrWhiteSpace(tb) ? act : $"{act} — {tb}";
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

    private sealed class Totals
    {
        public decimal Sales, Collected, Purchases, Expenses;
        public decimal Net => Collected - Purchases - Expenses;
    }

    private static async Task<Totals> TotalsAsync(db24804Context db, DateTime from, DateTime to)
    {
        var toEnd = to.Date.AddDays(1).AddTicks(-1);
        var fromD = from.Date;

        var sales = await db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Sale
                     && t.InvoiceStatus != InvoiceStatuses.Cancelled
                     && t.TransactionDate >= fromD && t.TransactionDate <= toEnd)
            .SumAsync(t => (decimal?)t.GrandTotal) ?? 0m;

        var collected = await db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Sale
                     && t.InvoiceStatus != InvoiceStatuses.Cancelled
                     && t.TransactionDate >= fromD && t.TransactionDate <= toEnd)
            .SumAsync(t => (decimal?)t.PaidAmount) ?? 0m;

        var purchases = await db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Purchase
                     && t.InvoiceStatus != InvoiceStatuses.Cancelled
                     && t.TransactionDate >= fromD && t.TransactionDate <= toEnd)
            .SumAsync(t => (decimal?)t.GrandTotal) ?? 0m;

        var expenses = await db.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDate >= fromD && e.ExpenseDate <= toEnd)
            .SumAsync(e => (decimal?)e.Amount) ?? 0m;

        return new Totals { Sales = sales, Collected = collected, Purchases = purchases, Expenses = expenses };
    }

    private static ExecutiveKpiDto Kpi(decimal cur, decimal prev, decimal year)
        => new() { Value = cur, PrevValue = prev, YearValue = year };

    // ════════════════════════════════════════════════════════
    public async Task<ExecutiveCockpitDto> GetCockpitAsync(string period)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var today = DateTime.Today;
        var (from, to) = RangeOf(period);

        // ── 1) المؤشرات: الفترة / السابقة / نفس الفترة السنة الماضية ──
        var len = (to - from).Days + 1;
        var prevTo = from.AddDays(-1);
        var prevFrom = prevTo.AddDays(-(len - 1));

        var cur = await TotalsAsync(db, from, to);
        var prev = await TotalsAsync(db, prevFrom, prevTo);
        var yr = await TotalsAsync(db, from.AddYears(-1), to.AddYears(-1));

        // ── 2) اتجاه 12 شهر ──
        var trendStart = new DateTime(today.Year, today.Month, 1).AddMonths(-11);
        var txRows = await db.Transactions.AsNoTracking()
            .Where(t => (t.TransactionType == TransactionTypes.Sale || t.TransactionType == TransactionTypes.Purchase)
                     && t.InvoiceStatus != InvoiceStatuses.Cancelled
                     && t.TransactionDate >= trendStart)
            .Select(t => new { t.TransactionDate, t.TransactionType, t.GrandTotal, t.PaidAmount })
            .ToListAsync();

        var trend = new List<ExecutiveTrendPointDto>();
        for (int i = 0; i < 12; i++)
        {
            var mStart = trendStart.AddMonths(i);
            var mEnd = mStart.AddMonths(1);
            var mRows = txRows.Where(t => t.TransactionDate >= mStart && t.TransactionDate < mEnd).ToList();
            trend.Add(new ExecutiveTrendPointDto
            {
                Label = mStart.ToString("MM/yyyy"),
                Sales = mRows.Where(t => t.TransactionType == TransactionTypes.Sale).Sum(t => t.GrandTotal),
                Collected = mRows.Where(t => t.TransactionType == TransactionTypes.Sale).Sum(t => t.PaidAmount),
                Purchases = mRows.Where(t => t.TransactionType == TransactionTypes.Purchase).Sum(t => t.GrandTotal)
            });
        }

        // ── 3) توقع إقفال الشهر الحالي ──
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthSales = txRows.Where(t => t.TransactionDate >= monthStart && t.TransactionType == TransactionTypes.Sale)
                               .Sum(t => t.GrandTotal);
        var lastMonthSales = txRows.Where(t => t.TransactionDate >= monthStart.AddMonths(-1) && t.TransactionDate < monthStart
                                            && t.TransactionType == TransactionTypes.Sale)
                                   .Sum(t => t.GrandTotal);
        var dayOfMonth = Math.Max(today.Day, 1);
        var forecast = Math.Round(monthSales / dayOfMonth * DateTime.DaysInMonth(today.Year, today.Month), 0);
        var forecastPct = lastMonthSales == 0 ? 0 : Math.Round((forecast - lastMonthSales) / lastMonthSales * 100, 1);

        // ── 4) الفريق: موظفين + تدقيق + حضور + مهام + إنتاج ──
        var employees = await db.Employees.AsNoTracking()
            .Where(e => e.EndDate == null)
            .OrderBy(e => e.FullName)
            .ToListAsync();

        var users = await db.Users.AsNoTracking()
            .Where(u => u.EmployeeId != null)
            .Select(u => new { u.Username, u.EmployeeId })
            .ToListAsync();
        var loginByEmp = users.Where(u => u.EmployeeId.HasValue)
            .GroupBy(u => u.EmployeeId!.Value)
            .ToDictionary(g => g.Key, g => g.First().Username);
        var empByLogin = loginByEmp.ToDictionary(kv => kv.Value, kv => kv.Key);

        var auditFrom = today.AddDays(-34);
        var logins = loginByEmp.Values.Distinct().ToList();
        var auditRows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.ActionDate != null && a.ActionDate >= auditFrom
                     && a.LoginName != null && logins.Contains(a.LoginName))
            .Select(a => new { a.LoginName, a.ActionDate })
            .ToListAsync();

        var attToday = await db.Attendances.AsNoTracking()
            .Where(a => a.LogDate == today)
            .Select(a => new { a.BiometricCode, a.LateMinutes })
            .ToListAsync();
        var attByBio = attToday.GroupBy(a => a.BiometricCode)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.LateMinutes).First());

        var salesByEmp = await db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Sale
                     && t.InvoiceStatus != InvoiceStatuses.Cancelled
                     && t.TransactionDate >= monthStart && t.EmpId != null)
            .GroupBy(t => t.EmpId!.Value)
            .Select(g => new { EmpId = g.Key, Total = g.Sum(x => x.GrandTotal), Paid = g.Sum(x => x.PaidAmount) })
            .ToListAsync();
        var salesDict = salesByEmp.ToDictionary(x => x.EmpId, x => x);

        var pendingTasks = await db.CrmTasks.AsNoTracking()
            .Where(t => t.Status == "Pending")
            .Select(t => new { t.AssignedTo, t.DueDate })
            .ToListAsync();

        var empRows = new List<ExecutiveEmployeeRowDto>();
        foreach (var e in employees)
        {
            loginByEmp.TryGetValue(e.EmployeeId, out var login);
            var myAudits = login == null ? new List<DateTime>()
                : auditRows.Where(a => a.LoginName == login && a.ActionDate.HasValue)
                           .Select(a => a.ActionDate!.Value).ToList();

            var heat = new List<int>();
            for (int d = 6; d >= 0; d--)
            {
                var day = today.AddDays(-d);
                heat.Add(myAudits.Count(x => x.Date == day));
            }

            var actionsToday = myAudits.Count(x => x.Date == today);
            var actionsMonth = myAudits.Count(x => x.Date >= today.AddDays(-29));
            var last = myAudits.Count > 0 ? myAudits.Max() : (DateTime?)null;

            string attText = "بلا تسجيل";
            if (e.Status == EmployeeStatuses.Suspended) attText = "موقوف";
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

            // ترتيب الحالات: حالة الـHR أولاً (موقوف/إجازة/مستقيل = off) → بلا حساب نظام = gray → نشط/قليل/ساكن
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

        // ── 5) لوحة المدير العام ──
        var hrOffIds = empRows.Where(x => x.Status == "off").Select(x => x.EmployeeId).ToHashSet();
        var gm = employees.FirstOrDefault(x => (x.JobTitle ?? "").Contains("مدير عام"))
              ?? employees.FirstOrDefault(x => (x.JobTitle ?? "").Contains("مدير عامًا"));
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
                var gmAudits = auditRows.Where(a => a.LoginName == gmLogin && a.ActionDate.HasValue)
                                        .Select(a => a.ActionDate!.Value).ToList();
                gmDto.ActionsWeek = gmAudits.Count(x => x.Date >= today.AddDays(-6));
                gmDto.LastActivityText = gmAudits.Count > 0 ? HumanizeWhen(gmAudits.Max()) : "لا يوجد نشاط";

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

        // ── 6) الاستثناءات ──
        var cutoff30 = today.AddDays(-30);
        var debtRows = await db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Sale
                     && t.InvoiceStatus != InvoiceStatuses.Cancelled
                     && t.GrandTotal > t.PaidAmount
                     && (t.DueDate ?? t.TransactionDate) < cutoff30)
            .Select(t => new { t.GrandTotal, t.PaidAmount })
            .ToListAsync();

        var lateDeliveries = await db.Transactions.AsNoTracking()
            .CountAsync(t => t.TransactionType == TransactionTypes.Sale
                          && t.InvoiceStatus != InvoiceStatuses.Cancelled
                          && t.IsDelivered != true
                          && t.DueDate != null && t.DueDate < today);

        var pendingPrice = await db.PriceChangeRequests.AsNoTracking()
            .CountAsync(r => r.Status == "Pending");

        var negativeStock = await db.StockLevels.AsNoTracking()
            .Where(s => s.Quantity < 0)
            .Select(s => s.ProductId).Distinct().CountAsync();

        var stalledLeads = await db.LeadsCRMs.AsNoTracking()
            .CountAsync(l => (l.LeadStatus == "جديد" || l.LeadStatus == "تم الإسناد")
                          && l.CreatedAt < today.AddDays(-10));

        var complaintStatuses = await db.Complaints.AsNoTracking()
            .Select(c => c.Status).ToListAsync();
        var openComplaints = complaintStatuses.Count(s => !ComplaintStatus.IsClosed(s));
        var escalated = complaintStatuses.Count(s => s == ComplaintStatus.Escalated);

        gmDto.PendingPrice = pendingPrice;
        gmDto.EscalatedComplaints = escalated;

        // ── 7) التشغيل: مصنع / رعاية / تسويق ──
        var factoryOpen = await db.Transactions.AsNoTracking()
            .Join(db.Parties.AsNoTracking(), t => t.PartyId, p => p.PartyId, (t, p) => new { t, p })
            .CountAsync(x => x.p.PartyName == "المصنع"
                          && x.t.TransactionType == TransactionTypes.Purchase
                          && x.t.InvoiceStatus != InvoiceStatuses.Cancelled
                          && x.t.IsDelivered != true);

        var factoryMonth = await db.Transactions.AsNoTracking()
            .Join(db.Parties.AsNoTracking(), t => t.PartyId, p => p.PartyId, (t, p) => new { t, p })
            .CountAsync(x => x.p.PartyName == "المصنع"
                          && x.t.TransactionType == TransactionTypes.Purchase
                          && x.t.InvoiceStatus != InvoiceStatuses.Cancelled
                          && x.t.DeliveredAt != null && x.t.DeliveredAt >= monthStart);

        var newLeads = await db.LeadsCRMs.AsNoTracking()
            .CountAsync(l => l.CreatedAt >= monthStart);

        // ── 7ب) خزينة · بايبلاين الفرص · B2B · مبيعات الفروع ──
        var cashOpening = await db.CashBoxes.AsNoTracking().SumAsync(c => (decimal?)c.OpeningBalance) ?? 0m;
        var cashIn = await db.CashboxTransactions.AsNoTracking()
            .Where(t => t.TransactionType == "قبض").SumAsync(t => (decimal?)t.Amount) ?? 0m;
        var cashOut = await db.CashboxTransactions.AsNoTracking()
            .Where(t => t.TransactionType == "صرف").SumAsync(t => (decimal?)t.Amount) ?? 0m;
        var cashBalance = cashOpening + cashIn - cashOut;

        var openOpps = await db.SalesOpportunities.AsNoTracking()
            .Where(o => o.ClosedAt == null)
            .Select(o => new { o.ExpectedValue, o.NextFollowUpDate })
            .ToListAsync();
        var pipelineOpen = openOpps.Count;
        var pipelineValue = openOpps.Sum(o => o.ExpectedValue ?? 0m);
        var pipelineStalled = openOpps.Count(o => o.NextFollowUpDate != null && o.NextFollowUpDate < today);

        var b2bPending = await db.B2BRequests.AsNoTracking()
            .CountAsync(r => r.Status == B2BRequestStatuses.New || r.Status == B2BRequestStatuses.UnderReview);

        var branchSales = await db.Transactions.AsNoTracking()
            .Join(db.Warehouses.AsNoTracking(), t => t.WarehouseId, w => w.WarehouseId, (t, w) => new { t, w })
            .Join(db.Branches.AsNoTracking(), x => x.w.BranchId, b => (int?)b.BranchId, (x, b) => new { x.t, b })
            .Where(x => x.t.TransactionType == TransactionTypes.Sale
                     && x.t.InvoiceStatus != InvoiceStatuses.Cancelled
                     && x.t.TransactionDate >= from && x.t.TransactionDate <= to.AddDays(1).AddTicks(-1))
            .GroupBy(x => x.b.BranchNameAr)
            .Select(g => new ExecutiveBranchSaleDto { Name = g.Key, Value = g.Sum(x => x.t.GrandTotal) })
            .ToListAsync();
        branchSales = branchSales.OrderByDescending(b => b.Value).ToList();

        // ── 8) القصة ──
        var dormant = empRows.Count(x => x.Status == "red");
        var decisions = pendingPrice + escalated + dormant;
        var salesPct = cur.Sales == 0 || prev.Sales == 0 ? 0 : Math.Round((cur.Sales - prev.Sales) / prev.Sales * 100, 1);
        var story =
            $"{PeriodLabelAr(period)}: المبيعات {cur.Sales:N0} ج ({(salesPct >= 0 ? "+" : "")}{salesPct}% عن الفترة السابقة) — " +
            $"المحصَّل من فواتير الفترة {cur.Collected:N0} ج — " +
            $"الفريق: {empRows.Count(x => x.ActionsToday > 0)} نشط من {empRows.Count}" +
            (dormant > 0 ? $" — {dormant} موظف ساكن يحتاج قرارًا" : "") +
            (decisions > 0 ? $" — {decisions} بند في انتظارك." : " — لا بنود معلقة.");

        return new ExecutiveCockpitDto
        {
            Sales = Kpi(cur.Sales, prev.Sales, yr.Sales),
            Collected = Kpi(cur.Collected, prev.Collected, yr.Collected),
            Purchases = Kpi(cur.Purchases, prev.Purchases, yr.Purchases),
            Expenses = Kpi(cur.Expenses, prev.Expenses, yr.Expenses),
            Net = Kpi(cur.Net, prev.Net, yr.Net),
            Trend = trend,
            Employees = empRows,
            Gm = gmDto,
            Exceptions = new ExecutiveExceptionsDto
            {
                Debts30 = debtRows.Count,
                Debts30Amount = debtRows.Sum(x => x.GrandTotal - x.PaidAmount),
                LateDeliveries = lateDeliveries,
                PendingPrice = pendingPrice,
                NegativeStock = negativeStock,
                StalledLeads = stalledLeads,
                OpenComplaints = openComplaints
            },
            Ops = new ExecutiveOpsDto
            {
                FactoryOpen = factoryOpen,
                FactoryMonth = factoryMonth,
                OpenComplaints = openComplaints,
                NewLeads = newLeads
            },
            CashBalance = cashBalance,
            PipelineOpen = pipelineOpen,
            PipelineValue = pipelineValue,
            PipelineStalled = pipelineStalled,
            B2bPending = b2bPending,
            BranchSales = branchSales,
            PeriodLabel = PeriodLabelAr(period),
            Story = story,
            DecisionsPending = decisions,
            DormantCount = dormant,
            ForecastValue = forecast,
            LastMonthSales = lastMonthSales,
            ForecastVsLastPct = forecastPct,
            GeneratedAt = DateTime.Now
        };
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
