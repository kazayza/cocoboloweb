using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class ShortPermission
{
    public int PermissionId { get; set; }

    public int EmployeeId { get; set; }

    public DateOnly PermissionDate { get; set; }

    public string PermissionType { get; set; } = null!;

    public TimeOnly? FromTime { get; set; }

    public TimeOnly? ToTime { get; set; }

    public int? DurationMinutes { get; set; }

    public string? Reason { get; set; }

    public string? Status { get; set; }

    public string? ManagerComment { get; set; }

    public int? ApprovedByUserId { get; set; }

    public DateTime? ApprovalDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public int? AttendanceId { get; set; }
}
