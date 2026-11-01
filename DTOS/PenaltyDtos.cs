namespace COCOBOLOERPNEW.DTOs;

// ═══════════════════════════════════════════════════════════
// DTOs نظام جزاءات الموظفين (EmployeePenalties)
// مرآة وحدة الـ API (modules/employee-penalties) في القواعد
// والأرقام والرسائل — تعريف واحد للنظام كله.
// PenaltyType: "Days" | "Amount"
// Status: Pending | Approved | Rejected | Cancelled | Applied
// ═══════════════════════════════════════════════════════════

public static class PenaltyStatuses
{
    public const string Pending   = "Pending";
    public const string Approved  = "Approved";
    public const string Rejected  = "Rejected";
    public const string Cancelled = "Cancelled";
    public const string Applied   = "Applied";
}

public static class PenaltyTypes
{
    public const string Days   = "Days";
    public const string Amount = "Amount";
}

// ── صف في قائمة الجزاءات ───────────────────────────────────
public class PenaltyListDto
{
    public int       Id              { get; set; }
    public int       EmployeeId      { get; set; }
    public string    EmployeeName    { get; set; } = "";
    public string?   Department      { get; set; }
    public string    PenaltyType     { get; set; } = PenaltyTypes.Days;
    public decimal?  DaysCount       { get; set; }
    public decimal?  FixedAmount     { get; set; }
    public decimal?  ComputedAmount  { get; set; }
    public string    TargetMonth     { get; set; } = "";
    public string    Reason          { get; set; } = "";
    public string    Status          { get; set; } = PenaltyStatuses.Pending;
    public int       CreatedByUserId { get; set; }
    public string?   CreatedByName   { get; set; }
    public int?      ApprovedByUserId{ get; set; }
    public string?   ApprovedByName  { get; set; }
    public DateTime? CreatedAt       { get; set; }
    public DateTime? ApprovedAt      { get; set; }
    public int?      AppliedPayrollId{ get; set; }

    // قدرات الفاعل الحالي على هذا الصف (تحسبها الخدمة — الخادم هو الحاكم)
    public bool CanApprove { get; set; }
    public bool CanCancel  { get; set; }

    public bool IsPendingStatus => Status == PenaltyStatuses.Pending;

    public string TypeAr => PenaltyType == PenaltyTypes.Days ? "أيام" : "مبلغ مقطوع";

    public string StatusAr => Status switch
    {
        PenaltyStatuses.Pending   => "بانتظار الاعتماد",
        PenaltyStatuses.Approved  => "معتمدة",
        PenaltyStatuses.Rejected  => "مرفوضة",
        PenaltyStatuses.Cancelled => "ملغية",
        PenaltyStatuses.Applied   => "مطبقة",
        _ => Status
    };

    public string ValueDisplay => PenaltyType == PenaltyTypes.Days
        ? $"{DaysCount:0.##} يوم"
        : $"{FixedAmount:N2} ج.م";
}

// ── فلاتر القائمة ──────────────────────────────────────────
public class PenaltyFilterDto
{
    public string? Status     { get; set; }
    public string? SearchText { get; set; }   // اسم الموظف
    public string? Month      { get; set; }   // TargetMonth بصيغة YYYY-MM
    public int PageNumber { get; set; } = 1;
    public int PageSize   { get; set; } = 25;
}

// ── إحصائيات (نطاق رؤية الفاعل + فلاتر البحث/الشهر) ────────
public class PenaltyStatsDto
{
    public int TotalCount     { get; set; }
    public int PendingCount   { get; set; }
    public int ApprovedCount  { get; set; }
    public int RejectedCount  { get; set; }
    public int CancelledCount { get; set; }
    public int AppliedCount   { get; set; }
}

// ── إنشاء جزاء ─────────────────────────────────────────────
public class PenaltyCreateDto
{
    public int      EmployeeId  { get; set; }
    public string   PenaltyType { get; set; } = PenaltyTypes.Days;
    public decimal? DaysCount   { get; set; }
    public decimal? FixedAmount { get; set; }
    public string   Reason      { get; set; } = "";
    public string   TargetMonth { get; set; } = "";   // YYYY-MM
}

// ── معاينة حية (نموذج الإنشاء) ─────────────────────────────
public class PenaltyPreviewDto
{
    public int      WorkingDays    { get; set; }
    public decimal  DayValue       { get; set; }   // قيمة اليوم معروضة (مقربة منزلتين)
    public decimal  ComputedAmount { get; set; }   // المبلغ عند الشهر النهائي
    public string   FinalMonth     { get; set; } = "";
    public string?  RolledFrom     { get; set; }   // الشهر المطلوب إن تجاوزه التجاوز
    public bool     WillBeApproved { get; set; }   // الفاعل صاحب توقيع مباشر على الموظف
}

// ── نتيجة عملية (إنشاء/اعتماد/رفض/إلغاء) ───────────────────
public class PenaltyMutationResult
{
    public bool    Success    { get; set; }
    public string  Message    { get; set; } = "";
    public int?    PenaltyId  { get; set; }
    public string? Status     { get; set; }
    public string? FinalMonth { get; set; }
    public string? RolledFrom { get; set; }

    public static PenaltyMutationResult Fail(string message) => new() { Success = false, Message = message };
    public static PenaltyMutationResult Ok(string message)   => new() { Success = true,  Message = message };
}

// ── سياق الفاعل الحالي (مرآة req.auth في الـ API) ──────────
public class PenaltyActorDto
{
    public int     UserId             { get; set; }
    public string  Username           { get; set; } = "";
    public string? Role               { get; set; }
    public int?    EmployeeId         { get; set; }   // الموظف المرتبط بالحساب (Users.employeeID)
    public bool    HasDirectSignClaim { get; set; }   // صف frm_EmployeePenaltiesDirectSign بصلاحية View
    public bool    CanCreateByRole    { get; set; }   // Admin/GM/AccountManager/ProductionManager
    public bool    CanViewAll         { get; set; }   // أدوار الإنشاء + HrManager
    public bool    IsAnyDirectManager { get; set; }   // مدير مباشر لموظف واحد على الأقل

    public bool CanCreate     => CanCreateByRole || IsAnyDirectManager;
    public bool CanAccessPage => CanViewAll || HasDirectSignClaim || CanCreateByRole || EmployeeId != null;
}
