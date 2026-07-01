using COCOBOLOERPNEW.Models;

namespace COCOBOLOERPNEW.DTOs;

// ═══════════════════════════════════════════════════════════════
// Opportunity Details 
// ═══════════════════════════════════════════════════════════════
public class OpportunityDetailDto
{
    public OpportunityListDto Opportunity { get; set; } = new();
    public List<InteractionListDto> Interactions { get; set; } = new();
    public List<TaskListDto> Tasks { get; set; } = new();
    public List<SalesStage> AvailableStages { get; set; } = new();
    public int CurrentStageId { get; set; }
}

public class InteractionListDto
{
    public int InteractionId { get; set; }
    public int OpportunityId { get; set; }
    public int PartyId { get; set; }
    public string ClientName { get; set; } = "";
    public string? Phone { get; set; }
    public string? AllPhones { get; set; }
    public int? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public int? SourceId { get; set; }
    public string? SourceName { get; set; }
    public string? SourceIcon { get; set; }
    public int? StatusId { get; set; }
    public string? StatusName { get; set; }
    public string? StatusNameAr { get; set; }
    public DateTime InteractionDate { get; set; }
    public TimeOnly? InteractionTime { get; set; }
    public string? Summary { get; set; }
    public int? StageBeforeId { get; set; }
    public string? StageBeforeName { get; set; }
    public string? StageBeforeNameAr { get; set; }
    public string? StageBeforeColor { get; set; }
    public int? StageAfterId { get; set; }
    public string? StageAfterName { get; set; }
    public string? StageAfterNameAr { get; set; }
    public string? StageAfterColor { get; set; }
    public int? AdTypeId { get; set; }
    public string? AdTypeName { get; set; }
    public string? AdTypeNameAr { get; set; }
    public DateTime? NextFollowUpDate { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TaskListDto
{
    public int TaskId { get; set; }
    public int? OpportunityId { get; set; }
    public int? PartyId { get; set; }
    public int? LeadId { get; set; }
    public bool IsLeadTask { get; set; }
    public string? ClientName { get; set; }
    public string? Phone { get; set; }
    public string? CampaignName { get; set; }
    public string? Platform { get; set; }
    public int AssignedTo { get; set; }
    public string? AssignedToName { get; set; }
    public int? TaskTypeId { get; set; }
    public string? TaskTypeName { get; set; }
    public string? TaskTypeNameAr { get; set; }
    public string? TaskDescription { get; set; }
    public DateTime DueDate { get; set; }
    public TimeOnly? DueTime { get; set; }
    public string Priority { get; set; } = "Medium";
    public string Status { get; set; } = "Pending";
    public DateTime? CompletedDate { get; set; }
    public string? CompletedBy { get; set; }
    public string? CompletionNotes { get; set; }
    public bool ReminderEnabled { get; set; }
    public int? ReminderMinutes { get; set; }
    public bool IsActive { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string TaskDueStatus { get; set; } = "";
    public int? DaysUntilDue { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// Quick Add Interaction (inline from Opportunity Details)
// ═══════════════════════════════════════════════════════════════
public class QuickInteractionDto
{
    public int OpportunityId { get; set; }
    public int PartyId { get; set; }
    public string? Summary { get; set; }
    public int? StageAfterId { get; set; }
    public int? StageBeforeId { get; set; }      // ← جديد
    public int? StatusId { get; set; }            // ← جديد
    public int? SourceId { get; set; }            // ← جديد
    public int? EmployeeId { get; set; }          // ← جديد
    public int? LostReasonId { get; set; }        // ← جديد
    public string? LostNotes { get; set; }        // ← جديد
    public DateTime? NextFollowUpDate { get; set; }
    public string? Notes { get; set; }
    public int? TaskTypeId { get; set; }
    public string? Priority { get; set; } = "Medium";
}

// ═══════════════════════════════════════════════════════════════
// Quick Task
// ═══════════════════════════════════════════════════════════════
public class QuickTaskDto
{
     public int? OpportunityId { get; set; }
    public int? PartyId { get; set; }
    public int AssignedTo { get; set; }
    public int? TaskTypeId { get; set; }          // ← جديد
    public string? TaskDescription { get; set; }
    public DateTime DueDate { get; set; } = DateTime.Today.AddDays(1);
    public string Priority { get; set; } = "Medium";
}

// ═══════════════════════════════════════════════════════════════
// Interaction Filter
// ═══════════════════════════════════════════════════════════════
public class InteractionFilterDto
{
    public string? SearchText { get; set; }
    public int? OpportunityId { get; set; }
    public int? EmployeeId { get; set; }
    public int? SourceId { get; set; }
    public int? AdTypeId { get; set; }
    public int? StatusId { get; set; }
    public int? StageBeforeId { get; set; }
    public int? StageAfterId { get; set; }
    public int? PartyId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string SortBy { get; set; } = "InteractionDate";
    public bool SortDescending { get; set; } = true;
}

// ═══════════════════════════════════════════════════════════════
// Task Filter
// ═══════════════════════════════════════════════════════════════
public class TaskFilterDto
{
    public string? SearchText { get; set; }
    public int? OpportunityId { get; set; }
    public int? AssignedTo { get; set; }
    public int? TaskTypeId { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public bool? IsOverdue { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 5000;
    public string SortBy { get; set; } = "DueDate";
    public bool SortDescending { get; set; } = false;
}