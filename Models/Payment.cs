using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class Payment
{
    public int PaymentId { get; set; }

    public int TransactionId { get; set; }

    public DateTime PaymentDate { get; set; }

    public int? CashboxTransactionId { get; set; }

    public decimal Amount { get; set; }

    public string? PaymentMethod { get; set; }

    public string? Notes { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<CashboxTransaction> CashboxTransactions { get; set; } = new List<CashboxTransaction>();

    public virtual Transaction Transaction { get; set; } = null!;
}
