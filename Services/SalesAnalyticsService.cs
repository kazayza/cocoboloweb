using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// 📈 Sales Analytics — لوحة تحليلات المبيعات.
/// يُطبَّق السكوب إجبارياً في طبقة الخدمة (مصدر واحد للحقيقة):
///  ① CrmAccessFrom (تاريخ بدء الاطلاع من الـ Claims — Admin معفى)
///  ② SalesInvoiceAccess (فواتير البيع التي أنشأها AccountManager لا يراها إلا
///     Admin/AccountManager/Account)
/// والفلاتر (الفرع/الموظف/الحالة) تنطبق على كل الأرقام والرسوم معاً.
/// </summary>
public class SalesAnalyticsService : ISalesAnalyticsService
{
    private readonly IDbContextFactory<db24804Context> _factory;
    private readonly IHttpContextAccessor _http;

    private static readonly string[] SaleStatusOptions =
    {
        InvoiceStatuses.Open,
        InvoiceStatuses.PartiallyPaid,
        InvoiceStatuses.Paid
    };

    public SalesAnalyticsService(IDbContextFactory<db24804Context> factory, IHttpContextAccessor http)
    {
        _factory = factory;
        _http = http;
    }

    // ════════════════════════════ السكوب ════════════════════════════
    private DateTime? ScopeFrom() => _http?.HttpContext?.User?.GetAccessFromDate();
    private bool CanSeeAcctMgr() => SalesInvoiceAccess.CanViewAccountManagerInvoices(_http?.HttpContext?.User);
    private async Task<List<string>> ProtectedCreatorsAsync(db24804Context db)
        => CanSeeAcctMgr()
            ? new List<string>()
            : await SalesInvoiceAccess.GetProtectedCreatorUsernamesAsync(db);

    // ════════════════════════════ بيانات الفلاتر ════════════════════════════
    public async Task<SalesAnalyticsFilterDataDto> GetFilterDataAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();

        var branches = await db.Branches.AsNoTracking()
            .OrderBy(b => b.BranchNameAr)
            .Select(b => new { b.BranchId, Name = b.BranchNameAr })
            .ToListAsync();

        var salesUserEmpIds = await db.Users.AsNoTracking()
            .Where(u => u.IsActive == true && u.Role == SystemRoles.Sales && u.EmployeeId != null)
            .Select(u => u.EmployeeId!.Value)
            .Distinct()
            .ToListAsync();

        var employees = new List<SalesAnalyticsOptionDto>();
        if (salesUserEmpIds.Any())
        {
            employees = await db.Employees.AsNoTracking()
                .Where(e => salesUserEmpIds.Contains(e.EmployeeId))
                .Select(e => new SalesAnalyticsOptionDto { Id = e.EmployeeId, Name = e.FullName, BranchId = e.BranchId })
                .ToListAsync();
            employees = employees.Where(e => e.Name != null).OrderBy(e => e.Name).ToList();
        }

