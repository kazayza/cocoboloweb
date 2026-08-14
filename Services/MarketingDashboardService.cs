using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

// صفوف خام خفيفة للاستعلامات
public record LeadRow(int LeadId, DateTime? LeadDate, string? LeadStatus, bool IsConverted, int? ConvertedPartyId, int? ConvertedOpportunityId, string? FormId, string? Platform, string? AdSetName, string? AdName, int? AssignedEmployeeId);
public record OppRow(int OpportunityId, int PartyId, int StageId, DateTime CreatedAt, int? TransactionId, int? SourceId);
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
            .Select(o => new OppRow(o.OpportunityId, o.PartyId, o.StageId, o.CreatedAt, o.TransactionId, o.SourceId))
            .ToListAsync();

        var prevOpps = await db.SalesOpportunities.AsNoTracking()
            .Where(o => o.CreatedAt >= prevFrom && o.CreatedAt < prevTo.AddDays(1) && o.IsActive)
            .Select(o => new OppRow(o.OpportunityId, o.PartyId, o.StageId, o.CreatedAt, o.TransactionId, o.SourceId))
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

        // ── الفلترة على القناة ──
        var channelFilter = (int? sourceId) => channel == "all" || ChannelOfSource(sourceId) == channel;

        // ── الحسابات ──
        var spend = expenses.Sum(e => e.Amount);
        var prevSpend = prevExpenses.Sum(e => e.Amount);

        // الإنفاق لكل قناة (تقريبي من وصف المصروف)
        var spendF = channel == "all" ? spend : expenses.Where(e => ChannelOfExpense(e.Notes + " " + e.ExpenseName) == channel).Sum(e => e.Amount);
        var prevSpendF = channel == "all" ? prevSpend : prevExpenses.Where(e => ChannelOfExpense(e.Notes + " " + e.ExpenseName) == channel).Sum(e => e.Amount);

        // 1) إجمالي اللييدز
        var leadsCount = leads.Count;
        var prevLeadsCount = prevLeads.Count;

        // 2) تم التواصل
        var contacted = leads.Count(l => l.LeadStatus == "تم التواصل");
        var prevContacted = prevLeads.Count(l => l.LeadStatus == "تم التواصل");

        // 3) المؤهل = اللييدز المحولين المربوطين بفرص مرحلة 1
        var stage1OppIds = opps.Where(o => o.StageId == 1).Select(o => o.OpportunityId).ToHashSet();
        var prevStage1OppIds = prevOpps.Where(o => o.StageId == 1).Select(o => o.OpportunityId).ToHashSet();

        var qualified = leads.Count(l => l.IsConverted && l.ConvertedOpportunityId != null && stage1OppIds.Contains(l.ConvertedOpportunityId.Value));
        var prevQualified = prevLeads.Count(l => l.IsConverted && l.ConvertedOpportunityId != null && prevStage1OppIds.Contains(l.ConvertedOpportunityId.Value));

        // 4) قيد التقدم = فرص المرحلة 2 + 7
        var inProgress = opps.Count(o => o.StageId == 2 || o.StageId == 7);
        var prevInProgress = prevOpps.Count(o => o.StageId == 2 || o.StageId == 7);

        // 5) تم البيع = فرص المرحلة 3 المرتبطة بفواتير
        var saleOppIds = opps.Where(o => o.StageId == 3 && o.TransactionId != null).Select(o => o.OpportunityId).ToHashSet();
        var prevSaleOppIds = prevOpps.Where(o => o.StageId == 3 && o.TransactionId != null).Select(o => o.OpportunityId).ToHashSet();

        var deals = opps.Count(o => o.StageId == 3 && o.TransactionId != null);
        var prevDeals = prevOpps.Count(o => o.StageId == 3 && o.TransactionId != null);

        // الإيرادات = الفواتير المرتبطة بفرص تم البيع
        var linkedInvoiceIds = opps.Where(o => o.StageId == 3 && o.TransactionId != null).Select(o => o.TransactionId!.Value).ToHashSet();
        var prevLinkedInvoiceIds = prevOpps.Where(o => o.StageId == 3 && o.TransactionId != null).Select(o => o.TransactionId!.Value).ToHashSet();

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
        };

        result.Kpis = BuildKpis(spendF, prevSpendF, leadsCount, prevLeadsCount, cpl, prevCpl,
            contacted, prevContacted, qualified, prevQualified, inProgress, prevInProgress,
            deals, prevDeals, customers, prevCustomers, cac, prevCac, revenue, prevRevenue, roas, prevRoas,
            convRate, prevConvRate);

        result.OverallDelta = result.Kpis.Where(k => k.Delta.HasValue).Select(k => k.Delta!.Value).DefaultIfEmpty(0).Average();

        // ── القمع (5 مراحل) ──
        result.Funnel = BuildFunnel(leadsCount, contacted, qualified, inProgress, deals);

        // ── الحملات (من AdSetName + AdName) ──
        var campaigns = BuildCampaigns(leads);
        result.TopCampaigns = campaigns.OrderByDescending(c => c.Qualified).ThenByDescending(c => c.Leads).Take(5).ToList();
        result.WorstCampaigns = campaigns.Where(c => c.Leads > 0).OrderBy(c => c.Qualified).ThenBy(c => c.Leads).Take(5).ToList();

        // ── المصادر ──
        result.Channels = BuildChannels(sources, leads, opps, expenses, channel);

        // ── المستهدف ──
        result.Targets = BuildTargets(leadsCount, contacted, qualified, deals, revenue);

        // ── التسويق مقابل المبيعات ──
        result.VsSales = new MarketingVsSalesDto
        {
            Leads = leadsCount,
            Contacted = contacted,
            Qualified = qualified,
            Opportunities = opps.Count,
            Customers = customers,
            SalesInvoices = sales.Count(s => linkedInvoiceIds.Contains(s.TransactionId)),
            SalesValue = revenue,
            ContactedRate = contactRate,
            QualifiedRate = convRate,
            StatusText = convRate >= 20 ? "أداء التسويق ممتاز" : convRate >= 10 ? "أداء تسويقي جيد" : "يحتاج تحسين",
            StatusClass = convRate >= 20 ? "vs-good" : convRate >= 10 ? "vs-mid" : "vs-bad",
        };

        // ── التنبيهات + المؤشر ──
        result.Alerts = BuildAlerts(roas, convRate, contactRate, cac, spendF, leadsCount, customers);
        (result.PerformanceScore, result.ScoreLabel, result.ScoreClass) = BuildScore(convRate, contactRate, roas);

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
            new() { Key = "leads", Label = "إجمالي اللييدز", Value = leads.ToString("N0"), Delta = DeltaPct(leads, prevLeads), Color = "#8b5cf6", Icon = "person_search" },
            new() { Key = "contacted", Label = "تم التواصل", Value = contacted.ToString("N0"), Delta = DeltaPct(contacted, prevContacted), Color = "#3b82f6", Icon = "phone_in_talk" },
            new() { Key = "qualified", Label = "عملاء مؤهلين", Value = qualified.ToString("N0"), Delta = DeltaPct(qualified, prevQualified), Color = "#10b981", Icon = "verified" },
            new() { Key = "progress", Label = "فرص قيد التقدم", Value = inProgress.ToString("N0"), Delta = DeltaPct(inProgress, prevInProgress), Color = "#f59e0b", Icon = "trending_up" },
            new() { Key = "deals", Label = "صفقات مغلقة", Value = deals.ToString("N0"), Delta = DeltaPct(deals, prevDeals), Color = "#ef4444", Icon = "handshake" },
            new() { Key = "customers", Label = "عملاء فواتير", Value = customers.ToString("N0"), Sub = "مرتبطين بفرص تم بيع", Delta = DeltaPct(customers, prevCustomers), Color = "#14b8a6", Icon = "groups" },
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
    // 🪜 القمع (5 مراحل)
    // ═══════════════════════════════════════════
    private static List<MarketingFunnelStageDto> BuildFunnel(
        int leads, int contacted, int qualified, int inProgress, int deals)
    {
        var stages = new List<(string Name, string Sub, int Count)>
        {
            ("إجمالي اللييدز", "كل المحتملين", leads),
            ("تم التواصل", "أول اتصال", contacted),
            ("عميل مؤهل", "المرحلة 1 — محول", qualified),
            ("قيد التقدم", "المرحلتين 2+7 — مهتم/عالي", inProgress),
            ("تم البيع", "المرحلة 3 + فاتورة", deals),
        };

        var list = new List<MarketingFunnelStageDto>();
        for (int i = 0; i < stages.Count; i++)
        {
            var (name, sub, count) = stages[i];
            var prev = i > 0 ? stages[i - 1].Count : 0;
            double? retention = i == 0 || prev == 0 ? null : (double)count / prev * 100;
            list.Add(new MarketingFunnelStageDto
            {
                Step = i + 1,
                Name = name,
                Sub = sub,
                Count = count,
                PercentOfFirst = leads > 0 ? (double)count / leads * 100 : 0,
                Retention = retention,
                DropOff = retention.HasValue ? 100 - retention.Value : null,
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
    // ⚠️ التنبيهات
    // ═══════════════════════════════════════════
    private static List<MarketingAlertDto> BuildAlerts(
        decimal roas, double convRate, double contactRate, decimal cac,
        decimal spend, int leads, int customers)
    {
        var alerts = new List<MarketingAlertDto>();

        if (roas > 0)
        {
            alerts.Add(roas >= 4m
                ? new MarketingAlertDto { Type = "good", Text = "العائد على الإنفاق ممتاز", Detail = $"{roas:0.0}x — أعلى من المستهدف (4x)" }
                : new MarketingAlertDto { Type = "bad", Text = "العائد على الإنفاق منخفض", Detail = $"{roas:0.0}x — أقل من المستهدف (4x)" });
        }

        if (convRate > 0)
        {
            alerts.Add(convRate >= 20
                ? new MarketingAlertDto { Type = "good", Text = "معدل التحويل ممتاز", Detail = $"{convRate:0.0}% من اللييدز بيتحولوا لعملاء مؤهلين" }
                : new MarketingAlertDto { Type = "warn", Text = "معدل التحويل يحتاج تحسين", Detail = $"{convRate:0.0}% (المستهدف 20%+)" });
        }

        if (contactRate > 0)
        {
            alerts.Add(contactRate >= 50
                ? new MarketingAlertDto { Type = "good", Text = "نسبة التواصل ممتازة", Detail = $"{contactRate:0.0}% من اللييدز تم التواصل معهم" }
                : new MarketingAlertDto { Type = "warn", Text = "نسبة التواصل منخفضة", Detail = $"{contactRate:0.0}% (المستهدف 50%+)" });
        }

        var dropOff = leads > 0 ? 100 - (customers > 0 ? (double)customers / leads * 100 : 0) : 0;
        alerts.Add(new MarketingAlertDto
        {
            Type = dropOff <= 90 ? "info" : "warn",
            Text = "التسرب من اللييد للعميل",
            Detail = $"{dropOff:0.0}% من اللييدز لم يتحولوا لعملاء",
        });

        return alerts.Take(5).ToList();
    }

    // ═══════════════════════════════════════════
    // 📈 المؤشر (0..10)
    // ═══════════════════════════════════════════
    private static (double Score, string Label, string Class) BuildScore(
        double convRate, double contactRate, decimal roas)
    {
        var convScore = Math.Min(10, convRate / 25 * 10);
        var contactScore = Math.Min(10, contactRate / 50 * 10);
        var roasScore = Math.Min(10, (double)roas / 4 * 10);

        var score = Math.Round(0.5 * convScore + 0.2 * contactScore + 0.3 * roasScore, 1);

        return score switch
        {
            >= 8 => (score, "ممتاز", "score-excellent"),
            >= 5 => (score, "يحتاج تحسين", "score-needs"),
            _ => (score, "ضعيف", "score-poor"),
        };
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
        if (t.Contains("تيك توك") || t.Contains("tiktok")) return "tiktok";
        if (t.Contains("جوجل") || t.Contains("google")) return "google";
        return "other";
    }

    public static string ChannelOfSource(int? sourceId) => sourceId switch
    {
        2 => "fb",
        5 => "ig",
        7 => "google",
        8 => "tiktok",
        _ => "other",
    };

    public static string ChannelName(string key) => key switch
    {
        "fb" => "فيسبوك",
        "ig" => "انستجرام",
        "google" => "جوجل",
        "tiktok" => "تيك توك",
        "other" => "أخرى",
        _ => "الكل",
    };

    public static string FmtMoney(decimal v) =>
        v >= 1_000_000 ? (v / 1_000_000m).ToString("0.0") + "M" :
        v >= 1_000 ? (v / 1_000m).ToString("0.0") + "K" :
        v.ToString("N0");
}
