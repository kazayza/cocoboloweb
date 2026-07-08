namespace COCOBOLOERPNEW.DTOs;

// ═══════════════════════════════════════════════════════════
// DTOs نظام الاستثناءات - محدّث بالأعمدة الجديدة
// DailyExemptions: ExemptionID, BioEmployeeID, EmployeeID,
//   ExemptionDate, ReasonCode, Description, ApprovedBy,
//   CreatedDate, IsFullDay, Hours, IsDeducted, Notes,
//   CreatedBy, Status
// ═══════════════════════════════════════════════════════════

// ── قائمة الاستثناءات ────────────────────────────────────
public class ExemptionListDto
{
    public int       ExemptionId    { get; set; }
    public int       BioEmployeeId  { get; set; }
    public int?      EmployeeId     { get; set; }
    public string    EmployeeName   { get; set; } = "";
    public string?   Department     { get; set; }
    public DateTime  ExemptionDate  { get; set; }
    public string    ReasonCode     { get; set; } = "";
    public string?   Description    { get; set; }
    public string?   ApprovedBy     { get; set; }
    public DateTime? CreatedDate    { get; set; }

    // ── أعمدة جديدة ──
    public bool      IsFullDay      { get; set; } = true;
    public decimal?  Hours          { get; set; }
    public bool      IsDeducted     { get; set; } = true;
    public string?   Notes          { get; set; }
    public string?   CreatedBy      { get; set; }
    public string    Status         { get; set; } = ExemptionStatus.Approved;

    // Display
    public string  DateDisplay      => ExemptionDate.ToString("yyyy/MM/dd");
    public string  DayName          => ExemptionDate.ToString("dddd", new System.Globalization.CultureInfo("ar-EG"));
    public string  StatusAr         => ExemptionStatus.GetArabic(Status);
    public string  StatusColor      => ExemptionStatus.GetColor(Status);
    public string  ReasonColor      => ExemptionReasonCodes.GetColor(ReasonCode);
    public bool    IsApprovedStatus => ExemptionStatus.IsApproved(Status);
    public bool    IsPendingStatus  => ExemptionStatus.IsPending(Status);
    public bool    IsRejectedStatus => ExemptionStatus.IsRejected(Status);
    public string  DeductionDisplay => IsDeducted ? "يُخصم" : "لا يُخصم";
    public string  DeductionColor   => IsDeducted ? "#EF4444" : "#10B981";
    public string  DurationDisplay  => IsFullDay ? "يوم كامل" : $"{Hours:F1} ساعة";
}

// ── إنشاء استثناء ────────────────────────────────────────
public class ExemptionCreateDto
{
    public int       EmployeeId     { get; set; }
    public int? BioEmployeeId { get; set; }
    public DateTime  ExemptionDate  { get; set; }
    public string    ReasonCode     { get; set; } = "";  // نص حر
    public string?   Description    { get; set; }
    public bool      IsFullDay      { get; set; } = true;
    public decimal?  Hours          { get; set; }         // لو IsFullDay = false
    public bool?     IsDeducted     { get; set; }         // null = يتعمل DefaultIsDeducted
    public string?   Notes          { get; set; }
}

// ── اعتماد/رفض استثناء ───────────────────────────────────
public class ExemptionApprovalDto
{
    public int       ExemptionId    { get; set; }
    public string    Action         { get; set; } = "";   // "Approve" or "Reject"
    public string?   RejectionReason { get; set; }        // سبب الرفض (اختياري)
}

// ── فلتر الاستثناءات ─────────────────────────────────────
public class ExemptionFilterDto
{
    public DateTime? DateFrom       { get; set; }
    public DateTime? DateTo         { get; set; }
    public int?      EmployeeId     { get; set; }
    public string?   Department     { get; set; }
    public string?   ReasonCode     { get; set; }
    public string?   Status         { get; set; }         // Pending, Approved, Rejected, أو null = الكل
    public bool?     IsDeducted     { get; set; }         // فلتر الخصم
    public string?   SearchText     { get; set; }
    public int       PageNumber     { get; set; } = 1;
    public int       PageSize       { get; set; } = 25;

    /// <summary>
    /// للتوافق مع الكود القديم - IsApproved true=معتمد, false=قيد الاعتماد
    /// بيتم تحويلها لـ Status في الـ Service
    /// </summary>
    public bool?     IsApproved     { get; set; }
}

// ── إحصائيات الاستثناءات ─────────────────────────────────
public class ExemptionStatsDto
{
    public int TotalCount       { get; set; }
    public int ApprovedCount    { get; set; }
    public int PendingCount     { get; set; }
    public int RejectedCount    { get; set; }
    public int ErrandCount      { get; set; }   // مأمورية/مهمة رسمية
    public int PermissionCount  { get; set; }   // إذن
    public int EarlyLeaveCount  { get; set; }   // انصراف مبكر
    public int DeductedCount    { get; set; }   // يُخصم
    public int NotDeductedCount { get; set; }   // لا يُخصم
}
