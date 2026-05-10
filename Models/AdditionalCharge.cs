using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class AdditionalCharge
{
    public int ChargeId { get; set; }

    public int? TransactionId { get; set; }

    public int? QuotationId { get; set; }

    public int? PartyId { get; set; }

    public string? ChargeDescription { get; set; }

    public decimal? ChargeAmount { get; set; }

    public string? Notes { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }
}
