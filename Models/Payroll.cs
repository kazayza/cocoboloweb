using System;
using System.Collections.Generic;
namespace COCOBOLOERPNEW.Models;

public partial class Payroll
{
    public int       PayrollId            { get; set; }
    public int       EmployeeId           { get; set; }
    public string    PayrollMonth         { get; set; } = null!;
    public decimal   BasicSalary         { get; set; }
    public decimal?  Allowances          { get; set; }
    public decimal?  BonusInPayroll      { get; set; }
    public decimal?  Deductions          { get; set; }
    public decimal?  AbsenceDeduction    { get; set; }
    public decimal?  LateDeduction       { get; set; }
    public decimal?  LoanDeduction       { get; set; }
    public decimal?  PenaltyDeduction    { get; set; }
    public decimal?  NetSalary           { get; set; }
    public int?      WorkingDaysInMonth  { get; set; }
    public int?      PresentDays         { get; set; }
    public int?      AbsenceDays         { get; set; }
    public int?      LateMinutesTotal    { get; set; }
    public bool?     IsManualAttendance  { get; set; }
    public string?   PaymentStatus       { get; set; }
    public DateTime? PaymentDate         { get; set; }
    public int?      CashboxTransactionId { get; set; }
    public int?      PayrollRunId         { get; set; }
    public string?   Notes               { get; set; }
    public string?   CreatedBy           { get; set; }
    public DateTime? CreatedAt           { get; set; }

    public virtual CashboxTransaction?        CashboxTransaction { get; set; }
    public virtual Employee                   Employee           { get; set; } = null!;
    public virtual PayrollRun?                PayrollRun         { get; set; }
    public virtual ICollection<PayrollDetail> PayrollDetails     { get; set; } = new List<PayrollDetail>();
}