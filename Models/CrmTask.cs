using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class CrmTask
{
    public int TaskId { get; set; }

    public int? OpportunityId { get; set; }

    public int? PartyId { get; set; }

    public int? LeadId { get; set; }

    public int AssignedTo { get; set; }

    public int? TaskTypeId { get; set; }

    public string? TaskDescription { get; set; }

    public DateTime DueDate { get; set; }

    public TimeOnly? DueTime { get; set; }

    public string Priority { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime? CompletedDate { get; set; }

    public string? CompletedBy { get; set; }

    public string? CompletionNotes { get; set; }

    public DateTime? StartedAt { get; set; }

    public string? StartedBy { get; set; }

    public string? StartNotes { get; set; }

    // ─── تطوير التكليفات العامة (general-tasks-2-platform.sql) ───────────────
    /// <summary>وقت إلغاء التكليف — يُضبط فقط من CancelGeneralTaskAsync (المنشئ أو مدير، بسبب إلزامي)</summary>
    public DateTime? CancelledAt { get; set; }

    public string? CancelledBy { get; set; }

    public string? CancellationReason { get; set; }

    /// <summary>وقت تأكيد المكلَّف استلام التكليف (AcknowledgeTaskAsync) — يُصفَّر عند الإرجاع/إعادة الإسناد</summary>
    public DateTime? AcknowledgedAt { get; set; }

    public string? AcknowledgedBy { get; set; }

    /// <summary>آخر إرجاع من المكلَّف للمنشئ (ReturnTaskAsync — بسبب إلزامي، الحالة تعود Pending)</summary>
    public DateTime? ReturnedAt { get; set; }

    public string? ReturnedBy { get; set; }

    public string? ReturnReason { get; set; }

    /// <summary>رابط دليل إتمام اختياري يرفقه المنفّذ مع ملاحظة الإتمام</summary>
    public string? CompletionLink { get; set; }

    /// <summary>نوع المستند المرتبط (مفردات GeneralTaskLinks) — NULL = تكليف غير مرتبط</summary>
    public string? LinkedEntityType { get; set; }

    /// <summary>رقم المستند المرتبط (مفتاحه في نظامه)</summary>
    public int? LinkedEntityId { get; set; }

    /// <summary>عنوان المستند المرتبط — لقطة وقت الإنشاء (لا يتغير بتغير المستند)</summary>
    public string? LinkedEntityTitle { get; set; }

    public bool ReminderEnabled { get; set; }

    public int? ReminderMinutes { get; set; }

    public bool IsActive { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? AssignmentSource { get; set; }

    public string? TaskScope { get; set; }

    public virtual Employee AssignedToNavigation { get; set; } = null!;

    public virtual SalesOpportunity? Opportunity { get; set; }

    public virtual Party? Party { get; set; }

    public virtual TaskType? TaskType { get; set; }
}
