using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.DTOs
{
    // ═══════════════════════════════════════════
    // قائمة الموظفين
    // ═══════════════════════════════════════════
    public class EmployeeListDto
    {
        public int EmployeeId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? JobTitle { get; set; }
        public string? Department { get; set; }
        public string NationalId { get; set; } = string.Empty;
        public string? Gender { get; set; }
        public string? MobilePhone { get; set; }
        public string? EmailAddress { get; set; }
        public DateTime? HireDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal? CurrentSalaryBase { get; set; }
        public string Status { get; set; } = "نشط";
        public int? BioEmployeeId { get; set; }
        public bool? IsPermanentlyExempt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? CreatedAt { get; set; }
        // Computed
        public string StatusColor => Status switch
        {
            "نشط" => "Success",
            "موقوف" => "Warning",
            "مستقيل" => "Error",
            "بالإجازة" => "Info",
            _ => "Default"
        };
        public string StatusIcon => Status switch
        {
            "نشط" => "CheckCircle",
            "موقوف" => "PauseCircle",
            "مستقيل" => "Cancel",
            "بالإجازة" => "BeachAccess",
            _ => "Person"
        };
    }

    // ═══════════════════════════════════════════
    // نموذج إضافة/تعديل موظف
    // ═══════════════════════════════════════════
    public class EmployeeFormDto
    {
        public int EmployeeId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? JobTitle { get; set; }
        public string? Department { get; set; }
        public string NationalId { get; set; } = string.Empty;
        public string? Gender { get; set; }
        public DateTime? BirthDate { get; set; }
        public string? Qualification { get; set; }
        public string? YearQualification { get; set; }
        public string? Address { get; set; }
        public string? MobilePhone { get; set; }
        public string? MobilePhone2 { get; set; }
        public string? EmailAddress { get; set; }
        public DateTime? HireDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int? BioEmployeeId { get; set; }
        public bool? IsPermanentlyExempt { get; set; }
        public decimal? CurrentSalaryBase { get; set; }
        public string Status { get; set; } = "نشط";
        public string? Notes { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? CreatedAt { get; set; }
        // Salary change tracking
        public bool SalaryChanged { get; set; }
        public decimal? OldSalary { get; set; }
        public string? SalaryChangeReason { get; set; }
    }

    // ═══════════════════════════════════════════
    // تفاصيل الموظف
    // ═══════════════════════════════════════════
    public class EmployeeDetailDto
    {
        public int EmployeeId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? JobTitle { get; set; }
        public string? Department { get; set; }
        public string NationalId { get; set; } = string.Empty;
        public string? Gender { get; set; }
        public DateTime? BirthDate { get; set; }
        public string? Qualification { get; set; }
        public string? YearQualification { get; set; }
        public string? Address { get; set; }
        public string? MobilePhone { get; set; }
        public string? MobilePhone2 { get; set; }
        public string? EmailAddress { get; set; }
        public DateTime? HireDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int? BioEmployeeId { get; set; }
        public bool? IsPermanentlyExempt { get; set; }
        public decimal? CurrentSalaryBase { get; set; }
        public string Status { get; set; } = "نشط";
        public string? Notes { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? CreatedAt { get; set; }
        public List<SalaryHistoryDto> SalaryHistory { get; set; } = new();
        public EmployeeStatsDto Stats { get; set; } = new();
    }

    // ═══════════════════════════════════════════
    // تاريخ المرتبات
    // ═══════════════════════════════════════════
    public class SalaryHistoryDto
    {
        public int SalaryHistoryId { get; set; }
        public int EmployeeId { get; set; }
        public decimal? OldSalary { get; set; }
        public decimal NewSalary { get; set; }
        public DateOnly ChangeDate { get; set; }
        public string? Reason { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    // ═══════════════════════════════════════════
    // فلتر البحث
    // ═══════════════════════════════════════════
    public class EmployeeFilterDto
    {
        public string? SearchTerm { get; set; }
        public string? Department { get; set; }
        public string? Status { get; set; }
        public string? Gender { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    // ═══════════════════════════════════════════
    // إحصائيات الموظفين
    // ═══════════════════════════════════════════
    public class EmployeeStatsDto
    {
        public int TotalEmployees { get; set; }
        public int ActiveCount { get; set; }
        public int SuspendedCount { get; set; }
        public int ResignedCount { get; set; }
        public int OnLeaveCount { get; set; }
        public decimal TotalSalaries { get; set; }
        public decimal AverageSalary { get; set; }
        public int DepartmentCount { get; set; }
    }

    // ═══════════════════════════════════════════
    // نتيجة العمليات
    // ═══════════════════════════════════════════
    public class EmployeeResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public EmployeeDetailDto? Data { get; set; }
        public EmployeeStatsDto? Stats { get; set; }
    }

    // ═══════════════════════════════════════════
    // ثوابت حالة الموظف
    // ═══════════════════════════════════════════
    public static class EmployeeStatuses
    {
        public const string Active = "نشط";
        public const string Suspended = "موقوف";
        public const string Resigned = "مستقيل";
        public const string OnLeave = "بالإجازة";
    }

    // ═══════════════════════════════════════════
    // الأقسام (للفلتر)
    // ═══════════════════════════════════════════
    public static class EmployeeDepartments
    {
        public const string Sales = "المبيعات";
        public const string Accounting = "الحسابات";
        public const string HR = "الموارد البشرية";
        public const string IT = "تكنولوجيا المعلومات";
        public const string Admin = "الإدارة";
        public const string Warehouse = "المخازن";
        public const string Operations = "العمليات";
        public const string CustomerService = "خدمة العملاء";
    }
}