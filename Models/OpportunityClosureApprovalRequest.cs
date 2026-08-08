namespace COCOBOLOERPNEW.Models;

public partial class OpportunityClosureApprovalRequest
{
    public int RequestId { get; set; }
    public int OpportunityId { get; set; }
    public int PartyId { get; set; }
    public int CurrentStageId { get; set; }
    public int RequestedStageId { get; set; }
    public int? LostReasonId { get; set; }
    public string? RequestReasonNotes { get; set; }
    public string? RequestSource { get; set; }
    public string Status { get; set; } = "Pending";
    public string RequestedBy { get; set; } = null!;
    public DateTime RequestedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNotes { get; set; }

    public virtual SalesOpportunity Opportunity { get; set; } = null!;
    public virtual Party Party { get; set; } = null!;
    public virtual SalesStage CurrentStage { get; set; } = null!;
    public virtual SalesStage RequestedStage { get; set; } = null!;
    public virtual LostReason? LostReason { get; set; }
}
