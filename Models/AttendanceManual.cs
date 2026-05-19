using System;
namespace COCOBOLOERPNEW.Models;

/// <summary>
/// الحضور اليدوي للموظفين اللي مش عندهم بصمة
/// </summary>
public partial class AttendanceManual
{
    public int      ManualId        { get; set; }
    public int      EmployeeId      { get; set; }
    public string   AttendanceMonth { get; set; } = null!; // 2026-05
    public int      PresentDays     { get; set; }
    public int      AbsenceDays     { get; set; }
    public int      LateMinutes     { get; set; }
    public string?  Notes           { get; set; }
    public string?  CreatedBy       { get; set; }
    public DateTime CreatedAt       { get; set; }

    public virtual Employee Employee { get; set; } = null!;
}