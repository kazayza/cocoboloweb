using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class Party
{
    public int PartyId { get; set; }

    public string PartyName { get; set; } = null!;

    public int PartyType { get; set; }

    public string? ContactPerson { get; set; }

    public string? Phone { get; set; }

    public string? Phone2 { get; set; }

    public string? Email { get; set; }

    public string? Address { get; set; }

    public string? TaxNumber { get; set; }

    public decimal? OpeningBalance { get; set; }

    public string? BalanceType { get; set; }

    public string? Notes { get; set; }

    public bool? IsActive { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public int? ReferralSourceId { get; set; }

    public int? ReferralSourceClient { get; set; }

    public string? NationalId { get; set; }

    public string? FloorNumber { get; set; }

    public string? DataDone { get; set; }

    // =========================
    // New Fields
    // =========================
    public string? CustomerStage { get; set; }
    public int? StageId { get; set; }

    public string? JobTitle { get; set; }

    public int? ParentPartyId { get; set; }

    public string? City { get; set; }

    public string? Area { get; set; }

    public int? ContactSourceId { get; set; }

    public DateTime? LastContactDate { get; set; }

    public int? Rating { get; set; }

    public string? LastUpdatedBy { get; set; }

    public DateTime? LastUpdatedAt { get; set; }

    // =========================
    // Navigation Properties
    // =========================
    public virtual ICollection<Complaint> Complaints { get; set; } = new List<Complaint>();

    public virtual ICollection<CrmTask> CrmTasks { get; set; } = new List<CrmTask>();

    public virtual ICollection<CustomerInteraction> CustomerInteractions { get; set; } = new List<CustomerInteraction>();

    public virtual PartyType PartyTypeNavigation { get; set; } = null!;

    public virtual ReferralSource? ReferralSource { get; set; }

    public virtual ICollection<SalesOpportunity> SalesOpportunities { get; set; } = new List<SalesOpportunity>();

    // Parent / Child
    public virtual Party? ParentParty { get; set; }

    public virtual ICollection<Party> InverseParentParty { get; set; } = new List<Party>();

    // Contact Source
    public virtual ContactSource? ContactSource { get; set; }
    // Sales Stage
    public virtual SalesStage? Stage { get; set; }

    // Party Contacts
    public virtual ICollection<PartyContact> PartyContacts { get; set; } = new List<PartyContact>();
}