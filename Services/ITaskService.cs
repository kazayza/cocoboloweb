using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface ITaskService
{
    Task<PagedResult<TaskListDto>> GetTasksAsync(TaskFilterDto filter);
    Task<List<TaskListDto>> GetByOpportunityAsync(int opportunityId);
    Task<(bool Success, string Message)> AddQuickAsync(QuickTaskDto dto, string userName);
    Task<(bool Success, string Message)> CompleteAsync(int taskId, string notes, string userName);
    Task<(bool Success, string Message)> DeleteAsync(int taskId, string userName);
    Task<(bool Success, string Message)> CloseAllTasksForOpportunityAsync(
        int opportunityId, string status, string notes, string userName);
}