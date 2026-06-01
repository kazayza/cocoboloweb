namespace COCOBOLOERPNEW.DTOs;

// ============================================================
// ✅ EmployeeLoanDto.cs - النسخة المُصلَحة
// ملاحظة: احذف PagedResult<T> من هنا لو موجود في ملف DTOs تاني
// ============================================================

public class LoanListDto
{
    public int      LoanId               { get; set; }
    public int      EmployeeId           { get; set; }
    public string   EmployeeName         { get; set; } = "";
    public string?  Department           { get; set; }
    public string?  JobTitle             { get; set; }

    public decimal  LoanAmount           { get; set; }
    public decimal  MonthlyInstallment   { get; set; }
    public int      TotalInstallments    { get; set; }
    public int      PaidInstallments     { get; set; }
    public int      RemainingInstallments => TotalInstallments - PaidInstallments;
    public decimal  RemainingAmount      { get; set; }
    public decimal  PaidAmount           => LoanAmount - RemainingAmount;

    public DateTime LoanDate             { get; set; }
    public string   StartDeductionMonth  { get; set; } = "";
    public string?  ExpectedEndMonth     { get; set; }

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

    public string?  CashBoxName          { get; set; }
    public string?  Notes                { get; set; }
    public string?  ApprovedBy           { get; set; }
    public string?  CreatedBy            { get; set; }
    public DateTime CreatedAt            { get; set; }
}

public class LoanFormDto
{
    public int      LoanId               { get; set; }
    public int      EmployeeId           { get; set; }
    public string   EmployeeName         { get; set; } = "";
    public decimal  LoanAmount           { get; set; }
    public int      TotalInstallments    { get; set; } = 1;
    public decimal  MonthlyInstallment   { get; set; }
    public DateTime LoanDate             { get; set; } = DateTime.Today;
    public string   StartDeductionMonth  { get; set; } = DateTime.Today.AddMonths(1).ToString("yyyy-MM");
    public int?     CashBoxId            { get; set; }
    public string?  Notes                { get; set; }
    public string?  ApprovedBy           { get; set; }
}

public class LoanDetailDto
{
    public LoanListDto               Loan         { get; set; } = new();
    public List<InstallmentListDto>  Installments { get; set; } = new();
}

public class InstallmentListDto
{
    public int       InstallmentId     { get; set; }
    public int       LoanId            { get; set; }
    public int       InstallmentNumber { get; set; }
    public string    DeductionMonth    { get; set; } = "";

    // ✅ عرض الشهر بالعربي
    public string MonthDisplay =>
        DateTime.TryParseExact(DeductionMonth + "-01", "yyyy-MM-dd",
            null, System.Globalization.DateTimeStyles.None, out var d)
            ? d.ToString("MMMM yyyy", new System.Globalization.CultureInfo("ar-EG"))
            : DeductionMonth;

    public decimal   Amount            { get; set; }
    public string    Status            { get; set; } = "";
    public string    StatusAr          => Status switch
    {
        "Pending"  => "لم يُخصم بعد",
        "Deducted" => "تم الخصم",
        "Skipped"  => "مؤجل",
        _          => Status
    };
    public string    StatusColor       => Status switch
    {
        "Pending"  => "#f59e0b",
        "Deducted" => "#10b981",
        "Skipped"  => "#94a3b8",
        _          => "#94a3b8"
    };
    public string?   PayrollMonth      { get; set; }
    public DateTime? DeductionDate     { get; set; }
    public string?   Notes             { get; set; }
}

public class LoanFilterDto
{
    public int?    EmployeeId  { get; set; }
    public string? Status      { get; set; }
    public string? SearchText  { get; set; }
    public string? Month       { get; set; }
    public int     PageNumber  { get; set; } = 1;
    public int     PageSize    { get; set; } = 20;
}

public class LoanStatsDto
{
    public int     ActiveLoansCount     { get; set; }
    public decimal TotalRemainingAmount { get; set; }
    public decimal ThisMonthDeductions  { get; set; }
    public int     CompletedThisMonth   { get; set; }
    public int     EmployeesWithLoans   { get; set; }
}

public class EmployeeLookupDto
{
    public int     EmployeeId   { get; set; }
    public string  FullName     { get; set; } = "";
    public string? Department   { get; set; }
    public string? JobTitle     { get; set; }
    public string? MobilePhone  { get; set; }
    public string  DisplayText  => $"{FullName} - {Department ?? "بدون قسم"}";
}

public class CashBoxLookupDto
{
    public int     CashBoxId      { get; set; }
    public string  CashBoxName    { get; set; } = "";
    public decimal CurrentBalance { get; set; }
    public bool    IsDefault      { get; set; }
    public string  DisplayText    => $"{CashBoxName} ({CurrentBalance:N2} جـ)";
}

// ============================================================
// ⚠️ تنبيه: PagedResult<T> لو موجود في ملف DTOs تاني احذفه من هنا
// لو مش موجود في أي حتة خليه هنا
// ============================================================
// public class PagedResult<T> { ... }  ← احذف لو موجود تاني
