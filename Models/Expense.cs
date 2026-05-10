using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class Expense
{
    public int ExpenseId { get; set; }

    public int ExpenseGroupId { get; set; }

    public string ExpenseName { get; set; } = null!;

    public DateTime ExpenseDate { get; set; }

    public bool? IsAdvance { get; set; }

    public int? AdvanceMonths { get; set; }

    public int CashBoxId { get; set; }

    public decimal Amount { get; set; }

    public string? Notes { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public string? Torecipient { get; set; }

    public virtual ExpenseGroup ExpenseGroup { get; set; } = null!;
}
