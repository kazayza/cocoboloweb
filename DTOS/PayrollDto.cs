namespace COCOBOLOERPNEW.DTOs;

// ============================================================
// DTOs نظام المرتبات - النسخة النهائية
// ============================================================

// ── قائمة المرتبات ───────────────────────────────────────────
public class PayrollListDto
{
    public int       PayrollID          { get; set; }
    public int?      PayrollRunId       { get; set; }
    public int       EmployeeID         { get; set; }
    public string    EmployeeName       { get; set; } = "";
    public string?   Department         { get; set; }
    public string?   JobTitle           { get; set; }
    public string    PayrollMonth       { get; set; } = "";

    // المستحقات
    public decimal   BasicSalary        { get; set; }
    public decimal   BonusInPayroll     { get; set; }   // مكافآت داخل الراتب
    public decimal   GrossSalary        => BasicSalary + BonusInPayroll;

    // الخصومات
    public decimal   AbsenceDeduction   { get; set; }
    public decimal   LateDeduction      { get; set; }
    public decimal   LoanDeduction      { get; set; }
    public decimal   PenaltyDeduction   { get; set; }
    public decimal   TotalDeductions    => AbsenceDeduction + LateDeduction + LoanDeduction + PenaltyDeduction;

    // الصافي (نفس الـ Computed Column في DB)
    public decimal   NetSalary          { get; set; }

    // الحضور
    public int       WorkingDaysInMonth { get; set; }
    public int       PresentDays        { get; set; }
    public int       AbsenceDays        { get; set; }
    public int       LateMinutesTotal   { get; set; }
    public bool      IsManualAttendance { get; set; }
    public string    AttendanceSource   => IsManualAttendance ? "يدوي" : "بصمة";
    public string    LateDisplay        =>
        LateMinutesTotal > 0 ? $"{LateMinutesTotal / 60}س {LateMinutesTotal % 60}د" : "—";

    // الحالة
    public string    PaymentStatus      { get; set; } = PayrollPaymentStatuses.Unpaid;
    public string    StatusColor        => PaymentStatus switch
    {
        PayrollPaymentStatuses.Paid          => "#10b981",
        PayrollPaymentStatuses.PendingReview => "#f59e0b",
        PayrollPaymentStatuses.Approved      => "#3b82f6",
        PayrollPaymentStatuses.Rejected      => "#ef4444",
        PayrollPaymentStatuses.Unpaid        => "#94a3b8",
        PayrollPaymentStatuses.Cancelled     => "#64748b",
        _                                    => "#94a3b8"
    };
    public DateTime? PaymentDate        { get; set; }
    public string?   CreatedBy          { get; set; }
    public DateTime? CreatedAt          { get; set; }
}

// ── حساب راتب موظف في شاشة المعالجة ─────────────────────────
public class PayrollCalculationDto
{
    public int      EmployeeID          { get; set; }
    public string   EmployeeName        { get; set; } = "";
    public string?  Department          { get; set; }
    public decimal  BasicSalary         { get; set; }

    // الحضور
    public int      WorkingDaysInMonth  { get; set; }
    public int      PresentDays         { get; set; }
    public int      AbsenceDays         { get; set; }
    public int      LateMinutesTotal    { get; set; }
    public bool     IsManualAttendance  { get; set; }
    public string   LateDisplay         =>
        LateMinutesTotal > 0 ? $"{LateMinutesTotal / 60}س {LateMinutesTotal % 60}د" : "لا يوجد";

    // خصومات تلقائية (محسوبة من الكود)
    public decimal  AutoAbsenceDeduction { get; set; }
    public decimal  AutoLateDeduction    { get; set; }
    public decimal  AbsenceDeduction     { get; set; }
    public decimal  LateDeduction        { get; set; }
    public decimal  LoanDeduction        { get; set; }
    public string?  AbsenceOverrideReason { get; set; }
    public string?  LateOverrideReason    { get; set; }
    public bool     HasAbsenceOverride    => AbsenceDeduction != AutoAbsenceDeduction;
    public bool     HasLateOverride       => LateDeduction != AutoLateDeduction;

    // بنود السلف التفصيلية (للعرض)
    public List<PayrollDetailDto> LoanItems   { get; set; } = new();

