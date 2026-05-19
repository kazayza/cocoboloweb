namespace COCOBOLOERPNEW.DTOs;

// ============================
// ثوابت أنواع الشيفتات
// ============================
public static class ShiftTypes
{
    public const string Morning = "صباحى";
    public const string Evening = "مسائى";
    public const string DailyWork = "عمل يومى";

    public static readonly Dictionary<string, string> Colors = new()
    {
        { Morning, "#4CAF50" },
        { Evening, "#FF9800" },
        { DailyWork, "#2196F3" }
    };

    public static readonly Dictionary<string, TimeSpan> DefaultStartTimes = new()
    {
        { Morning, new TimeSpan(8, 0, 0) },
        { Evening, new TimeSpan(16, 0, 0) },
        { DailyWork, new TimeSpan(9, 0, 0) }
    };

    public static readonly Dictionary<string, TimeSpan> DefaultEndTimes = new()
    {
        { Morning, new TimeSpan(16, 0, 0) },
        { Evening, new TimeSpan(0, 0, 0) },
        { DailyWork, new TimeSpan(17, 0, 0) }
    };

    public static string GetColor(string shiftType) =>
        Colors.GetValueOrDefault(shiftType, "#9E9E9E");

    public static string GetIcon(string shiftType) => shiftType switch
    {
        Morning => "WbSunny",
        Evening => "Nightlight",
        DailyWork => "Work",
        _ => "Schedule"
    };
}

// ============================
// DTO للعرض في القائمة
// ============================
public class EmployeeShiftListDto
{
    public int EmployeeShiftId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public string? Department { get; set; }
    public int? BiometricCode { get; set; }
    public string ShiftType { get; set; } = "";
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public string StartTimeDisplay => StartTime.ToString(@"hh\:mm");
    public string EndTimeDisplay => EndTime.ToString(@"hh\:mm");
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string EffectiveFromDisplay => EffectiveFrom.ToString("yyyy/MM/dd");
    public string? EffectiveToDisplay => EffectiveTo?.ToString("yyyy/MM/dd") ?? "مستمر";
    public string DurationDisplay
    {
        get
        {
            var start = StartTime.ToTimeSpan();
            var end = EndTime.ToTimeSpan();
            var duration = end > start ? end - start : TimeSpan.FromHours(24) - start + end;
            return $"{(int)duration.TotalHours} ساعة";
        }
    }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
    public bool IsActive => !EffectiveTo.HasValue || EffectiveTo.Value >= DateTime.Today;
}

// ============================
// DTO للفورم (إضافة / تعديل)
// ============================
public class EmployeeShiftFormDto
{
    public int EmployeeShiftId { get; set; }
    public int? EmployeeId { get; set; }
    public int? BiometricCode { get; set; }
    public string ShiftType { get; set; } = ShiftTypes.Morning;
    public TimeOnly StartTime { get; set; } = new(8, 0);
    public TimeOnly EndTime { get; set; } = new(16, 0);
    public DateTime EffectiveFrom { get; set; } = DateTime.Today;
    public DateTime? EffectiveTo { get; set; }
}

// ============================
// DTO لإضافة شيفت
// ============================
public class AddEmployeeShiftDto
{
    public int? EmployeeId { get; set; }
    public int? BiometricCode { get; set; }
    public string ShiftType { get; set; } = ShiftTypes.Morning;
    public TimeOnly StartTime { get; set; } = new(8, 0);
    public TimeOnly EndTime { get; set; } = new(16, 0);
    public DateTime EffectiveFrom { get; set; } = DateTime.Today;
    public DateTime? EffectiveTo { get; set; }
}

// ============================
// فلتر البحث
// ============================
public class EmployeeShiftFilterDto
{
    public string? SearchText { get; set; }
    public string? ShiftType { get; set; }
    public int? EmployeeId { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool? ActiveOnly { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string SortBy { get; set; } = "EffectiveFrom";
    public bool SortDescending { get; set; } = true;
}

// ============================
// DTO لبحث الموظفين (لـ Autocomplete)
// ============================
public class ShiftEmployeeLookupDto
{
    public int EmployeeId { get; set; }
    public string FullName { get; set; } = "";
    public string? Department { get; set; }
    public int? BiometricCode { get; set; }
    public string? NationalId { get; set; }
    public string DisplayText => $"{FullName} - {(string.IsNullOrEmpty(Department) ? "بدون قسم" : Department)}{(BiometricCode.HasValue ? $" (كود: {BiometricCode})" : "")}";
}

// ============================
// نتيجة الاستيراد من Excel
// ============================
public class ShiftImportResultDto
{
    public int TotalRows { get; set; }
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
public class ShiftStatisticsDto
{
    public int TotalCount { get; set; }
    public int ActiveCount { get; set; }
    public int MorningCount { get; set; }
    public int EveningCount { get; set; }
    public int DailyCount { get; set; }
}