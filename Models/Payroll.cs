using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class Payroll
{
    public int PayrollId { get; set; }

    public int EmployeeId { get; set; }

    public string PayrollMonth { get; set; } = null!;

    public decimal BasicSalary { get; set; }

    public decimal? Allowances { get; set; }

    public decimal? Deductions { get; set; }

    public decimal? NetSalary { get; set; }

    public string? PaymentStatus { get; set; }

    public DateTime? PaymentDate { get; set; }

    public int? CashboxTransactionId { get; set; }

    public string? Notes { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual CashboxTransaction? CashboxTransaction { get; set; }

    public virtual Employee Employee { get; set; } = null!;

    public virtual ICollection<PayrollDetail> PayrollDetails { get; set; } = new List<PayrollDetail>();
}