    // بنود يضيفها المستخدم يدوياً في الشاشة
    public List<PayrollDetailDto> Penalties   { get; set; } = new();  // جزاءات
    public List<PayrollDetailDto> BonusItems  { get; set; } = new();  // مكافآت وعمولات داخل الراتب

    // إجماليات محسوبة
    public decimal TotalBonusInPayroll  => BonusItems.Where(b => b.PaymentType == "InPayroll").Sum(b => b.Amount);
    public decimal TotalPenalties       => Penalties.Sum(p => p.Amount);
    public decimal TotalDeductions      => AbsenceDeduction + LateDeduction + LoanDeduction + TotalPenalties;
    public decimal GrossSalary          => BasicSalary + TotalBonusInPayroll;
    public decimal NetSalary            => GrossSalary - TotalDeductions;
    public bool HasManualOverrides      => HasAbsenceOverride || HasLateOverride;

    // مكافآت خارج الراتب (منفصلة)
    public List<PayrollDetailDto> SeparateBonuses { get; set; } = new();
    public decimal TotalSeparateBonuses => SeparateBonuses.Sum(b => b.Amount);

    // حالة
    public bool     IsSelected          { get; set; } = true;
    public bool     HasExistingPayroll  { get; set; }
    public int?     ExistingPayrollID   { get; set; }
    public string?  Warning             { get; set; }
}

// ── بند تفصيلي ──────────────────────────────────────────────
public class PayrollDetailDto
{
    public int      DetailID     { get; set; }

    // النوع
    public string   DetailType   { get; set; } = "";
    public string   DetailTypeAr => DetailType switch
    {
        "AbsenceDeduction"   => "خصم غياب",
        "LateDeduction"      => "خصم تأخير",
        "LoanDeduction"      => "خصم سلفة",
        "Penalty"            => "جزاء",
        "ManualDeduction"    => "خصم يدوي",
        "Bonus"              => "مكافأة",
        "Commission"         => "عمولة",
        "BonusSeparate"      => "مكافأة (منفصلة)",
        "CommissionSeparate" => "عمولة (منفصلة)",
        _                    => DetailType
    };

    public string   Description  { get; set; } = "";
    public decimal  Amount       { get; set; }
    public bool     IsDeduction  { get; set; }

    // InPayroll = في الراتب | Separate = منفصل
    public string   PaymentType  { get; set; } = "InPayroll";
    public bool     IsSeparate   => PaymentType == "Separate";

    public string?  Notes        { get; set; }

    // للعرض
    public string AmountDisplay  => IsDeduction
        ? $"- {Amount:N2} جـ"
        : $"+ {Amount:N2} جـ";
    public string AmountColor    => IsDeduction ? "#ef4444" : "#10b981";
}

// ── قسيمة الراتب ─────────────────────────────────────────────
public class PayslipDto
{
    // بيانات الموظف
    public int       EmployeeID         { get; set; }
    public string    EmployeeName       { get; set; } = "";
    public string?   Department         { get; set; }
    public string?   JobTitle           { get; set; }
    public string?   NationalId         { get; set; }
    public DateTime? HireDate           { get; set; }

    // الشهر
    public string    PayrollMonth       { get; set; } = "";
    public string    MonthDisplay       =>
        DateTime.TryParseExact(PayrollMonth + "-01", "yyyy-MM-dd",
            null, System.Globalization.DateTimeStyles.None, out var d)
            ? d.ToString("MMMM yyyy", new System.Globalization.CultureInfo("ar-EG"))
            : PayrollMonth;

    // المستحقات
    public decimal   BasicSalary        { get; set; }
    public List<PayrollDetailDto> BonusItems    { get; set; } = new();
    public decimal   TotalBonusInPayroll => BonusItems.Sum(b => b.Amount);
    public decimal   GrossSalary        => BasicSalary + TotalBonusInPayroll;

    // الحضور
    public int       WorkingDaysInMonth { get; set; }
    public int       PresentDays        { get; set; }
    public int       AbsenceDays        { get; set; }
    public int       LateMinutesTotal   { get; set; }
    public bool      IsManualAttendance { get; set; }
    public string    AttendanceSource   => IsManualAttendance ? "يدوي" : "بصمة";
    public string    LateDisplay        =>
        LateMinutesTotal > 0 ? $"{LateMinutesTotal / 60}س {LateMinutesTotal % 60}د" : "لا يوجد";

