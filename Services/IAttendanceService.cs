using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IAttendanceService
{
    // ═══════════════ الحضور ═══════════════
    Task<PagedResult<AttendanceListDto>> GetAttendanceAsync(AttendanceFilterDto filter);
    Task<AttendanceListDto?> GetAttendanceByIdAsync(int attendanceId);
    Task<AttendanceStatisticsDto> GetStatisticsAsync(AttendanceFilterDto filter);
    Task<(bool Success, string Message)> UpdateAttendanceAsync(AttendanceEditDto dto, string currentUserName);
    Task<(bool Success, string Message)> DeleteAttendanceAsync(int attendanceId, string currentUserName);
    
    // ═══════════════ التقارير ═══════════════
    Task<List<AttendanceReportDto>> GetAttendanceReportAsync(AttendanceReportFilterDto filter);
    Task<byte[]> ExportAttendanceToExcelAsync(AttendanceFilterDto filter);
    Task<byte[]> ExportReportToExcelAsync(AttendanceReportFilterDto filter);
    
    // ═══════════════ سجلات البصمة الخام ═══════════════
    Task<PagedResult<BiometricLogDto>> GetBiometricLogsAsync(BiometricLogFilterDto filter);
    Task<(bool Success, string Message)> ProcessBiometricLogsAsync(DateTime date, string currentUserName);
    
    // ═══════════════ بصمتي ═══════════════
    Task<List<AttendanceListDto>> GetMyAttendanceAsync(string userName, DateTime? month = null);
    Task<AttendanceStatisticsDto> GetMyStatisticsAsync(string userName, DateTime? month = null);
        // ═══════════════ Dashboard ═══════════════
    Task<AttendanceDashboardDto> GetDashboardDataAsync(DateTime? dateFrom = null, DateTime? dateTo = null);
    Task<AttendanceStatisticsDto> GetTodayStatisticsAsync();
}