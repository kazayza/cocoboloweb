using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

/// <summary>
/// جدول السلف - كل سلفة موظف
/// </summary>
public partial class EmployeeLoan
{
    public int      LoanId               { get; set; }
    public int      EmployeeId           { get; set; }

    // بيانات السلفة
    public decimal  LoanAmount           { get; set; }   // إجمالي السلفة
    public int      TotalInstallments    { get; set; }   // عدد الأقساط
    public decimal  MonthlyInstallment   { get; set; }   // قيمة القسط
    public decimal  RemainingAmount      { get; set; }   // المتبقي
    public int      PaidInstallments     { get; set; }   // الأقساط المدفوعة

    // التواريخ
    public DateTime LoanDate             { get; set; }
    public string   StartDeductionMonth  { get; set; } = null!; // 2026-06

    // الحالة
    public string   Status               { get; set; } = "Active";
    // Active | Completed | Cancelled

    // الخزينة
    public int?     CashBoxId            { get; set; }
    public int?     CashboxTransactionId { get; set; }

    // تتبع
    public string?  Notes                { get; set; }
    public string?  ApprovedBy           { get; set; }
    public string?  CreatedBy            { get; set; }
    public DateTime CreatedAt            { get; set; }
    public DateTime? LastUpdatedAt       { get; set; }

    // Navigation Properties
    public virtual Employee              Employee             { get; set; } = null!;
    public virtual CashBox?              CashBox              { get; set; }
    public virtual CashboxTransaction?   CashboxTransaction   { get; set; }
    public virtual ICollection<LoanInstallment> Installments  { get; set; } = new List<LoanInstallment>();
}
