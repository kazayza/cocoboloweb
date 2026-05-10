using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class ExpenseGroup
{
    public int ExpenseGroupId { get; set; }

    public string ExpenseGroupName { get; set; } = null!;

    public int? ParentGroupId { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<Expense> Expenses { get; set; } = new List<Expense>();

    public virtual ICollection<ExpenseGroup> InverseParentGroup { get; set; } = new List<ExpenseGroup>();

    public virtual ExpenseGroup? ParentGroup { get; set; }
}
