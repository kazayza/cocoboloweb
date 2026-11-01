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

        // 🏢 نطاق B2B (مصدر الحقيقة: Parties.IsB2B)
        var kind = B2bViewScope.KindOf(_http?.HttpContext?.User);
        if (kind != B2bViewScope.Kind.All)
        {
            var b2b = await B2bViewScope.ResolveB2bAsync(_db);
            switch (kind)
            {
                case B2bViewScope.Kind.ExcludeB2B:
                    query = query.Where(t =>
                        !(t.PartyId.HasValue && b2b.PartyIds.Contains(t.PartyId.Value))
                        && !(t.OpportunityId.HasValue && b2b.OpportunityIds.Contains(t.OpportunityId.Value)));
                    break;
                case B2bViewScope.Kind.AllB2B:
                    query = query.Where(t =>
                        (t.PartyId.HasValue && b2b.PartyIds.Contains(t.PartyId.Value))
                        || (t.OpportunityId.HasValue && b2b.OpportunityIds.Contains(t.OpportunityId.Value)));
                    break;
                case B2bViewScope.Kind.OwnB2B:
                    var emp = await B2bViewScope.ResolveEmployeeIdAsync(_db, _http?.HttpContext?.User);
                    if (emp <= 0)
                    {
                        query = query.Where(t => false);
                    }
                    else
                    {
                        var ownOpps = await B2bViewScope.ResolveOwnB2bOpportunitiesAsync(_db, b2b.PartyIds, emp);
                        query = query.Where(t =>
                            (t.OpportunityId.HasValue && ownOpps.Contains(t.OpportunityId.Value))
                            || (!t.OpportunityId.HasValue && t.PartyId.HasValue
                                && b2b.PartyIds.Contains(t.PartyId.Value) && t.AssignedTo == emp));
                    }
                    break;
            }
        }

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

        return await ProjectTasksAsync(query);
    }

    /// <summary>إسقاط موحّد CrmTask ← TaskListDto لمسارات التكليفات (العامة + «مهامي»)</summary>
    private static async Task<List<TaskListDto>> ProjectTasksAsync(IQueryable<CrmTask> query)
    {
        return await query
            .OrderBy(t => (t.Status == "Completed" || t.Status == "Cancelled") ? 1 : 0)
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
                CancelledAt = t.CancelledAt,
                CancelledBy = t.CancelledBy,
                CancellationReason = t.CancellationReason,
                AcknowledgedAt = t.AcknowledgedAt,
                AcknowledgedBy = t.AcknowledgedBy,
                ReturnedAt = t.ReturnedAt,
                ReturnedBy = t.ReturnedBy,
                ReturnReason = t.ReturnReason,
                CompletionLink = t.CompletionLink,
                LinkedEntityType = t.LinkedEntityType,
                LinkedEntityId = t.LinkedEntityId,
                LinkedEntityTitle = t.LinkedEntityTitle,
                TaskDueStatus = t.Status == "Completed" ? "Completed" : (t.DueDate.Date < DateTime.Today ? "Overdue" : (t.DueDate.Date == DateTime.Today ? "Today" : "Upcoming")),
                DaysUntilDue = (t.DueDate.Date - DateTime.Today).Days
            })
            .ToListAsync();
    }

    // ─── تطوير التكليفات العامة: الربط بالمستندات + «مهامي» ─────────────────────

    /// <summary>صلاحية إنشاء التكليفات العامة: الأدوار الثلاثة أو Claim الإنشاء</summary>
    private bool CanCreateGeneralTasks()
        => GeneralTaskPermissions.CanCreate(_http.HttpContext?.User);

    /// <summary>
    /// التحقق من مستند مرتبط وحسم عنوانه الرسمي (يُستخدم من حوار الإنشاء
    /// للمعاينة، ومن الإنشاء نفسه قبل الحفظ — العنوان لقطة لا تتغير لاحقًا).
    /// </summary>
    public async Task<LinkedEntityLookupDto?> ResolveLinkedEntityAsync(string entityType, int entityId)
        => await ResolveLinkedEntityCoreAsync(entityType, entityId);

    private async Task<LinkedEntityLookupDto?> ResolveLinkedEntityCoreAsync(string? entityType, int entityId)
    {
        if (!GeneralTaskLinks.IsValidType(entityType) || entityId <= 0) return null;

        string? title = entityType switch
        {
            GeneralTaskLinks.Customer => await _db.Parties.AsNoTracking()
                .Where(p => p.PartyId == entityId)
                .Select(p => new { Name = p.PartyName })
                .FirstOrDefaultAsync() is { } cust ? $"العميل {cust.Name}" : null,

            GeneralTaskLinks.Invoice => await TransactionTitleAsync(entityId, "فاتورة"),
            GeneralTaskLinks.Delivery => await TransactionTitleAsync(entityId, "تسليمة فاتورة"),
            GeneralTaskLinks.JobOrder => await TransactionTitleAsync(entityId, "أمر تشغيل"),

            GeneralTaskLinks.Quotation => await _db.Quotations.AsNoTracking()
                .Where(q => q.QuotationId == entityId)
                .Select(q => new { q.ReferenceNumber, q.PartyId })
                .FirstOrDefaultAsync() is { } quo
                ? $"عرض سعر {quo.ReferenceNumber ?? $"#{entityId}"} — {await PartyNameAsync(quo.PartyId)}"
                : null,

            GeneralTaskLinks.Maintenance => await _db.DeliveryMaintenanceItems.AsNoTracking()
                .Where(m => m.MaintenanceId == entityId)
                .Select(m => new { m.MaintenanceId, m.IssueDescription })
                .FirstOrDefaultAsync() is { } mnt
                ? $"صيانة تسليم #{mnt.MaintenanceId} — {mnt.IssueDescription}"
                : null,

            _ => null
        };

        if (string.IsNullOrWhiteSpace(title)) return null;
        if (title.Length > 250) title = title[..250];

        return new LinkedEntityLookupDto { EntityType = entityType!, EntityId = entityId, Title = title };
    }

    private async Task<string?> TransactionTitleAsync(int transactionId, string prefix)
    {
        var row = await _db.Transactions.AsNoTracking()
            .Where(t => t.TransactionId == transactionId)
            .Select(t => new { t.ReferenceNumber, t.PartyId })
            .FirstOrDefaultAsync();
        if (row == null) return null;
        return $"{prefix} {row.ReferenceNumber ?? $"#{transactionId}"} — {await PartyNameAsync(row.PartyId)}";
    }

    private async Task<string> PartyNameAsync(int partyId)
        => await _db.Parties.AsNoTracking()
            .Where(p => p.PartyId == partyId)
            .Select(p => p.PartyName)
            .FirstOrDefaultAsync() ?? "غير محدد";

    /// <summary>
    /// قوائم «مهامي»: التكليفات العامة فقط (TaskScope = General) بقرار المالك —
    /// مهام الـCRM والليدز والفرص لها شاشاتها المستقلة ولا تظهر هنا نهائيًا.
    /// وارد = ما هو مُسند إليّ من التكليفات العامة، صادر = ما أنشأته منها.
    /// </summary>
    public async Task<MyTasksDto> GetMyTasksAsync(string userName)
    {
        var result = new MyTasksDto();
        var empId = await ResolveEmployeeIdAsync(userName);

        var baseQuery = _db.CrmTasks.AsNoTracking()
            .Include(t => t.AssignedToNavigation)
            .Include(t => t.TaskType)
            .Where(t => t.IsActive && t.TaskScope == TaskScopes.General);

        if (empId.HasValue)
            result.Assigned = await ProjectTasksAsync(baseQuery.Where(t => t.AssignedTo == empId.Value));

        result.Created = await ProjectTasksAsync(baseQuery.Where(t => t.CreatedBy == userName));

        if (empId.HasValue)
            result.Mentioned = await GetMyMentionedTasksAsync(empId.Value);

        return result;
    }

    /// <summary>
    /// تكليفات ذُكر فيها الموظف (@) في أي تعليق — الأحدث ذكرًا أولًا.
    /// تتحلل بأمان لقائمة فارغة عند غياب جدولي الخيط (قبل تشغيل سكربت المنصة).
    /// </summary>
    private async Task<List<TaskListDto>> GetMyMentionedTasksAsync(int employeeId)
    {
        try
        {
            var latestByTask = await _db.TaskCommentMentions.AsNoTracking()
                .Where(m => m.MentionedEmployeeId == employeeId)
                .GroupBy(m => m.Comment.TaskId)
                .Select(g => new { TaskId = g.Key, Latest = g.Max(m => m.Comment.CreatedAt) })
                .OrderByDescending(x => x.Latest)
                .Take(50)
                .ToListAsync();

            if (latestByTask.Count == 0) return new();

            var ids = latestByTask.Select(x => x.TaskId).ToList();
            var tasks = await ProjectTasksAsync(_db.CrmTasks.AsNoTracking()
                .Include(t => t.AssignedToNavigation)
                .Include(t => t.TaskType)
                .Where(t => t.IsActive && t.TaskScope == TaskScopes.General && ids.Contains(t.TaskId)));

            var order = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
            return tasks.OrderBy(t => order.TryGetValue(t.TaskId, out var i) ? i : int.MaxValue).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mentioned-tasks query degraded (TaskComments tables may be missing).");
            return new();
        }
    }

    /// <summary>عدّادات «مهامي» لشريط الصفحة الرئيسية</summary>
    public async Task<MyTasksSummaryDto> GetMyTasksSummaryAsync(string userName)
    {
        var dto = new MyTasksSummaryDto();
        var today = DateTime.Today;
        var empId = await ResolveEmployeeIdAsync(userName);

        if (empId.HasValue)
        {
            var mine = _db.CrmTasks.AsNoTracking()
                .Where(t => t.IsActive && t.TaskScope == TaskScopes.General && t.AssignedTo == empId.Value
                            && t.Status != "Completed" && t.Status != "Cancelled");
            dto.OpenAssigned = await mine.CountAsync();
            dto.OverdueAssigned = await mine.CountAsync(t => t.DueDate < today);
        }

        dto.OpenCreated = await _db.CrmTasks.AsNoTracking()
            .CountAsync(t => t.IsActive && t.TaskScope == TaskScopes.General && t.CreatedBy == userName
                             && t.Status != "Completed" && t.Status != "Cancelled");

        if (empId.HasValue)
        {
            try
            {
                dto.MentionedTasks = await _db.TaskCommentMentions.AsNoTracking()
                    .Where(m => m.MentionedEmployeeId == empId.Value && m.Comment.Task.TaskScope == TaskScopes.General)
                    .Select(m => m.Comment.TaskId)
                    .Distinct()
                    .CountAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Mentioned count degraded (TaskComments tables may be missing).");
            }
        }

        return dto;
    }

    /// <summary>
    /// ملخص إدارة التكليفات العامة لكل موظف (استعلام تجميعي واحد) —
    /// لمن يرى الكل فقط (أدوار الإدارة)؛ غيرهم قائمة فارغة.
    /// </summary>
    public async Task<List<EmployeeTaskLoadDto>> GetGeneralTasksManagerSummaryAsync(string userName)
    {
        if (!CanManageGeneralTasks()) return new();

        var today = DateTime.Today;
        var grouped = await _db.CrmTasks.AsNoTracking()
            .Where(t => t.IsActive && t.TaskScope == TaskScopes.General)
            .GroupBy(t => t.AssignedTo)
            .Select(g => new
            {
                EmployeeId = g.Key,
                Open = g.Count(t => t.Status == "Pending" || t.Status == "In Progress"),
                InProgress = g.Count(t => t.Status == "In Progress"),
                Overdue = g.Count(t => (t.Status == "Pending" || t.Status == "In Progress") && t.DueDate < today),
                Completed = g.Count(t => t.Status == "Completed"),
                ReturnedPending = g.Count(t => t.Status == "Pending" && t.ReturnedAt != null)
            })
            .ToListAsync();

        if (grouped.Count == 0) return new();

        var ids = grouped.Select(g => g.EmployeeId).ToList();
        var names = await _db.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.EmployeeId))
            .Select(e => new { e.EmployeeId, e.FullName })
            .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName);

        return grouped
            .Select(g => new EmployeeTaskLoadDto
            {
                EmployeeId = g.EmployeeId,
                EmployeeName = names.TryGetValue(g.EmployeeId, out var n) ? n : $"موظف #{g.EmployeeId}",
                OpenCount = g.Open,
                InProgressCount = g.InProgress,
                OverdueCount = g.Overdue,
                CompletedCount = g.Completed,
                ReturnedPendingCount = g.ReturnedPending
            })
            .OrderByDescending(r => r.OverdueCount)
            .ThenByDescending(r => r.OpenCount)
            .ToList();
    }

    /// <summary>🏢 هل يمكن للمستخدم الحالي إضافة صفوف على فرصة/عميل B2B؟</summary>
    private async Task<bool> CanWriteOnB2bOpportunityAsync(int opportunityId)
    {
        var user = _http?.HttpContext?.User;
        var isB2bParty = await _db.SalesOpportunities.AsNoTracking()
            .Where(o => o.OpportunityId == opportunityId)
            .AnyAsync(o => _db.Parties.Any(p => p.PartyId == o.PartyId && p.IsB2B));
        if (!isB2bParty) return true;

        if (B2bCrmPermissions.CanManage(user)) return true;                 // المدراء/المختص
        if (B2bCrmPermissions.IsB2bSpecialist(user))                          // المختص: فرصه فقط
        {
            var emp = await B2bViewScope.ResolveEmployeeIdAsync(_db, user);
            if (emp <= 0) return false;
            var owned = await _db.SalesOpportunities.AsNoTracking()
                .AnyAsync(o => o.OpportunityId == opportunityId && o.EmployeeId == emp);
            return owned;
        }
        return false;
    }

    public async Task<(bool Success, string Message)> AddQuickAsync(QuickTaskDto dto, string userName)
    {
        try
        {
            if (dto.OpportunityId.HasValue && !await CanWriteOnB2bOpportunityAsync(dto.OpportunityId.Value))
                return (false, "لا يمكنك إضافة مهمة على ملف B2B — تحتاج صلاحية B2B.");

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
        if (!CanCreateGeneralTasks())
            return (false, "ليست لديك صلاحية إنشاء مهام عامة للموظفين.", null);

        if (dto.AssignedTo <= 0)
            return (false, "اختر الموظف المكلف.", null);

        if (string.IsNullOrWhiteSpace(dto.TaskDescription))
            return (false, "اكتب وصف المهمة.", null);

        if (dto.DueDate.Date < DateTime.Today)
            return (false, "تاريخ التنفيذ لا يمكن أن يكون في الماضي.", null);

        var assignee = await _db.Employees.AsNoTracking()
            .Where(e => e.EmployeeId == dto.AssignedTo && (e.Status == "نشط" || e.Status == "Active"))
            .Select(e => new { e.EmployeeId, e.FullName })
            .FirstOrDefaultAsync();

        if (assignee == null)
            return (false, "الموظف غير موجود أو غير نشط.", null);

        // ربط اختياري بمستند: العنوان يُحسم هنا خادميًا (لا يُوثق بعنوان العميل)
        LinkedEntityLookupDto? link = null;
        if (!string.IsNullOrWhiteSpace(dto.LinkedEntityType) && dto.LinkedEntityId.HasValue)
        {
            link = await ResolveLinkedEntityCoreAsync(dto.LinkedEntityType, dto.LinkedEntityId.Value);
            if (link == null)
                return (false, "المستند المرتبط غير موجود — تحقق من النوع والرقم.", null);
        }

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
            ReminderMinutes = dto.ReminderMinutes is > 0 ? dto.ReminderMinutes : null,
            IsActive = true,
            AssignmentSource = assignmentSource,
            TaskScope = TaskScopes.General,
            LinkedEntityType = link?.EntityType,
            LinkedEntityId = link?.EntityId,
            LinkedEntityTitle = link?.Title,
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
                task.TaskScope,
                task.LinkedEntityType,
                task.LinkedEntityId,
                task.LinkedEntityTitle
            }, userName);

        var assignmentNotificationWarning = await NotifyGeneralEmployeeTaskAssignedToEmployeeAsync(
            task.TaskId,
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
        if (task.Status == "Cancelled") return (false, "المهمة ملغية ولا يمكن بدء تنفيذها");
        if (task.Status == "In Progress") return (true, "المهمة بالفعل قيد التنفيذ");
        if (string.IsNullOrWhiteSpace(notes)) return (false, "برجاء كتابة ماذا ستبدأ تنفيذه قبل بدء المهمة.");

        // ⭐ حارس الملكية (التكليفات العامة): البدء للمكلَّف نفسه أو لمدير فقط —
        //    الحارس في الخدمة هو مصدر الحقيقة، لا إخفاء الأزرار في الواجهة.
        if (string.Equals(task.TaskScope, TaskScopes.General, StringComparison.OrdinalIgnoreCase)
            && !await CanActOnGeneralTaskAsync(task, userName))
            return (false, "لا يمكنك بدء تنفيذ تكليف غير مُسند إليك.");

        task.Status = "In Progress";
        task.StartedAt = DateTime.Now;
        task.StartedBy = userName;
        task.StartNotes = notes.Trim();

        // ⭐ الحفظ ذرّي: تغيير الحالة وسجل التفاعل في SaveChanges واحد لكل مسار
        //    (كانا حفظين منفصلين — فشل الثاني يترك الحالة متغيرة بلا سجلها).
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

            await _db.SaveChangesAsync();

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
        else
        {
            // نطاقات أخرى (مهام فرص عادية): حفظ تغيير الحالة فقط
            await _db.SaveChangesAsync();
        }

        return (true, "تم تحويل المهمة إلى جاري التنفيذ");
    }

    public async Task<(bool Success, string Message)> CompleteAsync(int taskId, string notes, string userName, string? completionLink = null)
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

            // ⭐ FindAsync القديم كان يتجاوز IsActive وبلا أي حارس حالة — مهمة
            //    محذوفة ناعمًا كانت تُستكمل، ومكتملة كانت تُعاد كتابة إتمامها بصمت.
            var task = await _db.CrmTasks.FirstOrDefaultAsync(t => t.TaskId == taskId && t.IsActive);
            if (task == null) return (false, "المهمة غير موجودة");
            if (string.IsNullOrWhiteSpace(notes)) return (false, "برجاء كتابة ما الذي تم تنفيذه قبل إكمال المهمة.");
            if (task.Status == "Completed") return (false, "تم إكمال المهمة بالفعل ولا يمكن إعادة إتمامها.");
            if (task.Status == "Cancelled") return (false, "المهمة ملغية ولا يمكن إتمامها.");

            // ⭐ حارس الملكية (التكليفات العامة): الإتمام للمكلَّف نفسه أو لمدير فقط.
            if (string.Equals(task.TaskScope, TaskScopes.General, StringComparison.OrdinalIgnoreCase)
                && !await CanActOnGeneralTaskAsync(task, userName))
                return (false, "لا يمكنك إتمام تكليف غير مُسند إليك.");

            task.Status = "Completed";
            task.CompletedDate = DateTime.Now;
            task.CompletedBy = userName;
            task.CompletionNotes = notes.Trim();
            if (!string.IsNullOrWhiteSpace(completionLink))
                task.CompletionLink = completionLink.Trim();

            // ⭐ الحفظ ذرّي: الحالة وسجل التفاعل في SaveChanges واحد لكل مسار.
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

                await _db.SaveChangesAsync();

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
            else
            {
                // نطاقات أخرى (مهام فرص عادية): حفظ تغيير الحالة فقط
                await _db.SaveChangesAsync();
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

    /// <summary>
    /// إلغاء تكليف عام (منشئ التكليف أو مدير) بسبب إلزامي — من Pending/In Progress فقط.
    /// حالة "Cancelled" لم يكن أي كود يكتبها قبل الآن؛ الإلغاء موثّق
    /// (CancelledAt/By/Reason) وبيُشعر المكلَّف وبيُدقَّق.
    /// </summary>
    public async Task<(bool Success, string Message)> CancelGeneralTaskAsync(int taskId, string reason, string userName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(reason))
                return (false, "برجاء كتابة سبب إلغاء التكليف.");

            var task = await _db.CrmTasks.FirstOrDefaultAsync(t => t.TaskId == taskId && t.IsActive);
            if (task == null) return (false, "المهمة غير موجودة");
            if (!string.Equals(task.TaskScope, TaskScopes.General, StringComparison.OrdinalIgnoreCase))
                return (false, "الإلغاء الموثّق متاح للتكليفات العامة فقط.");
            if (!CanAdministerGeneralTask(task, userName))
                return (false, "لا يمكنك إلغاء تكليف لم تنشئه.");
            if (task.Status == "Completed") return (false, "لا يمكن إلغاء تكليف مكتمل.");
            if (task.Status == "Cancelled") return (false, "التكليف ملغى بالفعل.");
            if (task.Status != "Pending" && task.Status != "In Progress")
                return (false, "لا يمكن إلغاء التكليف في حالته الحالية.");

            task.Status = "Cancelled";
            task.CancelledAt = DateTime.Now;
            task.CancelledBy = userName;
            task.CancellationReason = reason.Trim();
            await _db.SaveChangesAsync();

            await _audit.LogAsync("CRM_Tasks", "GeneralEmployeeTaskCancel", task.TaskId.ToString(), null,
                new { task.AssignedTo, CancelledBy = userName, Reason = reason.Trim(), task.TaskDescription }, userName);

            await NotifyTaskEmployeeAsync(task.AssignedTo,
                "🚫 تم إلغاء تكليف",
                $"ألغى {userName} تكليفًا كان مُسندًا إليك.\nالمهمة: {task.TaskDescription}\nسبب الإلغاء: {reason.Trim()}",
                userName, task.TaskId);

            return (true, "تم إلغاء التكليف وإشعار المكلَّف.");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.Message}");
        }
    }

    /// <summary>
    /// تعديل تكليف عام (منشئ التكليف أو مدير): المكلَّف/النوع/الوصف/التاريخ/الأولوية.
    /// ممنوع على المكتمل والملغى. إعادة الإسناد تُصفّر الاستلام وتُشعر الطرفين،
    /// والتعديل الجوهري يُشعر المكلَّف — الكل بتدقيق قبل/بعد.
    /// </summary>
    public async Task<(bool Success, string Message)> UpdateGeneralTaskAsync(UpdateGeneralTaskDto dto, string userName)
    {
        try
        {
            var task = await _db.CrmTasks.FirstOrDefaultAsync(t => t.TaskId == dto.TaskId && t.IsActive);
            if (task == null) return (false, "المهمة غير موجودة");
            if (!string.Equals(task.TaskScope, TaskScopes.General, StringComparison.OrdinalIgnoreCase))
                return (false, "التعديل متاح للتكليفات العامة فقط.");
            if (!CanAdministerGeneralTask(task, userName))
                return (false, "لا يمكنك تعديل تكليف لم تنشئه.");
            if (task.Status == "Completed" || task.Status == "Cancelled")
                return (false, "لا يمكن تعديل تكليف مكتمل أو ملغى.");
            if (string.IsNullOrWhiteSpace(dto.TaskDescription))
                return (false, "اكتب وصف المهمة.");
            if (dto.DueDate.Date < DateTime.Today)
                return (false, "تاريخ التنفيذ لا يمكن أن يكون في الماضي.");

            var before = new
            {
                task.AssignedTo,
                task.TaskTypeId,
                task.TaskDescription,
                task.DueDate,
                task.DueTime,
                task.Priority
            };

            var oldAssigneeId = task.AssignedTo;
            var reassigned = dto.AssignedTo != oldAssigneeId;
            string? newAssigneeName = null;

            if (reassigned)
            {
                var emp = await _db.Employees.AsNoTracking()
                    .Where(e => e.EmployeeId == dto.AssignedTo && (e.Status == "نشط" || e.Status == "Active"))
                    .Select(e => new { e.EmployeeId, e.FullName })
                    .FirstOrDefaultAsync();
                if (emp == null) return (false, "الموظف الجديد غير موجود أو غير نشط.");
                newAssigneeName = emp.FullName;
            }

            task.AssignedTo = dto.AssignedTo;
            task.TaskTypeId = dto.TaskTypeId;
            task.TaskDescription = dto.TaskDescription.Trim();
            task.DueDate = dto.DueDate;
            task.DueTime = dto.DueTime;
            task.Priority = NormalizePriority(dto.Priority);
            if (reassigned)
            {
                // مكلَّف جديد = استلام جديد: يُصفَّر تأكيد الاستلام السابق
                task.AcknowledgedAt = null;
                task.AcknowledgedBy = null;
            }
            await _db.SaveChangesAsync();

            await _audit.LogAsync<object>("CRM_Tasks", "GeneralEmployeeTaskUpdate", task.TaskId.ToString(),
                before,
                new
                {
                    task.AssignedTo,
                    task.TaskTypeId,
                    task.TaskDescription,
                    task.DueDate,
                    task.DueTime,
                    task.Priority,
                    UpdatedBy = userName
                }, userName);

            if (reassigned)
            {
                await NotifyTaskEmployeeAsync(oldAssigneeId,
                    "↩️ تم نقل تكليف منك",
                    $"نقل {userName} تكليفًا كان مُسندًا إليك إلى موظف آخر.\nالمهمة: {task.TaskDescription}",
                    userName, task.TaskId);

                var warning = await NotifyGeneralEmployeeTaskAssignedToEmployeeAsync(
                    task.TaskId,
                    dto.AssignedTo, newAssigneeName ?? "غير محدد", task.TaskDescription ?? "",
                    task.DueDate, task.DueTime, task.Priority, task.AssignmentSource ?? "GeneralTask", userName);

                var msg = "تم نقل التكليف إلى المكلَّف الجديد وإشعار الطرفين.";
                return (true, string.IsNullOrWhiteSpace(warning) ? msg : $"{msg} لكن {warning}");
            }

            await NotifyTaskEmployeeAsync(task.AssignedTo,
                "✏️ تم تعديل تكليفك",
                $"عدّل {userName} تكليفًا مُسندًا إليك.\nالمهمة: {task.TaskDescription}\nالمطلوب قبل: {task.DueDate:yyyy/MM/dd}",
                userName, task.TaskId);

            return (true, "تم تحديث التكليف وإشعار المكلَّف.");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // ⭐ مرحلة النقاش: خيط التعليقات + المناداة + الاستلام/الإرجاع
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>هل المستخدم طرف في التكليف؟ مكلَّف/منشئ/مدير/مذكور في خيطه — شرط التعليق والعرض</summary>
    private async Task<bool> IsTaskParticipantAsync(CrmTask task, string userName)
    {
        if (CanAdministerGeneralTask(task, userName)) return true;
        var empId = await ResolveEmployeeIdAsync(userName);
        if (!empId.HasValue) return false;
        if (empId.Value == task.AssignedTo) return true;
        try
        {
            return await _db.TaskCommentMentions.AsNoTracking()
                .AnyAsync(m => m.MentionedEmployeeId == empId.Value && m.Comment.TaskId == task.TaskId);
        }
        catch { return false; }
    }

    /// <summary>تفاصيل تكليف + خيط نقاشه + صلاحيات العارض (الخيط يتحلل فارغًا عند غياب جداوله)</summary>
    public async Task<TaskDetailDto?> GetTaskDetailAsync(int taskId, string userName)
    {
        var taskDto = (await ProjectTasksAsync(_db.CrmTasks.AsNoTracking()
            .Include(t => t.AssignedToNavigation)
            .Include(t => t.TaskType)
            .Where(t => t.IsActive && t.TaskId == taskId))).FirstOrDefault();
        if (taskDto is null) return null;

        var detail = new TaskDetailDto { Task = taskDto };
        var empId = await ResolveEmployeeIdAsync(userName);
        detail.ViewerEmployeeId = empId;
        detail.ViewerIsAssignee = empId.HasValue && empId.Value == taskDto.AssignedTo;
        detail.ViewerCanManage = CanManageGeneralTasks();
        detail.ViewerCanAdminister = detail.ViewerCanManage
            || string.Equals(taskDto.CreatedBy, userName, StringComparison.OrdinalIgnoreCase);

        try
        {
            detail.Comments = await _db.TaskComments.AsNoTracking()
                .Where(c => c.TaskId == taskId)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new TaskCommentDto
                {
                    Id = c.Id,
                    TaskId = c.TaskId,
                    AuthorUserName = c.AuthorUserName,
                    AuthorEmployeeId = c.AuthorEmployeeId,
                    AuthorName = c.AuthorName,
                    Body = c.Body,
                    Kind = c.Kind,
                    CreatedAt = c.CreatedAt,
                    MentionedEmployeeIds = c.Mentions.Select(m => m.MentionedEmployeeId).ToList(),
                    MentionedNames = c.Mentions.Select(m => m.MentionedEmployee.FullName).ToList()
                })
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Task thread degraded for task {TaskId} (TaskComments table may be missing).", taskId);
        }

        return detail;
    }

    /// <summary>
    /// إضافة تعليق/تحديث تقدم — المشاركون فقط (مكلَّف/منشئ/مدير/مذكور).
    /// المناداة تصل صراحة من الواجهة (بلا تحليل نصي هش) وتُشعر أصحابها.
    /// </summary>
    public async Task<(bool Success, string Message)> AddTaskCommentAsync(AddTaskCommentDto dto, string userName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Body))
                return (false, "برجاء كتابة نص التعليق أولًا.");
            if (dto.Body.Trim().Length > 2000)
                return (false, "نص التعليق أطول من الحد المسموح (2000 حرف).");

            var task = await _db.CrmTasks.FirstOrDefaultAsync(t => t.TaskId == dto.TaskId && t.IsActive);
            if (task == null) return (false, "المهمة غير موجودة");
            if (!await IsTaskParticipantAsync(task, userName))
                return (false, "التعليق متاح لأطراف التكليف فقط (المكلَّف والمنشئ والإدارة والمذكورون).");

            var authorEmpId = await ResolveEmployeeIdAsync(userName);
            var authorName = authorEmpId.HasValue
                ? await _db.Employees.AsNoTracking()
                    .Where(e => e.EmployeeId == authorEmpId.Value)
                    .Select(e => e.FullName)
                    .FirstOrDefaultAsync()
                : null;

            var comment = new TaskComment
            {
                TaskId = task.TaskId,
                AuthorUserName = userName,
                AuthorEmployeeId = authorEmpId,
                AuthorName = authorName,
                Body = dto.Body.Trim(),
                Kind = dto.Kind == TaskCommentKinds.Progress ? TaskCommentKinds.Progress : TaskCommentKinds.Comment,
                CreatedAt = DateTime.Now
            };

            var mentionIds = (dto.MentionedEmployeeIds ?? new())
                .Where(id => id > 0 && id != authorEmpId)
                .Distinct()
                .ToList();
            foreach (var mid in mentionIds)
                comment.Mentions.Add(new TaskCommentMention { MentionedEmployeeId = mid });

            _db.TaskComments.Add(comment);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (Exception ex) when (IsMissingThreadTable(ex))
            {
                return (false, "خيط النقاش غير مفعّل بعد — برجاء تشغيل سكربت المنصة (general-tasks-2-platform.sql) على قاعدة البيانات أولًا.");
            }

            foreach (var mid in mentionIds)
            {
                await NotifyTaskEmployeeAsync(mid,
                    "📣 ذُكرت في تكليف",
                    $"ذكرك {authorName ?? userName} في تكليف «{task.TaskDescription}».\nافتح الإشعار لعرض التكليف والرد على الخيط.",
                    userName, task.TaskId);
            }

            return (true, "تمت إضافة تعليقك إلى خيط التكليف.");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.Message}");
        }
    }

    /// <summary>استلام التكليف: المكلَّف يؤكد الاستلام — يُختم ويُشعر المنشئ</summary>
    public async Task<(bool Success, string Message)> AcknowledgeTaskAsync(int taskId, string userName)
    {
        try
        {
            var task = await _db.CrmTasks.FirstOrDefaultAsync(t => t.TaskId == taskId && t.IsActive);
            if (task == null) return (false, "المهمة غير موجودة");
            if (!string.Equals(task.TaskScope, TaskScopes.General, StringComparison.OrdinalIgnoreCase))
                return (false, "الاستلام الموثّق متاح للتكليفات العامة فقط.");
            var empId = await ResolveEmployeeIdAsync(userName);
            if (!empId.HasValue || empId.Value != task.AssignedTo)
                return (false, "استلام التكليف متاح للمكلَّف به فقط.");
            if (task.Status == "Completed" || task.Status == "Cancelled")
                return (false, "لا يمكن استلام تكليف منتهٍ أو ملغى.");
            if (task.AcknowledgedAt.HasValue)
                return (true, "التكليف مُستلم بالفعل.");

            task.AcknowledgedAt = DateTime.Now;
            task.AcknowledgedBy = userName;
            await _db.SaveChangesAsync();

            await _audit.LogAsync("CRM_Tasks", "GeneralEmployeeTaskAcknowledge", task.TaskId.ToString(), null,
                new { task.AssignedTo, AcknowledgedBy = userName, task.TaskDescription }, userName);

            await NotifyTaskCreatorAsync(task,
                "👀 تم استلام تكليفك",
                $"استلم {userName} التكليف «{task.TaskDescription}».",
                userName);

            return (true, "تم تأكيد استلام التكليف وإشعار المنشئ.");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.Message}");
        }
    }

    /// <summary>
    /// إرجاع التكليف: المكلَّف يعيده بسبب إلزامي — يعود Pending ويُصفّر الاستلام
    /// ويُشعر المنشئ ليقرر (تعديل/إعادة إسناد/إلغاء).
    /// </summary>
    public async Task<(bool Success, string Message)> ReturnTaskAsync(int taskId, string reason, string userName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(reason))
                return (false, "برجاء كتابة سبب إرجاع التكليف.");
            var task = await _db.CrmTasks.FirstOrDefaultAsync(t => t.TaskId == taskId && t.IsActive);
            if (task == null) return (false, "المهمة غير موجودة");
            if (!string.Equals(task.TaskScope, TaskScopes.General, StringComparison.OrdinalIgnoreCase))
                return (false, "الإرجاع الموثّق متاح للتكليفات العامة فقط.");
            var empId = await ResolveEmployeeIdAsync(userName);
            if (!empId.HasValue || empId.Value != task.AssignedTo)
                return (false, "إرجاع التكليف متاح للمكلَّف به فقط.");
            if (task.Status == "Completed" || task.Status == "Cancelled")
                return (false, "لا يمكن إرجاع تكليف منتهٍ أو ملغى.");

            task.Status = "Pending";
            task.StartedAt = null;
            task.StartedBy = null;
            task.StartNotes = null;
            task.ReturnedAt = DateTime.Now;
            task.ReturnedBy = userName;
            task.ReturnReason = reason.Trim();
            task.AcknowledgedAt = null;
            task.AcknowledgedBy = null;
            await _db.SaveChangesAsync();

            await _audit.LogAsync("CRM_Tasks", "GeneralEmployeeTaskReturn", task.TaskId.ToString(), null,
                new { task.AssignedTo, ReturnedBy = userName, Reason = reason.Trim(), task.TaskDescription }, userName);

            await NotifyTaskCreatorAsync(task,
                "↩️ أُرجع تكليف أنشأته",
                $"أرجع {userName} التكليف «{task.TaskDescription}».\nسبب الإرجاع: {reason.Trim()}\nيمكنك تعديله أو إعادة إسناده أو إلغاءه من صفحة التكليفات.",
                userName);

            return (true, "تم إرجاع التكليف وإشعار المنشئ.");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.Message}");
        }
    }

    /// <summary>إشعار منشئ التكليف بحسابه (إن وُجد وكان نشطًا) — صامت عند الغياب</summary>
    private async Task NotifyTaskCreatorAsync(CrmTask task, string title, string message, string actor)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(task.CreatedBy)) return;
            var creatorActive = await _db.Users.AsNoTracking()
                .AnyAsync(u => u.Username == task.CreatedBy && u.IsActive == true);
            if (!creatorActive) return;
            await _notify.AddAsync(
                title: title,
                message: message,
                recipientUser: task.CreatedBy,
                createdBy: actor,
                formName: "crm/general-tasks",
                relatedTable: "CRM_Tasks",
                relatedId: task.TaskId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify creator of task {TaskId}.", task.TaskId);
        }
    }

    /// <summary>هل الخطأ ناتج عن غياب جدولي خيط النقاش؟ (رسالة واضحة بدل خطأ SQL خام)</summary>
    private static bool IsMissingThreadTable(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
            if (e.Message.Contains("Invalid object name") && e.Message.Contains("TaskComment"))
                return true;
        return false;
    }

    /// <summary>إشعار موظف بحسابه المرتبط (صامت عند غياب الحساب — يُسجَّل تحذير فقط)</summary>
    private async Task NotifyTaskEmployeeAsync(int employeeId, string title, string message, string actor, int taskId)
    {
        try
        {
            var user = await _db.Users.AsNoTracking()
                .Where(u => u.EmployeeId == employeeId && u.IsActive == true)
                .Select(u => u.Username)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(user))
            {
                _logger.LogWarning("Task notification skipped: employee {EmployeeId} has no active linked user. Title={Title}", employeeId, title);
                return;
            }

            await _notify.AddAsync(
                title: title,
                message: message,
                recipientUser: user,
                createdBy: actor,
                formName: "crm/general-tasks",
                relatedTable: "CRM_Tasks",
                relatedId: taskId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify employee {EmployeeId} about a general task event.", employeeId);
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

    private async Task<string?> NotifyGeneralEmployeeTaskAssignedToEmployeeAsync(int taskId, int assignedEmployeeId, string assignedEmployeeName, string taskDescription, DateTime dueDate, TimeOnly? dueTime, string priority, string assignmentSource, string actor)
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
                relatedTable: "CRM_Tasks",
                relatedId: taskId);

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
                relatedTable: "CRM_Tasks",
                relatedId: task.TaskId);
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

    /// <summary>رقم الموظف المرتبط بحساب المستخدم (null لو الحساب غير مربوط بموظف)</summary>
    private async Task<int?> ResolveEmployeeIdAsync(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName)) return null;
        return await _db.Users.AsNoTracking()
            .Where(u => u.Username == userName && u.EmployeeId != null)
            .Select(u => u.EmployeeId)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// ⭐ من يحق له بدء/إتمام تكليف عام: المكلَّف نفسه (حسابه مربوط بموظف التكليف)
    /// أو مدير بأدوار CanManageGeneralTasks. الحارس هنا في الخدمة هو مصدر
    /// الحقيقة — قبل هذا الإصلاح كان البدء/الإتمام محميين بإخفاء الأزرار فقط.
    /// </summary>
    private async Task<bool> CanActOnGeneralTaskAsync(CrmTask task, string userName)
    {
        if (CanManageGeneralTasks()) return true;
        var empId = await ResolveEmployeeIdAsync(userName);
        return empId.HasValue && empId.Value == task.AssignedTo;
    }

    /// <summary>من يحق له إلغاء/تعديل تكليف عام بعينه: منشؤه أو مدير.</summary>
    private bool CanAdministerGeneralTask(CrmTask task, string userName)
        => CanManageGeneralTasks()
           || string.Equals(task.CreatedBy, userName, StringComparison.OrdinalIgnoreCase);

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
