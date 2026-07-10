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
    public const string Permission = "إذن";           // إذن شخصي
    public const string Errand = "مأمورية";           // مأمورية شغل
    public const string OffDay = "راحة";              // راحة أسبوعية من الشفت

    public static string GetColor(string? status) => status switch
    {
        Present => "#4CAF50",
        Absent => "#F44336",
        Late => "#FF9800",
        EarlyLeave => "#FF5722",
        Holiday => "#2196F3",
        Weekend => "#9E9E9E",
        Permission => "#9C27B0",
        Errand => "#00BCD4",
        OffDay => "#78909C",
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
        Permission => "EventNote",
        Errand => "WorkOutline",
        OffDay => "Hotel",
        _ => "Help"
    };
}

// ═══════════════════════════════════════════════════════════
// أنواع الاستثناءات - ReasonCode نص حر
// الكلمات المفتاحية بتحدد المنطق (خصم ولا لا)
// ═══════════════════════════════════════════════════════════
public static class ExemptionReasonCodes
{
    // كلمات مفتاحية لتحديد المنطق
    public const string Permission = "إذن";              // إذن شخصي
    public const string Errand = "مأمورية";              // مأمورية شغل  
    public const string EarlyLeave = "انصراف مبكر";     // انصراف مبكر

    /// <summary>
    /// أي قيم ReasonCode متعارف عليها (للعرض في الـ dropdown كمقترحات بس)
    /// </summary>
    public static readonly string[] Suggestions = { "إذن", "مأمورية", "مهمة رسمية", "انصراف مبكر" };

    /// <summary>
    /// هل السبب يعني مأمورية/شغل شركة؟ → مفيش خصم
    /// </summary>
    public static bool IsErrandType(string? reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode)) return false;
        var lower = reasonCode.Trim();
        return lower.Contains("مأمورية") || lower.Contains("مهمة رسمية") ||
               lower.Contains("مهمة") || lower.Contains("عمل");
    }

    /// <summary>
    /// هل السبب يعني انصراف مبكر؟ → مفيش خصم (تسجيل بس)
    /// </summary>
    public static bool IsEarlyLeaveType(string? reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode)) return false;
        return reasonCode.Trim().Contains("انصراف مبكر") || reasonCode.Trim().Contains("خروج مبكر");
    }

    /// <summary>
    /// هل السبب يعني إذن شخصي؟ → يُخصم افتراضياً
    /// </summary>
    public static bool IsPermissionType(string? reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode)) return false;
        return reasonCode.Trim().Contains("إذن") || reasonCode.Trim().Contains("اذن");
    }

    /// <summary>
    /// Default: يُخصم ولا لا بناءً على نوع السبب
    /// مأمورية/مهمة رسمية → لا يُخصم
    /// انصراف مبكر → لا يُخصم (تسجيل)
    /// إذن → يُخصم
    /// أي حاجة تانية → يُخصم
    /// </summary>
    public static bool DefaultIsDeducted(string? reasonCode)
    {
        if (IsErrandType(reasonCode)) return false;
        if (IsEarlyLeaveType(reasonCode)) return false;
        return true; // إذن أو أي حاجة تانية → يُخصم
    }

    /// <summary>
    /// لون العرض
    /// </summary>
    public static string GetColor(string? code)
    {
        if (IsErrandType(code)) return "#00BCD4";
        if (IsEarlyLeaveType(code)) return "#FF9800";
        if (IsPermissionType(code)) return "#9C27B0";
        return "#757575";
    }

    /// <summary>
    /// أيقونة العرض
    /// </summary>
    public static string GetIcon(string? code)
    {
        if (IsErrandType(code)) return "WorkOutline";
        if (IsEarlyLeaveType(code)) return "ExitToApp";
        if (IsPermissionType(code)) return "EventNote";
        return "Help";
    }
}

