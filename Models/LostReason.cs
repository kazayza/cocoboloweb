using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class LostReason
{
    public int LostReasonId { get; set; }

    public string ReasonName { get; set; } = null!;

    public string? ReasonNameAr { get; set; }

    public bool IsActive { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public string? LastUpdatedBy { get; set; }

    public DateTime? LastUpdatedAt { get; set; }

    public virtual ICollection<SalesOpportunity> SalesOpportunities { get; set; } = new List<SalesOpportunity>();
}
