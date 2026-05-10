using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class CashboxTransaction
{
    public int CashboxTransactionId { get; set; }

    public int CashBoxId { get; set; }

    public int? PaymentId { get; set; }

    public int? ReferenceId { get; set; }

    public string? ReferenceType { get; set; }

    public string TransactionType { get; set; } = null!;

    public decimal Amount { get; set; }

    public DateTime TransactionDate { get; set; }

    public string? Notes { get; set; }

    public string CreatedBy { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public virtual CashBox CashBox { get; set; } = null!;

    public virtual Payment? Payment { get; set; }

    public virtual ICollection<Payroll> Payrolls { get; set; } = new List<Payroll>();
}
