using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class CustomerInteraction
{
    public int InteractionId { get; set; }

    public int OpportunityId { get; set; }

    public int PartyId { get; set; }

    public int? EmployeeId { get; set; }

    public int? SourceId { get; set; }

    public int? StatusId { get; set; }

    public DateTime InteractionDate { get; set; }

    public TimeOnly? InteractionTime { get; set; }

    public string? Summary { get; set; }

    public int? StageBeforeId { get; set; }

    public int? StageAfterId { get; set; }

    public DateTime? NextFollowUpDate { get; set; }

    public string? Notes { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? EditBy { get; set; }

    public DateTime? EditAt { get; set; }

    public virtual Employee? Employee { get; set; }

    public virtual SalesOpportunity Opportunity { get; set; } = null!;

    public virtual Party Party { get; set; } = null!;

    public virtual ContactSource? Source { get; set; }

    public virtual SalesStage? StageAfter { get; set; }

    public virtual SalesStage? StageBefore { get; set; }

    public virtual ContactStatus? Status { get; set; }
}
