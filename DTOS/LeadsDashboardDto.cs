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
    public List<CampaignPerformanceDto> TopCampaigns { get; set; } = new();
    public List<ProjectTypeSummaryDto> ProjectSummary { get; set; } = new();
    public List<RecentConvertedDto> RecentConverted { get; set; } = new();

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
    public int ClosedDealCount { get; set; }       // ← أضف ده
    public decimal ClosedDealValue { get; set; }
    public decimal ClosedDealExpectedValue { get; set; }
    public decimal ValueVariance { get; set; }      // الفرق (المتوقع - الفعلي)
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
    public decimal TotalValue { get; set; }     // إجمالي القيم
    public decimal ExpectedTotalValue { get; set; }  // إجمالي القيمة المتوقعة
    public int DealCount { get; set; }          // عدد الصفقات
}

public class ValueComparisonDto
{
    public string Period { get; set; } = "";       // "يناير 2026"
    public decimal ExpectedValue { get; set; }      // القيمة المتوقعة
    public decimal ActualValue { get; set; }        // القيمة الفعلية
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
