using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class Attendance
{
    public int AttendanceId { get; set; }

    public int BiometricCode { get; set; }

    public DateTime LogDate { get; set; }

    public TimeOnly? TimeIn { get; set; }

    public TimeOnly? TimeOut { get; set; }

    public string? Status { get; set; }

    public decimal? TotalHours { get; set; }

    public int? LateMinutes { get; set; }

    public int? EarlyLeaveMinutes { get; set; }

    public decimal? PenaltyHours { get; set; }
}
