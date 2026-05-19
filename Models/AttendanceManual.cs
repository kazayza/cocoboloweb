using System;
namespace COCOBOLOERPNEW.Models;

public partial class AttendanceManual
{
    public int      ManualId        { get; set; }
    public int      EmployeeId      { get; set; }
    public string   AttendanceMonth { get; set; } = null!;
    public int      PresentDays     { get; set; }
    public int      AbsenceDays     { get; set; }
    public int      LateMinutes     { get; set; }
    public string?  Notes           { get; set; }
    public string?  CreatedBy       { get; set; }
    public DateTime CreatedAt       { get; set; }

    public virtual Employee Employee { get; set; } = null!;
}