using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class PayrollDetail
{
    public int PayrollDetailId { get; set; }

    public int PayrollId { get; set; }

    public string DetailType { get; set; } = null!;

    public string? DetailDescription { get; set; }

    public decimal Amount { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Payroll Payroll { get; set; } = null!;
}
