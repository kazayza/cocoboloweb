using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface ITaskService
{
    Task<PagedResult<TaskListDto>> GetTasksAsync(TaskFilterDto filter);
    Task<List<TaskListDto>> GetByOpportunityAsync(int opportunityId);
    Task<List<TaskListDto>> GetByLeadAsync(int leadId);
    Task<List<TaskListDto>> GetGeneralTasksAsync(string userName);
    Task<(bool Success, string Message)> AddQuickAsync(QuickTaskDto dto, string userName);
    Task<(bool Success, string Message, int? TaskId)> AddGeneralManagerOpportunityTaskAsync(GeneralManagerOpportunityTaskDto dto, string userName);
    Task<(bool Success, string Message, int? TaskId)> AddGeneralEmployeeTaskAsync(GeneralEmployeeTaskDto dto, string userName);
    Task<(bool Success, string Message, int? TaskId)> AddLeadTaskAsync(LeadTaskDto dto, string userName);
    Task<(bool Success, string Message)> StartAsync(int taskId, string? notes, string userName);
    Task<(bool Success, string Message)> CompleteAsync(int taskId, string notes, string userName);
    Task<(bool Success, string Message)> DeleteAsync(int taskId, string userName);
    Task<(bool Success, string Message)> CloseAllTasksForOpportunityAsync(
        int opportunityId, string status, string notes, string userName);
}
