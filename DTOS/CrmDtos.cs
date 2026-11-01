using System;
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
    public DateTime? StartedAt { get; set; }
    public string? StartedBy { get; set; }
    public string? StartNotes { get; set; }
    public DateTime? CompletedDate { get; set; }
    public string? CompletedBy { get; set; }
    public string? CompletionNotes { get; set; }
    public bool ReminderEnabled { get; set; }
    public int? ReminderMinutes { get; set; }
    public bool IsActive { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? AssignmentSource { get; set; }
    public string? TaskScope { get; set; }
    // ─── تطوير التكليفات العامة ───
    public DateTime? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public string? ReturnedBy { get; set; }
    public string? ReturnReason { get; set; }
    public string? CompletionLink { get; set; }
    public string? LinkedEntityType { get; set; }
    public int? LinkedEntityId { get; set; }
    public string? LinkedEntityTitle { get; set; }
    public bool IsGeneralTask => string.Equals(TaskScope, TaskScopes.General, StringComparison.OrdinalIgnoreCase);
    public bool IsGeneralManagerAssignment => string.Equals(AssignmentSource, "GeneralManager", StringComparison.OrdinalIgnoreCase);
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
public static class TaskScopes
{
    public const string Opportunity = "Opportunity";
    public const string Lead = "Lead";
    public const string General = "General";
}

public class QuickTaskDto
{
     public int? OpportunityId { get; set; }
    public int? PartyId { get; set; }
    public int AssignedTo { get; set; }
    public int? TaskTypeId { get; set; }
    public string? TaskDescription { get; set; }
    public DateTime DueDate { get; set; } = DateTime.Today.AddDays(1);
    public TimeOnly? DueTime { get; set; }
    public string Priority { get; set; } = "Medium";
    public string? AssignmentSource { get; set; }
    public string? TaskScope { get; set; }
}

public class GeneralManagerOpportunityTaskDto
{
    public int OpportunityId { get; set; }
    public int PartyId { get; set; }
    public int AssignedTo { get; set; }
    public int? TaskTypeId { get; set; }
    public string? TaskDescription { get; set; }
    public DateTime DueDate { get; set; } = DateTime.Today.AddDays(1);
    public TimeOnly? DueTime { get; set; }
    public string Priority { get; set; } = "Medium";
}

public class GeneralEmployeeTaskDto
{
    public int AssignedTo { get; set; }
    public int? TaskTypeId { get; set; }
    public string? TaskDescription { get; set; }
    public DateTime DueDate { get; set; } = DateTime.Today.AddDays(1);
    public TimeOnly? DueTime { get; set; }
    public string Priority { get; set; } = "Medium";
    /// <summary>ربط اختياري بمستند (مفردات GeneralTaskLinks) — العنوان يُحسم خادميًا وقت الإنشاء</summary>
    public string? LinkedEntityType { get; set; }
    public int? LinkedEntityId { get; set; }
    /// <summary>دقائق التذكير قبل الموعد (اختياري — الافتراضي من إعداد العمود)</summary>
    public int? ReminderMinutes { get; set; }
}

/// <summary>نتيجة تحقق ربط مستند: العنوان الرسمي للمستند كما سيُحفظ لقطة على التكليف</summary>
public class LinkedEntityLookupDto
{
    public string EntityType { get; set; } = "";
    public int EntityId { get; set; }
    public string Title { get; set; } = "";
}

/// <summary>قوائم صفحة «مهامي»: وارد (مُسندة إليّ بكل النطاقات) / صادر (أنشأتها) / إشاراتي</summary>
public class MyTasksDto
{
    public List<TaskListDto> Assigned { get; set; } = new();
    public List<TaskListDto> Created { get; set; } = new();
    public List<TaskListDto> Mentioned { get; set; } = new();
}

/// <summary>عدّادات «مهامي» لشريط الصفحة الرئيسية</summary>
public class MyTasksSummaryDto
{
    public int OpenAssigned { get; set; }
    public int OverdueAssigned { get; set; }
    public int OpenCreated { get; set; }
    public int MentionedTasks { get; set; }
}

/// <summary>حِمل موظف واحد في ملخص إدارة التكليفات العامة (لمن يرى الكل فقط)</summary>
public class EmployeeTaskLoadDto
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public int OpenCount { get; set; }
    public int InProgressCount { get; set; }
    public int OverdueCount { get; set; }
    public int CompletedCount { get; set; }
    /// <summary>أرجعها المكلَّف وما زالت بانتظار قرار المنشئ</summary>
    public int ReturnedPendingCount { get; set; }
}

/// <summary>إضافة تعليق/تحديث تقدم لخيط نقاش تكليف — المناداة تُرسل صراحة من واجهة الاختيار</summary>
public class AddTaskCommentDto
{
    public int TaskId { get; set; }
    public string Body { get; set; } = "";
    /// <summary>Comment | Progress</summary>
    public string Kind { get; set; } = "Comment";
    public List<int> MentionedEmployeeIds { get; set; } = new();
}

/// <summary>تعليق واحد في خيط النقاش مع أسماء المنادَين فيه</summary>
public class TaskCommentDto
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public string AuthorUserName { get; set; } = "";
    public int? AuthorEmployeeId { get; set; }
    public string? AuthorName { get; set; }
    public string Body { get; set; } = "";
    public string Kind { get; set; } = "Comment";
    public DateTime CreatedAt { get; set; }
    public List<int> MentionedEmployeeIds { get; set; } = new();
    public List<string> MentionedNames { get; set; } = new();
}

/// <summary>تفاصيل تكليف كاملة: بياناته + خيط النقاش + صلاحيات العارض لحسم أزرار الإجراءات</summary>
public class TaskDetailDto
{
    public TaskListDto Task { get; set; } = new();
    public List<TaskCommentDto> Comments { get; set; } = new();
    /// <summary>موظف العارض (إن كان حسابه مربوطًا)</summary>
    public int? ViewerEmployeeId { get; set; }
    public bool ViewerIsAssignee { get; set; }
    /// <summary>العارض هو المنشئ أو مدير (تعديل/إلغاء)</summary>
    public bool ViewerCanAdminister { get; set; }
    /// <summary>العارض مدير بأدوار الإدارة (يرى الكل ويتصرف)</summary>
    public bool ViewerCanManage { get; set; }
}

/// <summary>تعديل تكليف عام قائم (المنشئ أو مدير) — الحقول القابلة للتعديل فقط</summary>
public class UpdateGeneralTaskDto
{
    public int TaskId { get; set; }
    public int AssignedTo { get; set; }
    public int? TaskTypeId { get; set; }
    public string? TaskDescription { get; set; }
    public DateTime DueDate { get; set; }
    public TimeOnly? DueTime { get; set; }
    public string Priority { get; set; } = "Medium";
}

public class LeadTaskDto
{
    public int LeadId { get; set; }
    public int AssignedTo { get; set; }
    public int? TaskTypeId { get; set; }
    public string? TaskDescription { get; set; }
    public DateTime DueDate { get; set; } = DateTime.Today.AddDays(1);
    public TimeOnly? DueTime { get; set; }
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
    public bool? StageChangesOnly { get; set; }
    public bool? NoFollowUpOnly { get; set; }
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
    public string? TaskScope { get; set; }
    public string? CreatedBy { get; set; }
    public bool? IsOverdue { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 5000;
    public string SortBy { get; set; } = "DueDate";
    public bool SortDescending { get; set; } = false;
}