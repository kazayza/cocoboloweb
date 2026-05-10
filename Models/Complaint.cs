using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class Complaint
{
    public int ComplaintId { get; set; }

    public int PartyId { get; set; }

    public int? OpportunityId { get; set; }

    public DateTime? ComplaintDate { get; set; }

    public int TypeId { get; set; }

    public string Subject { get; set; } = null!;

    public string Details { get; set; } = null!;

    public byte? Priority { get; set; }

    public byte? Status { get; set; }

    public int? AssignedTo { get; set; }

    public string? Solution { get; set; }

    public DateTime? SolvedDate { get; set; }

    public bool? Escalated { get; set; }

    public DateTime? EscalatedDate { get; set; }

    public string? EscalatedTo { get; set; }

    public byte? SatisfactionLevel { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public string? EscalationReason { get; set; }

    public int? EscalatedBy { get; set; }

    public bool? IsActive { get; set; }

    public virtual Employee? AssignedToNavigation { get; set; }

    public virtual ICollection<ComplaintFollowUp> ComplaintFollowUps { get; set; } = new List<ComplaintFollowUp>();

    public virtual SalesOpportunity? Opportunity { get; set; }

    public virtual Party Party { get; set; } = null!;

    public virtual ComplaintType Type { get; set; } = null!;
}
