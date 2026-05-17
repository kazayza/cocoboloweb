using System;

namespace COCOBOLOERPNEW.Models;

/// <summary>
/// أقساط السلفة - كل قسط على حدة
/// </summary>
public partial class LoanInstallment
{
    public int      InstallmentId     { get; set; }
    public int      LoanId            { get; set; }
    public int      EmployeeId        { get; set; }

    // بيانات القسط
    public int      InstallmentNumber { get; set; }   // رقم القسط 1, 2, 3...
    public string   DeductionMonth    { get; set; } = null!; // 2026-06
    public decimal  Amount            { get; set; }

    // الحالة
    public string   Status            { get; set; } = "Pending";
    // Pending | Deducted | Skipped

    // ربط بالراتب لما يتخصم
    public int?     PayrollId         { get; set; }
    public int?     PayrollDetailId   { get; set; }
    public DateTime? DeductionDate    { get; set; }

    // تتبع
    public string?  Notes             { get; set; }
    public string?  CreatedBy         { get; set; }
    public DateTime CreatedAt         { get; set; }

    // Navigation Properties
    public virtual EmployeeLoan  Loan          { get; set; } = null!;
    public virtual Employee      Employee      { get; set; } = null!;
    public virtual Payroll?      Payroll       { get; set; }
    public virtual PayrollDetail? PayrollDetail { get; set; }
}
