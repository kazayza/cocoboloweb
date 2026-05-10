using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class AdType
{
    public int AdTypeId { get; set; }

    public string AdTypeName { get; set; } = null!;

    public string? AdTypeNameAr { get; set; }

    public bool IsActive { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public string? LastUpdatedBy { get; set; }

    public DateTime? LastUpdatedAt { get; set; }

    public virtual ICollection<SalesOpportunity> SalesOpportunities { get; set; } = new List<SalesOpportunity>();
}
