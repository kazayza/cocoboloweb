using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class vw_SalesDeliveryStatus
{
    public int TransactionID { get; set; }

    public DateTime TransactionDate { get; set; }

    public int Partyid { get; set; }

    public string PartyName { get; set; } = null!;

    public int? EmpId { get; set; }

    public string EmployeeName { get; set; } = null!;

    public string TransactionType { get; set; } = null!;

    public DateTime? DueDate { get; set; }

    public int? DaysRemaining { get; set; }

    public string DeliveryStatus { get; set; } = null!;

    public decimal TotalAmount { get; set; }

    public decimal? DiscountPercentage { get; set; }

    public decimal? DiscountAmount { get; set; }

    public decimal? NetTotalAmount { get; set; }

    public decimal PaidAmount { get; set; }

    public decimal TotalChargesAmount { get; set; }

    public decimal GrandTotal { get; set; }
}