// ═══════════════════════════════════════════════════════════
// حالات الاستثناء
// الآن بنستخدم عمود Status بشكل صريح: Pending, Approved, Rejected
// ═══════════════════════════════════════════════════════════
public static class ExemptionStatus
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";

    /// <summary>
    /// الحالة بالعربي
    /// </summary>
    public static string GetArabic(string? status) => status switch
    {
        Pending => "قيد الاعتماد",
        Approved => "معتمد",
        Rejected => "مرفوض",
        _ => "غير معروف"
    };

    /// <summary>
    /// لون الحالة
    /// </summary>
    public static string GetColor(string? status) => status switch
    {
        Pending => "#F59E0B",
        Approved => "#10B981",
        Rejected => "#EF4444",
        _ => "#757575"
    };

    /// <summary>
    /// لون MudBlazor كـ string (للـ UI)
    /// </summary>
    public static string GetMudColorName(string? status) => status switch
    {
        Pending => "Warning",
        Approved => "Success",
        Rejected => "Error",
        _ => "Default"
    };

    public static bool IsApproved(string? status) => status == Approved;
    public static bool IsPending(string? status) => status == Pending;
    public static bool IsRejected(string? status) => status == Rejected;

    /// <summary>
    /// التوافق مع البيانات القديمة: لو ApprovedBy موجود = معتمد
    /// </summary>
    public static string InferStatus(string? status, string? approvedBy)
    {
        if (!string.IsNullOrWhiteSpace(status) && status != "Approved")
            return status;
        // لو مفيش Status صريح، نستنتج من ApprovedBy
        return string.IsNullOrWhiteSpace(approvedBy) ? Pending : Approved;
    }
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
    public bool IsPresent => TimeIn.HasValue || Status == AttendanceStatus.Present || Status == AttendanceStatus.Late || Status == AttendanceStatus.Errand || Status == AttendanceStatus.EarlyLeave;
    public bool IsAbsent => Status == AttendanceStatus.Absent;
    public bool IsLate => Status == AttendanceStatus.Late || (LateMinutes ?? 0) > 0;
    public bool HasEarlyLeave => Status == AttendanceStatus.EarlyLeave || (EarlyLeaveMinutes ?? 0) > 0;
    public bool IsHolidayStatus => Status == AttendanceStatus.Holiday;
    public bool IsOffDayStatus => Status == AttendanceStatus.OffDay || Status == AttendanceStatus.Weekend;
    public bool IsPermissionStatus => Status == AttendanceStatus.Permission;
    public bool IsErrandStatus => Status == AttendanceStatus.Errand;
    public bool IsWeekend => LogDate.DayOfWeek == DayOfWeek.Friday || LogDate.DayOfWeek == DayOfWeek.Saturday;
    public bool IsWorkingDayRecord => !IsHolidayStatus && !IsOffDayStatus;
    public string StatusDisplay => Status switch
    {
        AttendanceStatus.Absent => "غائب",
        AttendanceStatus.Late => "متأخر",
        AttendanceStatus.Holiday => "إجازة رسمية",
        AttendanceStatus.OffDay => "راحة",
        AttendanceStatus.Weekend => "عطلة أسبوعية",
        AttendanceStatus.Permission => "إذن",
        AttendanceStatus.Errand => "مأمورية",
        AttendanceStatus.EarlyLeave => "انصراف مبكر",
        AttendanceStatus.Present => "حاضر",
        _ when IsPresent => "حاضر",
        _ => Status ?? "غير محدد"
    };
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
// الحضور اليدوي اليومي
// ═══════════════════════════════════════════════════════════
public class DailyManualAttendanceDto
{
    public int? AttendanceId { get; set; }
    public int EmployeeId { get; set; }
    public int BiometricCode { get; set; }
    public string EmployeeName { get; set; } = "";
    public string? Department { get; set; }
    public DateTime LogDate { get; set; } = DateTime.Today;
    public TimeOnly? TimeIn { get; set; }
    public TimeOnly? TimeOut { get; set; }
    public string Status { get; set; } = AttendanceStatus.Present;
    public string? EditReason { get; set; }
    public bool HasExistingRecord { get; set; }
    public decimal? TotalHours { get; set; }
    public int? LateMinutes { get; set; }
    public int? EarlyLeaveMinutes { get; set; }
}

// ═══════════════════════════════════════════════════════════
// استيراد البصمة
// ═══════════════════════════════════════════════════════════
public class BiometricImportOptionsDto
{
    public int? SpecificBiometricCode { get; set; }
    public bool ProcessAfterImport { get; set; } = true;
    public bool GenerateDailyCoverage { get; set; } = false;
}

public class BiometricImportResultDto
{
    public int TotalRows { get; set; }
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public int ErrorCount { get; set; }
    public List<DateTime> ImportedDates { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
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