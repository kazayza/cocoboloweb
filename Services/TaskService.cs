using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class TaskService : ITaskService
{
    private readonly db24804Context _db;
    private readonly IHttpContextAccessor _http;

    public TaskService(db24804Context db, IHttpContextAccessor http)
    { _db = db; _http = http; }

    public async Task<PagedResult<TaskListDto>> GetTasksAsync(TaskFilterDto filter)
    {
        var crmAccess = _http.GetCrmAccessFrom();
        var query = _db.VwCrmTasks.AsNoTracking().AsQueryable();

        if (crmAccess.HasValue)
            query = query.Where(t => t.CreatedAt >= crmAccess.Value);
        if (filter.OpportunityId.HasValue)
            query = query.Where(t => t.OpportunityId == filter.OpportunityId.Value);
        if (filter.AssignedTo.HasValue)
            query = query.Where(t => t.AssignedTo == filter.AssignedTo.Value);
        if (filter.TaskTypeId.HasValue)
            query = query.Where(t => t.TaskTypeId == filter.TaskTypeId.Value);
        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(t => t.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.Priority))
            query = query.Where(t => t.Priority == filter.Priority);
        if (filter.IsOverdue == true)
            query = query.Where(t => t.DueDate < DateTime.Today && t.Status != "Completed");
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();
            query = query.Where(t =>
                (t.TaskDescription != null && t.TaskDescription.Contains(s)) ||
                (t.ClientName != null && t.ClientName.Contains(s)));
        }

        var total = await query.CountAsync();

        query = filter.SortBy switch
        {
            "DueDate" => filter.SortDescending ? query.OrderByDescending(t => t.DueDate) : query.OrderBy(t => t.DueDate),
            "Priority" => filter.SortDescending ? query.OrderByDescending(t => t.Priority) : query.OrderBy(t => t.Priority),
            _ => filter.SortDescending ? query.OrderByDescending(t => t.CreatedAt) : query.OrderBy(t => t.CreatedAt)
        };

        var items = await query.Skip((filter.PageNumber - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(t => new TaskListDto
            {
                TaskId = t.TaskId, OpportunityId = t.OpportunityId, PartyId = t.PartyId,
                ClientName = t.ClientName, Phone = t.Phone,
                AssignedTo = t.AssignedTo, AssignedToName = t.AssignedToName,
                TaskTypeId = t.TaskTypeId, TaskTypeName = t.TaskTypeName,
                TaskDescription = t.TaskDescription, DueDate = t.DueDate, DueTime = t.DueTime,
                Priority = t.Priority, Status = t.Status,
                CompletedDate = t.CompletedDate, CompletedBy = t.CompletedBy,
                CompletionNotes = t.CompletionNotes,
                ReminderEnabled = t.ReminderEnabled, ReminderMinutes = t.ReminderMinutes,
                IsActive = t.IsActive, CreatedBy = t.CreatedBy, CreatedAt = t.CreatedAt,
                TaskDueStatus = t.TaskDueStatus, DaysUntilDue = t.DaysUntilDue
            }).ToListAsync();

        return new PagedResult<TaskListDto> { Items = items, TotalCount = total, PageNumber = filter.PageNumber, PageSize = filter.PageSize };
    }

    public async Task<List<TaskListDto>> GetByOpportunityAsync(int opportunityId)
    {
        return await _db.VwCrmTasks.AsNoTracking()
            .Where(t => t.OpportunityId == opportunityId)
            .OrderByDescending(t => t.DueDate)
            .Select(t => new TaskListDto
            {
                TaskId = t.TaskId, OpportunityId = t.OpportunityId, PartyId = t.PartyId,
                ClientName = t.ClientName, AssignedTo = t.AssignedTo, AssignedToName = t.AssignedToName,
                TaskDescription = t.TaskDescription, DueDate = t.DueDate, DueTime = t.DueTime,
                Priority = t.Priority, Status = t.Status,
                CompletedDate = t.CompletedDate, CompletedBy = t.CompletedBy,
                TaskDueStatus = t.TaskDueStatus, DaysUntilDue = t.DaysUntilDue,
                CreatedAt = t.CreatedAt
            }).ToListAsync();
    }

    public async Task<(bool Success, string Message)> AddQuickAsync(QuickTaskDto dto, string userName)
    {
        try
        {
            var task = new CrmTask
            {
                OpportunityId = dto.OpportunityId, PartyId = dto.PartyId,
                AssignedTo = dto.AssignedTo, TaskDescription = dto.TaskDescription,
                DueDate = dto.DueDate, Priority = dto.Priority,
                Status = "Pending", IsActive = true,
                CreatedBy = userName, CreatedAt = DateTime.Now
            };
            _db.CrmTasks.Add(task);
            await _db.SaveChangesAsync();
            return (true, "تم إضافة المهمة بنجاح");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> CompleteAsync(int taskId, string notes, string userName)
    {
        try
        {
            var task = await _db.CrmTasks.FindAsync(taskId);
            if (task == null) return (false, "المهمة غير موجودة");
            task.Status = "Completed";
            task.CompletedDate = DateTime.Now;
            task.CompletedBy = userName;
            task.CompletionNotes = notes;
            await _db.SaveChangesAsync();
            return (true, "تم إكمال المهمة");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> DeleteAsync(int taskId, string userName)
    {
        try
        {
            var task = await _db.CrmTasks.FindAsync(taskId);
            if (task == null) return (false, "المهمة غير موجودة");
            task.IsActive = false;
            await _db.SaveChangesAsync();
            return (true, "تم حذف المهمة");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.Message}");
        }
    }
        public async Task<(bool Success, string Message)> CloseAllTasksForOpportunityAsync(
        int opportunityId, string status, string notes, string userName)
    {
        var tasks = await _db.CrmTasks
            .Where(t => t.OpportunityId == opportunityId
                     && (t.Status == "Pending" || t.Status == "In Progress"))
            .ToListAsync();

        if (!tasks.Any())
            return (true, "لا توجد مهام مفتوحة");

        var now = DateTime.Now;
        foreach (var t in tasks)
        {
            t.Status = status;
            t.CompletedDate = now;
            t.CompletedBy = userName;
            t.CompletionNotes = notes;
        }

        await _db.SaveChangesAsync();
        return (true, $"تم إغلاق {tasks.Count} مهمة");
    }
}