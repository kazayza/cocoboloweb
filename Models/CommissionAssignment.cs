using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class CommissionAssignment
{
    public int AssignmentId { get; set; }

    public int TransactionId { get; set; }

    public int EmployeeId { get; set; }

    public int CommissionMonth { get; set; }

    public int CommissionYear { get; set; }

    public decimal? TransactionAmount { get; set; }

    public decimal CommissionRate { get; set; }

    public decimal? CommissionAmount { get; set; }

    public string? ApprovedBy { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}