        return new SalesAnalyticsFilterDataDto
        {
            Branches = branches.Select(b => new SalesAnalyticsOptionDto { Id = b.BranchId, Name = b.Name }).ToList(),
            SalesEmployees = employees,
            InvoiceStatuses = SaleStatusOptions.ToList()
        };
    }

    // ════════════════════════════ اللوحة ════════════════════════════
    public async Task<SalesAnalyticsDashboardDto> GetDashboardAsync(SalesAnalyticsFilterDto filter)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var from = filter.DateFrom.Date;
        var to = filter.DateTo.Date;
        if (to < from) to = from;
        var days = (int)(to - from).TotalDays + 1;
        var prevFrom = from.AddDays(-days);
        var prevTo = from.AddDays(-1);
        var accessFrom = ScopeFrom();

        var ctx = await CtxAsync(db);
        var protectedCreators = await ProtectedCreatorsAsync(db);

        var curSales = await FetchSalesAsync(db, from, to, accessFrom, protectedCreators);
        var prevSales = await FetchSalesAsync(db, prevFrom, prevTo, accessFrom, protectedCreators);

        var ownerMap = await BuildOwnerMapAsync(db, curSales.Concat(prevSales).ToList(), protectedCreators);
        var employeeFilter = filter.EmployeeId;

        List<SaleRow> OwnerApplied(IEnumerable<SaleRow> rows)
            => rows.Where(s => Matches(s, filter, ctx.WarehouseBranch, ownerMap)).ToList();

        var cur = OwnerApplied(curSales);
        var prev = OwnerApplied(prevSales);

        var curOpps = await FetchOppsAsync(db, from, to, accessFrom);
        var prevOpps = await FetchOppsAsync(db, prevFrom, prevTo, accessFrom);

        bool EmpMatches(int? empId)
            => employeeFilter.HasValue ? empId == employeeFilter.Value : true;
        bool OppBranchOk(int? empId)
            => !filter.BranchId.HasValue
                || (empId.HasValue && ctx.EmpBranch.TryGetValue(empId.Value, out var b) && b == filter.BranchId.Value);

        // ── مؤشرات الفترة الحالية ──
        var total = cur.Sum(s => s.Net);
        var prevTotal = prev.Sum(s => s.Net);
        var count = cur.Count;
        var prevCount = prev.Count;
        var customers = cur.Select(s => s.PartyId).Distinct().Count();
        var prevCustomers = prev.Select(s => s.PartyId).Distinct().Count();

        // ── فرص الفريق داخل النطاق ──
        var scopeOpps = curOpps.Where(o => EmpMatches(o.EmployeeId) && OppBranchOk(o.EmployeeId)).ToList();
        var prevScopeOpps = prevOpps.Where(o => EmpMatches(o.EmployeeId) && OppBranchOk(o.EmployeeId)).ToList();

        var teamOpps = scopeOpps.Count;
        var teamWon = scopeOpps.Count(o => o.Won);
        var teamLost = scopeOpps.Count(o => o.IsLost);
        var teamInProgress = Math.Max(0, teamOpps - teamWon - teamLost);
        var prevTeamOpps = prevScopeOpps.Count;
        var prevTeamWon = prevScopeOpps.Count(o => o.Won);

        // ── أداء الموظفين ──
        var employees = BuildEmployees(cur, curOpps, ctx.EmpNames, OppBranchOk,
            employeeFilter, s => OwnerOf(s, ownerMap));

        // ── بيانات مساعدة للعملاء/الفواتير (أسماء + تاريخ فرص) ──
        var partyIds = cur.Select(s => s.PartyId).Distinct().ToList();
        var partyNames = partyIds.Any()
            ? await db.Parties.AsNoTracking().Where(p => partyIds.Contains(p.PartyId))
                .ToDictionaryAsync(p => p.PartyId, p => p.PartyName)
            : new Dictionary<int, string>();

        var txIds = cur.Select(s => s.Id).Distinct().ToList();
        var oppDates = txIds.Any()
            ? await db.SalesOpportunities.AsNoTracking()
                .Where(o => o.IsActive && o.StageId == 3 && o.TransactionId != null
                            && txIds.Contains(o.TransactionId!.Value))
                .GroupBy(o => o.TransactionId!.Value)
                .Select(g => new { TxId = g.Key, Dt = g.Min(o => o.CreatedAt) })
                .ToDictionaryAsync(x => x.TxId, x => (DateTime?)x.Dt)
            : new Dictionary<int, DateTime?>();

        AttachEmployeeInvoices(employees, cur, ownerMap, partyNames, oppDates);

        // ── ⏱️ متوسط دورة البيع لكل موظف (فرصة مغلقة → فاتورتها) ──
        await AttachEmployeeCyclesAsync(db, employees, scopeOpps);

        // ── 💳 طرق الدفع + نسبة التحصيل ──
        var totalPaid = cur.Sum(s => s.Paid);
        var collectionRate = total > 0 ? Math.Min(100.0, (double)totalPaid / (double)total * 100.0) : 0;
        var outstanding = Math.Max(0, total - Math.Min(total, totalPaid));

        var paymentMethods = cur
            .Where(s => !string.IsNullOrWhiteSpace(s.PayMethod))
            .GroupBy(s => s.PayMethod!)
            .Select(g => new { Method = g.Key, Value = g.Sum(s => s.Net) })
            .OrderByDescending(x => x.Value)
            .Select(x => new SalesAnalyticsLabelValueDto { Label = PayMethodLabel(x.Method), Value = x.Value })
            .ToList();
        if (cur.Any(s => string.IsNullOrWhiteSpace(s.PayMethod)))
        {
            var undef = cur.Where(s => string.IsNullOrWhiteSpace(s.PayMethod)).Sum(s => s.Net);
            paymentMethods.Add(new SalesAnalyticsLabelValueDto { Label = "غير محددة", Value = undef });
        }

        // ── 🧾 عملاء جدد vs عائدين ──
        var firstBuy = partyIds.Any()
            ? await db.Transactions.AsNoTracking()
                .Where(t => t.TransactionType == TransactionTypes.Sale
                            && t.InvoiceStatus != InvoiceStatuses.Cancelled
                            && partyIds.Contains(t.PartyId)
                            && (!accessFrom.HasValue || t.TransactionDate >= accessFrom.Value))
                .GroupBy(t => t.PartyId)
                .Select(g => new { PartyId = g.Key, First = g.Min(t => t.TransactionDate) })
                .ToDictionaryAsync(x => x.PartyId, x => x.First)
            : new Dictionary<int, DateTime>();
        var newCustomers = firstBuy.Count(x => x.Value >= from);
        var returningCustomers = firstBuy.Count(x => x.Value < from);

        // ── 🏆 أهم العملاء (Top 10) ──
        var topCustomers = BuildTopCustomers(cur, partyNames, oppDates, total);

        // ── 📉 أسباب الخسارة ──
        var lostReasons = await BuildLostReasonsAsync(db, scopeOpps);

        // ── الرسوم ──
        var (trendLabels, trendCur, trendPrev) = BuildTrend(cur, from, prev, prevFrom, days);

        var branchChart = cur
            .Select(s => new { Br = BranchOf(s, ctx.WarehouseBranch), s.Net })
            .Where(x => x.Br.HasValue)
            .GroupBy(x => x.Br!.Value)
            .Select(g => new SalesAnalyticsLabelValueDto
            {
                Label = ctx.BranchNames.TryGetValue(g.Key, out var bn) ? bn : $"فرع {g.Key}",
                Value = g.Sum(x => x.Net)
            })
            .OrderByDescending(x => x.Value)
            .Take(10)
            .ToList();

        var shareChart = employees
            .Where(e => e.SalesAmount > 0)
            .Select(e => new SalesAnalyticsLabelValueDto { Label = e.Name, Value = e.SalesAmount })
            .OrderByDescending(x => x.Value)
            .Take(8)
            .ToList();

        // ── 🧭 القراءات الذكية ──
        var insights = BuildInsights(new InsightsContext
        {
            Total = total,
            PrevTotal = prevTotal,
            TopBranch = branchChart.FirstOrDefault()?.Label,
            TopBranchAmount = branchChart.FirstOrDefault()?.Value ?? 0,
            TopEmployee = employees.FirstOrDefault(e => e.SalesAmount > 0),
            TopCustomer = topCustomers.FirstOrDefault(),
            NewCustomers = newCustomers,
            ReturningCustomers = returningCustomers,
            TeamLost = teamLost,
            TopLostReason = lostReasons.FirstOrDefault()?.Label
        });

        return new SalesAnalyticsDashboardDto
        {
            DateFrom = from,
            DateTo = to,
            PrevFrom = prevFrom,
            PrevTo = prevTo,
            PeriodDays = days,
            ScopeFromDate = accessFrom,
            UserCanSeeAccountManagerInvoices = CanSeeAcctMgr(),
            HiddenAccountManagerSales = !CanSeeAcctMgr() && protectedCreators.Count > 0,

            TotalAmount = total,
            PrevTotalAmount = prevTotal,
            InvoiceCount = count,
            PrevInvoiceCount = prevCount,
            AvgInvoice = count > 0 ? Math.Round(total / count, 2) : 0,
            PrevAvgInvoice = prevCount > 0 ? Math.Round(prevTotal / prevCount, 2) : 0,
            Customers = customers,
            PrevCustomers = prevCustomers,

            TeamOpportunities = teamOpps,
            TeamWonDeals = teamWon,
            TeamConversionRate = teamOpps > 0 ? Math.Round((double)teamWon / teamOpps * 100, 1) : 0,
            PrevTeamOpportunities = prevTeamOpps,
            PrevTeamWonDeals = prevTeamWon,
            PrevTeamConversionRate = prevTeamOpps > 0 ? Math.Round((double)prevTeamWon / prevTeamOpps * 100, 1) : 0,

            CollectionRate = Math.Round(collectionRate, 1),
            CollectedAmount = Math.Round(Math.Min(totalPaid, total), 2),
            OutstandingAmount = Math.Round(outstanding, 2),
            NewCustomers = newCustomers,
            ReturningCustomers = returningCustomers,

            TeamInProgress = teamInProgress,
            TeamLostOpportunities = teamLost,
            LostReasons = lostReasons,

            Employees = employees,
            TrendLabels = trendLabels,
            TrendCurrent = trendCur,
            TrendPrev = trendPrev,
            Branches = branchChart,
            Share = shareChart,
            PaymentMethods = paymentMethods,
            TopCustomers = topCustomers,
            Insights = insights
        };
    }

    // ════════════════════════════ تصدير Excel ════════════════════════════
    public async Task<(byte[] Content, string Mime, string FileName)> ExportAsync(SalesAnalyticsFilterDto filter)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var from = filter.DateFrom.Date;
        var to = filter.DateTo.Date;
        if (to < from) to = from;
        var accessFrom = ScopeFrom();
        var ctx = await CtxAsync(db);
        var protectedCreators = await ProtectedCreatorsAsync(db);

        var sales = await FetchSalesAsync(db, from, to, accessFrom, protectedCreators);
        var ownerMap = await BuildOwnerMapAsync(db, sales, protectedCreators);

        var rows = sales
            .Where(s => Matches(s, filter, ctx.WarehouseBranch, ownerMap))
            .OrderBy(s => s.Date)
            .ToList();

        var partyIds = rows.Select(s => s.PartyId).Distinct().ToList();
        var partyNames = partyIds.Any()
            ? await db.Parties.AsNoTracking().Where(p => partyIds.Contains(p.PartyId))
                .ToDictionaryAsync(p => p.PartyId, p => p.PartyName)
            : new Dictionary<int, string>();

        var opps = await FetchOppsAsync(db, from, to, accessFrom);
        var branchOk = (int? e) => !filter.BranchId.HasValue
            || (e.HasValue && ctx.EmpBranch.TryGetValue(e.Value, out var eb) && eb == filter.BranchId.Value);
        var employees = BuildEmployees(rows, opps, ctx.EmpNames, branchOk,
            filter.EmployeeId, s => OwnerOf(s, ownerMap));
        var scopeOpps = opps.Where(o => branchOk(o.EmployeeId)).ToList();
        await AttachEmployeeCyclesAsync(db, employees, scopeOpps);

        var invoiceRows = rows.Select(s =>
        {
            var owner = OwnerOf(s, ownerMap);
            var br = BranchOf(s, ctx.WarehouseBranch);
            return new SalesAnalyticsInvoiceRowDto
            {
                Date = s.Date,
                ReferenceNumber = s.RefNo ?? "",
                PartyName = s.PartyId != 0 && partyNames.TryGetValue(s.PartyId, out var pn) ? pn : "",
                EmployeeName = owner.HasValue && ctx.EmpNames.TryGetValue(owner.Value, out var en) ? en : "",
                BranchName = br.HasValue && ctx.BranchNames.TryGetValue(br.Value, out var bn2) ? bn2 : "",
                Status = StatusAr(s.Status),
                Net = s.Net
            };
        }).ToList();

        var wb = new XLWorkbook();

        // ── ورقة الملخص ──
        var summary = wb.Worksheets.Add("الملخص");
        summary.Cell(1, 1).Value = "لوحة تحليلات المبيعات";
        summary.Cell(1, 1).Style.Font.Bold = true;
        summary.Cell(2, 1).Value = $"الفترة: {from:yyyy-MM-dd} إلى {to:yyyy-MM-dd}";
        summary.Cell(3, 1).Value = $"إجمالي المبيعات: {rows.Sum(x => x.Net)}";
        summary.Cell(4, 1).Value = $"عدد الفواتير: {rows.Count}";
        summary.Cell(5, 1).Value = rows.Count > 0
            ? $"متوسط الفاتورة: {Math.Round(rows.Sum(x => x.Net) / rows.Count, 2)}"
            : "متوسط الفاتورة: 0";
        summary.Cell(6, 1).Value = $"العملاء: {rows.Select(x => x.PartyId).Distinct().Count()}";
        summary.Columns(1, 2).AdjustToContents();

        // ── ورقة الفواتير ──
        var inv = wb.Worksheets.Add("الفواتير");
        var headers = new[] { "التاريخ", "رقم الفاتورة", "العميل", "المسؤول", "الفرع", "الحالة", "صافي القيمة" };
        for (var c = 0; c < headers.Length; c++)
        {
            inv.Cell(1, c + 1).Value = headers[c];
            inv.Cell(1, c + 1).Style.Font.Bold = true;
        }
        var r = 2;
        foreach (var item in invoiceRows)
        {
            inv.Cell(r, 1).Value = item.Date.ToString("yyyy-MM-dd");
            inv.Cell(r, 2).Value = item.ReferenceNumber;
            inv.Cell(r, 3).Value = item.PartyName;
            inv.Cell(r, 4).Value = item.EmployeeName;
            inv.Cell(r, 5).Value = item.BranchName;
            inv.Cell(r, 6).Value = item.Status;
            inv.Cell(r, 7).Value = item.Net;
            r++;
        }
        inv.Columns().AdjustToContents();

        // ── ورقة الموظفين ──
        var emp = wb.Worksheets.Add("الموظفون");
        var empHeaders = new[] { "الموظف", "الفرص", "مغلقة", "التحويل %", "عدد الفواتير", "قيمة الفواتير", "متوسط الفاتورة", "متوسط الدورة (أيام)" };
        for (var c = 0; c < empHeaders.Length; c++)
        {
            emp.Cell(1, c + 1).Value = empHeaders[c];
            emp.Cell(1, c + 1).Style.Font.Bold = true;
        }
        r = 2;
        foreach (var e in employees)
        {
            emp.Cell(r, 1).Value = e.Name;
            emp.Cell(r, 2).Value = e.Opportunities;
            emp.Cell(r, 3).Value = e.WonDeals;
            emp.Cell(r, 4).Value = e.ConversionRate;
            emp.Cell(r, 5).Value = e.InvoiceCount;
            emp.Cell(r, 6).Value = e.SalesAmount;
            emp.Cell(r, 7).Value = e.AvgInvoice;
            emp.Cell(r, 8).Value = e.AvgCycleDays > 0 ? Math.Round(e.AvgCycleDays, 1) : 0;
            r++;
        }
        emp.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var fileName = $"Sales-Analytics-{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx";
        return (ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // ════════════════════════════ Helpers ════════════════════════════

    private sealed record SaleRow(
        int Id, DateTime Date, decimal Net, int PartyId,
        int? WarehouseId, int? EmpId, string? Status, string? RefNo,
        decimal Paid, string? PayMethod);

    private sealed record OppRow(
        int EmployeeId, int StageId, int? TransactionId, int? LostReasonId, DateTime CreatedAt)
    {
        public bool Won => StageId == 3 && TransactionId.HasValue;
        public bool IsLost => StageId == 4 || StageId == 5;
    }

    private sealed class CtxData
    {
        public Dictionary<int, string> BranchNames { get; init; } = new();
        public Dictionary<int, int?> WarehouseBranch { get; init; } = new();
        public Dictionary<int, string> EmpNames { get; init; } = new();
        public Dictionary<int, int?> EmpBranch { get; init; } = new();
    }

    private static async Task<CtxData> CtxAsync(db24804Context db)
    {
        var branches = await db.Branches.AsNoTracking()
            .Select(b => new { b.BranchId, b.BranchNameAr }).ToListAsync();
        var warehouses = await db.Warehouses.AsNoTracking()
            .Select(w => new { w.WarehouseId, w.BranchId }).ToListAsync();
        var emps = await db.Employees.AsNoTracking()
            .Select(e => new { e.EmployeeId, e.FullName, e.BranchId }).ToListAsync();

        return new CtxData
        {
            BranchNames = branches.ToDictionary(b => b.BranchId, b => b.BranchNameAr),
            WarehouseBranch = warehouses.GroupBy(w => w.WarehouseId)
                .ToDictionary(g => g.Key, g => g.First().BranchId),
            EmpNames = emps.ToDictionary(e => e.EmployeeId, e => e.FullName ?? $"موظف {e.EmployeeId}"),
            EmpBranch = emps.ToDictionary(e => e.EmployeeId, e => e.BranchId)
        };
    }

    private async Task<List<SaleRow>> FetchSalesAsync(
        db24804Context db, DateTime from, DateTime to,
        DateTime? accessFrom, List<string> protectedCreators)
    {
        var end = to.AddDays(1);
        var query = db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Sale
                        && t.InvoiceStatus != InvoiceStatuses.Cancelled
                        && t.TransactionDate >= from
                        && t.TransactionDate < end);

        if (accessFrom.HasValue)
            query = query.Where(t => t.TransactionDate >= accessFrom.Value);

        query = query.ExcludeProtectedSales(protectedCreators);

        return await query
            .Select(t => new SaleRow(
                t.TransactionId,
                t.TransactionDate,
                t.NetTotalAmount ?? 0m,
                t.PartyId,
                t.WarehouseId,
                t.EmpId,
                t.InvoiceStatus,
                t.ReferenceNumber,
                t.PaidAmount,
                t.PaymentMethod))
            .ToListAsync();
    }

    private async Task<List<OppRow>> FetchOppsAsync(
        db24804Context db, DateTime from, DateTime to, DateTime? accessFrom)
    {
        var end = to.AddDays(1);
        var query = db.SalesOpportunities.AsNoTracking()
            .Where(o => o.IsActive && o.CreatedAt >= from && o.CreatedAt < end);

        if (accessFrom.HasValue)
            query = query.Where(o => o.CreatedAt >= accessFrom.Value);

        var opps = await query
            .Select(o => new { o.OpportunityId, o.EmployeeId, o.StageId, o.TransactionId, o.LostReasonId, o.CreatedAt })
            .ToListAsync();

        var result = new List<OppRow>(opps.Count);
        foreach (var o in opps)
        {
            if (o.EmployeeId == null) continue;
            result.Add(new OppRow(o.EmployeeId.Value, o.StageId, o.TransactionId, o.LostReasonId, o.CreatedAt));
        }
        return result;
    }

    /// <summary>مالك الفاتورة = موظف فرصة «تم البيع» المرتبطة بها، وإلا منشئها (EmpId)</summary>
    private async Task<Dictionary<int, int?>> BuildOwnerMapAsync(
        db24804Context db, List<SaleRow> sales, List<string> protectedCreators)
    {
        var map = new Dictionary<int, int?>();
        if (sales.Count == 0) return map;
        foreach (var s in sales)
            map[s.Id] = s.EmpId;

        var txIds = sales.Select(s => s.Id).ToList();
        var linked = await db.SalesOpportunities.AsNoTracking()
            .Where(o => o.IsActive && o.StageId == 3 && o.TransactionId != null
                        && o.EmployeeId != null && txIds.Contains(o.TransactionId!.Value))
            .Select(o => new { o.TransactionId, o.EmployeeId })
            .ToListAsync();

        var txCreators = new Dictionary<int, string>();
        if (linked.Count > 0 && !CanSeeAcctMgr())
        {
            var linkedTxIds = linked.Select(x => x.TransactionId!.Value).Distinct().ToList();
            txCreators = await db.Transactions.AsNoTracking()
                .Where(t => linkedTxIds.Contains(t.TransactionId))
                .ToDictionaryAsync(t => t.TransactionId, t => t.CreatedBy ?? "");
        }

        foreach (var x in linked)
        {
            var tid = x.TransactionId!.Value;
            if (!CanSeeAcctMgr() && txCreators.TryGetValue(tid, out var creator)
                && protectedCreators.Contains(creator))
                continue;
            map[tid] = x.EmployeeId;
        }
        return map;
    }

    private static int? BranchOf(SaleRow s, Dictionary<int, int?> warehouseBranch)
        => s.WarehouseId.HasValue && warehouseBranch.TryGetValue(s.WarehouseId.Value, out var b) ? b : null;

    private static int? OwnerOf(SaleRow s, Dictionary<int, int?> ownerMap)
        => ownerMap.TryGetValue(s.Id, out var o) ? o : s.EmpId;

    private static bool Matches(
        SaleRow s, SalesAnalyticsFilterDto filter,
        Dictionary<int, int?> warehouseBranch, Dictionary<int, int?> ownerMap)
    {
        var branch = BranchOf(s, warehouseBranch);
        if (filter.BranchId.HasValue && branch != filter.BranchId.Value) return false;
        if (filter.EmployeeId.HasValue)
        {
            var owner = OwnerOf(s, ownerMap);
            if (owner != filter.EmployeeId.Value) return false;
        }
        if (!string.IsNullOrWhiteSpace(filter.InvoiceStatus) && s.Status != filter.InvoiceStatus) return false;
        return true;
    }

    private static string StatusAr(string? status)
        => status switch
        {
            InvoiceStatuses.Open => "غير مسددة",
            InvoiceStatuses.PartiallyPaid => "مدفوعة جزئياً",
            InvoiceStatuses.Paid => "مدفوعة",
            _ => status ?? ""
        };

    private static string PayMethodLabel(string method)
        => method switch
        {
            PaymentMethods.Cash => "نقدي",
            PaymentMethods.Bank => "بنك / تحويل",
            PaymentMethods.Visa => "فيزا",
            PaymentMethods.InstaPay => "انستاباي",
            PaymentMethods.Credit => "اعتماد / آجل",
            PaymentMethods.Advance => "عربون / دفعة مقدمة",
            _ => "أخرى"
        };

    /// <summary>تجميع أداء الموظفين — الفواتير تُنسب للمالك، والفُرص لموظفها</summary>
    private static List<SalesAnalyticsEmployeeDto> BuildEmployees(
        List<SaleRow> cur, List<OppRow> opps,
        Dictionary<int, string> empNames,
        Func<int?, bool> oppBranchOk,
        int? onlyEmployeeId,
        Func<SaleRow, int?> owner)
    {
        var invoiceByOwner = cur
            .Where(s => owner(s).HasValue)
            .GroupBy(s => owner(s)!.Value)
            .Select(g => new { EmpId = g.Key, Count = g.Count(), Sum = g.Sum(s => s.Net) })
            .ToList();

        var oppEmpIds = opps
            .Where(o => oppBranchOk(o.EmployeeId))
            .Select(o => o.EmployeeId)
            .Distinct()
            .ToList();

        var ids = invoiceByOwner.Select(x => x.EmpId).Concat(oppEmpIds).Distinct().ToList();
        if (onlyEmployeeId.HasValue && !ids.Contains(onlyEmployeeId.Value))
            ids.Add(onlyEmployeeId.Value);

        var list = new List<SalesAnalyticsEmployeeDto>();
        foreach (var empId in ids)
        {
            if (onlyEmployeeId.HasValue && empId != onlyEmployeeId.Value) continue;

            var empOpps = opps.Where(o => o.EmployeeId == empId && oppBranchOk(empId)).ToList();
            var won = empOpps.Count(o => o.Won);
            var inv = invoiceByOwner.FirstOrDefault(x => x.EmpId == empId);

            list.Add(new SalesAnalyticsEmployeeDto
            {
                EmpId = empId,
                Name = empNames.TryGetValue(empId, out var n) ? n : $"موظف {empId}",
                Opportunities = empOpps.Count,
                WonDeals = won,
                ConversionRate = empOpps.Count > 0 ? Math.Round((double)won / empOpps.Count * 100, 1) : 0,
                InvoiceCount = inv?.Count ?? 0,
                SalesAmount = inv?.Sum ?? 0,
                AvgInvoice = inv is { Count: > 0 } ? Math.Round(inv.Sum / inv.Count, 2) : 0
            });
        }

        return list
            .OrderByDescending(x => x.SalesAmount)
            .ThenByDescending(x => x.InvoiceCount)
            .ThenByDescending(x => x.Opportunities)
            .ToList();
    }

    private static void AttachEmployeeInvoices(
        List<SalesAnalyticsEmployeeDto> employees,
        List<SaleRow> cur,
        Dictionary<int, int?> ownerMap,
        Dictionary<int, string> partyNames,
        Dictionary<int, DateTime?> oppDates)
    {
        if (employees.Count == 0 || cur.Count == 0) return;

        var byOwner = cur
            .Where(s => OwnerOf(s, ownerMap).HasValue)
            .GroupBy(s => OwnerOf(s, ownerMap)!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.Date)
                     .Select(s => new SalesAnalyticsInvoiceItemDto
                     {
                         Customer = partyNames.TryGetValue(s.PartyId, out var pn) ? pn : $"عميل {s.PartyId}",
                         Reference = s.RefNo ?? "",
                         Net = s.Net,
                         Date = s.Date,
                         OpportunityDate = oppDates.TryGetValue(s.Id, out var od) ? od : null
                     })
                     .ToList());

        foreach (var e in employees)
        {
            if (byOwner.TryGetValue(e.EmpId, out var invoices))
                e.Invoices = invoices;
        }
    }

    /// <summary>متوسط أيام إغلاق صفقة الموظف (من تاريخ الفرصة ← تاريخ فاتورتها)</summary>
    private static async Task AttachEmployeeCyclesAsync(
        db24804Context db, List<SalesAnalyticsEmployeeDto> employees, List<OppRow> scopeOpps)
    {
        if (employees.Count == 0) return;

        var won = scopeOpps.Where(o => o.Won).ToList();
        if (won.Count == 0) return;

        var txIds = won.Select(o => o.TransactionId!.Value).Distinct().ToList();
        var dates = txIds.Any()
            ? await db.Transactions.AsNoTracking()
                .Where(t => txIds.Contains(t.TransactionId))
                .ToDictionaryAsync(t => t.TransactionId, t => t.TransactionDate)
            : new Dictionary<int, DateTime>();

        var daysByEmp = new Dictionary<int, List<double>>();
        foreach (var o in won)
        {
            if (!dates.TryGetValue(o.TransactionId!.Value, out var invDate)) continue;
            var days = (invDate.Date - o.CreatedAt.Date).TotalDays;
            if (days < 0) days = 0;
            if (!daysByEmp.TryGetValue(o.EmployeeId, out var lst))
                daysByEmp[o.EmployeeId] = lst = new List<double>();
            lst.Add(days);
        }

        foreach (var e in employees)
        {
            if (daysByEmp.TryGetValue(e.EmpId, out var lst) && lst.Count > 0)
                e.AvgCycleDays = Math.Round(lst.Average(), 1);
        }
    }

    private static List<SalesAnalyticsCustomerDto> BuildTopCustomers(
        List<SaleRow> cur,
        Dictionary<int, string> partyNames,
        Dictionary<int, DateTime?> oppDates,
        decimal total)
    {
        var top = cur
            .GroupBy(s => s.PartyId)
            .Select(g => new
            {
                PartyId = g.Key,
                Amount = g.Sum(s => s.Net),
                Count = g.Count()
            })
            .OrderByDescending(x => x.Amount)
            .Take(10)
            .ToList();

        var result = new List<SalesAnalyticsCustomerDto>();
        foreach (var g in top)
        {
            var invoices = cur
                .Where(s => s.PartyId == g.PartyId)
                .OrderByDescending(s => s.Date)
                .Select(s => new SalesAnalyticsInvoiceItemDto
                {
                    Customer = partyNames.TryGetValue(s.PartyId, out var pn) ? pn : $"عميل {s.PartyId}",
                    Reference = s.RefNo ?? "",
                    Net = s.Net,
                    Date = s.Date,
                    OpportunityDate = oppDates.TryGetValue(s.Id, out var od) ? od : null
                })
                .Take(300)
                .ToList();

            result.Add(new SalesAnalyticsCustomerDto
            {
                PartyId = g.PartyId,
                Name = partyNames.TryGetValue(g.PartyId, out var nm) ? nm : $"عميل {g.PartyId}",
                Amount = g.Amount,
                InvoiceCount = g.Count,
                Pct = total > 0 ? Math.Round((double)g.Amount / (double)total * 100, 1) : 0,
                Invoices = invoices
            });
        }
        return result;
    }

    private static async Task<List<SalesAnalyticsLabelValueDto>> BuildLostReasonsAsync(
        db24804Context db, List<OppRow> scopeOpps)
    {
        var lost = scopeOpps
            .Where(o => o.IsLost && o.LostReasonId.HasValue)
            .ToList();
        if (lost.Count == 0) return new List<SalesAnalyticsLabelValueDto>();

        var reasonIds = lost.Select(o => o.LostReasonId!.Value).Distinct().ToList();
        var names = await db.LostReasons.AsNoTracking()
            .Where(r => reasonIds.Contains(r.LostReasonId))
            .Select(r => new { r.LostReasonId, Name = r.ReasonNameAr ?? r.ReasonName })
            .ToDictionaryAsync(r => r.LostReasonId, r => r.Name);

        return lost
            .GroupBy(o => o.LostReasonId!.Value)
            .Select(g => new SalesAnalyticsLabelValueDto
            {
                Label = names.TryGetValue(g.Key, out var n) ? n : $"سبب {g.Key}",
                Value = g.Count()
            })
            .OrderByDescending(x => x.Value)
            .ToList();
    }

    // ════════════════════════════ القراءات الذكية ════════════════════════════
    private sealed class InsightsContext
    {
        public decimal Total { get; init; }
        public decimal PrevTotal { get; init; }
        public string? TopBranch { get; init; }
        public decimal TopBranchAmount { get; init; }
        public SalesAnalyticsEmployeeDto? TopEmployee { get; init; }
        public SalesAnalyticsCustomerDto? TopCustomer { get; init; }
        public int NewCustomers { get; init; }
        public int ReturningCustomers { get; init; }
        public int TeamLost { get; init; }
        public string? TopLostReason { get; init; }
    }

    private static List<string> BuildInsights(InsightsContext c)
    {
        var list = new List<string>();
        var f = CultureInfo.InvariantCulture;

        // ① اتجاه الإجمالي مقارنة بالسابقة
        if (c.PrevTotal > 0)
        {
            var pct = (double)(c.Total - c.PrevTotal) / (double)c.PrevTotal * 100;
            list.Add(pct >= 0
                ? $"مبيعات هذه الفترة أعلى بنسبة {pct.ToString("0.#", f)}% من الفترة السابقة ({FmtMoney(c.Total)} مقابل {FmtMoney(c.PrevTotal)})."
                : $"مبيعات هذه الفترة أقل بنسبة {(-pct).ToString("0.#", f)}% من الفترة السابقة ({FmtMoney(c.Total)} مقابل {FmtMoney(c.PrevTotal)}).");
        }
        else if (c.Total > 0)
        {
            list.Add($"سجّلت هذه الفترة مبيعات بقيمة {FmtMoney(c.Total)} مقابل صفر في الفترة السابقة.");
        }

        // ② أعلى فرع
        if (!string.IsNullOrWhiteSpace(c.TopBranch) && c.TopBranchAmount > 0)
            list.Add($"الفرع «{c.TopBranch}» هو الأعلى إسهاماً بمبيعات {FmtMoney(c.TopBranchAmount)}.");

        // ③ أعلى موظف قيمة فواتير
        if (c.TopEmployee != null && c.TopEmployee.SalesAmount > 0)
            list.Add($"الموظف «{c.TopEmployee.Name}» الأعلى قيمة فواتير ({FmtMoney(c.TopEmployee.SalesAmount)}) بمتوسط فاتورة {FmtMoney(c.TopEmployee.AvgInvoice)}.");

        // ④ أكبر عميل
        if (c.TopCustomer != null && c.TopCustomer.Amount > 0)
            list.Add($"أكبر عملاء الفترة «{c.TopCustomer.Name}» بقيمة {FmtMoney(c.TopCustomer.Amount)} تمثّل {c.TopCustomer.Pct.ToString("0.#", f)}% من إجمالي الفترة.");

        // ⑤ عملاء جدد/عائدون
        if (c.NewCustomers > 0 || c.ReturningCustomers > 0)
            list.Add($"تعاملت الفترة مع {c.NewCustomers} عميل جديد و{c.ReturningCustomers} عميل عائد.");

        // ⑥ خسائر
        if (c.TeamLost > 0)
        {
            var suffix = string.IsNullOrWhiteSpace(c.TopLostReason)
                ? "دون تسجيل سبب."
                : $"أكثرها تكراراً «{c.TopLostReason}».";
            list.Add($"رُصدت {c.TeamLost} فرصة خاسرة ضمن الفرص النشطة في الفترة — {suffix}");
        }

        return list;
    }

    private static string FmtMoney(decimal v)
        => v.ToString("N0", CultureInfo.InvariantCulture) + " ج";

    private static (List<string>, List<decimal>, List<decimal>) BuildTrend(
        List<SaleRow> cur, DateTime from,
        List<SaleRow> prev, DateTime prevFrom, int days)
    {
        int step = days <= 31 ? 1 : days <= 183 ? 7 : 30;

        List<DateTime> StartsOf(DateTime baseDate)
        {
            var list = new List<DateTime>();
            var end = baseDate.AddDays(days - 1);
            for (var d = baseDate.Date; d <= end; d = d.AddDays(step))
                list.Add(d);
            if (list.Count == 0) list.Add(baseDate.Date);
            return list;
        }

        var curStarts = StartsOf(from);
        var prevStarts = StartsOf(prevFrom);
        var n = Math.Max(curStarts.Count, prevStarts.Count);

        int Bucket(DateTime date, DateTime baseDate, int count)
        {
            var idx = (int)((date.Date - baseDate.Date).TotalDays) / step;
            if (idx < 0) idx = 0;
            if (idx >= count) idx = count - 1;
            return idx;
        }

        var curSum = new decimal[n];
        var prevSum = new decimal[n];
        foreach (var s in cur) curSum[Bucket(s.Date, from, n)] += s.Net;
        foreach (var s in prev) prevSum[Bucket(s.Date, prevFrom, n)] += s.Net;

        return (curStarts.Select(d => d.ToString("dd/MM")).ToList(), curSum.ToList(), prevSum.ToList());
    }
}
