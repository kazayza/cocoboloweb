using System;
namespace COCOBOLOERPNEW.Models;

public partial class PayrollDetail
{
    public int       PayrollDetailID      { get; set; }
    public int       PayrollID            { get; set; }

    // نوع البند
    // ── خصومات (IsDeduction=true) ──
    //   'AbsenceDeduction'   = خصم غياب
    //   'LateDeduction'      = خصم تأخير
    //   'LoanDeduction'      = خصم سلفة
    //   'Penalty'            = جزاء
    // ── إضافات (IsDeduction=false) ──
    //   'Bonus'              = مكافأة داخل الراتب
    //   'Commission'         = عمولة داخل الراتب
    //   'BonusSeparate'      = مكافأة خارج الراتب
    //   'CommissionSeparate' = عمولة خارج الراتب
    public string    DetailType           { get; set; } = null!;
    public string?   DetailDescription    { get; set; }
    public decimal   Amount               { get; set; }

    public bool      IsDeduction          { get; set; } = true;
    // true  = خصم (يزيد Deductions ويقلل NetSalary)
    // false = إضافة

    public string?   PaymentType          { get; set; } = "InPayroll";
    // 'InPayroll'  = داخل الراتب (يأثر في NetSalary)
    // 'Separate'   = منفصل (حركة خزينة مستقلة - مش في NetSalary)

    public int?      CashboxTransactionID { get; set; }
    // لو PaymentType='Separate' → رقم حركة الخزينة المنفصلة

    public string?   CreatedBy            { get; set; }
    public DateTime? CreatedAt            { get; set; }

    public virtual Payroll             Payroll            { get; set; } = null!;
    public virtual CashboxTransaction? CashboxTransaction { get; set; }
}