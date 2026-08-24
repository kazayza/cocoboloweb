namespace COCOBOLOERPNEW.DTOs;

// ═══════════════════════════════════════════════════════════════
//  Main Dashboard Response DTO
// ═══════════════════════════════════════════════════════════════

public class LeadsDashboardDataDto
{
    public LeadsDashboardKpisDto Kpis { get; set; } = new();
    public List<ChartItemDto> StatusDistribution { get; set; } = new();
    public List<ChartItemDto> PlatformData { get; set; } = new();
    public List<DailyTrendItemDto> DailyTrend { get; set; } = new();
    public List<ChartItemDto> BudgetDistribution { get; set; } = new();
    public List<ChartItemDto> TopCities { get; set; } = new();
    public List<DashboardEmployeeDto> EmployeePerformance { get; set; } = new();
    public List<FunnelItemDto> FunnelData { get; set; } = new();
    public List<SalesByPeriodDto> SalesByPeriod { get; set; } = new();
    public List<ValueComparisonDto> ValueComparison { get; set; } = new();
    public OpportunityClosureMetricsDto OpportunityClosureMetrics { get; set; } = new();
    public List<ChartItemDto> ConvertedLeadOutcomeDistribution { get; set; } = new();
    public List<ChartItemDto> QuotationStatusDistribution { get; set; } = new();
    public List<QuotationStatusSummaryDto> QuotationStatusSummary { get; set; } = new();
    public List<ChartItemDto> QuotationPackageDistribution { get; set; } = new();
    public List<QuotationPackageMetricDto> QuotationPackageMetrics { get; set; } = new();
    public ShowroomVisitMetricsDto ShowroomVisitMetrics { get; set; } = new();
    public List<ChartItemDto> ShowroomVisitOriginDistribution { get; set; } = new();
    public List<ChartItemDto> ExternalCustomerSourceDistribution { get; set; } = new();
    public List<CampaignPerformanceDto> TopCampaigns { get; set; } = new();
    public List<ProjectTypeSummaryDto> ProjectSummary { get; set; } = new();
    public List<RecentConvertedDto> RecentConverted { get; set; } = new();
    public List<ClosedDealDetailDto> ClosedDealsDetails { get; set; } = new();
    public LeadJourneyTreeNodeDto? LeadJourneyTree { get; set; }

    // Filter options (for dropdowns)
    public List<string> AvailableCities { get; set; } = new();
    public List<string> AvailableProjectTypes { get; set; } = new();
    public List<string> AvailableProjectStages { get; set; } = new();
    public List<string> AvailableCampaigns { get; set; } = new();
}

// ═══════════════════════════════════════════════════════════════
//  KPIs
// ═══════════════════════════════════════════════════════════════

public class LeadsDashboardKpisDto
{
    public int TotalLeads { get; set; }
    public decimal ConversionRate { get; set; }
    public double AvgConversionDays { get; set; }
    public int ConvertedCount { get; set; }
    public int RejectedCount { get; set; }
    public int LeadOriginOpportunitiesCount { get; set; }
    public int LeadOriginLostCount { get; set; }
    public int ClosedDealCount { get; set; }
    public decimal ClosedDealValue { get; set; }
    public decimal ClosedDealExpectedValue { get; set; }
    public decimal ValueVariance { get; set; }
    public decimal DuplicateRate { get; set; }
    public decimal RejectionRate { get; set; }

    // Δ Change vs previous period (nullable = no previous data)
    public decimal? TotalLeadsChange { get; set; }
    public decimal? ConversionRateChange { get; set; }
    public double? AvgConversionDaysChange { get; set; }
    public decimal? DuplicateRateChange { get; set; }
    public decimal? RejectionRateChange { get; set; }
}

// ═══════════════════════════════════════════════════════════════
//  Chart Data DTOs
// ═══════════════════════════════════════════════════════════════

public class ChartItemDto
{
    public string Label { get; set; } = "";
    public decimal Value { get; set; }
    public string? Color { get; set; }
    public string? Key { get; set; }

    // ApexCharts renders labels inside SVG. Unicode bidi-isolation keeps Arabic,
    // numbers and mixed Arabic/English labels stable across Safari/WebKit.
    public string ChartLabel => ChartTextForApex(Label);

    private static string ChartTextForApex(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : $"\u2067{value.Trim()}\u2069";
}

public class DailyTrendItemDto
{
    public DateTime Date { get; set; }
    public int Leads { get; set; }
    public int Contacted { get; set; }
    public int Converted { get; set; }
}

// NOTE: Renamed from EmployeePerformanceDto to DashboardEmployeeDto
// to avoid conflict with existing class in the project
public class DashboardEmployeeDto
{
    public string Name { get; set; } = "";
    public int Total { get; set; }
    public int NewCount { get; set; }
    public int ContactedCount { get; set; }
    public int QualifiedCount { get; set; }
    public int ConvertedCount { get; set; }
    public int RejectedCount { get; set; }
    public int ClosedDealCount { get; set; }
    public decimal ClosedDealValue { get; set; }
}

public class FunnelItemDto
{
    public string Stage { get; set; } = "";
    public int Count { get; set; }
    public decimal Percentage { get; set; }
    public string Color { get; set; } = "#6366f1";
}

// ═══════════════════════════════════════════════════════════════
//  Table Data DTOs
// ═══════════════════════════════════════════════════════════════

public class CampaignPerformanceDto
{
    public string CampaignName { get; set; } = "";
    public string Platform { get; set; } = "";
    public int TotalLeads { get; set; }
    public int ConvertedLeads { get; set; }
    public decimal ConversionRate { get; set; }
}

public class ClosedDealDetailDto
{
    public int OpportunityId { get; set; }
    public string ClientName { get; set; } = "";
    public string InvoiceReference { get; set; } = "بدون فاتورة";
    public decimal ActualValue { get; set; }
    public string SourceName { get; set; } = "غير محدد";
}

public class LeadJourneyTreeNodeDto
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string CountLabel { get; set; } = "0";
    public string? SubLabel { get; set; }
    public string BgColor { get; set; } = "#ffffff";
    public string BorderColor { get; set; } = "#cbd5e1";
    public string BorderHoverColor { get; set; } = "#94a3b8";
    public string AccentColor { get; set; } = "#475569";
    public List<LeadJourneyTreeNodeDto> Children { get; set; } = new();
}

