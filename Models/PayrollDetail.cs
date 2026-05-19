using System;
namespace COCOBOLOERPNEW.Models;

// ✅ الأسماء متطابقة مع db24804Context تماماً
public partial class PayrollDetail
{
    public int       PayrollDetailID     { get; set; }  // uppercase - زي الـ Context
    public int       PayrollID           { get; set; }  // uppercase - زي الـ Context
    public string    DetailType          { get; set; } = null!;
    public string?   DetailDescription   { get; set; }
    public decimal   Amount              { get; set; }

    // حقول جديدة - مش موجودة في الـ Context أصلاً
    public bool      IsDeduction         { get; set; } = true;
    public string?   PaymentType         { get; set; } = "InPayroll";
    public int?      CashboxTransactionID { get; set; }  // uppercase

    public string?   CreatedBy           { get; set; }
    public DateTime? CreatedAt           { get; set; }

    public virtual Payroll             Payroll            { get; set; } = null!;
    public virtual CashboxTransaction? CashboxTransaction { get; set; }
}