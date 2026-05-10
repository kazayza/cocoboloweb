using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class VwPriceChangeRequest
{
    public int RequestId { get; set; }

    public int ProductId { get; set; }

    public string ProductName { get; set; } = null!;

    public string ProductDescription { get; set; } = null!;

    public string GroupName { get; set; } = null!;

    public string PriceType { get; set; } = null!;

    public decimal CurrentPrice { get; set; }

    public decimal RequestedPrice { get; set; }

    public decimal? PriceDifference { get; set; }

    public string Reason { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string RequestedBy { get; set; } = null!;

    public DateTime RequestedAt { get; set; }

    public string? ReviewedBy { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public string? ReviewNotes { get; set; }

    public decimal? RequestedMarginPercent { get; set; }

    public decimal? CurrentMarginPercent { get; set; }
}
