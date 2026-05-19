using System;
using System.Collections.Generic;
namespace COCOBOLOERPNEW.Models;

public partial class PayrollRun
{
    public int       RunId           { get; set; }
    public string    PayrollMonth    { get; set; } = null!;
    public string    Status          { get; set; } = "Draft";
    public int?      TotalEmployees  { get; set; }
    public decimal?  TotalGross      { get; set; }
    public decimal?  TotalDeductions { get; set; }
    public decimal?  TotalNet        { get; set; }
    public int?      CashBoxId       { get; set; }
    public string?   Notes           { get; set; }
    public string?   ProcessedBy     { get; set; }
    public DateTime? ProcessedAt     { get; set; }
    public string?   CreatedBy       { get; set; }
    public DateTime  CreatedAt       { get; set; }

    public virtual CashBox?              CashBox  { get; set; }
    public virtual ICollection<Payroll>  Payrolls { get; set; } = new List<Payroll>();
}