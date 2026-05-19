using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IEmployeeShiftService
{
    // === القراءة ===
    Task<PagedResult<EmployeeShiftListDto>> GetShiftsAsync(EmployeeShiftFilterDto filter);
    Task<EmployeeShiftFormDto?> GetShiftByIdAsync(int shiftId);
    Task<List<ShiftEmployeeLookupDto>> SearchEmployeesAsync(string search, CancellationToken cancellationToken);
    Task<List<EmployeeShiftListDto>> GetMyShiftsAsync(string userName);

    // === الكتابة ===
    Task<(bool Success, string Message)> AddShiftAsync(AddEmployeeShiftDto dto, string currentUserName);
    Task<(bool Success, string Message)> UpdateShiftAsync(int shiftId, EmployeeShiftFormDto dto, string currentUserName);
    Task<(bool Success, string Message)> DeleteShiftAsync(int shiftId, string currentUserName);

    // === Excel ===
    Task<byte[]> ExportToExcelAsync(EmployeeShiftFilterDto filter);
    Task<ShiftImportResultDto> ImportFromExcelAsync(Stream fileStream, string currentUserName);
    Task<byte[]> GetImportTemplateAsync();
    Task<ShiftStatisticsDto> GetStatisticsAsync();
}