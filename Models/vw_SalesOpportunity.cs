using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class vw_SalesOpportunity
{
    public int OpportunityID { get; set; }

    public int Partyid { get; set; }

    public string? ClientName { get; set; }

    public string? Phone1 { get; set; }

    public string? Phone2 { get; set; }

    public string? Address { get; set; }

    public int? EmployeeID { get; set; }

    public string? EmployeeName { get; set; }

    public string? Username { get; set; }

    public int? SourceID { get; set; }

    public string? SourceName { get; set; }

    public string? SourceNameAr { get; set; }

    public string? SourceIcon { get; set; }

    public int? AdTypeID { get; set; }

    public string? AdTypeName { get; set; }

    public string? AdTypeNameAr { get; set; }

    public int StageID { get; set; }

    public string? StageName { get; set; }

    public string? StageNameAr { get; set; }

    public string? StageColor { get; set; }

    public int? StageOrder { get; set; }

    public int? StatusID { get; set; }

    public string? StatusName { get; set; }

    public string? StatusNameAr { get; set; }

    public int? CategoryID { get; set; }

    public string? CategoryName { get; set; }

    public string? CategoryNameAr { get; set; }

    public string? InterestedProduct { get; set; }

    public decimal? ExpectedValue { get; set; }

    public string? Location { get; set; }

    public DateTime FirstContactDate { get; set; }

    public DateTime? NextFollowUpDate { get; set; }

    public DateTime? LastContactDate { get; set; }

    public int? LostReasonID { get; set; }

    public string? LostReasonName { get; set; }

    public string? LostReasonNameAr { get; set; }

    public string? LostNotes { get; set; }

    public string? Notes { get; set; }

    public string? Guidance { get; set; }

    public int? QuotationID { get; set; }

    public int? TransactionID { get; set; }

    public bool IsActive { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? LastUpdatedBy { get; set; }

    public DateTime? LastUpdatedAt { get; set; }

    public int? DaysSinceFirstContact { get; set; }

    public string FollowUpStatus { get; set; } = null!;
}
