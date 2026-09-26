using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IExecutiveCockpitService
{
    // period: today | week | month | quarter | year
    Task<ExecutiveCockpitDto> GetCockpitAsync(string period);

    // الخط الزمني الكامل لموظف (تدقيق + فواتير أنشأها)
    Task<List<ExecutiveTimelineRowDto>> GetEmployeeTimelineAsync(int employeeId);
}
