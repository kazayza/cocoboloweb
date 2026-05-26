using System;
using System.Collections.Generic;


namespace COCOBOLOERPNEW.Models;

public partial class Transaction
{
    
    public int TransactionId { get; set; }

    public DateTime TransactionDate { get; set; }

    public int PartyId { get; set; }

    public string TransactionType { get; set; } = null!;

    public int WarehouseId { get; set; }

    public string? ReferenceNumber { get; set; }

    public string? ReferenceType { get; set; }

    public int? EmpId { get; set; }

    public DateTime? DueDate { get; set; }

    public decimal TotalAmount { get; set; }

    public decimal? DiscountPercentage { get; set; }

    public decimal? DiscountAmount { get; set; }

    public decimal? NetTotalAmount { get; set; }

    public decimal PaidAmount { get; set; }

    public decimal TotalChargesAmount { get; set; }

    public decimal GrandTotal { get; set; }

    public string? PaymentMethod { get; set; }

    public string? Notes { get; set; }

    public int? EditStatus { get; set; }

    public string? EditReason { get; set; }

    public DateTime? EditRequestDate { get; set; }

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public string? EditBy { get; set; }

    public DateTime? EditAt { get; set; }

    public string? EditDone { get; set; }

    public bool? IsDelivered { get; set; }

    public string? InvoiceStatus { get; set; }

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual ICollection<SalesOpportunity> SalesOpportunities { get; set; } = new List<SalesOpportunity>();

    public virtual ICollection<TransactionDetail> TransactionDetails { get; set; } = new List<TransactionDetail>();
}
