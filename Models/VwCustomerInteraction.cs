using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class VwCustomerInteraction
{
    public int InteractionId { get; set; }

    public int OpportunityId { get; set; }

    public int PartyId { get; set; }

    public string? ClientName { get; set; }

    public string? Phone { get; set; }

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

    public int? StageAfterId { get; set; }

    public string? StageAfterName { get; set; }

    public DateTime? NextFollowUpDate { get; set; }

    public string? Notes { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? EditBy { get; set; }

    public DateTime? EditAt { get; set; }
}
