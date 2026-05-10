using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class ContactSource
{
    public int SourceId { get; set; }

    public string SourceName { get; set; } = null!;

    public string? SourceNameAr { get; set; }

    public string? SourceIcon { get; set; }

    public bool IsActive { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public string? LastUpdatedBy { get; set; }

    public DateTime? LastUpdatedAt { get; set; }

    // ============================
    // Navigation Properties
    // ============================
    public virtual ICollection<CustomerInteraction> CustomerInteractions { get; set; } = new List<CustomerInteraction>();

    public virtual ICollection<SalesOpportunity> SalesOpportunities { get; set; } = new List<SalesOpportunity>();

    // New
    public virtual ICollection<Party> Parties { get; set; } = new List<Party>();
}