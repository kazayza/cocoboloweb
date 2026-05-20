using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class EmployeeShift
{
    public int EmployeeShiftId { get; set; }

    public int EmployeeId { get; set; }

    public int? BiometricCode { get; set; }

    public string ShiftType { get; set; } = null!;

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    public DateTime EffectiveFrom { get; set; }

    public DateTime? EffectiveTo { get; set; }

    public DateTime? CreatedAt { get; set; }

    public string? CreatedBy { get; set; }
    public byte? OffDay1 { get; set; }
    public byte? OffDay2 { get; set; }
}
