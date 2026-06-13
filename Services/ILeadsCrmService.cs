using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;

namespace COCOBOLOERPNEW.Services;

public interface ILeadsCrmService
{
    Task<PagedResult<LeadsCrmListDto>> GetLeadsAsync(LeadsCrmFilterDto filter);
    Task<LeadsCrmDetailDto?> GetLeadByIdAsync(int leadId);
    Task<List<LeadInteractionDto>> GetLeadInteractionsAsync(int leadId);

    Task<(bool Success, string Message)> AddLeadInteractionAsync(
    LeadInteractionCreateDto dto, string userName);
    Task<(bool Success, string Message)> UpdateLeadAsync(LeadsCrmUpdateDto dto, string userName);
    Task<(bool Success, string Message, int PartyId, int OpportunityId)> ConvertLeadToClientAsync(LeadConvertDto dto, string userName);
    Task<LeadsCrmStatsDto> GetStatsAsync();
    Task<LeadsDashboardDataDto> GetDashboardDataAsync(LeadsDashboardFilterDto filter);
    Task<(bool Success, string Message, int LeadId)> CreateLeadAsync(LeadsCrmCreateDto dto, string userName);
    Task<(bool Success, string Message)> DeleteLeadAsync(int leadId, string userName);
    Task<List<Employee>> GetEmployeesAsync();
    Task<List<Employee>> GetAssignableEmployeesAsync();
}