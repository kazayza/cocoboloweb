namespace COCOBOLOERPNEW.DTOs;

// ═══════════════════════════════════════════════════════════
// حالات الحضور
// ═══════════════════════════════════════════════════════════
public static class AttendanceStatus
{
    public const string Present = "حاضر";
    public const string Absent = "غائب";
    public const string Late = "متأخر";
    public const string EarlyLeave = "خروج مبكر";
    public const string Holiday = "إجازة";
    public const string Weekend = "عطلة أسبوعية";
    
    public static string GetColor(string? status) => status switch
    {
        Present => "#4CAF50",
        Absent => "#F44336",
        Late => "#FF9800",
        EarlyLeave => "#FF5722",
        Holiday => "#2196F3",
        Weekend => "#9E9E9E",
        _ => "#757575"
    };

    public static string GetIcon(string? status) => status switch
    {
        Present => "CheckCircle",
        Absent => "Cancel",
        Late => "Schedule",
        EarlyLeave => "ExitToApp",
        Holiday => "BeachAccess",
        Weekend => "Weekend",
        _ => "Help"
    };
}

// ═══════════════════════════════════════════════════════════
// DTO للعرض في القائمة
// ═══════════════════════════════════════════════════════════
public class AttendanceListDto
{
    public int AttendanceId { get; set; }
    public int BiometricCode { get; set; }
    public int? EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public string? Department { get; set; }
    public DateTime LogDate { get; set; }
    public TimeOnly? TimeIn { get; set; }
    public TimeOnly? TimeOut { get; set; }
    public string? Status { get; set; }
    public decimal? TotalHours { get; set; }
    public int? LateMinutes { get; set; }
    public int? EarlyLeaveMinutes { get; set; }
    public decimal? PenaltyHours { get; set; }

    // Display Properties
    public string LogDateDisplay => LogDate.ToString("yyyy/MM/dd");
    public string DayName => LogDate.ToString("dddd", new System.Globalization.CultureInfo("ar-EG"));
    public string TimeInDisplay => TimeIn?.ToString("HH:mm") ?? "--:--";
    public string TimeOutDisplay => TimeOut?.ToString("HH:mm") ?? "--:--";
    public string TotalHoursDisplay => TotalHours.HasValue ? $"{TotalHours:F1}" : "--";
    public string LateDisplay => LateMinutes > 0 ? $"{LateMinutes} د" : "--";
    public string EarlyLeaveDisplay => EarlyLeaveMinutes > 0 ? $"{EarlyLeaveMinutes} د" : "--";
    public string PenaltyDisplay => PenaltyHours > 0 ? $"{PenaltyHours:F1}" : "--";
    
    // Status Checks
    public bool IsPresent => Status == AttendanceStatus.Present || TimeIn.HasValue;
    public bool IsAbsent => Status == AttendanceStatus.Absent || (!TimeIn.HasValue && !TimeOut.HasValue);
    public bool IsLate => LateMinutes > 0;
    public bool HasEarlyLeave => EarlyLeaveMinutes > 0;
    public bool IsWeekend => LogDate.DayOfWeek == DayOfWeek.Friday || LogDate.DayOfWeek == DayOfWeek.Saturday;
}

// ═══════════════════════════════════════════════════════════
// DTO للفلترة
// ═══════════════════════════════════════════════════════════
public class AttendanceFilterDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int? EmployeeId { get; set; }
    public int? BiometricCode { get; set; }
    public string? Department { get; set; }
    public string? Status { get; set; }
    public bool? LateOnly { get; set; }
    public bool? AbsentOnly { get; set; }
    public string? SearchText { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string SortBy { get; set; } = "LogDate";
    public bool SortDescending { get; set; } = true;
}

// ═══════════════════════════════════════════════════════════
// DTO للتعديل
// ═══════════════════════════════════════════════════════════
public class AttendanceEditDto
{
    public int AttendanceId { get; set; }
    public int BiometricCode { get; set; }
    public DateTime LogDate { get; set; }
    public TimeOnly? TimeIn { get; set; }
    public TimeOnly? TimeOut { get; set; }
    public string? Status { get; set; }
    public string? EditReason { get; set; }
}

// ═══════════════════════════════════════════════════════════
// DTO للإحصائيات
// ═══════════════════════════════════════════════════════════
// ═══════════════════════════════════════════════════════════
// DTO للإحصائيات - مُحدّث
// ═══════════════════════════════════════════════════════════
public class AttendanceStatisticsDto
{
    // إحصائيات عامة
    public int TotalRecords { get; set; }           // إجمالي السجلات
    public int TotalEmployees { get; set; }         // عدد الموظفين
    public int TotalDays { get; set; }              // عدد الأيام الفريدة
    
    // إحصائيات الحضور
    public int PresentCount { get; set; }           // عدد سجلات الحضور
    public int AbsentCount { get; set; }            // عدد سجلات الغياب
    public int LateCount { get; set; }              // عدد سجلات التأخير
    public int EarlyLeaveCount { get; set; }        // عدد سجلات الخروج المبكر
    
    // إحصائيات الساعات
    public decimal TotalHours { get; set; }         // إجمالي الساعات
    public decimal AverageHours { get; set; }       // متوسط الساعات اليومي
    public decimal AverageHoursPerEmployee { get; set; } // متوسط الساعات لكل موظف
    
    // إحصائيات التأخير
    public int TotalLateMinutes { get; set; }       // إجمالي دقائق التأخير
    public decimal TotalPenaltyHours { get; set; }  // إجمالي ساعات الجزاء
    
