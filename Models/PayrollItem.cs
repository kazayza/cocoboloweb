using System;
namespace COCOBOLOERPNEW.Models;

/// <summary>
/// بنود الراتب المتغيرة: جزاءات / حوافز / مكافآت / عمولات
/// </summary>
public partial class PayrollItem
{
    public int      ItemId      { get; set; }
    public int      PayrollId   { get; set; }
    public int      EmployeeId  { get; set; }

    // النوع: Penalty | Bonus | Commission | Other
    public string   ItemType    { get; set; } = null!;
    public string   Description { get; set; } = null!;
    public decimal  Amount      { get; set; }
    public bool     IsDeduction { get; set; }  // true=خصم  false=إضافة

    public string?  Notes       { get; set; }
    public string?  CreatedBy   { get; set; }
    public DateTime CreatedAt   { get; set; }

    public virtual Payroll  Payroll  { get; set; } = null!;
    public virtual Employee Employee { get; set; } = null!;
}