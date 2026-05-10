using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class ComplaintFollowUp
{
    public int FollowUpId { get; set; }

    public int ComplaintId { get; set; }

    public DateTime? FollowUpDate { get; set; }

    public int FollowUpBy { get; set; }

    public string? Notes { get; set; }

    public string? ActionTaken { get; set; }

    public DateTime? NextFollowUpDate { get; set; }

    public virtual Complaint Complaint { get; set; } = null!;

    public virtual Employee FollowUpByNavigation { get; set; } = null!;
}
