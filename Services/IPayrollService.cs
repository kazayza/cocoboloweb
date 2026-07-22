using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IPayrollService
{
    // ── عرض ─────────────────────────────────────────────────
    Task<PagedResult<PayrollListDto>>   GetPayrollsAsync(PayrollFilterDto filter);
    Task<PayslipDto?>                   GetPayslipAsync(int payrollId);
    Task<PayrollStatsDto>               GetStatsAsync(string month);
    Task<List<PayrollRunDto>>           GetRunsAsync(string? month = null);

    // ── حساب (بدون حفظ) ─────────────────────────────────────
    Task<List<PayrollCalculationDto>>   CalculateMonthAsync(string month);
    Task<PayrollCalculationDto>         CalculateOneAsync(int employeeId, string month);

    // ── حفظ وصرف ────────────────────────────────────────────
    Task<(bool Ok, string Msg, int? RunId)>
        ProcessMonthAsync(string month, List<PayrollCalculationDto> data,
                          int cashBoxId, string user);

    Task<(bool Ok, string Msg)> PayOneAsync(int payrollId, int cashBoxId, string user);
    Task<(bool Ok, string Msg)> ApprovePayrollAsync(int payrollId, string user);
    Task<(bool Ok, string Msg)> RejectPayrollAsync(int payrollId, string user, string? reason = null);
    Task<(bool Ok, string Msg)> CancelAsync(int payrollId, string user);
    Task<List<OffPayrollPaymentListDto>> GetOffPayrollPaymentsAsync(OffPayrollPaymentFilterDto filter);
    Task<OffPayrollPaymentStatsDto> GetOffPayrollPaymentStatsAsync(OffPayrollPaymentFilterDto filter);
    Task<(bool Ok, string Msg, int? PayrollId)> SaveOffPayrollPaymentAsync(OffPayrollPaymentFormDto dto, string user);

    // ── الحضور اليدوي ────────────────────────────────────────
    Task<List<ManualAttendanceDto>>     GetManualAttendanceAsync(string month);
    Task<(bool Ok, string Msg)>         SaveManualAttendanceAsync(ManualAttendanceDto dto, string user);
    Task<(bool Ok, string Msg, int? RunId)> SaveOnlyAsync(
    string month, List<PayrollCalculationDto> data, string user);

    // ── Export ───────────────────────────────────────────────
    Task<byte[]> ExportMonthExcelAsync(string month);
    Task<byte[]> ExportPayrollsExcelAsync(PayrollFilterDto filter);
}