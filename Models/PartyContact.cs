using System;

namespace COCOBOLOERPNEW.Models;

public partial class PartyContact
{
    public int ContactId { get; set; }

    public int PartyId { get; set; }

    public string ContactName { get; set; } = null!;

    public string? JobTitle { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Notes { get; set; }

    public bool IsPrimary { get; set; }

    public bool IsActive { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    // Navigation
    public virtual Party Party { get; set; } = null!;
}