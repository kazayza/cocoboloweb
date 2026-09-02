using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

// صفوف خام خفيفة للاستعلامات
public record LeadRow(int LeadId, DateTime? LeadDate, string? LeadStatus, bool IsConverted, int? ConvertedPartyId, int? ConvertedOpportunityId, string? FormId, string? Platform, string? AdSetName, string? AdName, int? AssignedEmployeeId);
public record OppRow(int OpportunityId, int PartyId, int StageId, DateTime CreatedAt, int? TransactionId, int? SourceId, int? EmployeeId);
public record SaleRow(int TransactionId, DateTime TransactionDate, decimal NetTotalAmount, int PartyId);
public record ExpenseRow(DateTime ExpenseDate, decimal Amount, string? Notes, string ExpenseName);
public record ExpenseGroupRow(int ExpenseGroupId, string? ExpenseGroupName, int? ParentGroupId);

public interface IMarketingDashboardService
{
    Task<MarketingDashboardDto> GetDashboardAsync(
        DateTime dateFrom, DateTime dateTo, string channel = "all");
}

// ═══════════════════════════════════════════════════════════════
// 📊 Marketing Performance Dashboard — Service
// ── القمع (5 مراحل): اللييدز هو الأساس ← بعده الفرص
//    1) إجمالي اللييدز           ← LeadsCRM (الكل)
//    2) تم التواصل              ← LeadStatus = 'تم التواصل'
//    3) عميل مؤهل (محول)        ← اللييدز المحولين المربوطين بفرص مرحلة 1
//    4) قيد التقدم (مهتم+عالي)  ← فرص المرحلتين 2 + 7
//    5) تم البيع                ← فرص المرحلة 3 المرتبطة بفواتير
// ── الحملات: من AdSetName + AdName (إعلان/مجموعة إعلانات)
// ── المصادر: ContactSources (مصدر العميل)
// ── الإنفاق: مجموعة "اعلانات" (29) + فروعها
// ── الإيرادات: فواتير مرتبطة بفرص تم البيع
// ═══════════════════════════════════════════════════════════════
public class MarketingDashboardService : IMarketingDashboardService
{
    private readonly IDbContextFactory<db24804Context> _factory;

    public MarketingDashboardService(IDbContextFactory<db24804Context> factory)
    {
        _factory = factory;
    }

    // 🎯 الأهداف (ثابتة مؤقتاً — تعدل هنا)
    private static readonly (string Metric, double Target)[] DefaultTargets =
    {
        ("لييدز",            500),
        ("تم التواصل",       300),
        ("عملاء مؤهلين",     150),
        ("صفقات مغلقة",       30),
        ("إيرادات",    3_000_000),
    };

    private const decimal MonthlyTarget = 3_000_000m;

    public async Task<MarketingDashboardDto> GetDashboardAsync(
        DateTime dateFrom, DateTime dateTo, string channel = "all")
    {
        using var db = await _factory.CreateDbContextAsync();

        dateFrom = dateFrom.Date;
        dateTo = dateTo.Date;
        var days = (int)(dateTo - dateFrom).TotalDays + 1;

        var prevFrom = dateFrom.AddDays(-days);
        var prevTo = dateFrom.AddDays(-1);

        var marketingGroupIds = await GetMarketingGroupIdsAsync(db);

        var sources = await db.ContactSources.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.SourceId)
            .ToListAsync();

        // ── اللييدز ──
        var leads = await db.LeadsCRMs.AsNoTracking()
            .Where(l => l.LeadDate >= dateFrom && l.LeadDate < dateTo.AddDays(1))
            .Select(l => new LeadRow(l.LeadId, l.LeadDate, l.LeadStatus, l.IsConverted, l.ConvertedPartyId, l.ConvertedOpportunityId, l.FormId, l.Platform, l.AdSetName, l.AdName, l.AssignedEmployeeId))
            .ToListAsync();

        var prevLeads = await db.LeadsCRMs.AsNoTracking()
            .Where(l => l.LeadDate >= prevFrom && l.LeadDate < prevTo.AddDays(1))
            .Select(l => new LeadRow(l.LeadId, l.LeadDate, l.LeadStatus, l.IsConverted, l.ConvertedPartyId, l.ConvertedOpportunityId, l.FormId, l.Platform, l.AdSetName, l.AdName, l.AssignedEmployeeId))
            .ToListAsync();

        // ── الفرص (مراحل البيع) ──
        var opps = await db.SalesOpportunities.AsNoTracking()
            .Where(o => o.CreatedAt >= dateFrom && o.CreatedAt < dateTo.AddDays(1) && o.IsActive)
            .Select(o => new OppRow(o.OpportunityId, o.PartyId, o.StageId, o.CreatedAt, o.TransactionId, o.SourceId, o.EmployeeId))
            .ToListAsync();

        var prevOpps = await db.SalesOpportunities.AsNoTracking()
            .Where(o => o.CreatedAt >= prevFrom && o.CreatedAt < prevTo.AddDays(1) && o.IsActive)
            .Select(o => new OppRow(o.OpportunityId, o.PartyId, o.StageId, o.CreatedAt, o.TransactionId, o.SourceId, o.EmployeeId))
            .ToListAsync();

