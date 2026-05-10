using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class vw_CustomerInteraction
{
    public int InteractionID { get; set; }

    public int OpportunityID { get; set; }

    public int Partyid { get; set; }

    public string? ClientName { get; set; }

    public string? Phone { get; set; }

    public int? EmployeeID { get; set; }

    public string? EmployeeName { get; set; }

    public int? SourceID { get; set; }

    public string? SourceName { get; set; }

    public string? SourceIcon { get; set; }

    public int? StatusID { get; set; }

    public string? StatusName { get; set; }

    public string? StatusNameAr { get; set; }

    public DateTime InteractionDate { get; set; }

    public TimeOnly? InteractionTime { get; set; }

    public string? Summary { get; set; }

    public int? StageBeforeID { get; set; }

    public string? StageBeforeName { get; set; }

    public int? StageAfterID { get; set; }

    public string? StageAfterName { get; set; }

    public DateTime? NextFollowUpDate { get; set; }

    public string? Notes { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? EditBy { get; set; }

    public DateTime? EditAt { get; set; }
}
