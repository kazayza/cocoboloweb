using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// CRM Dashboard Service – v5 (FINAL)
/// فلتر تاريخ على كل الدوال + Lost chart + ConversionRate
/// </summary>
public class CrmDashboardService : ICrmDashboardService
{
    private readonly db24804Context _db;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<CrmDashboardService> _logger;

    private static readonly HashSet<string> WonKeywords  = new() { "تم البيع", "بيع", "Closed Deal" };
    private static readonly HashSet<string> LostKeywords = new() { "خسارة", "Lost", "غير مهتم", "Not Interested" };

    public CrmDashboardService(db24804Context db, IHttpContextAccessor http, ILogger<CrmDashboardService> logger) => (_db, _http, _logger) = (db, http, logger);

    public async Task<CrmDashboardDto> GetDashboardAsync(
        string currentUserName, string? role, CrmDashboardFilterDto filter)
    {
        var dto = new CrmDashboardDto();
        try
        {
            var crmAccess = _http.GetCrmAccessFrom();
            var dateFrom = filter.DateFrom;
            var dateTo   = filter.DateTo;
            var hasRange = dateFrom.HasValue && dateTo.HasValue;

            var stages = await _db.SalesStages.AsNoTracking()
                .Where(s => s.IsActive).ToListAsync();

            var wonIds  = stages.Where(IsWon).Select(s => s.StageId).ToHashSet();
            var lostIds = stages.Where(IsLost).Select(s => s.StageId).ToHashSet();
            lostIds.ExceptWith(wonIds);

            // ⭐ استعلامات متتالية — آمنة
            var (total, open, won, lost, pipe, wonV) = await LoadKpis(wonIds, lostIds, hasRange, dateFrom, dateTo, crmAccess);
            dto.TotalOpportunities = total;
            dto.OpenOpportunities  = open;
            dto.WonOpportunities   = won;
            dto.LostOpportunities  = lost;
            dto.PipelineValue      = pipe;
            dto.WonValue           = wonV;
            dto.ConversionRate     = total > 0 ? Math.Round((decimal)won / total * 100, 1) : 0;

            var (overdue, todayT, upcoming, fuToday, fuOver) = await LoadTaskStats();
            dto.OverdueTasks     = overdue;
            dto.TodayTasks       = todayT;
            dto.UpcomingTasks    = upcoming;
            dto.FollowUpsToday   = fuToday;
            dto.FollowUpsOverdue = fuOver;

            dto.OpportunitiesByStage = await LoadStageDistribution(stages, hasRange, dateFrom, dateTo, crmAccess);
            dto.MonthlyTrend         = await LoadMonthlyTrend(wonIds, lostIds, hasRange, dateFrom, dateTo, crmAccess);
            dto.TopPerformers        = await LoadTopPerformers(wonIds, lostIds, hasRange, dateFrom, dateTo, crmAccess);
            dto.RecentActivities     = await LoadRecentActivities(hasRange, dateFrom, dateTo, crmAccess);
            dto.UpcomingFollowUps    = await LoadUpcomingFollowUps();
            dto.OverdueTasksList     = await LoadOverdueTasks();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CRM Dashboard load failed: {Msg}", ex.Message);
        }
        return dto;
    }

    // ════════════════════ HELPERS ════════════════════
    private static bool IsWon(SalesStage s) =>
        WonKeywords.Any(k => (s.StageNameAr ?? "").Contains(k) || (s.StageName ?? "").Contains(k));

    private static bool IsLost(SalesStage s) =>
        LostKeywords.Any(k => (s.StageNameAr ?? "").Contains(k) || (s.StageName ?? "").Contains(k));

    private static IQueryable<SalesOpportunity> ApplyDateFilter(
        IQueryable<SalesOpportunity> q, bool hasRange, DateTime? from, DateTime? to)
        => hasRange ? q.Where(o => o.CreatedAt >= from && o.CreatedAt <= to) : q;

