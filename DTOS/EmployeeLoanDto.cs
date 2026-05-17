namespace COCOBOLOERPNEW.DTOs;

// ============================================================
// DTOs لنظام السلف
// ============================================================

/// <summary>
/// للعرض في القائمة
/// </summary>
public class LoanListDto
{
    public int      LoanId               { get; set; }
    public int      EmployeeId           { get; set; }
    public string   EmployeeName         { get; set; } = "";
    public string?  Department           { get; set; }
    public string?  JobTitle             { get; set; }

    // بيانات السلفة
    public decimal  LoanAmount           { get; set; }
    public decimal  MonthlyInstallment   { get; set; }
    public int      TotalInstallments    { get; set; }
    public int      PaidInstallments     { get; set; }
    public int      RemainingInstallments => TotalInstallments - PaidInstallments;
    public decimal  RemainingAmount      { get; set; }
    public decimal  PaidAmount           => LoanAmount - RemainingAmount;

    // التواريخ
    public DateTime LoanDate             { get; set; }
    public string   StartDeductionMonth  { get; set; } = "";
    public string?  ExpectedEndMonth     { get; set; } // آخر قسط

    // الحالة
    public string   Status               { get; set; } = "";
    public string   StatusAr             => Status switch
    {
        "Active"    => "نشطة",
        "Completed" => "منتهية",
        "Cancelled" => "ملغية",
        _           => Status
    };
    public string   StatusColor          => Status switch
    {
        "Active"    => "#10b981",
        "Completed" => "#3b82f6",
        "Cancelled" => "#ef4444",
        _           => "#94a3b8"
    };

    // الخزينة
    public string?  CashBoxName          { get; set; }

    // تتبع
    public string?  Notes                { get; set; }
    public string?  ApprovedBy           { get; set; }
    public string?  CreatedBy            { get; set; }
    public DateTime CreatedAt            { get; set; }
}

/// <summary>
/// للفورم - إضافة / تعديل سلفة
/// </summary>
public class LoanFormDto
{
    public int      LoanId               { get; set; }  // 0 = جديد
    public int      EmployeeId           { get; set; }
    public string   EmployeeName         { get; set; } = "";

    public decimal  LoanAmount           { get; set; }
    public int      TotalInstallments    { get; set; } = 1;
    public decimal  MonthlyInstallment   { get; set; }  // بيتحسب تلقائياً

    public DateTime LoanDate             { get; set; } = DateTime.Today;
    public string   StartDeductionMonth  { get; set; } = DateTime.Today.ToString("yyyy-MM");

    public int?     CashBoxId            { get; set; }
    public string?  Notes                { get; set; }
    public string?  ApprovedBy           { get; set; }
}

/// <summary>
/// للعرض التفصيلي - سلفة واحدة مع كل أقساطها
/// </summary>
public class LoanDetailDto
{
    public LoanListDto              Loan         { get; set; } = new();
    public List<InstallmentListDto> Installments { get; set; } = new();
}

/// <summary>
/// قسط واحد في القائمة
/// </summary>
public class InstallmentListDto
{
    public int      InstallmentId     { get; set; }
    public int      LoanId            { get; set; }
    public int      InstallmentNumber { get; set; }
    public string   DeductionMonth    { get; set; } = "";
    public string   MonthDisplay      => // عرض "يونيو 2026" بدل "2026-06"
        DateTime.TryParseExact(DeductionMonth + "-01", "yyyy-MM-dd",
            null, System.Globalization.DateTimeStyles.None, out var d)
        ? d.ToString("MMMM yyyy", new System.Globalization.CultureInfo("ar-EG"))
        : DeductionMonth;

    public decimal  Amount            { get; set; }
    public string   Status            { get; set; } = "";
    public string   StatusAr          => Status switch
    {
        "Pending"  => "لم يُخصم بعد",
        "Deducted" => "تم الخصم",
        "Skipped"  => "مؤجل",
        _          => Status
    };
    public string   StatusColor       => Status switch
    {
        "Pending"  => "#f59e0b",
        "Deducted" => "#10b981",
        "Skipped"  => "#94a3b8",
        _          => "#94a3b8"
    };
    public string?  PayrollMonth      { get; set; }  // الراتب اللي اتخصم فيه
    public DateTime? DeductionDate    { get; set; }
    public string?  Notes             { get; set; }
}

/// <summary>
/// فلتر البحث
/// </summary>
public class LoanFilterDto
{
    public int?     EmployeeId   { get; set; }
    public string?  Status       { get; set; }  // Active | Completed | Cancelled | null = الكل
    public string?  SearchText   { get; set; }
    public string?  Month        { get; set; }  // فلتر بشهر معين
    public int      PageNumber   { get; set; } = 1;
    public int      PageSize     { get; set; } = 20;
}

/// <summary>
/// إحصائيات السلف (للـ Dashboard)
/// </summary>
public class LoanStatsDto
{
    public int     ActiveLoansCount     { get; set; }  // سلف نشطة
    public decimal TotalRemainingAmount { get; set; }  // إجمالي المتبقي
    public decimal ThisMonthDeductions  { get; set; }  // خصومات الشهر الحالي
    public int     CompletedThisMonth   { get; set; }  // انتهت الشهر ده
    public int     EmployeesWithLoans   { get; set; }  // موظفين عندهم سلف
}
