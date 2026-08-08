namespace COCOBOLOERPNEW.DTOs;

public static class OpportunityClosureApprovalStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public class OpportunityClosureApprovalCreateDto
{
    public int OpportunityId { get; set; }
    public int PartyId { get; set; }
    public int CurrentStageId { get; set; }
    public int RequestedStageId { get; set; }
    public int? LostReasonId { get; set; }
    public string? RequestReasonNotes { get; set; }
    public string? RequestSource { get; set; }
}

public class OpportunityClosureApprovalRequestDto
{
    public int RequestId { get; set; }
    public int OpportunityId { get; set; }
    public int PartyId { get; set; }
    public string ClientName { get; set; } = "";

    public int CurrentStageId { get; set; }
    public string? CurrentStageName { get; set; }

    public int RequestedStageId { get; set; }
    public string? RequestedStageName { get; set; }

    public int? LostReasonId { get; set; }
    public string? LostReasonName { get; set; }
    public string? RequestReasonNotes { get; set; }
    public string? RequestSource { get; set; }

    public string Status { get; set; } = OpportunityClosureApprovalStatuses.Pending;
    public string RequestedBy { get; set; } = "";
    public DateTime RequestedAt { get; set; }

    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNotes { get; set; }
}
