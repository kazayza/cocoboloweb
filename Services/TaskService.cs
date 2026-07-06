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

        // ★ أضف السطر ده هنا - استبعد Completed و Cancelled
        query = query.Where(t => t.Status != "Completed" && t.Status != "Cancelled");

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
        {
            var dbPriority = filter.Priority == "Medium" ? "Normal" : filter.Priority;
            query = query.Where(t => t.Priority == dbPriority);
        }
        if (filter.IsOverdue == true)
            query = query.Where(t => t.DueDate < DateTime.Today && t.Status != "Completed");
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();
            query = query.Where(t =>
                (t.TaskDescription != null && t.TaskDescription.Contains(s)) ||
                (t.ClientName != null && t.ClientName.Contains(s)));
        }

        var items = await query
            .Select(t => new TaskListDto
            {
                TaskId = t.TaskId, OpportunityId = t.OpportunityId, PartyId = t.PartyId,
                ClientName = t.ClientName, Phone = t.Phone,
                AssignedTo = t.AssignedTo, AssignedToName = t.AssignedToName,
                TaskTypeId = t.TaskTypeId, TaskTypeName = t.TaskTypeName,TaskTypeNameAr = t.TaskTypeNameAr,
                TaskDescription = t.TaskDescription, DueDate = t.DueDate, DueTime = t.DueTime,
                Priority = t.Priority, Status = t.Status,
                CompletedDate = t.CompletedDate, CompletedBy = t.CompletedBy,
                CompletionNotes = t.CompletionNotes,
                ReminderEnabled = t.ReminderEnabled, ReminderMinutes = t.ReminderMinutes,
                IsActive = t.IsActive, CreatedBy = t.CreatedBy, CreatedAt = t.CreatedAt,
                TaskDueStatus = t.TaskDueStatus, DaysUntilDue = t.DaysUntilDue
            }).ToListAsync();

        // ═══════════════════════════════════════════════════════════════
        // ★ دمج مهام متابعة الـ Leads المفتوحة من جدول التفاعلات ★
        // ═══════════════════════════════════════════════════════════════
        var leadQuery = _db.LeadInteractions.AsNoTracking()
            .Include(i => i.Lead)
            .Include(i => i.Employee)
            .Where(i => i.NextFollowUpDate.HasValue && !i.IsCompleted && !i.Lead.IsConverted && i.Lead.LeadStatus != "محول" && i.Lead.LeadStatus != "مرفوض");

        if (crmAccess.HasValue)
            leadQuery = leadQuery.Where(i => i.CreatedAt >= crmAccess.Value);
        if (filter.AssignedTo.HasValue)
            leadQuery = leadQuery.Where(i => i.Lead.AssignedEmployeeId == filter.AssignedTo.Value || i.EmployeeId == filter.AssignedTo.Value);
        if (filter.IsOverdue == true)
            leadQuery = leadQuery.Where(i => i.NextFollowUpDate.Value < DateTime.Today);
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();
            leadQuery = leadQuery.Where(i =>
                (i.Summary != null && i.Summary.Contains(s)) ||
                (i.Lead.FullName != null && i.Lead.FullName.Contains(s)));
        }

        var rawLeads = await leadQuery.ToListAsync();

        // جلب أسماء الموظفين للـ Leads في الذاكرة لتجنب أي مشكلة في ترجمة EF Core
        var empIds = rawLeads.Where(r => r.Lead?.AssignedEmployeeId != null).Select(r => r.Lead!.AssignedEmployeeId!.Value).Distinct().ToList();
        var empNames = new Dictionary<int, string>();
        if (empIds.Count > 0)
        {
            empNames = await _db.Employees.AsNoTracking().Where(e => empIds.Contains(e.EmployeeId)).ToDictionaryAsync(e => e.EmployeeId, e => e.FullName);
        }

        var leadTasks = rawLeads.Select(i => new TaskListDto
        {
            TaskId = i.LeadInteractionId + 1000000,
            LeadId = i.LeadId,
            IsLeadTask = true,
            ClientName = i.Lead?.FullName ?? "غير محدد",
            Phone = i.Lead?.Phone ?? "",
            CampaignName = i.Lead?.CampaignName,
            Platform = i.Lead?.Platform,
            AssignedTo = i.Lead?.AssignedEmployeeId ?? i.EmployeeId ?? 0,
            AssignedToName = i.Lead?.AssignedEmployeeId != null && empNames.TryGetValue(i.Lead.AssignedEmployeeId.Value, out var name) ? name : (i.Employee?.FullName ?? "غير محدد"),
            TaskDescription = "متابعة Lead: " + (i.Summary ?? "تواصل مستحق"),
            DueDate = i.NextFollowUpDate!.Value,
            Priority = "Normal",
            Status = "Pending",
            IsActive = true,
            CreatedBy = i.CreatedBy,
            CreatedAt = i.CreatedAt
        }).ToList();

        var allItems = items.Concat(leadTasks).ToList();

        allItems = filter.SortBy switch
        {
            "DueDate" => filter.SortDescending ? allItems.OrderByDescending(t => t.DueDate).ToList() : allItems.OrderBy(t => t.DueDate).ToList(),
            "Priority" => filter.SortDescending ? allItems.OrderByDescending(t => t.Priority).ToList() : allItems.OrderBy(t => t.Priority).ToList(),
            _ => filter.SortDescending ? allItems.OrderByDescending(t => t.CreatedAt).ToList() : allItems.OrderBy(t => t.CreatedAt).ToList()
        };

        var total = allItems.Count;
        filter.PageSize = 50000; // إجبار الباك إند على إرجاع كافة المهام لتغذية إحصائيات الشيبس بشكل صحيح
        var pagedItems = allItems.Skip((filter.PageNumber - 1) * filter.PageSize).Take(filter.PageSize).ToList();

        return new PagedResult<TaskListDto> { Items = pagedItems, TotalCount = total, PageNumber = filter.PageNumber, PageSize = filter.PageSize };
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
                DueDate = dto.DueDate, Priority = (dto.Priority == "Medium" ? "Normal" : dto.Priority) ?? "Normal",
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
            if (taskId > 1000000)
            {
                var interactionId = taskId - 1000000;
                var leadInteraction = await _db.LeadInteractions
                    .Include(i => i.Lead)
                    .FirstOrDefaultAsync(i => i.LeadInteractionId == interactionId);
                if (leadInteraction == null) return (false, "تفاعل الـ Lead غير موجود");

                var userEmpId = await _db.Users.AsNoTracking()
                    .Where(u => u.Username == userName && u.EmployeeId != null)
                    .Select(u => u.EmployeeId)
                    .FirstOrDefaultAsync();

                var empId = leadInteraction.Lead?.AssignedEmployeeId ?? leadInteraction.EmployeeId ?? userEmpId;

                leadInteraction.IsCompleted = true;
                leadInteraction.CompletedByEmployeeId = empId;
                leadInteraction.CompletedDate = DateTime.Now;
                if (!string.IsNullOrWhiteSpace(notes))
                {
                    leadInteraction.Notes = (leadInteraction.Notes + " [ملاحظة إنجاز: " + notes + "]").Trim();
                }
                await _db.SaveChangesAsync();
                return (true, "تم إتمام متابعة الـ Lead بنجاح");
            }

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