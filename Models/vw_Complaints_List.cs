using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class vw_Complaints_List
{
    public int ComplaintID { get; set; }

    public DateTime? ComplaintDate { get; set; }

    public int Partyid { get; set; }

    public string? ClientName { get; set; }

    public string? ClientPhone { get; set; }

    public int TypeID { get; set; }

    public string? ComplaintType { get; set; }

    public string Subject { get; set; } = null!;

    public string Details { get; set; } = null!;

    public byte? Priority { get; set; }

    public string PriorityName { get; set; } = null!;

    public byte? Status { get; set; }

    public string StatusName { get; set; } = null!;

    public int? AssignedTo { get; set; }

    public string? EmployeeName { get; set; }

    public DateTime? SolvedDate { get; set; }

    public int? DaysOpen { get; set; }

    public bool? Escalated { get; set; }

    public string? EscalatedTo { get; set; }

    public int? OpportunityID { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }
}