    // الخصومات
    public decimal   AbsenceDeduction   { get; set; }
    public decimal   LateDeduction      { get; set; }
    public decimal   LoanDeduction      { get; set; }
    public List<PayrollDetailDto> Penalties     { get; set; } = new();
    public List<PayrollDetailDto> LoanItems     { get; set; } = new();
    public decimal   TotalPenalties     => Penalties.Sum(p => p.Amount);
    public decimal   TotalDeductions    =>
        AbsenceDeduction + LateDeduction + LoanDeduction + TotalPenalties;

    // الصافي
    public decimal   NetSalary          { get; set; }

    // مكافآت منفصلة (خارج الراتب)
    public List<PayrollDetailDto> SeparateBonuses { get; set; } = new();

    // الصرف
    public string    PaymentStatus      { get; set; } = "غير مدفوع";
    public DateTime? PaymentDate        { get; set; }
    public string?   CashBoxName        { get; set; }
    public string?   Notes              { get; set; }
}

// ── جلسة الصرف ───────────────────────────────────────────────
public class PayrollRunDto
{
    public int       RunID           { get; set; }
    public string    PayrollMonth    { get; set; } = "";
    public string    MonthDisplay    =>
        DateTime.TryParseExact(PayrollMonth + "-01", "yyyy-MM-dd",
            null, System.Globalization.DateTimeStyles.None, out var d)
            ? d.ToString("MMMM yyyy", new System.Globalization.CultureInfo("ar-EG"))
            : PayrollMonth;
    public string    Status          { get; set; } = "Draft";
    public string    StatusAr        => Status switch
    {
        "Draft"         => "مسودة",
        "PendingReview" => "قيد المراجعة",
        "Approved"      => "معتمد",
        "Rejected"      => "مرفوض",
        "Completed"     => "مكتمل",
        "Cancelled"     => "ملغي",
        _                => Status
    };
    public string    StatusColor     => Status switch
    {
        "Draft"         => "#f59e0b",
        "PendingReview" => "#f59e0b",
        "Approved"      => "#3b82f6",
        "Rejected"      => "#ef4444",
        "Completed"     => "#10b981",
        "Cancelled"     => "#64748b",
        _                => "#94a3b8"
    };
    public int       TotalEmployees  { get; set; }
    public decimal   TotalGross      { get; set; }
    public decimal   TotalDeductions { get; set; }
    public decimal   TotalNet        { get; set; }
    public string?   CashBoxName     { get; set; }
    public string?   ProcessedBy     { get; set; }
    public DateTime? ProcessedAt     { get; set; }
    public DateTime  CreatedAt       { get; set; }
}

// ── إحصائيات ─────────────────────────────────────────────────
public class PayrollStatsDto
{
    public decimal TotalNetThisMonth    { get; set; }
    public decimal TotalGrossThisMonth  { get; set; }
    public decimal TotalLoanDeductions  { get; set; }
    public decimal TotalPenalties       { get; set; }
    public decimal TotalSeparateBonuses { get; set; }
    public int     PaidCount            { get; set; }
    public int     PendingCount         { get; set; }
    public int     ReviewCount          { get; set; }
    public int     ApprovedCount        { get; set; }
    public decimal AverageNetSalary     { get; set; }
}

public class OffPayrollPaymentFormDto
{
    public int      PayrollID     { get; set; }
    public int?     EmployeeID    { get; set; }
    public string   EmployeeName  { get; set; } = "";
    public string?  Department    { get; set; }
    public DateTime PaymentDate   { get; set; } = DateTime.Today;
    public string   PaymentMonth  => PaymentDate.ToString("yyyy-MM");
    public string   PaymentType   { get; set; } = "BonusSeparate";
    public string   PaymentTypeAr => PaymentType switch
    {
        "BonusSeparate"      => "مكافأة منفصلة",
        "CommissionSeparate" => "عمولة منفصلة",
        _                     => "دفعة خارج الراتب"
    };
    public decimal  Amount       { get; set; }
    public string   Description  { get; set; } = "";
    public string?  Reason       { get; set; }
}

