using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;

namespace COCOBOLOERPNEW.Services;

public interface IOpportunityService
{
    Task<PagedResult<OpportunityListDto>> GetOpportunitiesAsync(OpportunityFilterDto filter);
    Task<KanbanBoardDto> GetKanbanBoardAsync(OpportunityFilterDto filter);
    Task<OpportunityFormDto?> GetOpportunityForEditAsync(int opportunityId);
    Task<OpportunityListDto?> GetOpportunityDetailAsync(int opportunityId);
    Task<List<Employee>> GetEmployeesAsync();
    Task<OpportunityStatsDto> GetStatsAsync(OpportunityFilterDto filter);
    Task<(bool Success, string Message, int OpportunityId)> SaveOpportunityAsync(OpportunityFormDto dto, string userName);
    Task<(bool Success, string Message)> MoveStageAsync(int opportunityId, int newStageId, string userName);
    Task<(bool Success, string Message)> DeleteOpportunityAsync(int opportunityId, string userName);
    Task<List<SalesStage>> GetStagesAsync();
    Task<List<ContactSource>> GetSourcesAsync();
    Task<List<InterestCategory>> GetCategoriesAsync();
    Task<List<LostReason>> GetLostReasonsAsync();
    Task<List<AdType>> GetAdTypesAsync();
    Task<List<Employee>> GetActiveEmployeesAsync();
    Task<List<ContactStatus>> GetContactStatusesAsync();
    Task<List<TaskType>> GetTaskTypesAsync();
    Task<OpportunityWorkflowDto?> GetActiveOpportunityByPartyAsync(int partyId);
    Task<List<PartySearchDto>> SearchPartiesAsync(string searchText);
    Task<bool> CheckPhoneExistsAsync(string phone);
    Task<(bool Success, string Message, int OpportunityId)> SaveWorkflowAsync(OpportunityWorkflowDto dto, string userName);
}