    // ════════════════════ KPI ════════════════════
    private async Task<(int, int, int, int, decimal, decimal)>
        LoadKpis(HashSet<int> wonIds, HashSet<int> lostIds, bool hasRange, DateTime? from, DateTime? to, DateTime? crmAccess)
    {
        var q = _db.SalesOpportunities.AsNoTracking();
        q = ApplyDateFilter(q, hasRange, from, to);
        if (crmAccess.HasValue) q = q.Where(o => o.CreatedAt >= crmAccess.Value);

        var opps = await q.Select(o => new { o.OpportunityId, o.StageId, o.ExpectedValue }).ToListAsync();
        if (!opps.Any()) return (0, 0, 0, 0, 0, 0);

        var total = opps.Count;
        var won   = opps.Count(o => wonIds.Contains(o.StageId));
        var lost  = opps.Count(o => lostIds.Contains(o.StageId));
        var open  = total - won - lost;
        var pipe  = opps.Where(o => !wonIds.Contains(o.StageId) && !lostIds.Contains(o.StageId)).Sum(o => o.ExpectedValue ?? 0);
        var wonV  = opps.Where(o => wonIds.Contains(o.StageId)).Sum(o => o.ExpectedValue ?? 0);
        return (total, open, won, lost, pipe, wonV);
    }

    // ════════════════════ STAGE DISTRIBUTION ════════════════════
    private async Task<List<StageDistributionDto>> LoadStageDistribution(
        List<SalesStage> stages, bool hasRange, DateTime? from, DateTime? to, DateTime? crmAccess)
    {
        var q = _db.SalesOpportunities.AsNoTracking().Where(o => o.IsActive);
        q = ApplyDateFilter(q, hasRange, from, to);
        if (crmAccess.HasValue) q = q.Where(o => o.CreatedAt >= crmAccess.Value);

        var counts = await q
            .GroupBy(o => o.StageId)
            .Select(g => new { StageId = g.Key, Count = g.Count(), Value = g.Sum(o => (decimal?)(o.ExpectedValue ?? 0)) ?? 0 })
            .ToListAsync();

        var cd = counts.ToDictionary(c => c.StageId, c => c.Count);
        var vd = counts.ToDictionary(c => c.StageId, c => c.Value);

        return stages.Select(s => new StageDistributionDto
        {
            StageId     = s.StageId,
            StageName   = s.StageName ?? "",
            StageNameAr = s.StageNameAr ?? s.StageName ?? "",
            StageColor  = string.IsNullOrWhiteSpace(s.StageColor) ? "#94a3b8" : s.StageColor,
            StageOrder  = s.StageOrder,
            Count       = cd.TryGetValue(s.StageId, out var c) ? c : 0,
            Value       = vd.TryGetValue(s.StageId, out var v) ? v : 0
        }).ToList();
    }

    // ════════════════════ MONTHLY TREND ════════════════════
    private async Task<List<CrmMonthlyTrendDto>>
        LoadMonthlyTrend(HashSet<int> wonIds, HashSet<int> lostIds, bool hasRange, DateTime? from, DateTime? to, DateTime? crmAccess)
    {
        var today  = DateTime.Today;

        // نحدد عدد الشهور حسب الفلتر — لو كل الفترات = 6 شهور
        var monthsBack = 6;
        if (hasRange && from.HasValue)
            monthsBack = Math.Max(1, ((today.Year - from.Value.Year) * 12) + today.Month - from.Value.Month + 1);

        var months = Enumerable.Range(0, monthsBack)
            .Select(i => today.AddMonths(-i))
            .Select(d => new { d.Year, d.Month }).Reverse().ToList();

        var q = _db.SalesOpportunities.AsNoTracking()
            .Where(o => o.CreatedAt.Year >= months.First().Year);
        q = ApplyDateFilter(q, hasRange, from, to);
        if (crmAccess.HasValue) q = q.Where(o => o.CreatedAt >= crmAccess.Value);

        var opps = await q.Select(o => new { o.CreatedAt.Year, o.CreatedAt.Month, o.StageId }).ToListAsync();

        var ar = new[] { "", "يناير", "فبراير", "مارس", "إبريل", "مايو", "يونيو",
            "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };

        return months.Select(m =>
        {
            var sub = opps.Where(o => o.Year == m.Year && o.Month == m.Month).ToList();
            var newCount = sub.Count;
            var wonCount = sub.Count(o => wonIds.Contains(o.StageId));
            var lostCnt  = sub.Count(o => lostIds.Contains(o.StageId));
            return new CrmMonthlyTrendDto
            {
                Month = $"{m.Year}-{m.Month:D2}", MonthAr = ar[m.Month],
                NewOpportunities = newCount,
                WonOpportunities  = wonCount,
                LostOpportunities = lostCnt,
                ConversionRate    = newCount > 0 ? Math.Round((decimal)wonCount / newCount * 100, 1) : 0
            };
        }).ToList();
    }

    // ════════════════════ TOP PERFORMERS ════════════════════
    private async Task<List<EmployeePerformanceDto>>
        LoadTopPerformers(HashSet<int> wonIds, HashSet<int> lostIds, bool hasRange, DateTime? from, DateTime? to, DateTime? crmAccess)
    {
        var q = _db.SalesOpportunities.AsNoTracking()
            .Where(o => o.EmployeeId != null && o.IsActive);
        q = ApplyDateFilter(q, hasRange, from, to);
        if (crmAccess.HasValue) q = q.Where(o => o.CreatedAt >= crmAccess.Value);

        var opps = await q.Select(o => new { o.EmployeeId, o.StageId, Expected = (decimal?)(o.ExpectedValue ?? 0) }).ToListAsync();
        if (!opps.Any()) return new();

        var empIds = opps.Select(o => o.EmployeeId!.Value).Distinct().ToList();
        var emps = await _db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.EmployeeId)).Select(e => new { e.EmployeeId, e.FullName }).ToListAsync();
        var empDict = emps.ToDictionary(e => e.EmployeeId, e => e.FullName ?? "—");