public class OffPayrollPaymentListDto
{
    public int       PayrollID      { get; set; }
    public int       EmployeeID     { get; set; }
    public string    EmployeeName   { get; set; } = "";
    public string?   Department     { get; set; }
    public string    PayrollMonth   { get; set; } = "";
    public string    PaymentType    { get; set; } = "BonusSeparate";
    public string    PaymentTypeAr  => PaymentType switch
    {
        "BonusSeparate"      => "مكافأة منفصلة",
        "CommissionSeparate" => "عمولة منفصلة",
        _                     => "دفعة خارج الراتب"
    };
    public string    Description    { get; set; } = "";
    public decimal   Amount         { get; set; }
    public string    PaymentStatus  { get; set; } = PayrollPaymentStatuses.PendingReview;
    public string    StatusColor    => PaymentStatus switch
    {
        PayrollPaymentStatuses.Paid          => "#10b981",
        PayrollPaymentStatuses.PendingReview => "#f59e0b",
        PayrollPaymentStatuses.Approved      => "#3b82f6",
        PayrollPaymentStatuses.Rejected      => "#ef4444",
        PayrollPaymentStatuses.Unpaid        => "#94a3b8",
        PayrollPaymentStatuses.Cancelled     => "#64748b",
        _                                    => "#94a3b8"
    };
    public DateTime? RequestedAt    { get; set; }
    public DateTime? PaidAt         { get; set; }
    public string?   CreatedBy      { get; set; }
    public string?   Notes          { get; set; }
}

public class OffPayrollPaymentStatsDto
{
    public decimal TotalAmount   { get; set; }
    public int     ReviewCount   { get; set; }
    public int     ApprovedCount { get; set; }
    public int     PaidCount     { get; set; }
    public int     RejectedCount { get; set; }
    public int     CancelledCount { get; set; }
}

public class OffPayrollPaymentFilterDto
{
    public string? Month         { get; set; }
    public int?    EmployeeID    { get; set; }
    public string? PaymentType   { get; set; }
    public string? PaymentStatus { get; set; }
    public string? SearchText    { get; set; }
}

public static class PayrollPaymentStatuses
{
    public const string PendingReview = "قيد المراجعة";
    public const string Approved      = "معتمد";
    public const string Rejected      = "مرفوض";
    public const string Unpaid        = "غير مدفوع";
    public const string Paid          = "مدفوع";
    public const string Cancelled     = "ملغي";
}

// ── فلاتر ────────────────────────────────────────────────────
public class PayrollFilterDto
{
    // شهر واحد
    public string? PayrollMonth   { get; set; }

    // فترة شهرية (yyyy-MM)
    public string? MonthFrom      { get; set; }
    public string? MonthTo        { get; set; }

    // فلاتر رئيسية
    public int?    PayrollRunId   { get; set; }
    public int?    EmployeeID     { get; set; }
    public string? Department     { get; set; }
    public string? JobTitle       { get; set; }
    public string? PaymentStatus  { get; set; }
    public string? SearchText     { get; set; }

    // فلاتر متقدمة
    public bool?   HasLoans         { get; set; }
    public bool?   HasAbsence       { get; set; }
    public bool?   HasLate          { get; set; }
    public string? AttendanceSource { get; set; } // Manual | Biometric
    public int?    MinLateMinutes   { get; set; }
    public int?    MaxLateMinutes   { get; set; }
    public decimal? MinNetSalary    { get; set; }
    public decimal? MaxNetSalary    { get; set; }
    public decimal? MinDeductions   { get; set; }
    public decimal? MaxDeductions   { get; set; }

    public int     PageNumber     { get; set; } = 1;
    public int     PageSize       { get; set; } = 20;
}

// ── الحضور اليدوي ────────────────────────────────────────────
public class ManualAttendanceDto
{
    public int     ManualID         { get; set; }
    public int     EmployeeID       { get; set; }
    public string  EmployeeName     { get; set; } = "";
    public string? Department       { get; set; }
    public string  AttendanceMonth  { get; set; } = "";
    public int     PresentDays      { get; set; }
    public int     AbsenceDays      { get; set; }
    public int     LateMinutes      { get; set; }
    public string? Notes            { get; set; }
}