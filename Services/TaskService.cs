using System;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace COCOBOLOERPNEW.Services;

public class TaskService : ITaskService
{
    private readonly db24804Context _db;
    private readonly IHttpContextAccessor _http;
    private readonly NotificationService _notify;
    private readonly IAuditService _audit;
    private readonly ILogger<TaskService> _logger;

    public TaskService(db24804Context db, IHttpContextAccessor http, NotificationService notify, IAuditService audit, ILogger<TaskService> logger)
    {
        _db = db;
        _http = http;
        _notify = notify;
        _audit = audit;
        _logger = logger;
    }

    public async Task<PagedResult<TaskListDto>> GetTasksAsync(TaskFilterDto filter)
    {
        var crmAccess = _http.GetCrmAccessFrom();
        var query = _db.VwCrmTasks.AsNoTracking().AsQueryable();

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
                TaskId = t.TaskId,
                OpportunityId = t.OpportunityId,
                PartyId = t.PartyId,
                ClientName = t.ClientName,
                Phone = t.Phone,
                AssignedTo = t.AssignedTo,
                AssignedToName = t.AssignedToName,
                TaskTypeId = t.TaskTypeId,
                TaskTypeName = t.TaskTypeName,
                TaskTypeNameAr = t.TaskTypeNameAr,
                TaskDescription = t.TaskDescription,
                DueDate = t.DueDate,
                DueTime = t.DueTime,
                Priority = t.Priority,
                Status = t.Status,
                StartedAt = null,
                StartedBy = null,
                StartNotes = null,
                CompletedDate = t.CompletedDate,
                CompletedBy = t.CompletedBy,
                CompletionNotes = t.CompletionNotes,
                ReminderEnabled = t.ReminderEnabled,
                ReminderMinutes = t.ReminderMinutes,
                IsActive = t.IsActive,
                CreatedBy = t.CreatedBy,
                CreatedAt = t.CreatedAt,
                TaskDueStatus = t.TaskDueStatus,
                DaysUntilDue = t.DaysUntilDue
            }).ToListAsync();

        if (items.Count > 0)
        {
            var taskIds = items.Select(i => i.TaskId).Distinct().ToList();
            var taskMeta = await _db.CrmTasks.AsNoTracking()
                .Where(t => taskIds.Contains(t.TaskId))
                .Select(t => new { t.TaskId, t.TaskScope, t.AssignmentSource, t.StartedAt, t.StartedBy, t.StartNotes })
                .ToListAsync();

            var taskMetaMap = taskMeta.ToDictionary(t => t.TaskId);

            items = items
                .Where(i => !taskMetaMap.TryGetValue(i.TaskId, out var meta)
                    || (meta.TaskScope != TaskScopes.General && meta.TaskScope != TaskScopes.Lead))
                .ToList();

            foreach (var item in items)
            {
                if (taskMetaMap.TryGetValue(item.TaskId, out var meta))
                {
                    item.TaskScope = meta.TaskScope;
                    item.AssignmentSource = meta.AssignmentSource;
                    item.StartedAt = meta.StartedAt;
                    item.StartedBy = meta.StartedBy;
                    item.StartNotes = meta.StartNotes;
                }
            }
        }

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

        var empIds = rawLeads.Where(r => r.Lead?.AssignedEmployeeId != null).Select(r => r.Lead!.AssignedEmployeeId!.Value).Distinct().ToList();
        var empNames = new Dictionary<int, string>();
        if (empIds.Count > 0)
        {
            empNames = await _db.Employees.AsNoTracking()
                .Where(e => empIds.Contains(e.EmployeeId))
                .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName);
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

        var leadCrmTaskQuery = _db.CrmTasks.AsNoTracking()
            .Include(t => t.AssignedToNavigation)
            .Include(t => t.TaskType)
            .Where(t => t.IsActive && t.LeadId != null && t.TaskScope == TaskScopes.Lead);

        if (crmAccess.HasValue)
            leadCrmTaskQuery = leadCrmTaskQuery.Where(t => t.CreatedAt >= crmAccess.Value);
        if (filter.AssignedTo.HasValue)
            leadCrmTaskQuery = leadCrmTaskQuery.Where(t => t.AssignedTo == filter.AssignedTo.Value);
        if (filter.TaskTypeId.HasValue)
            leadCrmTaskQuery = leadCrmTaskQuery.Where(t => t.TaskTypeId == filter.TaskTypeId.Value);
        if (!string.IsNullOrWhiteSpace(filter.Status))
            leadCrmTaskQuery = leadCrmTaskQuery.Where(t => t.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.Priority))
        {
            var dbPriority = filter.Priority == "Medium" ? "Normal" : filter.Priority;
            leadCrmTaskQuery = leadCrmTaskQuery.Where(t => t.Priority == dbPriority);
        }
        if (filter.IsOverdue == true)
            leadCrmTaskQuery = leadCrmTaskQuery.Where(t => t.DueDate < DateTime.Today && t.Status != "Completed");
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();
            leadCrmTaskQuery = leadCrmTaskQuery.Where(t => t.TaskDescription != null && t.TaskDescription.Contains(s));
        }

        var leadCrmTasksRaw = await leadCrmTaskQuery.ToListAsync();
        var leadIds = leadCrmTasksRaw.Where(t => t.LeadId.HasValue).Select(t => t.LeadId!.Value).Distinct().ToList();
        var leadLookup = leadIds.Count > 0
            ? await _db.LeadsCRMs.AsNoTracking()
                .Where(l => leadIds.Contains(l.LeadId))
                .ToDictionaryAsync(l => l.LeadId)
            : new Dictionary<int, LeadsCrm>();

        var leadCrmTasks = leadCrmTasksRaw
            .Where(t => t.LeadId.HasValue && leadLookup.ContainsKey(t.LeadId.Value))
            .Select(t =>
            {
                var lead = leadLookup[t.LeadId!.Value];
                return new TaskListDto
                {
                    TaskId = t.TaskId,
                    LeadId = t.LeadId,
                    IsLeadTask = true,
                    ClientName = lead.FullName,
                    Phone = lead.Phone,
                    CampaignName = lead.CampaignName,
                    Platform = lead.Platform,
                    AssignedTo = t.AssignedTo,
                    AssignedToName = t.AssignedToNavigation?.FullName,
                    TaskTypeId = t.TaskTypeId,
                    TaskTypeName = t.TaskType?.TaskTypeName,
                    TaskTypeNameAr = t.TaskType?.TaskTypeNameAr,
                    TaskDescription = t.TaskDescription,
                    DueDate = t.DueDate,
                    DueTime = t.DueTime,
                    Priority = t.Priority,
                    Status = t.Status,
                    StartedAt = t.StartedAt,
                    StartedBy = t.StartedBy,
                    StartNotes = t.StartNotes,
                    CompletedDate = t.CompletedDate,
                    CompletedBy = t.CompletedBy,
                    CompletionNotes = t.CompletionNotes,
                    ReminderEnabled = t.ReminderEnabled,
                    ReminderMinutes = t.ReminderMinutes,
                    IsActive = t.IsActive,
                    CreatedBy = t.CreatedBy,
                    CreatedAt = t.CreatedAt,
                    AssignmentSource = t.AssignmentSource,
                    TaskScope = t.TaskScope,
                    TaskDueStatus = t.Status == "Completed" ? "Completed" : (t.DueDate.Date < DateTime.Today ? "Overdue" : (t.DueDate.Date == DateTime.Today ? "Today" : "Upcoming")),
                    DaysUntilDue = (t.DueDate.Date - DateTime.Today).Days
                };
            })
            .ToList();

        var allItems = items.Concat(leadTasks).Concat(leadCrmTasks).ToList();

        allItems = filter.SortBy switch
        {
            "DueDate" => filter.SortDescending ? allItems.OrderByDescending(t => t.DueDate).ToList() : allItems.OrderBy(t => t.DueDate).ToList(),
            "Priority" => filter.SortDescending ? allItems.OrderByDescending(t => t.Priority).ToList() : allItems.OrderBy(t => t.Priority).ToList(),
            _ => filter.SortDescending ? allItems.OrderByDescending(t => t.CreatedAt).ToList() : allItems.OrderBy(t => t.CreatedAt).ToList()
        };

        var total = allItems.Count;
        filter.PageSize = 50000;
        var pagedItems = allItems.Skip((filter.PageNumber - 1) * filter.PageSize).Take(filter.PageSize).ToList();

        return new PagedResult<TaskListDto> { Items = pagedItems, TotalCount = total, PageNumber = filter.PageNumber, PageSize = filter.PageSize };
    }

    public async Task<List<TaskListDto>> GetByOpportunityAsync(int opportunityId)
    {
        var items = await _db.CrmTasks.AsNoTracking()
            .Where(t => t.OpportunityId == opportunityId && t.IsActive)
            .OrderByDescending(t => t.DueDate)
            .ThenByDescending(t => t.TaskId)
            .Select(t => new TaskListDto
            {
                TaskId = t.TaskId,
                OpportunityId = t.OpportunityId,
                PartyId = t.PartyId,
                ClientName = t.Party != null ? t.Party.PartyName : null,
                Phone = t.Party != null ? t.Party.Phone : null,
                AssignedTo = t.AssignedTo,
                AssignedToName = t.AssignedToNavigation.FullName,
                TaskTypeId = t.TaskTypeId,
                TaskTypeName = t.TaskType != null ? t.TaskType.TaskTypeName : null,
                TaskTypeNameAr = t.TaskType != null ? t.TaskType.TaskTypeNameAr : null,
                TaskDescription = t.TaskDescription,
                DueDate = t.DueDate,
                DueTime = t.DueTime,
                Priority = t.Priority,
                Status = t.Status,
                StartedAt = t.StartedAt,
                StartedBy = t.StartedBy,
                StartNotes = t.StartNotes,
                CompletedDate = t.CompletedDate,
                CompletedBy = t.CompletedBy,
                CompletionNotes = t.CompletionNotes,
                ReminderEnabled = t.ReminderEnabled,
                ReminderMinutes = t.ReminderMinutes,
                IsActive = t.IsActive,
                CreatedBy = t.CreatedBy,
                CreatedAt = t.CreatedAt,
                AssignmentSource = t.AssignmentSource,
                TaskScope = t.TaskScope,
                TaskDueStatus = t.Status == "Completed" ? "Completed" : (t.DueDate.Date < DateTime.Today ? "Overdue" : (t.DueDate.Date == DateTime.Today ? "Today" : "Upcoming")),
                DaysUntilDue = (t.DueDate.Date - DateTime.Today).Days
            })
            .ToListAsync();

        return items;
    }

    public async Task<List<TaskListDto>> GetByLeadAsync(int leadId)
    {
        var lead = await _db.LeadsCRMs.AsNoTracking().FirstOrDefaultAsync(l => l.LeadId == leadId);
        if (lead == null) return new();

        return await _db.CrmTasks.AsNoTracking()
            .Include(t => t.AssignedToNavigation)
            .Include(t => t.TaskType)
            .Where(t => t.LeadId == leadId && t.IsActive && t.TaskScope == TaskScopes.Lead)
            .OrderByDescending(t => t.DueDate)
            .ThenByDescending(t => t.TaskId)
            .Select(t => new TaskListDto
            {
                TaskId = t.TaskId,
                LeadId = t.LeadId,
                IsLeadTask = true,
                ClientName = lead.FullName,
                Phone = lead.Phone,
                CampaignName = lead.CampaignName,
                Platform = lead.Platform,
                AssignedTo = t.AssignedTo,
                AssignedToName = t.AssignedToNavigation.FullName,
                TaskTypeId = t.TaskTypeId,
                TaskTypeName = t.TaskType != null ? t.TaskType.TaskTypeName : null,
                TaskTypeNameAr = t.TaskType != null ? t.TaskType.TaskTypeNameAr : null,
                TaskDescription = t.TaskDescription,
                DueDate = t.DueDate,
                DueTime = t.DueTime,
                Priority = t.Priority,
                Status = t.Status,
                StartedAt = t.StartedAt,
                StartedBy = t.StartedBy,
                StartNotes = t.StartNotes,
                CompletedDate = t.CompletedDate,
                CompletedBy = t.CompletedBy,
                CompletionNotes = t.CompletionNotes,
                ReminderEnabled = t.ReminderEnabled,
                ReminderMinutes = t.ReminderMinutes,
                IsActive = t.IsActive,
                CreatedBy = t.CreatedBy,
                CreatedAt = t.CreatedAt,
                AssignmentSource = t.AssignmentSource,
                TaskScope = t.TaskScope,
                TaskDueStatus = t.Status == "Completed" ? "Completed" : (t.DueDate.Date < DateTime.Today ? "Overdue" : (t.DueDate.Date == DateTime.Today ? "Today" : "Upcoming")),
                DaysUntilDue = (t.DueDate.Date - DateTime.Today).Days
            })
            .ToListAsync();
    }

    public async Task<List<TaskListDto>> GetGeneralTasksAsync(string userName)
    {
        var currentEmployeeId = await _db.Users.AsNoTracking()
            .Where(u => u.Username == userName && u.EmployeeId != null)
            .Select(u => u.EmployeeId)
            .FirstOrDefaultAsync();

        var canManageAll = CanManageGeneralTasks();

        var query = _db.CrmTasks.AsNoTracking()
            .Include(t => t.AssignedToNavigation)
            .Include(t => t.TaskType)
            .Where(t => t.IsActive && t.TaskScope == TaskScopes.General);

        if (!canManageAll)
        {
            if (currentEmployeeId.HasValue)
                query = query.Where(t => t.AssignedTo == currentEmployeeId.Value || t.CreatedBy == userName);
            else
                query = query.Where(t => t.CreatedBy == userName);
        }

        return await query
            .OrderBy(t => t.Status == "Completed" ? 1 : 0)
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.DueTime)
            .Select(t => new TaskListDto
            {
                TaskId = t.TaskId,
                OpportunityId = t.OpportunityId,
                PartyId = t.PartyId,
                ClientName = null,
                Phone = null,
                AssignedTo = t.AssignedTo,
                AssignedToName = t.AssignedToNavigation.FullName,
                TaskTypeId = t.TaskTypeId,
                TaskTypeName = t.TaskType != null ? t.TaskType.TaskTypeName : null,
                TaskTypeNameAr = t.TaskType != null ? t.TaskType.TaskTypeNameAr : null,
                TaskDescription = t.TaskDescription,
                DueDate = t.DueDate,
                DueTime = t.DueTime,
                Priority = t.Priority,
                Status = t.Status,
                StartedAt = t.StartedAt,
                StartedBy = t.StartedBy,
                StartNotes = t.StartNotes,
                CompletedDate = t.CompletedDate,
                CompletedBy = t.CompletedBy,
                CompletionNotes = t.CompletionNotes,
                ReminderEnabled = t.ReminderEnabled,
                ReminderMinutes = t.ReminderMinutes,
                IsActive = t.IsActive,
                CreatedBy = t.CreatedBy,
                CreatedAt = t.CreatedAt,
                AssignmentSource = t.AssignmentSource,
                TaskScope = t.TaskScope,
                TaskDueStatus = t.Status == "Completed" ? "Completed" : (t.DueDate.Date < DateTime.Today ? "Overdue" : (t.DueDate.Date == DateTime.Today ? "Today" : "Upcoming")),
                DaysUntilDue = (t.DueDate.Date - DateTime.Today).Days
            })
            .ToListAsync();
    }

    public async Task<(bool Success, string Message)> AddQuickAsync(QuickTaskDto dto, string userName)
    {
        try
        {
            var task = new CrmTask
            {
                OpportunityId = dto.OpportunityId,
                PartyId = dto.PartyId,
                AssignedTo = dto.AssignedTo,
                TaskTypeId = dto.TaskTypeId,
                TaskDescription = dto.TaskDescription,
                DueDate = dto.DueDate,
                DueTime = dto.DueTime,
                Priority = NormalizePriority(dto.Priority),
                Status = "Pending",
                IsActive = true,
                AssignmentSource = dto.AssignmentSource,
                TaskScope = string.IsNullOrWhiteSpace(dto.TaskScope) ? TaskScopes.Opportunity : dto.TaskScope,
                CreatedBy = userName,
                CreatedAt = DateTime.Now
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

    public async Task<(bool Success, string Message, int? TaskId)> AddGeneralManagerOpportunityTaskAsync(GeneralManagerOpportunityTaskDto dto, string userName)
    {
        if (!CanAssignGeneralManagerTasks())
            return (false, "ليست لديك صلاحية تكليف مهام المدير العام.", null);

        if (dto.OpportunityId <= 0)
            return (false, "الفرصة غير موجودة.", null);
        if (dto.AssignedTo <= 0)
            return (false, "اختر الموظف المكلف.", null);
        if (string.IsNullOrWhiteSpace(dto.TaskDescription))
            return (false, "اكتب تعليمات المهمة.", null);

        var assignee = await _db.Employees.AsNoTracking()
            .Where(e => e.EmployeeId == dto.AssignedTo)
            .Select(e => new { e.EmployeeId, e.FullName, e.Department })
            .FirstOrDefaultAsync();

        if (assignee == null)
            return (false, "الموظف غير موجود.", null);

        var allowedDepartments = new[] { "المبيعات", "إدارة العلاقات العامة" };
        if (string.IsNullOrWhiteSpace(assignee.Department) || !allowedDepartments.Contains(assignee.Department))
            return (false, "يمكن تكليف موظفي المبيعات أو إدارة العلاقات العامة فقط.", null);

        var opportunity = await _db.SalesOpportunities.AsNoTracking()
            .Where(o => o.OpportunityId == dto.OpportunityId)
            .Select(o => new { o.OpportunityId, o.PartyId, ClientName = o.Party.PartyName })
            .FirstOrDefaultAsync();

        if (opportunity == null)
            return (false, "الفرصة غير موجودة.", null);

        var now = DateTime.Now;
        var task = new CrmTask
        {
            OpportunityId = opportunity.OpportunityId,
            PartyId = dto.PartyId > 0 ? dto.PartyId : opportunity.PartyId,
            AssignedTo = dto.AssignedTo,
            TaskTypeId = dto.TaskTypeId,
            TaskDescription = dto.TaskDescription.Trim(),
            DueDate = dto.DueDate,
            DueTime = dto.DueTime,
            Priority = NormalizePriority(dto.Priority),
            Status = "Pending",
            ReminderEnabled = true,
            IsActive = true,
            AssignmentSource = "GeneralManager",
            TaskScope = TaskScopes.Opportunity,
            CreatedBy = userName,
            CreatedAt = now
        };

        _db.CrmTasks.Add(task);

        _db.CustomerInteractions.Add(new CustomerInteraction
        {
            OpportunityId = opportunity.OpportunityId,
            PartyId = task.PartyId ?? opportunity.PartyId,
            EmployeeId = null,
            SourceId = null,
            StatusId = null,
            InteractionDate = now,
            Summary = $"تم تكليف {assignee.FullName} بواسطة المدير العام {userName}",
            StageBeforeId = null,
            StageAfterId = null,
            NextFollowUpDate = null,
            Notes = BuildGeneralManagerTaskNotes(task.TaskDescription, task.DueDate, task.DueTime, task.Priority),
            CreatedBy = userName,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();

        await _audit.LogAsync("CRM_Tasks", "GeneralManagerTaskAssignOpportunity", task.TaskId.ToString(), null,
            new
            {
                task.OpportunityId,
                task.PartyId,
                task.AssignedTo,
                Assignee = assignee.FullName,
                task.TaskTypeId,
                task.TaskDescription,
                task.DueDate,
                task.DueTime,
                task.Priority,
                task.AssignmentSource
            }, userName);

        var assignmentNotificationWarning = await NotifyGeneralManagerTaskAssignedToEmployeeAsync(task.TaskId, task.OpportunityId ?? 0, assignee.EmployeeId, assignee.FullName ?? "غير محدد", opportunity.ClientName ?? $"عميل #{opportunity.PartyId}", task.TaskDescription, task.DueDate, task.DueTime, task.Priority, userName);

        var successMessage = string.IsNullOrWhiteSpace(assignmentNotificationWarning)
            ? "تم إرسال التكليف من المدير العام بنجاح."
            : $"تم حفظ التكليف، لكن {assignmentNotificationWarning}";

        return (true, successMessage, task.TaskId);
    }

    public async Task<(bool Success, string Message, int? TaskId)> AddGeneralEmployeeTaskAsync(GeneralEmployeeTaskDto dto, string userName)
    {
        if (!CanManageGeneralTasks())
            return (false, "ليست لديك صلاحية إنشاء مهام عامة للموظفين.", null);

        if (dto.AssignedTo <= 0)
            return (false, "اختر الموظف المكلف.", null);

        if (string.IsNullOrWhiteSpace(dto.TaskDescription))
            return (false, "اكتب وصف المهمة.", null);

        var assignee = await _db.Employees.AsNoTracking()
            .Where(e => e.EmployeeId == dto.AssignedTo && (e.Status == "نشط" || e.Status == "Active"))
            .Select(e => new { e.EmployeeId, e.FullName })
            .FirstOrDefaultAsync();

        if (assignee == null)
            return (false, "الموظف غير موجود أو غير نشط.", null);

        var now = DateTime.Now;
        var assignmentSource = ResolveGeneralTaskAssignmentSource();

        var task = new CrmTask
        {
            AssignedTo = dto.AssignedTo,
            TaskTypeId = dto.TaskTypeId,
            TaskDescription = dto.TaskDescription.Trim(),
            DueDate = dto.DueDate,
            DueTime = dto.DueTime,
            Priority = NormalizePriority(dto.Priority),
            Status = "Pending",
            ReminderEnabled = true,
            IsActive = true,
            AssignmentSource = assignmentSource,
            TaskScope = TaskScopes.General,
            CreatedBy = userName,
            CreatedAt = now
        };

        _db.CrmTasks.Add(task);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("CRM_Tasks", "GeneralEmployeeTaskAssign", task.TaskId.ToString(), null,
            new
            {
                task.AssignedTo,
                Assignee = assignee.FullName,
                task.TaskTypeId,
                task.TaskDescription,
                task.DueDate,
                task.DueTime,
                task.Priority,
                task.AssignmentSource,
                task.TaskScope
            }, userName);

        var assignmentNotificationWarning = await NotifyGeneralEmployeeTaskAssignedToEmployeeAsync(
            assignee.EmployeeId,
            assignee.FullName ?? "غير محدد",
            task.TaskDescription ?? "",
            task.DueDate,
            task.DueTime,
            task.Priority,
            assignmentSource,
            userName);

        var successMessage = string.IsNullOrWhiteSpace(assignmentNotificationWarning)
            ? "تم إرسال المهمة العامة بنجاح."
            : $"تم حفظ المهمة العامة، لكن {assignmentNotificationWarning}";

        return (true, successMessage, task.TaskId);
    }

    public async Task<(bool Success, string Message, int? TaskId)> AddLeadTaskAsync(LeadTaskDto dto, string userName)
    {
        if (!CanManageLeadTasks())
            return (false, "ليست لديك صلاحية إنشاء تكليفات على الليدز.", null);

        if (dto.LeadId <= 0)
            return (false, "الليد غير موجودة.", null);
        if (dto.AssignedTo <= 0)
            return (false, "اختر الموظف المكلف.", null);
        if (string.IsNullOrWhiteSpace(dto.TaskDescription))
            return (false, "اكتب وصف المهمة.", null);

        var lead = await _db.LeadsCRMs.AsNoTracking()
            .Where(l => l.LeadId == dto.LeadId)
            .Select(l => new { l.LeadId, l.FullName, l.Phone, l.LeadStatus, l.IsConverted })
            .FirstOrDefaultAsync();

        if (lead == null)
            return (false, "الليد غير موجودة.", null);
        if (lead.IsConverted || lead.LeadStatus == "محول")
            return (false, "لا يمكن إنشاء تكليف على ليد تم تحويلها بالفعل.", null);
        if (lead.LeadStatus == "مرفوض")
            return (false, "لا يمكن إنشاء تكليف على ليد مرفوضة.", null);

        var assignee = await _db.Employees.AsNoTracking()
            .Where(e => e.EmployeeId == dto.AssignedTo && (e.Status == "نشط" || e.Status == "Active"))
            .Select(e => new { e.EmployeeId, e.FullName, e.Department })
            .FirstOrDefaultAsync();

        if (assignee == null)
            return (false, "الموظف غير موجود أو غير نشط.", null);

        var allowedDepartments = new[] { "المبيعات", "إدارة العلاقات العامة" };
        if (string.IsNullOrWhiteSpace(assignee.Department) || !allowedDepartments.Contains(assignee.Department))
            return (false, "يمكن تكليف موظفي المبيعات أو إدارة العلاقات العامة فقط على الليدز.", null);

        var now = DateTime.Now;
        var assignmentSource = ResolveLeadTaskAssignmentSource();

        var task = new CrmTask
        {
            LeadId = dto.LeadId,
            AssignedTo = dto.AssignedTo,
            TaskTypeId = dto.TaskTypeId,
            TaskDescription = dto.TaskDescription.Trim(),
            DueDate = dto.DueDate,
            DueTime = dto.DueTime,
            Priority = NormalizePriority(dto.Priority),
            Status = "Pending",
            ReminderEnabled = true,
            IsActive = true,
            AssignmentSource = assignmentSource,
            TaskScope = TaskScopes.Lead,
            CreatedBy = userName,
            CreatedAt = now
        };

        _db.CrmTasks.Add(task);

        _db.LeadInteractions.Add(new LeadInteraction
        {
            LeadId = dto.LeadId,
            EmployeeId = dto.AssignedTo,
            InteractionType = LeadInteractionTypes.Assigned,
            InteractionDate = now,
            Summary = $"تم تكليف {assignee.FullName} على الليد بواسطة {GetAssignmentSourceLabel(assignmentSource)}",
            Notes = BuildLeadTaskNotes(task.TaskDescription, task.DueDate, task.DueTime, task.Priority),
            OldLeadStatus = lead.LeadStatus,
            NewLeadStatus = lead.LeadStatus,
            IsSystemGenerated = true,
            CreatedBy = userName,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();

        await _audit.LogAsync("CRM_Tasks", "LeadTaskAssign", task.TaskId.ToString(), null,
            new
            {
                task.LeadId,
                task.AssignedTo,
                Assignee = assignee.FullName,
                task.TaskTypeId,
                task.TaskDescription,
                task.DueDate,
                task.DueTime,
                task.Priority,
                task.AssignmentSource,
                task.TaskScope
            }, userName);

        var assignmentNotificationWarning = await NotifyLeadTaskAssignedToEmployeeAsync(
            task.LeadId ?? 0,
            assignee.EmployeeId,
            assignee.FullName ?? "غير محدد",
            lead.FullName ?? $"Lead #{dto.LeadId}",
            task.TaskDescription ?? string.Empty,
            task.DueDate,
            task.DueTime,
            task.Priority,
            assignmentSource,
            userName);

        var successMessage = string.IsNullOrWhiteSpace(assignmentNotificationWarning)
            ? "تم إرسال تكليف الليد بنجاح."
            : $"تم حفظ تكليف الليد، لكن {assignmentNotificationWarning}";

        return (true, successMessage, task.TaskId);
    }

    public async Task<(bool Success, string Message)> StartAsync(int taskId, string? notes, string userName)
    {
        if (taskId > 1000000)
            return (false, "هذا الإجراء متاح للمهام المرتبطة بالفرص فقط.");

        var task = await _db.CrmTasks.FirstOrDefaultAsync(t => t.TaskId == taskId && t.IsActive);
        if (task == null) return (false, "المهمة غير موجودة");
        if (task.Status == "Completed") return (false, "تم إكمال المهمة بالفعل");
        if (task.Status == "In Progress") return (true, "المهمة بالفعل قيد التنفيذ");
        if (string.IsNullOrWhiteSpace(notes)) return (false, "برجاء كتابة ماذا ستبدأ تنفيذه قبل بدء المهمة.");

        task.Status = "In Progress";
        task.StartedAt = DateTime.Now;
        task.StartedBy = userName;
        task.StartNotes = notes.Trim();

        await _db.SaveChangesAsync();

        if (string.Equals(task.AssignmentSource, "GeneralManager", StringComparison.OrdinalIgnoreCase) && task.OpportunityId.HasValue)
        {
            var assigneeName = await _db.Employees.AsNoTracking().Where(e => e.EmployeeId == task.AssignedTo).Select(e => e.FullName).FirstOrDefaultAsync() ?? "الموظف";
            var clientName = await _db.Parties.AsNoTracking().Where(p => p.PartyId == (task.PartyId ?? 0)).Select(p => p.PartyName).FirstOrDefaultAsync() ?? "غير محدد";
            var now = DateTime.Now;

            _db.CustomerInteractions.Add(new CustomerInteraction
            {
                OpportunityId = task.OpportunityId.Value,
                PartyId = task.PartyId ?? 0,
                EmployeeId = null,
                SourceId = null,
                StatusId = null,
                InteractionDate = now,
                Summary = $"بدأ {assigneeName} تنفيذ تكليف المدير العام",
                StageBeforeId = null,
                StageAfterId = null,
                NextFollowUpDate = null,
                Notes = string.IsNullOrWhiteSpace(notes) ? task.TaskDescription : notes.Trim(),
                CreatedBy = userName,
                CreatedAt = now
            });
            await _db.SaveChangesAsync();

            await _audit.LogAsync("CRM_Tasks", "GeneralManagerTaskStart", task.TaskId.ToString(), null,
                new { task.OpportunityId, task.AssignedTo, StartedBy = userName, Notes = notes }, userName);

            await NotifyGeneralManagerTaskActionAsync(task, assigneeName, clientName, false, userName, notes);
        }
        else if (string.Equals(task.TaskScope, TaskScopes.General, StringComparison.OrdinalIgnoreCase))
        {
            var assigneeName = await _db.Employees.AsNoTracking()
                .Where(e => e.EmployeeId == task.AssignedTo)
                .Select(e => e.FullName)
                .FirstOrDefaultAsync() ?? "الموظف";

            await _audit.LogAsync("CRM_Tasks", "GeneralEmployeeTaskStart", task.TaskId.ToString(), null,
                new { task.AssignedTo, StartedBy = userName, Notes = notes, task.TaskDescription }, userName);

            await NotifyGeneralTaskActionAsync(task, assigneeName, false, userName, notes);
        }
        else if (string.Equals(task.TaskScope, TaskScopes.Lead, StringComparison.OrdinalIgnoreCase) && task.LeadId.HasValue)
        {
            var assigneeName = await _db.Employees.AsNoTracking()
                .Where(e => e.EmployeeId == task.AssignedTo)
                .Select(e => e.FullName)
                .FirstOrDefaultAsync() ?? "الموظف";
            var leadName = await _db.LeadsCRMs.AsNoTracking()
                .Where(l => l.LeadId == task.LeadId.Value)
                .Select(l => l.FullName)
                .FirstOrDefaultAsync() ?? $"Lead #{task.LeadId.Value}";
            var now = DateTime.Now;

            _db.LeadInteractions.Add(new LeadInteraction
            {
                LeadId = task.LeadId.Value,
                EmployeeId = task.AssignedTo,
                InteractionType = LeadInteractionTypes.Note,
                InteractionDate = now,
                Summary = $"بدأ {assigneeName} تنفيذ تكليف على الليد",
                Notes = string.IsNullOrWhiteSpace(notes) ? task.TaskDescription : notes.Trim(),
                IsSystemGenerated = true,
                CreatedBy = userName,
                CreatedAt = now
            });
            await _db.SaveChangesAsync();

            await _audit.LogAsync("CRM_Tasks", "LeadTaskStart", task.TaskId.ToString(), null,
                new { task.LeadId, task.AssignedTo, StartedBy = userName, Notes = notes, task.TaskDescription }, userName);

            await NotifyLeadTaskActionAsync(task, assigneeName, leadName, false, userName, notes);
        }

        return (true, "تم تحويل المهمة إلى جاري التنفيذ");
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
            if (string.IsNullOrWhiteSpace(notes)) return (false, "برجاء كتابة ما الذي تم تنفيذه قبل إكمال المهمة.");
            task.Status = "Completed";
            task.CompletedDate = DateTime.Now;
            task.CompletedBy = userName;
            task.CompletionNotes = notes.Trim();
            await _db.SaveChangesAsync();

            if (string.Equals(task.AssignmentSource, "GeneralManager", StringComparison.OrdinalIgnoreCase) && task.OpportunityId.HasValue)
            {
                var assigneeName = await _db.Employees.AsNoTracking().Where(e => e.EmployeeId == task.AssignedTo).Select(e => e.FullName).FirstOrDefaultAsync() ?? "الموظف";
                var clientName = await _db.Parties.AsNoTracking().Where(p => p.PartyId == (task.PartyId ?? 0)).Select(p => p.PartyName).FirstOrDefaultAsync() ?? "غير محدد";
                var now = DateTime.Now;

                _db.CustomerInteractions.Add(new CustomerInteraction
                {
                    OpportunityId = task.OpportunityId.Value,
                    PartyId = task.PartyId ?? 0,
                    EmployeeId = null,
                    SourceId = null,
                    StatusId = null,
                    InteractionDate = now,
                    Summary = $"أنهى {assigneeName} تكليف المدير العام",
                    StageBeforeId = null,
                    StageAfterId = null,
                    NextFollowUpDate = null,
                    Notes = string.IsNullOrWhiteSpace(notes) ? task.TaskDescription : notes.Trim(),
                    CreatedBy = userName,
                    CreatedAt = now
                });
                await _db.SaveChangesAsync();

                await _audit.LogAsync("CRM_Tasks", "GeneralManagerTaskComplete", task.TaskId.ToString(), null,
                    new { task.OpportunityId, task.AssignedTo, CompletedBy = userName, Notes = notes }, userName);

                await NotifyGeneralManagerTaskActionAsync(task, assigneeName, clientName, true, userName, notes);
            }
            else if (string.Equals(task.TaskScope, TaskScopes.General, StringComparison.OrdinalIgnoreCase))
            {
                var assigneeName = await _db.Employees.AsNoTracking()
                    .Where(e => e.EmployeeId == task.AssignedTo)
                    .Select(e => e.FullName)
                    .FirstOrDefaultAsync() ?? "الموظف";

                await _audit.LogAsync("CRM_Tasks", "GeneralEmployeeTaskComplete", task.TaskId.ToString(), null,
                    new { task.AssignedTo, CompletedBy = userName, Notes = notes, task.TaskDescription }, userName);

                await NotifyGeneralTaskActionAsync(task, assigneeName, true, userName, notes);
            }
            else if (string.Equals(task.TaskScope, TaskScopes.Lead, StringComparison.OrdinalIgnoreCase) && task.LeadId.HasValue)
            {
                var assigneeName = await _db.Employees.AsNoTracking()
                    .Where(e => e.EmployeeId == task.AssignedTo)
                    .Select(e => e.FullName)
                    .FirstOrDefaultAsync() ?? "الموظف";
                var leadName = await _db.LeadsCRMs.AsNoTracking()
                    .Where(l => l.LeadId == task.LeadId.Value)
                    .Select(l => l.FullName)
                    .FirstOrDefaultAsync() ?? $"Lead #{task.LeadId.Value}";
                var now = DateTime.Now;

                _db.LeadInteractions.Add(new LeadInteraction
                {
                    LeadId = task.LeadId.Value,
                    EmployeeId = task.AssignedTo,
                    InteractionType = LeadInteractionTypes.Note,
                    InteractionDate = now,
                    Summary = $"أنهى {assigneeName} تكليف الليد",
                    Notes = string.IsNullOrWhiteSpace(notes) ? task.TaskDescription : notes.Trim(),
                    IsSystemGenerated = true,
                    CreatedBy = userName,
                    CreatedAt = now
                });
                await _db.SaveChangesAsync();

                await _audit.LogAsync("CRM_Tasks", "LeadTaskComplete", task.TaskId.ToString(), null,
                    new { task.LeadId, task.AssignedTo, CompletedBy = userName, Notes = notes, task.TaskDescription }, userName);

                await NotifyLeadTaskActionAsync(task, assigneeName, leadName, true, userName, notes);
            }

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

    public async Task<(bool Success, string Message)> CloseAllTasksForOpportunityAsync(int opportunityId, string status, string notes, string userName)
    {
        var tasks = await _db.CrmTasks
            .Where(t => t.OpportunityId == opportunityId && (t.Status == "Pending" || t.Status == "In Progress"))
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

    private async Task<string?> NotifyGeneralManagerTaskAssignedToEmployeeAsync(int taskId, int opportunityId, int assignedEmployeeId, string assignedEmployeeName, string clientName, string taskDescription, DateTime dueDate, TimeOnly? dueTime, string priority, string actor)
    {
        try
        {
            var user = await _db.Users.AsNoTracking()
                .Where(u => u.EmployeeId == assignedEmployeeId && u.IsActive == true)
                .Select(u => u.Username)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(user))
            {
                _logger.LogWarning("General manager task {TaskId} assigned to employee {EmployeeId} but no active user is linked.", taskId, assignedEmployeeId);
                return $"الموظف {assignedEmployeeName} لا يملك حساب مستخدم نشط مربوط، لذلك لم يتم إرسال إشعار إليه.";
            }

            var dueTimeText = dueTime.HasValue ? dueTime.Value.ToString("HH:mm") : null;
            var dueText = dueTime.HasValue
                ? $"{dueDate:yyyy/MM/dd} الساعة {dueTimeText}"
                : dueDate.ToString("yyyy/MM/dd");

            var message = $"تم تكليفك من المدير العام بمتابعة الفرصة الخاصة بالعميل {clientName}.\nالوصف: {taskDescription}\nالأولوية: {GetPriorityLabel(priority)}\nالمطلوب قبل: {dueText}";

            await _notify.AddAsync(
                title: "📌 تكليف من المدير العام",
                message: message,
                recipientUser: user,
                createdBy: actor,
                formName: "crm/opportunities",
                relatedTable: "SalesOpportunities",
                relatedId: opportunityId);

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify assignee for general manager task. TaskId={TaskId}", taskId);
            return "حدثت مشكلة أثناء محاولة إرسال الإشعار للمستخدم المكلف.";
        }
    }

    private async Task NotifyGeneralManagerTaskActionAsync(CrmTask task, string assigneeName, string clientName, bool isCompleted, string actor, string? notes)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(task.CreatedBy))
            {
                _logger.LogWarning("General manager task action notification skipped because task creator is empty. TaskId={TaskId}", task.TaskId);
                return;
            }

            var title = isCompleted ? "✅ تم تنفيذ تكليفك" : "👀 بدأ تنفيذ تكليفك";
            var message = isCompleted
                ? $"قام {assigneeName} بتنفيذ التكليف الخاص بالعميل {clientName}."
                : $"بدأ {assigneeName} تنفيذ التكليف الخاص بالعميل {clientName}.";

            if (!string.IsNullOrWhiteSpace(task.TaskDescription))
                message += $"\nالتكليف: {task.TaskDescription}";

            if (!string.IsNullOrWhiteSpace(notes))
                message += $"\nملاحظات المنفذ: {notes.Trim()}";

            await _notify.AddAsync(
                title: title,
                message: message,
                recipientUser: task.CreatedBy,
                createdBy: actor,
                formName: "crm/opportunities",
                relatedTable: "SalesOpportunities",
                relatedId: task.OpportunityId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify task creator about task action. TaskId={TaskId}", task.TaskId);
        }
    }

    private async Task<string?> NotifyLeadTaskAssignedToEmployeeAsync(int leadId, int assignedEmployeeId, string assignedEmployeeName, string leadName, string taskDescription, DateTime dueDate, TimeOnly? dueTime, string priority, string assignmentSource, string actor)
    {
        try
        {
            var user = await _db.Users.AsNoTracking()
                .Where(u => u.EmployeeId == assignedEmployeeId && u.IsActive == true)
                .Select(u => u.Username)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(user))
            {
                _logger.LogWarning("Lead task assigned to employee {EmployeeId} but no active user is linked. LeadId={LeadId}", assignedEmployeeId, leadId);
                return $"الموظف {assignedEmployeeName} لا يملك حساب مستخدم نشط مربوط، لذلك لم يتم إرسال إشعار إليه.";
            }

            var dueTimeText = dueTime.HasValue ? dueTime.Value.ToString("HH:mm") : null;
            var dueText = dueTime.HasValue
                ? $"{dueDate:yyyy/MM/dd} الساعة {dueTimeText}"
                : dueDate.ToString("yyyy/MM/dd");

            var sourceLabel = GetAssignmentSourceLabel(assignmentSource);
            var message = $"تم تكليفك على الليد {leadName} من {sourceLabel}.\nالوصف: {taskDescription}\nالأولوية: {GetPriorityLabel(priority)}\nالمطلوب قبل: {dueText}";

            await _notify.AddAsync(
                title: "📌 تكليف جديد على ليد",
                message: message,
                recipientUser: user,
                createdBy: actor,
                formName: "crm/leads/details",
                relatedTable: "LeadsCRM",
                relatedId: leadId);

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify assignee for lead task. LeadId={LeadId}, EmployeeId={EmployeeId}", leadId, assignedEmployeeId);
            return "حدثت مشكلة أثناء محاولة إرسال الإشعار للمستخدم المكلف.";
        }
    }

    private async Task NotifyLeadTaskActionAsync(CrmTask task, string assigneeName, string leadName, bool isCompleted, string actor, string? notes)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(task.CreatedBy) || !task.LeadId.HasValue)
            {
                _logger.LogWarning("Lead task action notification skipped because creator or LeadId is empty. TaskId={TaskId}", task.TaskId);
                return;
            }

            var title = isCompleted ? "✅ تم تنفيذ تكليف الليد" : "👀 بدأ تنفيذ تكليف الليد";
            var message = isCompleted
                ? $"قام {assigneeName} بتنفيذ التكليف الخاص بالليد {leadName}."
                : $"بدأ {assigneeName} تنفيذ التكليف الخاص بالليد {leadName}.";

            if (!string.IsNullOrWhiteSpace(task.TaskDescription))
                message += $"\nالتكليف: {task.TaskDescription}";

            if (!string.IsNullOrWhiteSpace(notes))
                message += $"\nملاحظات المنفذ: {notes.Trim()}";

            await _notify.AddAsync(
                title: title,
                message: message,
                recipientUser: task.CreatedBy,
                createdBy: actor,
                formName: "crm/leads/details",
                relatedTable: "LeadsCRM",
                relatedId: task.LeadId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify task creator about lead task action. TaskId={TaskId}", task.TaskId);
        }
    }

    private async Task<string?> NotifyGeneralEmployeeTaskAssignedToEmployeeAsync(int assignedEmployeeId, string assignedEmployeeName, string taskDescription, DateTime dueDate, TimeOnly? dueTime, string priority, string assignmentSource, string actor)
    {
        try
        {
            var user = await _db.Users.AsNoTracking()
                .Where(u => u.EmployeeId == assignedEmployeeId && u.IsActive == true)
                .Select(u => u.Username)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(user))
            {
                _logger.LogWarning("General employee task assigned to employee {EmployeeId} but no active user is linked.", assignedEmployeeId);
                return $"الموظف {assignedEmployeeName} لا يملك حساب مستخدم نشط مربوط، لذلك لم يتم إرسال إشعار إليه.";
            }

            var dueTimeText = dueTime.HasValue ? dueTime.Value.ToString("HH:mm") : null;
            var dueText = dueTime.HasValue
                ? $"{dueDate:yyyy/MM/dd} الساعة {dueTimeText}"
                : dueDate.ToString("yyyy/MM/dd");

            var sourceLabel = GetAssignmentSourceLabel(assignmentSource);
            var message = $"تم تكليفك بمهمة عامة من {sourceLabel}.\nالوصف: {taskDescription}\nالأولوية: {GetPriorityLabel(priority)}\nالمطلوب قبل: {dueText}";

            await _notify.AddAsync(
                title: "📌 مهمة عامة جديدة",
                message: message,
                recipientUser: user,
                createdBy: actor,
                formName: "crm/general-tasks",
                relatedTable: "CRM_Tasks");

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify assignee for general employee task. EmployeeId={EmployeeId}", assignedEmployeeId);
            return "حدثت مشكلة أثناء محاولة إرسال الإشعار للمستخدم المكلف.";
        }
    }

    private async Task NotifyGeneralTaskActionAsync(CrmTask task, string assigneeName, bool isCompleted, string actor, string? notes)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(task.CreatedBy))
            {
                _logger.LogWarning("General task action notification skipped because task creator is empty. TaskId={TaskId}", task.TaskId);
                return;
            }

            var title = isCompleted ? "✅ تم تنفيذ المهمة العامة" : "👀 بدأ تنفيذ المهمة العامة";
            var message = isCompleted
                ? $"قام {assigneeName} بتنفيذ المهمة العامة التي أنشأتها."
                : $"بدأ {assigneeName} تنفيذ المهمة العامة التي أنشأتها.";

            if (!string.IsNullOrWhiteSpace(task.TaskDescription))
                message += $"\nالمهمة: {task.TaskDescription}";

            if (!string.IsNullOrWhiteSpace(notes))
                message += $"\nملاحظات المنفذ: {notes.Trim()}";

            await _notify.AddAsync(
                title: title,
                message: message,
                recipientUser: task.CreatedBy,
                createdBy: actor,
                formName: "crm/general-tasks",
                relatedTable: "CRM_Tasks");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify task creator about general task action. TaskId={TaskId}", task.TaskId);
        }
    }

    private static string NormalizePriority(string? priority) => priority switch
    {
        "High" => "High",
        "Low" => "Low",
        "Medium" => "Normal",
        "Normal" => "Normal",
        _ => "Normal"
    };

    private static string GetPriorityLabel(string? priority) => NormalizePriority(priority) switch
    {
        "High" => "عاجلة",
        "Low" => "منخفضة",
        _ => "متوسطة"
    };

    private static string BuildGeneralManagerTaskNotes(string? description, DateTime dueDate, TimeOnly? dueTime, string? priority)
    {
        var dueText = dueTime.HasValue ? $"{dueDate:yyyy/MM/dd} - {dueTime.Value.ToString("HH:mm")}" : dueDate.ToString("yyyy/MM/dd");
        return $"تكليف من المدير العام | الأولوية: {GetPriorityLabel(priority)} | التنفيذ خلال: {dueText}" +
               (string.IsNullOrWhiteSpace(description) ? string.Empty : $" | التعليمات: {description}");
    }

    private static string BuildLeadTaskNotes(string? description, DateTime dueDate, TimeOnly? dueTime, string? priority)
    {
        var dueText = dueTime.HasValue ? $"{dueDate:yyyy/MM/dd} - {dueTime.Value.ToString("HH:mm")}" : dueDate.ToString("yyyy/MM/dd");
        return $"تكليف على ليد | الأولوية: {GetPriorityLabel(priority)} | التنفيذ خلال: {dueText}" +
               (string.IsNullOrWhiteSpace(description) ? string.Empty : $" | التعليمات: {description}");
    }

    private static string GetAssignmentSourceLabel(string? assignmentSource) => assignmentSource switch
    {
        SystemRoles.GeneralManager => "المدير العام",
        SystemRoles.AccountManager => "مدير الحسابات",
        SystemRoles.Admin or "Admin" => "الإدارة",
        _ => "الإدارة"
    };

    private string ResolveGeneralTaskAssignmentSource()
    {
        var user = _http.HttpContext?.User;
        if (user?.IsInRole(SystemRoles.GeneralManager) == true) return SystemRoles.GeneralManager;
        if (user?.IsInRole(SystemRoles.AccountManager) == true) return SystemRoles.AccountManager;
        if (user?.IsInRole(SystemRoles.Admin) == true || user?.IsInRole("Admin") == true) return SystemRoles.Admin;
        return "GeneralTask";
    }

    private string ResolveLeadTaskAssignmentSource()
    {
        var user = _http.HttpContext?.User;
        if (user?.IsInRole(SystemRoles.GeneralManager) == true) return SystemRoles.GeneralManager;
        if (user?.IsInRole(SystemRoles.AccountManager) == true) return SystemRoles.AccountManager;
        if (user?.IsInRole(SystemRoles.Admin) == true || user?.IsInRole("Admin") == true) return SystemRoles.Admin;
        return "LeadTask";
    }

    private bool CanManageGeneralTasks()
    {
        var user = _http.HttpContext?.User;
        return user?.IsInRole(SystemRoles.GeneralManager) == true
            || user?.IsInRole(SystemRoles.AccountManager) == true
            || user?.IsInRole(SystemRoles.Admin) == true
            || user?.IsInRole("Admin") == true;
    }

    private bool CanManageLeadTasks()
    {
        var user = _http.HttpContext?.User;
        return user?.IsInRole(SystemRoles.GeneralManager) == true
            || user?.IsInRole(SystemRoles.AccountManager) == true
            || user?.IsInRole(SystemRoles.Admin) == true
            || user?.IsInRole("Admin") == true;
    }

    private bool CanAssignGeneralManagerTasks()
    {
        var user = _http.HttpContext?.User;
        return user?.IsInRole(SystemRoles.GeneralManager) == true;
    }
}
