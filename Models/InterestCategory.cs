using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class InterestCategory
{
    public int CategoryId { get; set; }

    public string CategoryName { get; set; } = null!;

    public string? CategoryNameAr { get; set; }

    public bool IsActive { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public string? LastUpdatedBy { get; set; }

    public DateTime? LastUpdatedAt { get; set; }

    public virtual ICollection<SalesOpportunity> SalesOpportunities { get; set; } = new List<SalesOpportunity>();
}
