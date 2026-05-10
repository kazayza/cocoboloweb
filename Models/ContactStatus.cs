using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class ContactStatus
{
    public int StatusId { get; set; }

    public string StatusName { get; set; } = null!;

    public string? StatusNameAr { get; set; }

    public bool IsActive { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public string? LastUpdatedBy { get; set; }

    public DateTime? LastUpdatedAt { get; set; }

    public virtual ICollection<CustomerInteraction> CustomerInteractions { get; set; } = new List<CustomerInteraction>();

    public virtual ICollection<SalesOpportunity> SalesOpportunities { get; set; } = new List<SalesOpportunity>();
}
