using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class Quotation
{
    public int QuotationId { get; set; }

    public DateTime QuotationDate { get; set; }

    public int PartyId { get; set; }

    public int? WarehouseId { get; set; }

    public string? PricingType { get; set; }

    public decimal TotalAmount { get; set; }

    public decimal? GrandTotal { get; set; }

    public int? InvoiceId { get; set; }

    public string? Notes { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<QuotationDetail> QuotationDetails { get; set; } = new List<QuotationDetail>();

    public virtual ICollection<SalesOpportunity> SalesOpportunities { get; set; } = new List<SalesOpportunity>();
}
