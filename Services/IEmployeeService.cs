using System.Threading.Tasks;
using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services
{
    public interface IEmployeeService
    {
        // القائمة مع فلتر وباجينيشن
        Task<PagedResult<EmployeeListDto>> GetEmployeesAsync(EmployeeFilterDto filter);

        // تفاصيل موظف واحد
        Task<EmployeeDetailDto?> GetEmployeeDetailAsync(int employeeId);

        // نموذج التعديل
        Task<EmployeeFormDto?> GetEmployeeForEditAsync(int employeeId);

        // إضافة موظف جديد
        Task<(bool Success, string Message, int? EmployeeId)> CreateEmployeeAsync(EmployeeFormDto dto, string userName);

        // تعديل موظف
        Task<(bool Success, string Message)> UpdateEmployeeAsync(int employeeId, EmployeeFormDto dto, string userName);

        // تغيير حالة الموظف
        Task<(bool Success, string Message)> ChangeStatusAsync(int employeeId, string newStatus, string reason, string userName);

        // الإحصائيات
        Task<EmployeeStatsDto> GetStatsAsync();

        // تاريخ المرتبات
        Task<List<SalaryHistoryDto>> GetSalaryHistoryAsync(int employeeId);

        // الأقسام المتاحة
        Task<List<string>> GetDepartmentsAsync();

        // بحث سريع (للإكمال التلقائي)
        Task<List<EmployeeListDto>> SearchEmployeesAsync(string searchTerm, int maxResults = 20);
    }
}
