namespace COCOBOLOERPNEW.DTOs;

// ═══════════════════════════════════════════════════════════════
// CRM Dashboard DTOs
// ═══════════════════════════════════════════════════════════════

public class CrmDashboardDto
{
    // KPI Cards
    public int TotalOpportunities { get; set; }
    public int OpenOpportunities { get; set; }
    public int WonOpportunities { get; set; }
    public int LostOpportunities { get; set; }
    public decimal PipelineValue { get; set; }
    public decimal WonValue { get; set; }
    public decimal ConversionRate { get; set; }

    // Tasks
    public int OverdueTasks { get; set; }
    public int TodayTasks { get; set; }
    public int UpcomingTasks { get; set; }

    // Follow-ups
    public int FollowUpsToday { get; set; }
    public int FollowUpsOverdue { get; set; }

    // Charts
    public List<StageDistributionDto> OpportunitiesByStage { get; set; } = new();
    public List<CrmMonthlyTrendDto> MonthlyTrend { get; set; } = new();
    public List<EmployeePerformanceDto> TopPerformers { get; set; } = new();

    // Recent Activity
    public List<RecentActivityDto> RecentActivities { get; set; } = new();
    public List<UpcomingFollowUpDto> UpcomingFollowUps { get; set; } = new();
    public List<OverdueTaskDto> OverdueTasksList { get; set; } = new();
}

// ═══════════════════════════════════════════════════════════════
// Stage Distribution (Donut Chart)
// ═══════════════════════════════════════════════════════════════
public class StageDistributionDto
{
    public int StageId { get; set; }
    public string StageName { get; set; } = "";
    public string StageNameAr { get; set; } = "";
    public string StageColor { get; set; } = "#94a3b8";
    public int StageOrder { get; set; }
    public int Count { get; set; }
    public decimal Value { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// Monthly Trend (Bar Chart) — CRM-specific, different from IncomeStatement one
// ═══════════════════════════════════════════════════════════════
public class CrmMonthlyTrendDto
{
    public string Month { get; set; } = "";
    public string MonthAr { get; set; } = "";
    public int NewOpportunities { get; set; }
    public int WonOpportunities { get; set; }
    public int LostOpportunities { get; set; }
    public decimal ConversionRate { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// Employee Performance (Leaderboard)
// ═══════════════════════════════════════════════════════════════
public class EmployeePerformanceDto
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public int TotalOpportunities { get; set; }
    public int OpenOpportunities { get; set; }
    public int WonCount { get; set; }
    public decimal WonValue { get; set; }
    public decimal ConversionRate { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// Recent Activity (Timeline)
// ═══════════════════════════════════════════════════════════════
public class RecentActivityDto
{
    public string ActivityType { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTime ActivityDate { get; set; }
    public int? RelatedId { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public string? Elapsed { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// Upcoming Follow-up (Action List)
// ═══════════════════════════════════════════════════════════════
public class UpcomingFollowUpDto
{
    public int OpportunityId { get; set; }
    public int PartyId { get; set; }
    public string ClientName { get; set; } = "";
    public string? Phone { get; set; }
    public DateTime FollowUpDate { get; set; }
    public string? StageNameAr { get; set; }
    public string? StageColor { get; set; }
    public string? EmployeeName { get; set; }
    public bool IsOverdue { get; set; }
    public string? LastSummary { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// Overdue Task (Action List)
// ═══════════════════════════════════════════════════════════════
public class OverdueTaskDto
{
    public int TaskId { get; set; }
    public string TaskDescription { get; set; } = "";
    public string? ClientName { get; set; }
    public string? AssignedToName { get; set; }
    public DateTime DueDate { get; set; }
    public string Priority { get; set; } = "Medium";
    public int DaysOverdue { get; set; }
}


// ═══════════════════════════════════════════════════════════════
// Dashboard Filter
// ═══════════════════════════════════════════════════════════════
public class CrmDashboardFilterDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string Period { get; set; } = "all";

    public static CrmDashboardFilterDto FromPeriod(string period) => period switch
    {
        "7d" => new() { DateFrom = DateTime.Today.AddDays(-7), DateTo = DateTime.Today, Period = "7d" },
        "30d" => new() { DateFrom = DateTime.Today.AddDays(-30), DateTo = DateTime.Today, Period = "30d" },
        "3m" => new() { DateFrom = DateTime.Today.AddMonths(-3), DateTo = DateTime.Today, Period = "3m" },
        "6m" => new() { DateFrom = DateTime.Today.AddMonths(-6), DateTo = DateTime.Today, Period = "6m" },
        "1y" => new() { DateFrom = DateTime.Today.AddYears(-1), DateTo = DateTime.Today, Period = "1y" },
        _ => new() { Period = "all" }
    };

    public static readonly Dictionary<string, string> PeriodLabels = new()
    {
        { "7d", "آخر 7 أيام" }, { "30d", "آخر 30 يوم" }, { "3m", "آخر 3 شهور" },
        { "6m", "آخر 6 شهور" }, { "1y", "آخر سنة" }, { "all", "كل الفترات" }
    };
}