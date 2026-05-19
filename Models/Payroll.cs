using System;
using System.Collections.Generic;
namespace COCOBOLOERPNEW.Models;

// ✅ الأسماء متطابقة مع db24804Context تماماً
public partial class Payroll
{
    public int       PayrollId            { get; set; }  // الـ Context بيعمل HasColumnName("PayrollID")
    public int       EmployeeId           { get; set; }  // الـ Context بيعمل HasColumnName("EmployeeID")
    public string    PayrollMonth         { get; set; } = null!;
    public decimal   BasicSalary          { get; set; }
    public decimal?  Allowances           { get; set; }
    public decimal?  Deductions           { get; set; }
    public decimal?  NetSalary            { get; set; }  // Computed Column
    public string?   PaymentStatus        { get; set; }
    public DateTime? PaymentDate          { get; set; }
    public int?      CashboxTransactionId  { get; set; } // الـ Context بيعمل HasColumnName("CashboxTransactionID")
    public string?   Notes                { get; set; }
    public string?   CreatedBy            { get; set; }
    public DateTime? CreatedAt            { get; set; }

    // ── حقول جديدة أضفناها ─────────────────────────────────
    public decimal?  BonusInPayroll       { get; set; }
    public decimal?  AbsenceDeduction     { get; set; }
    public decimal?  LateDeduction        { get; set; }
    public decimal?  LoanDeduction        { get; set; }
    public decimal?  PenaltyDeduction     { get; set; }
    public int?      WorkingDaysInMonth   { get; set; }
    public int?      PresentDays          { get; set; }
    public int?      AbsenceDays          { get; set; }
    public int?      LateMinutesTotal     { get; set; }
    public bool?     IsManualAttendance   { get; set; }
    public int?      PayrollRunId         { get; set; }

    // Navigation
    public virtual CashboxTransaction?        CashboxTransaction { get; set; }
    public virtual Employee                   Employee           { get; set; } = null!;
    public virtual PayrollRun?                PayrollRun         { get; set; }
    public virtual ICollection<PayrollDetail> PayrollDetails     { get; set; } = new List<PayrollDetail>();
}