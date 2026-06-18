using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class SalesOpportunity
{
    public int OpportunityId { get; set; }

    public int PartyId { get; set; }

    public int? EmployeeId { get; set; }

    public int? SourceId { get; set; }

    public int? AdTypeId { get; set; }

    public int StageId { get; set; }

    public int? StatusId { get; set; }

    public int? CategoryId { get; set; }

    public string? InterestedProduct { get; set; }

    public decimal? ExpectedValue { get; set; }
    public decimal? ActualValue { get; set; }

    public string? Location { get; set; }

    public DateTime FirstContactDate { get; set; }

    public DateTime? NextFollowUpDate { get; set; }

    public DateTime? LastContactDate { get; set; }

    public int? LostReasonId { get; set; }

    public string? LostNotes { get; set; }

    public string? Notes { get; set; }

    public string? Guidance { get; set; }

    public int? QuotationId { get; set; }

    public int? TransactionId { get; set; }

    public bool IsActive { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? LastUpdatedBy { get; set; }

    public DateTime? LastUpdatedAt { get; set; }

    public virtual AdType? AdType { get; set; }

    public virtual InterestCategory? Category { get; set; }

    public virtual ICollection<Complaint> Complaints { get; set; } = new List<Complaint>();

    public virtual ICollection<CrmTask> CrmTasks { get; set; } = new List<CrmTask>();

    public virtual ICollection<CustomerInteraction> CustomerInteractions { get; set; } = new List<CustomerInteraction>();

    public virtual Employee? Employee { get; set; }

    public virtual LostReason? LostReason { get; set; }

    public virtual Party Party { get; set; } = null!;

    public virtual Quotation? Quotation { get; set; }

    public virtual ContactSource? Source { get; set; }

    public virtual SalesStage Stage { get; set; } = null!;

    public virtual ContactStatus? Status { get; set; }

    public virtual Transaction? Transaction { get; set; }
}