        // ── الفواتير (بيع) ──
        var sales = await db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == "Sale"
                        && t.TransactionDate >= dateFrom && t.TransactionDate < dateTo.AddDays(1))
            .Select(t => new SaleRow(t.TransactionId, t.TransactionDate, t.NetTotalAmount ?? 0m, t.PartyId))
            .ToListAsync();

        var prevSales = await db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == "Sale"
                        && t.TransactionDate >= prevFrom && t.TransactionDate < prevTo.AddDays(1))
            .Select(t => new SaleRow(t.TransactionId, t.TransactionDate, t.NetTotalAmount ?? 0m, t.PartyId))
            .ToListAsync();

        // ── المصروفات (مجموعة اعلانات) ──
        var expenses = await db.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDate >= dateFrom && e.ExpenseDate < dateTo.AddDays(1)
                        && marketingGroupIds.Contains(e.ExpenseGroupId))
            .Select(e => new ExpenseRow(e.ExpenseDate, e.Amount, e.Notes, e.ExpenseName))
            .ToListAsync();

        var prevExpenses = await db.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDate >= prevFrom && e.ExpenseDate < prevTo.AddDays(1)
                        && marketingGroupIds.Contains(e.ExpenseGroupId))
            .Select(e => new ExpenseRow(e.ExpenseDate, e.Amount, e.Notes, e.ExpenseName))
            .ToListAsync();

        // ── موظفو المبيعات ──
        var salesEmployees = await db.Users.AsNoTracking()
            .CountAsync(u => u.IsActive == true && u.Role == "Sales");
        var salesEmployeeCount = Math.Max(salesEmployees, 1);

        // ── الفلترة على القناة (مطبقة فعلياً على كل الحسابات) ──
        var channelFilter = (int? sourceId) => channel == "all" || ChannelOfSource(sourceId) == channel;
        var leadChannelFilter = (LeadRow l) => channel == "all" || ChannelOfSource(LeadSourceId(l)) == channel;

        // 🎯 اللييدز والفرص المفلترة
        var filteredLeads = leads.Where(leadChannelFilter).ToList();
        var filteredPrevLeads = prevLeads.Where(leadChannelFilter).ToList();
        var filteredOpps = opps.Where(o => channelFilter(o.SourceId)).ToList();
        var filteredPrevOpps = prevOpps.Where(o => channelFilter(o.SourceId)).ToList();

        // ── الحسابات (على المفلتر) ──
        var spend = expenses.Sum(e => e.Amount);
        var prevSpend = prevExpenses.Sum(e => e.Amount);

        // الإنفاق لكل قناة (تقريبي من وصف المصروف)
        var spendF = channel == "all" ? spend : expenses.Where(e => ChannelOfExpense(e.Notes + " " + e.ExpenseName) == channel).Sum(e => e.Amount);
        var prevSpendF = channel == "all" ? prevSpend : prevExpenses.Where(e => ChannelOfExpense(e.Notes + " " + e.ExpenseName) == channel).Sum(e => e.Amount);

        // 1) إجمالي اللييدز
        var leadsCount = filteredLeads.Count;
        var prevLeadsCount = filteredPrevLeads.Count;

        // 2) تم التواصل
        var rejected = filteredLeads.Count(l => l.LeadStatus == "مرفوض");
        var prevRejected = filteredPrevLeads.Count(l => l.LeadStatus == "مرفوض");
        var contacted = filteredLeads.Count(l => l.LeadStatus == "تم التواصل");
        var prevContacted = filteredPrevLeads.Count(l => l.LeadStatus == "تم التواصل");

        // 3) المؤهل = اللييدز المحولين المربوطين بفرص مرحلة 1
        var stage1OppIds = filteredOpps.Where(o => o.StageId == 1).Select(o => o.OpportunityId).ToHashSet();
        var prevStage1OppIds = filteredPrevOpps.Where(o => o.StageId == 1).Select(o => o.OpportunityId).ToHashSet();

        // المحول = اللييدز المحولة (من جدول اللييدز — الأساس) — 28
        var qualified = filteredLeads.Count(l => l.IsConverted && l.ConvertedOpportunityId != null);
        var prevQualified = filteredPrevLeads.Count(l => l.IsConverted && l.ConvertedOpportunityId != null && prevStage1OppIds.Contains(l.ConvertedOpportunityId.Value));

        // 4) قيد التقدم = فرص المرحلة 2 + 7
        // فرص بيع = عالي الاهتمام (7) + معلق/مؤجل (9)
        var inProgress = filteredOpps.Count(o => o.StageId == 7 || o.StageId == 9);
        var prevInProgress = filteredPrevOpps.Count(o => o.StageId == 2 || o.StageId == 7);

        // 5) تم البيع = فرص المرحلة 3 المرتبطة بفواتير
        var deals = filteredOpps.Count(o => o.StageId == 3 && o.TransactionId != null);
        var prevDeals = filteredPrevOpps.Count(o => o.StageId == 3 && o.TransactionId != null);

        // الإيرادات = الفواتير المرتبطة بفرص تم البيع
        var linkedInvoiceIds = filteredOpps.Where(o => o.StageId == 3 && o.TransactionId != null).Select(o => o.TransactionId!.Value).ToHashSet();
        var prevLinkedInvoiceIds = filteredPrevOpps.Where(o => o.StageId == 3 && o.TransactionId != null).Select(o => o.TransactionId!.Value).ToHashSet();

        var revenue = sales.Where(s => linkedInvoiceIds.Contains(s.TransactionId)).Sum(s => s.NetTotalAmount);
        var prevRevenue = prevSales.Where(s => prevLinkedInvoiceIds.Contains(s.TransactionId)).Sum(s => s.NetTotalAmount);

        // العملاء = عملاء فواتير مرتبطة بفرص تم البيع
        var customers = sales.Where(s => linkedInvoiceIds.Contains(s.TransactionId)).Select(s => s.PartyId).Distinct().Count();
        var prevCustomers = prevSales.Where(s => prevLinkedInvoiceIds.Contains(s.TransactionId)).Select(s => s.PartyId).Distinct().Count();

        var cpl = leadsCount > 0 ? spendF / leadsCount : 0;
        var cac = customers > 0 ? spendF / customers : 0;
        var roas = spendF > 0 ? revenue / spendF : 0;

        var prevCpl = prevLeadsCount > 0 ? prevSpendF / prevLeadsCount : 0;
        var prevCac = prevCustomers > 0 ? prevSpendF / prevCustomers : 0;
        var prevRoas = prevSpendF > 0 ? prevRevenue / prevSpendF : 0;

        var convRate = leadsCount > 0 ? (double)qualified / leadsCount * 100 : 0;
        var contactRate = leadsCount > 0 ? (double)contacted / leadsCount * 100 : 0;
        var prevConvRate = prevLeadsCount > 0 ? (double)prevQualified / prevLeadsCount * 100 : 0;

        // ── 🎯 إنجاز تارجيت الموظفين ──
        // إيراد كل موظف = الفواتير المرتبطة بفرص تم البيع اللي هو مسؤول عنها (EmployeeID في الفرصة)
        var employeeTargets = await BuildEmployeeTargetsAsync(db, dateFrom, dateTo, filteredOpps, sales, channelFilter);

        // ── DTO ──
        var result = new MarketingDashboardDto
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            Channel = channel,
            ChannelName = ChannelName(channel),
            RevenueIsOverall = channel != "all",
            MonthlySalesTarget = MonthlyTarget,
            SalesEmployeeCount = salesEmployees,
            PerEmployeeMonthlyTarget = Math.Round(MonthlyTarget / salesEmployeeCount),
            CompanyActualRevenue = revenue,
            CompanyTargetPercent = MonthlyTarget > 0 ? (double)(revenue / MonthlyTarget * 100m) : 0,
            CompanyGaugeStyle = BuildGaugeStyle(MonthlyTarget > 0 ? (double)(revenue / MonthlyTarget * 100m) : 0),
            EmployeeTargets = employeeTargets,
        };

        result.Kpis = BuildKpis(spendF, prevSpendF, leadsCount, prevLeadsCount, cpl, prevCpl,
            contacted, prevContacted, qualified, prevQualified, inProgress, prevInProgress,
            deals, prevDeals, customers, prevCustomers, cac, prevCac, revenue, prevRevenue, roas, prevRoas,
            convRate, prevConvRate);

        result.OverallDelta = result.Kpis.Where(k => k.Delta.HasValue).Select(k => k.Delta!.Value).DefaultIfEmpty(0).Average();

        // ── القمع (5 مراحل) ──
        result.Funnel = BuildFunnel(leadsCount, rejected, qualified, inProgress, deals);

        // ── الحملات (من AdSetName + AdName) ──
        var campaigns = BuildCampaigns(filteredLeads);
        result.TopCampaigns = campaigns.OrderByDescending(c => c.Qualified).ThenByDescending(c => c.Leads).Take(5).ToList();
        result.WorstCampaigns = campaigns.Where(c => c.Leads > 0).OrderBy(c => c.Qualified).ThenBy(c => c.Leads).Take(5).ToList();

        // ── المصادر ──
        result.Channels = BuildChannels(sources, filteredLeads, filteredOpps, expenses, channel);

        // ── المستهدف ──
        result.Targets = BuildTargets(leadsCount, contacted, qualified, deals, revenue);

        // ── التسويق مقابل المبيعات (بتسربات فعلية) ──
        double VsDrop(int from, int to) => from > 0 ? Math.Round(100 - (double)to / from * 100, 1) : 0;

        var vsHealthy = qualified > 0 && customers > 0 && (double)customers / qualified * 100 >= 10;

        result.VsSales = new MarketingVsSalesDto
        {
            Leads = leadsCount,
            Contacted = contacted,
            Qualified = qualified,
            Opportunities = filteredOpps.Count,
            Customers = customers,
            SalesInvoices = sales.Count(s => linkedInvoiceIds.Contains(s.TransactionId)),
            SalesValue = revenue,
            ContactedRate = contactRate,
            QualifiedRate = convRate,
            ContactedDrop = VsDrop(leadsCount, contacted),
            QualifiedDrop = VsDrop(contacted, qualified),
            OpportunitiesDrop = VsDrop(qualified, filteredOpps.Count),
            CustomersDrop = VsDrop(filteredOpps.Count, customers),
            StatusText = vsHealthy ? "التسويق والمبيعات يعملان بشكل جيد" : "يوجد تسرب كبير بين التسويق والمبيعات",
            StatusClass = vsHealthy ? "vs-good" : "vs-warn",
        };

        // ── التنبيهات + المؤشر (المعادلة العادلة) ──
        var custConvRate = leadsCount > 0 ? (double)customers / leadsCount * 100 : 0;
        result.Alerts = BuildAlerts(roas, custConvRate, contactRate, cac, leadsCount, customers);
        (result.PerformanceScore, result.ScoreLabel, result.ScoreClass, result.ScoreCapped, result.ScoreComponents) =
            BuildScore(roas, custConvRate, cac, contactRate, convRate, leadsCount, customers, revenue);

        // ── الاتجاهات ──
        (result.LeadsTrend, result.ConversionTrend, result.CplTrend, result.RoasTrend) =
            BuildTrends(leads, expenses, sales, opps, dateFrom, dateTo);

        return result;
    }

    // ═══════════════════════════════════════════
    // 🃏 الكروت (11 كارت)
    // ═══════════════════════════════════════════
    private static List<MarketingKpiDto> BuildKpis(
        decimal spend, decimal prevSpend,
        int leads, int prevLeads,
        decimal cpl, decimal prevCpl,
        int contacted, int prevContacted,
        int qualified, int prevQualified,
        int inProgress, int prevInProgress,
        int deals, int prevDeals,
        int customers, int prevCustomers,
        decimal cac, decimal prevCac,
        decimal revenue, decimal prevRevenue,
        decimal roas, decimal prevRoas,
        double convRate, double prevConvRate)
    {
        const decimal cplThreshold = 1000m;
        return new List<MarketingKpiDto>
        {
            new() { Key = "leads", Label = "إجمالي المحتملين", Value = leads.ToString("N0"), Delta = DeltaPct(leads, prevLeads), Color = "#8b5cf6", Icon = "person_search" },
            new() { Key = "contacted", Label = "تم التواصل", Value = contacted.ToString("N0"), Delta = DeltaPct(contacted, prevContacted), Color = "#3b82f6", Icon = "phone_in_talk" },
            new() { Key = "qualified", Label = "مؤهل", Value = qualified.ToString("N0"), Delta = DeltaPct(qualified, prevQualified), Color = "#7447c6", Icon = "verified" },
            new() { Key = "conv", Label = "تحويل ليد → فرصة", Value = convRate.ToString("0.0") + "%", Delta = DeltaPct(convRate, prevConvRate), Color = "#0ea5e9", Icon = "conv" },
            new() { Key = "custconv", Label = "تحويل ليد → عميل", Value = (leads > 0 ? (double)customers / leads * 100 : 0).ToString("0.0") + "%", Delta = DeltaPct(leads > 0 ? (double)customers / leads * 100 : 0, prevLeads > 0 ? (double)prevCustomers / prevLeads * 100 : 0), Color = "#10b981", Icon = "swap_horiz" },
            new() { Key = "progress", Label = "فرص البيع", Value = inProgress.ToString("N0"), Delta = DeltaPct(inProgress, prevInProgress), Color = "#f59e0b", Icon = "trending_up" },
            new() { Key = "deals", Label = "صفقات مغلقة", Value = deals.ToString("N0"), Delta = DeltaPct(deals, prevDeals), Color = "#ef4444", Icon = "handshake" },
            new() { Key = "customers", Label = "العملاء", Value = customers.ToString("N0"), Sub = "مرتبطين بفرص تم بيع", Delta = DeltaPct(customers, prevCustomers), Color = "#14b8a6", Icon = "groups" },
            new() { Key = "revenue", Label = "الإيرادات", Value = FmtMoney(revenue), IsMoney = true, Delta = DeltaPct(revenue, prevRevenue), Color = "#2563eb", Icon = "savings" },
            new() { Key = "spend", Label = "الإنفاق الإعلاني", Value = FmtMoney(spend), IsMoney = true, Delta = DeltaPct(spend, prevSpend), Color = "#f472b6", Icon = "campaign" },
            new() { Key = "cpl", Label = "تكلفة اللييد CPL", Value = FmtMoney(cpl), IsMoney = true, Delta = DeltaPct(cpl, prevCpl), GoodWhenUp = false, Color = "#f59e0b", Icon = "payments",
                    Sub = cpl > cplThreshold ? "مرتفعة 🔴" : "منخفضة 🟢", SubClass = cpl > cplThreshold ? "sub-bad" : "sub-good" },
            new() { Key = "cac", Label = "تكلفة العميل CAC", Value = FmtMoney(cac), IsMoney = true, Delta = DeltaPct(cac, prevCac), GoodWhenUp = false, Color = "#14b8a6", Icon = "person_add" },
            new() { Key = "roas", Label = "العائد ROAS", Value = roas.ToString("0.0") + "x", Delta = DeltaPct(roas, prevRoas), Color = "#16a34a", Icon = "trending_up" },
        };
    }

    private static double? DeltaPct(decimal cur, decimal prev) => prev != 0 ? (double)((cur - prev) / prev * 100m) : null;
    private static double? DeltaPct(int cur, int prev) => prev != 0 ? (double)(cur - prev) / prev * 100 : null;
    private static double? DeltaPct(double cur, double prev) => prev != 0 ? (cur - prev) / prev * 100 : null;

    // ═══════════════════════════════════════════
    // 🪜 القمع (5 مراحل) — بألوان معبرة وتسرب صحيح المنطق
    // ═══════════════════════════════════════════
    private static List<MarketingFunnelStageDto> BuildFunnel(
        int leads, int rejected, int qualified, int inProgress, int deals)
    {
        var stages = new (string Name, string Sub, int Count, bool IsSide, string Color, string ColorSoft)[]
        {
            ("إجمالي اللييدز",   "كل المحتملين",            leads,      false, "#1769d5", "#e8f1fd"),
            ("مرفوض",           "من جدول اللييدز",          rejected,   true,  "#e11d48", "#ffe4e6"),
            ("محوّل لفرصة",      "ليدز محوّلة في الفترة",     qualified,  false, "#0e9488", "#e6fffa"),
            ("فرص بيع",         "عالي الاهتمام + معلق",      inProgress, false, "#f59e0b", "#fef3c7"),
            ("تم البيع",        "المرحلة 3 + فاتورة",       deals,      false, "#7c3aed", "#f3e8ff"),
        };

        var list = new List<MarketingFunnelStageDto>();
        for (int i = 0; i < stages.Length; i++)
        {
            var (name, sub, count, isSide, color, soft) = stages[i];

            // ⭐ التسرب: المحوّل بيتقارن بالإجمالي (لأن المرفوض جانبي)، والباقي بالسابقة مباشرة
            double? drop;
            if (i == 0 || isSide)
                drop = null;
            else if (i == 2)
                drop = stages[0].Count > 0
                    ? Math.Round(100 - (double)count / stages[0].Count * 100, 1)
                    : null;
            else
                drop = stages[i - 1].Count > 0
                    ? Math.Round(100 - (double)count / stages[i - 1].Count * 100, 1)
                    : null;

            double? retention = drop.HasValue ? 100 - drop : null;

            list.Add(new MarketingFunnelStageDto
            {
                Step = i + 1,
                Name = name,
                Sub = sub,
                Count = count,
                PercentOfFirst = leads > 0 ? Math.Round((double)count / leads * 100, 1) : 0,
                Retention = retention,
                DropOff = drop,
                IsSideStage = isSide,
                Color = color,
                ColorSoft = soft,
            });
        }
        return list;
    }

    // ═══════════════════════════════════════════
    // 📢 الحملات (من AdSetName + AdName)
    // ═══════════════════════════════════════════
    private static List<MarketingCampaignDto> BuildCampaigns(List<LeadRow> leads)
    {
        return leads
            .GroupBy(l => CampaignNameOf(l))
            .Select(g => new MarketingCampaignDto
            {
                Name = g.Key,
                Leads = g.Count(),
                Contacted = g.Count(l => l.LeadStatus == "تم التواصل"),
                Qualified = g.Count(l => l.IsConverted),
            })
            .OrderByDescending(c => c.Leads)
            .ToList();
    }

    // اسم الحملة: AdSetName أولاً، ولو فاضي AdName، ولو فاضي "غير محدد"
    private static string CampaignNameOf(LeadRow l)
    {
        if (!string.IsNullOrWhiteSpace(l.AdSetName)) return l.AdSetName!;
        if (!string.IsNullOrWhiteSpace(l.AdName)) return l.AdName!;
        return "غير محدد";
    }

    // ═══════════════════════════════════════════
    // 🌐 المصادر
    // ═══════════════════════════════════════════
    private static List<MarketingChannelDto> BuildChannels(
        List<ContactSource> sources, List<LeadRow> leads, List<OppRow> opps,
        List<ExpenseRow> expenses, string channelFilter)
    {
        var result = new List<MarketingChannelDto>();

        var sourceColors = new Dictionary<int, string>
        {
            [1] = "#25d366", [2] = "#1877f2", [3] = "#22c55e", [4] = "#f97316",
            [5] = "#e4405f", [6] = "#f59e0b", [7] = "#4285f4", [8] = "#010101",
        };

        foreach (var src in sources)
        {
            if (channelFilter != "all" && ChannelOfSource(src.SourceId) != channelFilter) continue;

            var srcLeads = leads.Where(l => LeadSourceId(l) == src.SourceId).ToList();
            var srcOpps = opps.Where(o => o.SourceId == src.SourceId).ToList();
            var srcSpend = expenses.Where(e => SourceOfExpense(e.Notes + " " + e.ExpenseName) == src.SourceId).Sum(e => e.Amount);

            if (srcLeads.Count == 0 && srcOpps.Count == 0 && srcSpend == 0) continue;

            result.Add(new MarketingChannelDto
            {
                Key = src.SourceId.ToString(),
                Name = string.IsNullOrWhiteSpace(src.SourceNameAr) ? src.SourceName : src.SourceNameAr!,
                Color = sourceColors.GetValueOrDefault(src.SourceId, "#94a3b8"),
                Leads = srcLeads.Count,
                Contacted = srcLeads.Count(l => l.LeadStatus == "تم التواصل"),
                Qualified = srcLeads.Count(l => l.IsConverted),
                Customers = srcOpps.Count(o => o.StageId == 3 && o.TransactionId != null),
                Spend = srcSpend,
                Cpl = srcLeads.Count > 0 ? srcSpend / srcLeads.Count : 0,
                Cac = 0,
            });
        }

        // الإجمالي
        var totalLeads = leads.Count;
        var totalSpend = expenses.Sum(e => e.Amount);
        var totalCust = opps.Count(o => o.StageId == 3 && o.TransactionId != null);
        result.Add(new MarketingChannelDto
        {
            Key = "total",
            Name = "الإجمالي",
            Color = "#0f172a",
            Leads = totalLeads,
            Contacted = leads.Count(l => l.LeadStatus == "تم التواصل"),
            Qualified = leads.Count(l => l.IsConverted),
            Customers = totalCust,
            Spend = totalSpend,
            Cpl = totalLeads > 0 ? totalSpend / totalLeads : 0,
            Cac = totalCust > 0 ? totalSpend / totalCust : 0,
            IsTotal = true,
        });

        return result;
    }

    // ═══════════════════════════════════════════
    // 🎯 المستهدف
    // ═══════════════════════════════════════════
    private static List<MarketingTargetDto> BuildTargets(
        int leads, int contacted, int qualified, int deals, decimal revenue)
    {
        var actuals = new Dictionary<string, double>
        {
            ["لييدز"] = leads,
            ["تم التواصل"] = contacted,
            ["عملاء مؤهلين"] = qualified,
            ["صفقات مغلقة"] = deals,
            ["إيرادات"] = (double)revenue,
        };

        var list = new List<MarketingTargetDto>();
        foreach (var (metric, target) in DefaultTargets)
        {
            var actual = actuals.GetValueOrDefault(metric, 0);
            var percent = target > 0 ? actual / target * 100 : 0;
            list.Add(new MarketingTargetDto
            {
                Metric = metric,
                Target = metric == "إيرادات" ? FmtMoney((decimal)target) : ((int)target).ToString("N0"),
                Actual = metric == "إيرادات" ? FmtMoney((decimal)actual) : ((int)actual).ToString("N0"),
                Percent = percent,
                Achieved = percent >= 100,
            });
        }
        return list;
    }

    // ═══════════════════════════════════════════
    // ⚠️ التنبيهات (نسخة محدثة — تحويل حقيقي + CAC)
    // ═══════════════════════════════════════════
    private static List<MarketingAlertDto> BuildAlerts(
        decimal roas, double custConvRate, double contactRate, decimal cac,
        int leads, int customers)
    {
        var alerts = new List<MarketingAlertDto>();

        if (roas > 0)
        {
            alerts.Add(roas >= 4m
                ? new MarketingAlertDto { Type = "good", Text = "العائد على الإنفاق ممتاز", Detail = $"{roas:0.0}x — أعلى من المستهدف (4x)" }
                : new MarketingAlertDto { Type = "bad", Text = "العائد على الإنفاق منخفض", Detail = $"{roas:0.0}x — أقل من المستهدف (4x)" });
        }

        if (leads > 0)
        {
            alerts.Add(custConvRate >= 10
                ? new MarketingAlertDto { Type = "good", Text = "معدل التحويل للعميل ممتاز", Detail = $"{custConvRate:0.0}% من اللييدز اتحولوا عملاء دفعوا (المستهدف 10%+)" }
                : new MarketingAlertDto { Type = "warn", Text = "معدل التحويل للعميل منخفض", Detail = $"{custConvRate:0.0}% فقط اتحولوا عملاء (المستهدف 10%+)" });
        }

        if (leads > 0)
        {
            alerts.Add(contactRate >= 50
                ? new MarketingAlertDto { Type = "good", Text = "نسبة التواصل ممتازة", Detail = $"{contactRate:0.0}% من اللييدز تم التواصل معهم" }
                : new MarketingAlertDto { Type = "warn", Text = "نسبة التواصل منخفضة", Detail = $"{contactRate:0.0}% (المستهدف 50%+)" });
        }

        if (customers > 0 && cac > 0)
        {
            alerts.Add(new MarketingAlertDto { Type = cac > 15_000m ? "bad" : "info", Text = "تكلفة اكتساب العميل CAC", Detail = $"{cac:N0} ج لكل عميل جديد" });
        }

        if (leads > 0 && customers == 0)
        {
            alerts.Add(new MarketingAlertDto { Type = "bad", Text = "لا يوجد عملاء في هذه الفترة!", Detail = $"{leads} ليد بدون أي تحويل لعميل — راجع جودة الليدز والمبيعات" });
        }

        return alerts.Take(5).ToList();
    }

    // ═══════════════════════════════════════════
    // 🏆 المؤشر — المعادلة العادلة (6 مكونات مع تارجت الإيراد)
    // ── تحويل ليد→عميل 25pts (≥6%) • CAC 15pts (≤15K) • ROAS 20pts (≥4x)
    // ── 🎯 إنجاز تارجت الإيراد 20pts (3M) • تواصل 10pts (≥50%) • تأهيل 10pts (≥25%)
    // ── 🚨 سقف 4/10 لو صفر عملاء أو صفر إيراد
    // ═══════════════════════════════════════════
    private static (double Score, string Label, string Class, bool Capped, List<MarketingV2ScoreComponentDto> Components) BuildScore(
        decimal roas, double custConvRate, decimal cac, double contactRate, double leadConvRate,
        int leads, int customers, decimal revenue)
    {
        const double TargetCustConv = 6.0;    // %
        const decimal TargetCac = 15_000m;    // ج
        const decimal TargetRoas = 4m;        // x
        const double TargetContact = 50.0;    // %
        const double TargetQual = 25.0;       // %

        var components = new List<MarketingV2ScoreComponentDto>();

        // 1) تحويل ليد→عميل — 25 نقطة
        var custConvScore = Math.Min(10, custConvRate / TargetCustConv * 10);
        components.Add(new MarketingV2ScoreComponentDto
        {
            Label = "تحويل ليد → عميل",
            Earned = Math.Round(custConvScore, 1),
            Weight = 25,
            Detail = $"{custConvRate:0.0}% من مستهدف {TargetCustConv:0}%",
        });

        // 2) CAC — 15 نقطة (عكسي)
        double cacScore;
        if (customers == 0 || cac <= 0) cacScore = 0;
        else cacScore = (double)Math.Min(10m, TargetCac / cac * 10m);
        components.Add(new MarketingV2ScoreComponentDto
        {
            Label = "تكلفة العميل CAC",
            Earned = Math.Round(cacScore, 1),
            Weight = 15,
            Detail = customers > 0 ? $"{cac:N0}ج — الكاملة عند ≤{TargetCac:N0}ج" : "لا يوجد عملاء",
        });

        // 3) ROAS — 20 نقطة
        var roasScore = Math.Min(10, (double)roas / (double)TargetRoas * 10);
        components.Add(new MarketingV2ScoreComponentDto
        {
            Label = "العائد ROAS",
            Earned = Math.Round(roasScore, 1),
            Weight = 20,
            Detail = $"{roas:0.0}x من مستهدف {TargetRoas:0.0}x",
        });

        // 4) 🎯 إنجاز تارجت الإيراد (3 مليون) — 20 نقطة
        var revenueTargetScore = MonthlyTarget > 0
            ? Math.Min(10, (double)(revenue / MonthlyTarget * 10m))
            : 0;
        components.Add(new MarketingV2ScoreComponentDto
        {
            Label = "إنجاز تارجت الإيراد",
            Earned = Math.Round(revenueTargetScore, 1),
            Weight = 20,
            Detail = $"{FmtMoney(revenue)} من {FmtMoney(MonthlyTarget)} ({(double)(revenue / MonthlyTarget * 100m):0.0}%)",
        });

        // 5) نسبة التواصل — 10 نقاط
        var contactScore = Math.Min(10, contactRate / TargetContact * 10);
        components.Add(new MarketingV2ScoreComponentDto
        {
            Label = "نسبة التواصل",
            Earned = Math.Round(contactScore, 1),
            Weight = 10,
            Detail = $"{contactRate:0.0}% من مستهدف {TargetContact:0}%",
        });

        // 6) نسبة التأهيل — 10 نقاط
        var qualScore = Math.Min(10, leadConvRate / TargetQual * 10);
        components.Add(new MarketingV2ScoreComponentDto
        {
            Label = "نسبة التأهيل (ليد→فرصة)",
            Earned = Math.Round(qualScore, 1),
            Weight = 10,
            Detail = $"{leadConvRate:0.0}% من مستهدف {TargetQual:0}%",
        });

        // ⭐ الإجمالي من 10
        var score = Math.Round(components.Sum(c => c.Points) / 10.0, 1);

        // 🚨 سقف الحماية
        var capped = customers == 0 || revenue == 0;
        if (capped) score = Math.Min(score, 4.0);

        var (label, cls) = score switch
        {
            >= 9 => ("ممتاز", "score-excellent"),
            >= 8 => ("جيد جداً", "score-vgood"),
            >= 7 => ("جيد", "score-good"),
            >= 6 => ("يحتاج تحسين", "score-needs"),
            _ => ("ضعيف", "score-poor"),
        };

        return (score, label, cls, capped, components);
    }

    // ═══════════════════════════════════════════
    // 📈 الاتجاهات
    // ═══════════════════════════════════════════
    private static (List<MarketingTrendDto>, List<MarketingTrendDto>, List<MarketingTrendDto>, List<MarketingTrendDto>) BuildTrends(
        List<LeadRow> leads, List<ExpenseRow> expenses, List<SaleRow> sales, List<OppRow> opps,
        DateTime dateFrom, DateTime dateTo)
    {
        var buckets = BuildBuckets(dateFrom, dateTo);
        var leadsTrend = new List<MarketingTrendDto>();
        var convTrend = new List<MarketingTrendDto>();
        var cplTrend = new List<MarketingTrendDto>();
        var roasTrend = new List<MarketingTrendDto>();

        foreach (var (label, start, end) in buckets)
        {
            var bLeads = leads.Where(l => l.LeadDate >= start && l.LeadDate < end).ToList();
            var bQualified = bLeads.Count(l => l.IsConverted);
            var bExpense = expenses.Where(e => e.ExpenseDate >= start && e.ExpenseDate < end).Sum(e => e.Amount);
            var bRevenue = sales.Where(s => s.TransactionDate >= start && s.TransactionDate < end).Sum(s => s.NetTotalAmount);

            leadsTrend.Add(new MarketingTrendDto { Label = label, Value = bLeads.Count });
            convTrend.Add(new MarketingTrendDto { Label = label, Value = bLeads.Count > 0 ? Math.Round((double)bQualified / bLeads.Count * 100, 1) : 0 });
            cplTrend.Add(new MarketingTrendDto { Label = label, Value = bLeads.Count > 0 ? (double)Math.Round(bExpense / bLeads.Count) : 0 });
            roasTrend.Add(new MarketingTrendDto { Label = label, Value = bExpense > 0 ? (double)Math.Round(bRevenue / bExpense, 2) : 0 });
        }

        return (leadsTrend, convTrend, cplTrend, roasTrend);
    }

    private static List<(string Label, DateTime Start, DateTime End)> BuildBuckets(DateTime from, DateTime to)
    {
        var days = (int)(to - from).TotalDays + 1;
        var buckets = new List<(string, DateTime, DateTime)>();

        if (days <= 45)
        {
            for (var d = from; d <= to; d = d.AddDays(1))
                buckets.Add((d.ToString("dd/MM"), d, d.AddDays(1)));
        }
        else if (days <= 200)
        {
            var start = from.AddDays(-((int)from.DayOfWeek + 6) % 7);
            for (var w = start; w <= to; w = w.AddDays(7))
            {
                var end = w.AddDays(7);
                if (end <= from) continue;
                var s = w < from ? from : w;
                var e = end > to.AddDays(1) ? to.AddDays(1) : end;
                buckets.Add(($"أسبوع {s:dd/MM}", s, e));
            }
        }
        else
        {
            var m = from;
            while (m <= to)
            {
                var end = m.AddMonths(1);
                var s = m < from ? from : m;
                var e = end > to.AddDays(1) ? to.AddDays(1) : end;
                buckets.Add((ArabicMonthName(m.Month) + " " + m.ToString("yy"), s, e));
                m = end;
            }
        }
        return buckets;
    }

    private static string ArabicMonthName(int month) => month switch
    {
        1 => "يناير", 2 => "فبراير", 3 => "مارس", 4 => "أبريل",
        5 => "مايو", 6 => "يونيو", 7 => "يوليو", 8 => "أغسطس",
        9 => "سبتمبر", 10 => "أكتوبر", 11 => "نوفمبر", _ => "ديسمبر",
    };

    // ═══════════════════════════════════════════
    // مجموعة مصروفات الإعلانات (29 + فروعها)
    // ═══════════════════════════════════════════
    private static async Task<List<int>> GetMarketingGroupIdsAsync(db24804Context db)
    {
        var groups = await db.ExpenseGroups.AsNoTracking()
            .Select(g => new ExpenseGroupRow(g.ExpenseGroupId, g.ExpenseGroupName, g.ParentGroupId))
            .ToListAsync();
        var groupDict = groups.ToDictionary(g => g.ExpenseGroupId);

        var root = groups.FirstOrDefault(g =>
            (g.ExpenseGroupName ?? "").Contains("اعلان", StringComparison.OrdinalIgnoreCase));

        if (root is null || root.ExpenseGroupId == 0)
            return new List<int> { 29 };

        var ids = new HashSet<int>();
        CollectTree(root.ExpenseGroupId, groupDict, ids);
        return ids.ToList();
    }

    private static void CollectTree(int id, Dictionary<int, ExpenseGroupRow> all, HashSet<int> acc)
    {
        if (!acc.Add(id)) return;
        foreach (var child in all.Values.Where(g => g.ParentGroupId == id))
            CollectTree(child.ExpenseGroupId, all, acc);
    }

    // ═══════════════════════════════════════════
    // 🏷️ مساعدات المصادر
    // ═══════════════════════════════════════════
    // مصدر اللييد: FormId رقم صغير (1-20) = رقم المصدر (اللييد اليدوي)
    // غير كده من Platform (فيسبوك/انستجرام/جوجل/تيك توك)
    public static int? LeadSourceId(LeadRow l)
    {
        if (int.TryParse(l.FormId, out var fid) && fid >= 1 && fid <= 20)
            return fid;

        return (l.Platform ?? "").Trim().ToLowerInvariant() switch
        {
            "fb" or "facebook" or "فيسبوك" => 2,
            "ig" or "instagram" or "انستجرام" or "انستا" => 5,
            "google" or "جوجل" => 7,
            "tiktok" or "تيك توك" => 8,
            _ => null,
        };
    }

    public static int? SourceOfExpense(string? text)
    {
        var t = (text ?? "").ToLowerInvariant();
        if (t.Contains("فيسبوك") || t.Contains("فيس بوك") || t.Contains("facebook") || t.Contains("fb")) return 2;
        if (t.Contains("انستجرام") || t.Contains("انستا") || t.Contains("instagram") || t.Contains("ig")) return 5;
        if (t.Contains("تيك توك") || t.Contains("tiktok")) return 8;
        if (t.Contains("جوجل") || t.Contains("google")) return 7;
        if (t.Contains("واتساب") || t.Contains("whatsapp") || t.Contains("wa")) return 1;
        return null;
    }

    public static string ChannelOfExpense(string? text)
    {
        var t = (text ?? "").ToLowerInvariant();
        if (t.Contains("فيسبوك") || t.Contains("فيس بوك") || t.Contains("facebook") || t.Contains("fb")) return "fb";
        if (t.Contains("انستجرام") || t.Contains("انستا") || t.Contains("instagram") || t.Contains("ig")) return "ig";
        if (t.Contains("واتساب") || t.Contains("whatsapp") || t.Contains("wa")) return "whatsapp";
        if (t.Contains("ترافيك") || t.Contains("traffic")) return "traffic";
        return "other";
    }

    public static string ChannelOfSource(int? sourceId) => sourceId switch
    {
        2 => "fb",        // فيسبوك
        5 => "ig",        // انستجرام
        1 => "whatsapp",  // واتساب
        4 => "traffic",   // Traffic
        _ => "other",     // مكالمة، توصية، جوجل، تيك توك، عميل قديم...
    };

    public static string ChannelName(string key) => key switch
    {
        "fb" => "فيسبوك",
        "ig" => "انستجرام",
        "whatsapp" => "واتساب",
        "traffic" => "Traffic",
        "other" => "أخرى",
        _ => "الكل",
    };

    public static string FmtMoney(decimal v) =>
        v >= 1_000_000 ? (v / 1_000_000m).ToString("0.0") + "M" :
        v >= 1_000 ? (v / 1_000m).ToString("0.0") + "K" :
        v.ToString("N0");

    // ═══════════════════════════════════════════
    // 🎯 إنجاز تارجيت الموظفين (Gauge لكل موظف)
    // ═══════════════════════════════════════════
    private async Task<List<EmployeeTargetDto>> BuildEmployeeTargetsAsync(
        db24804Context db,
        DateTime dateFrom, DateTime dateTo,
        List<OppRow> filteredOpps,
        List<SaleRow> sales,
        Func<int?, bool> channelFilter)
    {
        var result = new List<EmployeeTargetDto>();

        // 1) الموظفين أصحاب دور Sales (من Users) + أسمائهم من Employees
        var salesUsers = await db.Users.AsNoTracking()
            .Where(u => u.IsActive == true && u.Role == "Sales")
            .Select(u => new { u.UserId, u.EmployeeId })
            .ToListAsync();

        if (salesUsers.Count == 0) return result;

        // أسماء الموظفين
        var empIds = salesUsers.Select(su => su.EmployeeId).Where(e => e != null).Cast<int>().ToList();
        var empNames = new Dictionary<int, string>();
        if (empIds.Any())
        {
            empNames = await db.Employees.AsNoTracking()
                .Where(e => empIds.Contains(e.EmployeeId))
                .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName);
        }

        var perEmployeeTarget = Math.Round(MonthlyTarget / salesUsers.Count);

        // 2) لكل موظف: فرص تم البيع اللي EmployeeID بتاعه (من جدول الفرص)
        //    الفرص المرتبطة بفواتير → الإيراد بتاعه
        foreach (var su in salesUsers)
        {
            var empId = su.EmployeeId ?? 0;
            var empName = empId != 0 && empNames.ContainsKey(empId)
                ? empNames[empId]
                : $"موظف ({su.UserId})";

            // فرص تم البيع للموظف ده في الفترة
            var empDealOpps = filteredOpps
                .Where(o => o.StageId == 3 && o.TransactionId != null && o.EmployeeId == empId)
                .ToList();

            // الفواتير المرتبطة بيهم
            var empInvoiceIds = empDealOpps.Select(o => o.TransactionId!.Value).ToHashSet();
            var empRevenue = sales.Where(s => empInvoiceIds.Contains(s.TransactionId)).Sum(s => s.NetTotalAmount);

            var percent = perEmployeeTarget > 0 ? (double)(empRevenue / perEmployeeTarget * 100m) : 0;

            result.Add(new EmployeeTargetDto
            {
                EmployeeId = empId != 0 ? empId : su.UserId,
                EmployeeName = empName,
                TargetAmount = perEmployeeTarget,
                ActualAmount = empRevenue,
                Percent = Math.Round(percent, 1),
                GaugeStyle = BuildGaugeStyle(percent),
                StatusClass = percent >= 100 ? "good" : percent >= 50 ? "warn" : "bad",
                StatusText = percent >= 100 ? "ممتاز" : percent >= 50 ? "قريب من الهدف" : "متأخر",
            });
        }

        return result.OrderByDescending(e => e.Percent).ToList();
    }

    // ═══════════════════════════════════════════
    // 🎨 ستايل الـ Gauge (conic-gradient)
    // ═══════════════════════════════════════════
    public static string BuildGaugeStyle(double percent)
    {
        var normalized = Math.Max(0, Math.Min(100, percent));
        var deg = normalized * 3.6;

        string color = normalized >= 100 ? "#15965e"
            : normalized >= 75 ? "#35a86d"
            : normalized >= 50 ? "#efa21b"
            : "#d93445";

        return $"background:conic-gradient({color} 0deg {deg.ToString("0.0")}deg, #e8edf3 {deg.ToString("0.0")}deg 360deg);";
    }
}
