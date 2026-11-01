using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class AdditionalCharge
{
    public int ChargeId { get; set; }
    public int? TransactionId { get; set; }
    public int? QuotationId { get; set; }
    public int? PartyId { get; set; }
    public string? ChargeType { get; set; }
    public string? ChargeDescription { get; set; }
    public decimal? ChargeAmount { get; set; }
    public string? Status { get; set; }
    public int? AppliedToTransactionId { get; set; }
    /// <summary>حدث التسليم المولّد للرسم (رسوم تعديل العميل من تسليمة — القسم 11.4)؛ NULL للرسوم اليدوية</summary>
    public int? SourceDeliveryId { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
}