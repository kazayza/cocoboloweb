using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class PersonalAccount
{
    public int PersonalAccountId { get; set; }
    public string AccountName { get; set; } = null!;
    public string AccountType { get; set; } = null!;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? NationalId { get; set; }
    public string? Notes { get; set; }
    public decimal OpeningBalance { get; set; }
    public DateTime? OpeningDate { get; set; }
    public string OpeningType { get; set; } = "Credit";   // Credit (له) / Debit (عليه)
    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? LastUpdatedBy { get; set; }
    public DateTime? LastUpdatedAt { get; set; }
}
