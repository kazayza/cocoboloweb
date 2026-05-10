using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class PriceHistory
{
    public int HistoryId { get; set; }

    public int ProductId { get; set; }

    public string PriceType { get; set; } = null!;

    public decimal? OldPrice { get; set; }

    public decimal NewPrice { get; set; }

    public string ChangedBy { get; set; } = null!;

    public DateTime ChangedAt { get; set; }

    public string? ChangeReason { get; set; }

    public int? CustomerId { get; set; }

    public virtual Product Product { get; set; } = null!;
}
