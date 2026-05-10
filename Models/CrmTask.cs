using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class CrmTask
{
    public int TaskId { get; set; }

    public int? OpportunityId { get; set; }

    public int? PartyId { get; set; }

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

    public bool ReminderEnabled { get; set; }

    public int? ReminderMinutes { get; set; }

    public bool IsActive { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Employee AssignedToNavigation { get; set; } = null!;

    public virtual SalesOpportunity? Opportunity { get; set; }

    public virtual Party? Party { get; set; }

    public virtual TaskType? TaskType { get; set; }
}