public class ProjectTypeSummaryDto
{
    public string ProjectType { get; set; } = "";
    public int TotalLeads { get; set; }
    public int ConvertedLeads { get; set; }
    public decimal ConversionRate { get; set; }
}

public class RecentConvertedDto
{
    public string FullName { get; set; } = "";
    public string CampaignName { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public DateTime ConvertedDate { get; set; }
    public string Budget { get; set; } = "";
}
public class SalesByPeriodDto
{
    public string Period { get; set; } = "";   // "يناير 2026"
    public string ChartPeriod => string.IsNullOrWhiteSpace(Period)
        ? string.Empty
        : $"\u2067{Period.Trim()}\u2069";
    public decimal TotalValue { get; set; }     // إجمالي القيم
    public decimal ExpectedTotalValue { get; set; }  // إجمالي القيمة المتوقعة
    public int DealCount { get; set; }          // عدد الصفقات
}

public class ValueComparisonDto
{
    public string Period { get; set; } = "";       // "يناير 2026"
    public string ChartPeriod => string.IsNullOrWhiteSpace(Period)
        ? string.Empty
        : $"\u2067{Period.Trim()}\u2069";
    public decimal ExpectedValue { get; set; }      // القيمة المتوقعة
    public decimal ActualValue { get; set; }        // القيمة الفعلية
}

public class OpportunityClosureMetricsDto
{
    public int ClosedCount { get; set; }
    public decimal ClosureRate { get; set; }
    public double AvgDaysToClose { get; set; }
    public int? MinDaysToClose { get; set; }
    public int? MaxDaysToClose { get; set; }
}

public class QuotationStatusSummaryDto
{
    public string StatusKey { get; set; } = "";
    public string StatusName { get; set; } = "";
    public int Count { get; set; }
    public decimal Percent { get; set; }
    public string Color { get; set; } = "#6366f1";
}

public class QuotationPackageMetricDto
{
    public string PackageKey { get; set; } = "";
    public string PackageName { get; set; } = "";
    public int Count { get; set; }
    public decimal TotalValue { get; set; }
    public int RejectedCount { get; set; }
    public string Color { get; set; } = "#6366f1";
}

public class ShowroomVisitMetricsDto
{
    public int TotalVisits { get; set; }
    public int UniqueVisitors { get; set; }
    public int LeadOriginVisits { get; set; }
    public int DirectVisits { get; set; }
    public int RepeatVisitors { get; set; }
}

public class ShowroomVisitDetailDto
{
    public int InteractionId { get; set; }
    public int? LeadId { get; set; }
    public int OpportunityId { get; set; }
    public int PartyId { get; set; }
    public string CustomerName { get; set; } = "";
    public string Phone { get; set; } = "";
    public DateTime VisitDate { get; set; }
    public string? EmployeeName { get; set; }
    public string? CampaignName { get; set; }
    public string? Platform { get; set; }
    public string? OpportunityStage { get; set; }
    public string OriginKey { get; set; } = "";
    public string OriginName { get; set; } = "";
}

// ═══════════════════════════════════════════════════════════════
//  Dashboard Filter DTO
// ═══════════════════════════════════════════════════════════════

public class LeadsDashboardFilterDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? Platform { get; set; }
    public int? EmployeeId { get; set; }
    public string? City { get; set; }
    public string? ProjectType { get; set; }
    public string? ProjectStage { get; set; }
    public string? CampaignName { get; set; }

    // Helper: compute previous period filter
    public LeadsDashboardFilterDto GetPreviousPeriod()
    {
        if (DateFrom == null || DateTo == null)
            return new LeadsDashboardFilterDto
            {
                DateFrom = DateTime.Today.AddMonths(-1),
                DateTo = DateTime.Today.AddDays(-1),
                Platform = Platform,
                EmployeeId = EmployeeId,
                City = City,
                ProjectType = ProjectType,
                ProjectStage = ProjectStage,
                CampaignName = CampaignName
            };

        var duration = DateTo.Value - DateFrom.Value;
        return new LeadsDashboardFilterDto
        {
            DateFrom = DateFrom.Value.AddDays(-duration.Days),
            DateTo = DateFrom.Value.AddDays(-1),
            Platform = Platform,
            EmployeeId = EmployeeId,
            City = City,
            ProjectType = ProjectType,
            ProjectStage = ProjectStage,
            CampaignName = CampaignName
        };
    }
    
}
