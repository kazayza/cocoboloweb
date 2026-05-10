using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class SalaryHistory
{
    public int SalaryHistoryId { get; set; }

    public int EmployeeId { get; set; }

    public decimal? OldSalary { get; set; }

    public decimal NewSalary { get; set; }

    public DateOnly ChangeDate { get; set; }

    public string? Reason { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Employee Employee { get; set; } = null!;
}
