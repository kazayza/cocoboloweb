namespace COCOBOLOERPNEW.DTOs;

// ═══════════════════════════════════════════════════════════════
// قائمة الفرص البيعية
// ═══════════════════════════════════════════════════════════════
public class OpportunityListDto
{
    public int OpportunityId { get; set; }
    public int PartyId { get; set; }
    public string ClientName { get; set; } = "";
    public string? Phone { get; set; }
    public string? Phone2 { get; set; }
    public string? Address { get; set; }

    public int? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }

    public int? SourceId { get; set; }
    public string? SourceName { get; set; }
    public string? SourceIcon { get; set; }

    public int StageId { get; set; }
    public string StageName { get; set; } = "";
    public string? StageNameAr { get; set; }
    public string? StageColor { get; set; }
    public int StageOrder { get; set; }

    public int? StatusId { get; set; }
    public string? StatusName { get; set; }

    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }

    public string? InterestedProduct { get; set; }
    public decimal? ExpectedValue { get; set; }
    public string? Location { get; set; }

    public DateTime FirstContactDate { get; set; }
    public DateTime? NextFollowUpDate { get; set; }
    public DateTime? LastContactDate { get; set; }

    public int? LostReasonId { get; set; }
    public string? LostReasonName { get; set; }
    public string? LostNotes { get; set; }

    public string? Notes { get; set; }
    public string? Guidance { get; set; }

    public int? QuotationId { get; set; }
    public int? TransactionId { get; set; }

    public bool IsActive { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    // Computed
    public int DaysSinceFirstContact { get; set; }
    public string FollowUpStatus { get; set; } = ""; // Overdue, Today, Upcoming, None
    public int InteractionsCount { get; set; }
    public int TasksCount { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// فلتر الفرص
// ═══════════════════════════════════════════════════════════════
public class OpportunityFilterDto
{
    public string? SearchText { get; set; }
    public int? StageId { get; set; }
    public int? EmployeeId { get; set; }
    public int? SourceId { get; set; }
    public int? CategoryId { get; set; }
    public bool? IsActive { get; set; } = true;
    public bool? HasFollowUp { get; set; }
    public bool? IsOverdueFollowUp { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string SortBy { get; set; } = "CreatedAt";
    public bool SortDescending { get; set; } = true;
}

// ═══════════════════════════════════════════════════════════════
// إحصائيات سريعة
// ═══════════════════════════════════════════════════════════════
public class OpportunityStatsDto
{
    public int TotalCount { get; set; }
    public int OpenCount { get; set; }
    public int WonCount { get; set; }
    public int LostCount { get; set; }
    public decimal PipelineValue { get; set; }
    public int OverdueFollowUpCount { get; set; }
    public int TodayFollowUpCount { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// DTO للحفظ
// ═══════════════════════════════════════════════════════════════
public class OpportunityFormDto
{
    public int OpportunityId { get; set; }
    public int PartyId { get; set; }
    public string? PartyName { get; set; }
    public int? EmployeeId { get; set; }
    public int? SourceId { get; set; }
    public int? AdTypeId { get; set; }
    public int StageId { get; set; } = 1;
    public string? StageName { get; set; }
    public string? StageNameAr { get; set; }
    public string? StageColor { get; set; }
    public int? StatusId { get; set; }
    public int? CategoryId { get; set; }
    public string? InterestedProduct { get; set; }
    public decimal? ExpectedValue { get; set; }
    public string? Location { get; set; }
    public DateTime FirstContactDate { get; set; } = DateTime.Now;
    public DateTime? NextFollowUpDate { get; set; }
    public int? LostReasonId { get; set; }
    public string? LostNotes { get; set; }
    public string? Notes { get; set; }
    public string? Guidance { get; set; }
    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
