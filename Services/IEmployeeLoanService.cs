using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IEmployeeLoanService
{
    // ── قائمة وتفاصيل ──────────────────────────────────────
    Task<PagedResult<LoanListDto>>  GetLoansAsync(LoanFilterDto filter);
    Task<LoanDetailDto?>            GetLoanDetailAsync(int loanId);
    Task<LoanFormDto?>              GetLoanForEditAsync(int loanId);
    Task<LoanStatsDto>              GetStatsAsync(int? branchId = null);

    // ── إضافة وتعديل وإلغاء ────────────────────────────────
    Task<(bool Success, string Message, int? LoanId)> SaveLoanAsync(LoanFormDto dto, string userName);
    Task<(bool Success, string Message)>              CancelLoanAsync(int loanId, string userName);

    // ── الأقساط ─────────────────────────────────────────────
    Task<List<InstallmentListDto>>  GetMonthInstallmentsAsync(string month); // الأقساط المستحقة في شهر
    Task<(bool Success, string Message)> DeductInstallmentAsync(int installmentId, int payrollDetailId, string userName);
    Task<(bool Success, string Message)> SkipInstallmentAsync(int installmentId, string reason, string userName, string? targetMonth = null);
    Task<(bool Success, string Message)> SplitInstallmentAsync(int installmentId, decimal amountToKeepThisMonth, string reason, string userName, string? targetMonth = null);

    // (12-H9) تعديل قسط معلق (مبلغ/شهر/ملاحظات)
    Task<(bool Success, string Message)> UpdateInstallmentAsync(int installmentId, decimal? newAmount, string? newMonth, string? notes, string userName);

    // (12-H9) تعديل السلفة (ملاحظات/معتمد دائمًا — الحقول المالية فقط عند صفر خصم)
    Task<(bool Success, string Message)> UpdateLoanAsync(LoanFormDto dto, string userName);

    // (12-H11/ب) الأقساط المعلقة لموظف — لمقاصة الدفعات خارج الراتب
    Task<List<InstallmentListDto>> GetPendingInstallmentsAsync(int employeeId);

    // ── كشف الحساب ───────────────────────────────────────────
    Task<EmployeeLoanStatementDto?> GetEmployeeStatementAsync(int employeeId);

    // ── مساعد للـ Payroll ───────────────────────────────────
    Task<List<InstallmentListDto>>  GetEmployeeInstallmentsForMonth(int employeeId, string month);
    Task<List<EmployeeLookupDto>> GetEmployeesLookupAsync(string? search = null);
    Task<List<CashBoxLookupDto>>  GetCashBoxesLookupAsync();
}
