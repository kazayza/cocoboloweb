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
    Task<(bool Success, string Message)> CompleteAsync(int taskId, string notes, string userName, string? completionLink = null);
    Task<(bool Success, string Message)> DeleteAsync(int taskId, string userName);
    // ─── تطوير التكليفات العامة: إلغاء موثّق + تعديل/إعادة إسناد (المنشئ أو مدير) ───
    Task<(bool Success, string Message)> CancelGeneralTaskAsync(int taskId, string reason, string userName);
    Task<(bool Success, string Message)> UpdateGeneralTaskAsync(UpdateGeneralTaskDto dto, string userName);
    Task<TaskDetailDto?> GetTaskDetailAsync(int taskId, string userName);
    Task<(bool Success, string Message)> AddTaskCommentAsync(AddTaskCommentDto dto, string userName);
    Task<(bool Success, string Message)> AcknowledgeTaskAsync(int taskId, string userName);
    Task<(bool Success, string Message)> ReturnTaskAsync(int taskId, string reason, string userName);
    // ─── على مستوى النظام: ربط المستندات + «مهامي» ───
    Task<LinkedEntityLookupDto?> ResolveLinkedEntityAsync(string entityType, int entityId);
    Task<MyTasksDto> GetMyTasksAsync(string userName);
    Task<MyTasksSummaryDto> GetMyTasksSummaryAsync(string userName);
    Task<List<EmployeeTaskLoadDto>> GetGeneralTasksManagerSummaryAsync(string userName);
    Task<(bool Success, string Message)> CloseAllTasksForOpportunityAsync(
        int opportunityId, string status, string notes, string userName);
}