        return opps.GroupBy(o => o.EmployeeId!.Value).Select(g => new EmployeePerformanceDto
        {
            EmployeeId        = g.Key,
            EmployeeName      = empDict.TryGetValue(g.Key, out var n) ? n : "—",
            TotalOpportunities = g.Count(),
            OpenOpportunities  = g.Count(o => !wonIds.Contains(o.StageId) && !lostIds.Contains(o.StageId)),
            WonCount          = g.Count(o => wonIds.Contains(o.StageId)),
            WonValue          = g.Where(o => wonIds.Contains(o.StageId)).Sum(o => o.Expected) ?? 0,
            ConversionRate    = g.Count() > 0 ? Math.Round((decimal)g.Count(o => wonIds.Contains(o.StageId)) / g.Count() * 100, 1) : 0
        }).OrderByDescending(e => e.WonCount).ThenByDescending(e => e.WonValue).Take(6).ToList();
    }

    // ════════════════════ RECENT ACTIVITIES ════════════════════
    private async Task<List<RecentActivityDto>> LoadRecentActivities(
        bool hasRange, DateTime? from, DateTime? to, DateTime? crmAccess)
    {
        var list = new List<RecentActivityDto>();
        var cutoff = hasRange && from.HasValue ? from.Value : DateTime.Today.AddDays(-30);
        if (crmAccess.HasValue && crmAccess.Value > cutoff) cutoff = crmAccess.Value;

        var q = _db.CustomerInteractions.AsNoTracking()
            .Where(i => i.InteractionDate >= cutoff);
        if (hasRange && to.HasValue)
            q = q.Where(i => i.InteractionDate <= to.Value);

        var interactions = await q.OrderByDescending(i => i.InteractionDate)
            .ThenByDescending(i => i.InteractionId).Take(15)
            .Select(i => new { Id = (int?)i.InteractionId, Date = (DateTime?)i.InteractionDate,
                Sum = i.Summary, Pid = (int?)i.PartyId })
            .ToListAsync();

        var pids = interactions.Where(x => x.Pid.HasValue).Select(x => x.Pid!.Value).Distinct().ToList();
        var parties = pids.Any()
            ? (await _db.Parties.AsNoTracking().Where(p => pids.Contains(p.PartyId))
                .Select(p => new { p.PartyId, p.PartyName }).ToListAsync())
                .ToDictionary(p => p.PartyId, p => p.PartyName) : new();

        foreach (var i in interactions)
        {
            parties.TryGetValue(i.Pid ?? 0, out var pn);
            list.Add(new RecentActivityDto
            {
                ActivityType = "Interaction", Title = "تفاعل مع عميل",
                Description = $"{(pn ?? "—")}: {Trunc(i.Sum, 60)}",
                ActivityDate = i.Date ?? DateTime.Now, RelatedId = i.Id,
                Icon = "Chat", Color = "#3b82f6", Elapsed = Elapsed(i.Date)
            });
        }
        return list.OrderByDescending(a => a.ActivityDate).Take(20).ToList();
    }

    // ════════════════════ UPCOMING FOLLOW-UPS ════════════════════
    private async Task<List<UpcomingFollowUpDto>> LoadUpcomingFollowUps()
    {
        var today = DateTime.Today; var nextWeek = today.AddDays(7);

        var opps = await _db.VwSalesOpportunities.AsNoTracking()
            .Where(o => o.NextFollowUpDate.HasValue && o.NextFollowUpDate.Value <= nextWeek && o.IsActive)
            .OrderBy(o => o.NextFollowUpDate).Take(20)
            .Select(o => new { o.OpportunityId, o.PartyId, Client = o.ClientName, Phone = o.Phone1,
                FollowUp = o.NextFollowUpDate, StageAr = o.StageNameAr,
                StageCol = o.StageColor, EmpName = o.EmployeeName })
            .ToListAsync();

        return opps.Select(o => new UpcomingFollowUpDto
        {
            OpportunityId = o.OpportunityId, PartyId = o.PartyId,
            ClientName  = o.Client ?? "—", Phone = o.Phone,
            FollowUpDate = o.FollowUp ?? today,
            StageNameAr  = o.StageAr,
            StageColor   = string.IsNullOrWhiteSpace(o.StageCol) ? "#94a3b8" : o.StageCol,
            EmployeeName = o.EmpName,
            IsOverdue    = (o.FollowUp ?? today) < today
        }).ToList();
    }

    // ════════════════════ OVERDUE TASKS ════════════════════
    private async Task<List<OverdueTaskDto>> LoadOverdueTasks()
    {
        var today = DateTime.Today;
        var tasks = await _db.VwCrmTasks.AsNoTracking()
            .Where(t => t.DueDate < today && t.Status != "Completed" && t.IsActive)
            .OrderBy(t => t.DueDate).Take(10)
            .Select(t => new { t.TaskId, Desc = t.TaskDescription, Client = t.ClientName,
                Assigned = t.AssignedToName, t.DueDate, t.Priority })
            .ToListAsync();

        return tasks.Select(t => new OverdueTaskDto
        {
            TaskId = t.TaskId, TaskDescription = t.Desc ?? "—",
            ClientName = t.Client, AssignedToName = t.Assigned,
            DueDate = t.DueDate, Priority = t.Priority ?? "Medium",
            DaysOverdue = (int)(today - t.DueDate).TotalDays
        }).ToList();
    }

    // ════════════════════ TASK STATS ════════════════════
    private async Task<(int Overdue, int Today, int Upcoming, int FollowUpsToday, int FollowUpsOverdue)>
        LoadTaskStats()
    {
        var today = DateTime.Today; var tomorrow = today.AddDays(1);
        var overdue  = await _db.CrmTasks.AsNoTracking().CountAsync(t => t.DueDate < today && t.Status != "Completed" && t.IsActive);
        var todayT   = await _db.CrmTasks.AsNoTracking().CountAsync(t => t.DueDate >= today && t.DueDate < tomorrow && t.Status != "Completed" && t.IsActive);
        var upcoming = await _db.CrmTasks.AsNoTracking().CountAsync(t => t.DueDate >= tomorrow && t.Status != "Completed" && t.IsActive);
        var fuToday  = await _db.SalesOpportunities.AsNoTracking().CountAsync(o => o.NextFollowUpDate.HasValue && o.NextFollowUpDate.Value >= today && o.NextFollowUpDate.Value < tomorrow && o.IsActive);
        var fuOver   = await _db.SalesOpportunities.AsNoTracking().CountAsync(o => o.NextFollowUpDate.HasValue && o.NextFollowUpDate.Value < today && o.IsActive);
        return (overdue, todayT, upcoming, fuToday, fuOver);
    }

    // ════════════════════ HELPERS ════════════════════
    private static string Trunc(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "—";
        return s.Length <= max ? s : s[..max] + "...";
    }

    private static string? Elapsed(DateTime? dt)
    {
        if (dt == null) return null;
        var span = DateTime.Now - dt.Value;
        if (span.TotalMinutes < 1) return "الآن";
        if (span.TotalMinutes < 60) return $"منذ {(int)span.TotalMinutes} د";
        if (span.TotalHours < 24) return $"منذ {(int)span.TotalHours} س";
        if (span.TotalDays < 30) return $"منذ {(int)span.TotalDays} يوم";
        return dt.Value.ToString("dd/MM/yyyy");
    }
}