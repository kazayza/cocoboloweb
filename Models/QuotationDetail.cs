using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class QuotationDetail
{
    public int QuotationDetailId { get; set; }

    public int QuotationId { get; set; }

    public int ProductId { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal? TotalAmount { get; set; }

    public string? Notes { get; set; }

    public string PricingTier { get; set; } = null!;

    public virtual Quotation Quotation { get; set; } = null!;
}
