using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class LeadInteraction
{
    public int LeadInteractionId { get; set; }

    public int LeadId { get; set; }

    public int? EmployeeId { get; set; }

    public string InteractionType { get; set; } = "";

    public DateTime InteractionDate { get; set; }

    public string? Summary { get; set; }

    public string? Notes { get; set; }

    public string? OldLeadStatus { get; set; }

    public string? NewLeadStatus { get; set; }

    public DateTime? NextFollowUpDate { get; set; }

    public bool IsCompleted { get; set; } = false;

    public int? CompletedByEmployeeId { get; set; }

    public DateTime? CompletedDate { get; set; }

    public bool IsSystemGenerated { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual LeadsCrm Lead { get; set; } = null!;

    public virtual Employee? Employee { get; set; }
}