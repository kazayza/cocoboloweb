using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface ICrmDashboardService
{
    Task<CrmDashboardDto> GetDashboardAsync(string currentUserName, string? role, CrmDashboardFilterDto filter);
}