    // النسب المئوية
    public decimal AttendanceRate { get; set; }     // نسبة الحضور %
    public decimal AbsenceRate { get; set; }        // نسبة الغياب %
    public decimal LateRate { get; set; }           // نسبة التأخير %
    
    // إحصائيات اليوم
    public int TodayPresent { get; set; }           // حاضرين اليوم
    public int TodayAbsent { get; set; }            // غائبين اليوم
    public int TodayLate { get; set; }              // متأخرين اليوم
}

// ═══════════════════════════════════════════════════════════
// DTO لتقرير الحضور الشهري
// ═══════════════════════════════════════════════════════════
public class AttendanceReportDto
{
    public int EmployeeId { get; set; }
    public int BiometricCode { get; set; }
    public string EmployeeName { get; set; } = "";
    public string? Department { get; set; }
    
    // إحصائيات الأيام
    public int WorkingDays { get; set; }
    public int PresentDays { get; set; }
    public int AbsentDays { get; set; }
    public int LateDays { get; set; }
    public int EarlyLeaveDays { get; set; }
    public int HolidayDays { get; set; }
    
    // إحصائيات الساعات
    public decimal TotalHours { get; set; }
    public decimal RequiredHours { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal ShortageHours { get; set; }
    
    // التأخير والجزاءات
    public int TotalLateMinutes { get; set; }
    public int TotalEarlyLeaveMinutes { get; set; }
    public decimal TotalPenaltyHours { get; set; }
    
    // النسب المئوية
    public decimal AttendancePercentage => WorkingDays > 0 ? Math.Round((decimal)PresentDays / WorkingDays * 100, 1) : 0;
    public decimal LatePercentage => PresentDays > 0 ? Math.Round((decimal)LateDays / PresentDays * 100, 1) : 0;
    
    // عرض
    public string TotalHoursDisplay => $"{TotalHours:F1} ساعة";
    public string TotalLateDisplay => $"{TotalLateMinutes / 60}:{TotalLateMinutes % 60:D2}";
}

// ═══════════════════════════════════════════════════════════
// DTO لفلتر التقرير
// ═══════════════════════════════════════════════════════════
public class AttendanceReportFilterDto
{
    public DateTime DateFrom { get; set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    public DateTime DateTo { get; set; } = DateTime.Today;
    public int? EmployeeId { get; set; }
    public string? Department { get; set; }
    public bool IncludeWeekends { get; set; } = false;
    public bool IncludeHolidays { get; set; } = true;
}

// ═══════════════════════════════════════════════════════════
// DTO لسجلات البصمة الخام
// ═══════════════════════════════════════════════════════════
public class BiometricLogDto
{
    public int BiometricLogId { get; set; }
    public int BiometricCode { get; set; }
    public string? EmployeeName { get; set; }
    public string? Department { get; set; }
    public DateTime LogDate { get; set; }
    public TimeOnly LogTime { get; set; }
    
    public string LogDateDisplay => LogDate.ToString("yyyy/MM/dd");
    public string LogTimeDisplay => LogTime.ToString("HH:mm:ss");
    public string LogDateTimeDisplay => $"{LogDateDisplay} {LogTimeDisplay}";
}

// ═══════════════════════════════════════════════════════════
// DTO لفلتر سجلات البصمة
// ═══════════════════════════════════════════════════════════
public class BiometricLogFilterDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int? BiometricCode { get; set; }
    public string? SearchText { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
// ═══════════════════════════════════════════════════════════
// DTO للـ Dashboard
// ═══════════════════════════════════════════════════════════
public class AttendanceDashboardDto
{
    public AttendanceStatisticsDto Statistics { get; set; } = new();
    public List<DailyAttendanceSummary> DailyTrend { get; set; } = new();
    public List<DepartmentAttendanceSummary> ByDepartment { get; set; } = new();
    public List<EmployeeAttendanceRank> TopEmployees { get; set; } = new();
    public List<EmployeeAttendanceRank> BottomEmployees { get; set; } = new();
    public List<RecentAttendanceLog> RecentLogs { get; set; } = new();
    public List<HourlyDistribution> TodayHourlyDistribution { get; set; } = new();
}

public class DailyAttendanceSummary
{
    public DateTime Date { get; set; }
    public string DayName { get; set; } = "";
    public int PresentCount { get; set; }
    public int AbsentCount { get; set; }
    public int LateCount { get; set; }
    public decimal AttendanceRate { get; set; }
}

public class DepartmentAttendanceSummary
{
    public string Department { get; set; } = "";
    public int EmployeeCount { get; set; }
    public int PresentCount { get; set; }
    public int AbsentCount { get; set; }
    public decimal AttendanceRate { get; set; }
    public decimal AverageHours { get; set; }
}

public class EmployeeAttendanceRank
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public string? Department { get; set; }
    public int PresentDays { get; set; }
    public int AbsentDays { get; set; }
    public int LateDays { get; set; }
    public decimal AttendanceRate { get; set; }
    public decimal TotalHours { get; set; }
}

public class RecentAttendanceLog
{
    public int BiometricCode { get; set; }
    public string EmployeeName { get; set; } = "";
    public string? Department { get; set; }
    public TimeOnly LogTime { get; set; }
    public string LogType { get; set; } = ""; // حضور / انصراف
    public bool IsLate { get; set; }
    public string LogTimeDisplay => LogTime.ToString("HH:mm");
}

public class HourlyDistribution
{
    public int Hour { get; set; }
    public int Count { get; set; }
    public string HourDisplay => $"{Hour:D2}:00";
}