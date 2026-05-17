using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IEmployeeLoanService
{
    // ── قائمة وتفاصيل ──────────────────────────────────────
    Task<PagedResult<LoanListDto>>  GetLoansAsync(LoanFilterDto filter);
    Task<LoanDetailDto?>            GetLoanDetailAsync(int loanId);
    Task<LoanFormDto?>              GetLoanForEditAsync(int loanId);
    Task<LoanStatsDto>              GetStatsAsync();

    // ── إضافة وتعديل وإلغاء ────────────────────────────────
    Task<(bool Success, string Message, int? LoanId)> SaveLoanAsync(LoanFormDto dto, string userName);
    Task<(bool Success, string Message)>              CancelLoanAsync(int loanId, string userName);

    // ── الأقساط ─────────────────────────────────────────────
    Task<List<InstallmentListDto>>  GetMonthInstallmentsAsync(string month); // الأقساط المستحقة في شهر
    Task<(bool Success, string Message)> DeductInstallmentAsync(int installmentId, int payrollDetailId, string userName);
    Task<(bool Success, string Message)> SkipInstallmentAsync(int installmentId, string reason, string userName);

    // ── مساعد للـ Payroll ───────────────────────────────────
    Task<List<InstallmentListDto>>  GetEmployeeInstallmentsForMonth(int employeeId, string month);
}
