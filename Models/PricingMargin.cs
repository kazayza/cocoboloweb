using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class PricingMargin
{
    public int MarginId { get; set; }

    public decimal PremiumMargin { get; set; }

    public decimal EliteMargin { get; set; }

    public bool IsActive { get; set; }

    public decimal? PreviousPremium { get; set; }

    public decimal? PreviousElite { get; set; }

    public string? ChangeReason { get; set; }

    public string ChangedBy { get; set; } = null!;

    public DateTime ChangedAt { get; set; }
}
