using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class LeadsCrmService : ILeadsCrmService
{
    private readonly db24804Context _db;
    private readonly IAuditService _audit;
    private readonly NotificationService _notify;
    private readonly ILogger<LeadsCrmService> _logger;
    private readonly IHttpContextAccessor _httpContext;

    public LeadsCrmService(
    db24804Context db,
    IAuditService audit,
    NotificationService notify,
    ILogger<LeadsCrmService> logger,
    IHttpContextAccessor httpContext)
{
    _db = db;
    _audit = audit;
    _notify = notify;
    _logger = logger;
    _httpContext = httpContext;
}


    // ═══════════════════════════════════════════════════════════
    //  عرض كل الـ Leads مع فلترة وصفحات (مُحسّن — بدون N+1)
    // ═══════════════════════════════════════════════════════════
    public async Task<PagedResult<LeadsCrmListDto>> GetLeadsAsync(LeadsCrmFilterDto filter)
    {
        var query = _db.LeadsCRMs.AsNoTracking().AsQueryable();

        // فلترة
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var term = filter.SearchText.Trim();
            query = query.Where(l =>
                l.FullName.Contains(term) ||
                l.Phone.Contains(term) ||
                (l.Phone2 != null && l.Phone2.Contains(term)) ||
                (l.Email != null && l.Email.Contains(term)) ||
                (l.CampaignName != null && l.CampaignName.Contains(term)) ||
                (l.City != null && l.City.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim();
            query = query.Where(l =>
                l.FullName.Contains(term) ||
                l.Phone.Contains(term) ||
                (l.Phone2 != null && l.Phone2.Contains(term)) ||
                (l.Email != null && l.Email.Contains(term)) ||
                (l.City != null && l.City.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(filter.LeadStatus))
            query = query.Where(l => l.LeadStatus == filter.LeadStatus);

        if (!string.IsNullOrWhiteSpace(filter.CampaignName))
            query = query.Where(l => l.CampaignName == filter.CampaignName);

        if (!string.IsNullOrWhiteSpace(filter.Platform))
            query = query.Where(l => l.Platform == filter.Platform);
        if (!string.IsNullOrWhiteSpace(filter.ProjectType))
            query = query.Where(l => l.ProjectType == filter.ProjectType);

        if (!string.IsNullOrWhiteSpace(filter.FormLanguage))
            query = query.Where(l => l.FormLanguage == filter.FormLanguage);

        if (filter.AssignedEmployeeId.HasValue)
            query = query.Where(l => l.AssignedEmployeeId == filter.AssignedEmployeeId);

        if (filter.IsConverted.HasValue)
            query = query.Where(l => l.IsConverted == filter.IsConverted.Value);

        if (filter.DateFrom.HasValue)
            query = query.Where(l => l.CreatedAt >= filter.DateFrom.Value);

        if (filter.DateTo.HasValue)
            query = query.Where(l => l.CreatedAt <= filter.DateTo.Value.AddDays(1));
        if (filter.LateFollowUpOnly)
{
    var lateCutoff = DateTime.Now.AddHours(-1);

    query = query.Where(l =>
        !l.IsConverted &&
        l.LeadStatus != "محول" &&
        l.LeadStatus != "مرفوض" &&
        !l.LastContactDate.HasValue &&
        l.CreatedAt <= lateCutoff);
}

        var totalCount = await query.CountAsync();

        // الخطوة 1: جيب الـ Leads بس (من غير sub-query للاسم الموظف)
        var leads = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        // الخطوة 2: جيب أصحاب الفرص المرتبطة بالـ Leads المتحولة
        var opportunityIds = leads
            .Where(l => l.ConvertedOpportunityId.HasValue)
            .Select(l => l.ConvertedOpportunityId!.Value)
            .Distinct()
            .ToList();

        var opportunityEmployeeMap = opportunityIds.Count > 0
            ? await _db.SalesOpportunities.AsNoTracking()
                .Where(o => opportunityIds.Contains(o.OpportunityId) && o.EmployeeId.HasValue)
                .ToDictionaryAsync(o => o.OpportunityId, o => o.EmployeeId)
            : new Dictionary<int, int?>();

        // الخطوة 3: جيب أسماء الموظفين لوحدها (بدل N+1)
        var employeeIds = leads
            .Where(l => l.AssignedEmployeeId.HasValue)
            .Select(l => l.AssignedEmployeeId!.Value)
            .Concat(opportunityEmployeeMap.Values.Where(v => v.HasValue).Select(v => v!.Value))
            .Distinct()
            .ToList();

        Dictionary<int, string> employeeNames = new();
        if (employeeIds.Count > 0)
        {
            employeeNames = await _db.Employees.AsNoTracking()
                .Where(e => employeeIds.Contains(e.EmployeeId))
                .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName);
        }

        // الخطوة 4: ادمجهم في الذاكرة
        var items = leads.Select(l => new LeadsCrmListDto
        {
            LeadId = l.LeadId,
            FullName = l.FullName,
            Phone = l.Phone,
            Phone2 = l.Phone2,
            Email = l.Email,
            City = l.City,
            Address = l.Address,
            CampaignName = l.CampaignName,
            AdName = l.AdName,
            Platform = l.Platform,
            ProjectType = l.ProjectType,
            ProjectStage = l.ProjectStage,
            Budget = l.Budget,
            DecisionMaker = l.DecisionMaker,
            NextAction = l.NextAction,
            BestTimeToReach = l.BestTimeToReach,
            LeadStatus = l.LeadStatus,
            FormLanguage = l.FormLanguage,
            IsConverted = l.IsConverted,
            ConvertedOpportunityId = l.ConvertedOpportunityId,
            IsDuplicate = l.IsDuplicate,
            AssignedEmployeeId = l.AssignedEmployeeId,
            AssignedEmployeeName = l.AssignedEmployeeId.HasValue
                && employeeNames.TryGetValue(l.AssignedEmployeeId.Value, out var name)
                ? name : null,
            OpportunityEmployeeId = l.ConvertedOpportunityId.HasValue
                && opportunityEmployeeMap.TryGetValue(l.ConvertedOpportunityId.Value, out var oppEmpId)
                ? oppEmpId
                : null,
            OpportunityEmployeeName = l.ConvertedOpportunityId.HasValue
                && opportunityEmployeeMap.TryGetValue(l.ConvertedOpportunityId.Value, out var oppEmpNameId)
                && oppEmpNameId.HasValue
                && employeeNames.TryGetValue(oppEmpNameId.Value, out var oppEmpName)
                ? oppEmpName : null,
            Feedback = l.Feedback,
            SheetTabName = l.SheetTabName,
            LeadDate = l.LeadDate,
            CreatedAt = l.CreatedAt,
            LastContactDate = l.LastContactDate
        }).ToList();

        return new PagedResult<LeadsCrmListDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize
        };
    }
    

    // ═══════════════════════════════════════════════════════════
    //  تفاصيل Lead واحد (مُحسّن — بدون N+1)
    // ═══════════════════════════════════════════════════════════
    public async Task<LeadsCrmDetailDto?> GetLeadByIdAsync(int leadId)
    {
        var lead = await _db.LeadsCRMs.AsNoTracking()
            .FirstOrDefaultAsync(l => l.LeadId == leadId);
        if (lead == null) return null;

        string? assignedName = null;
        if (lead.AssignedEmployeeId.HasValue)
        {
            assignedName = await _db.Employees.AsNoTracking()
                .Where(e => e.EmployeeId == lead.AssignedEmployeeId.Value)
                .Select(e => e.FullName)
                .FirstOrDefaultAsync();
        }

        int? opportunityEmployeeId = null;
        string? opportunityEmployeeName = null;
        if (lead.ConvertedOpportunityId.HasValue)
        {
            opportunityEmployeeId = await _db.SalesOpportunities.AsNoTracking()
                .Where(o => o.OpportunityId == lead.ConvertedOpportunityId.Value)
                .Select(o => o.EmployeeId)
                .FirstOrDefaultAsync();

            if (opportunityEmployeeId.HasValue)
            {
                opportunityEmployeeName = await _db.Employees.AsNoTracking()
                    .Where(e => e.EmployeeId == opportunityEmployeeId.Value)
                    .Select(e => e.FullName)
                    .FirstOrDefaultAsync();
            }
        }

        return new LeadsCrmDetailDto
        {
            LeadId = lead.LeadId,
            FullName = lead.FullName,
            Phone = lead.Phone,
            Phone2 = lead.Phone2,
            Email = lead.Email,
            City = lead.City,
            Area = lead.Area,
            Address = lead.Address,
            MetaLeadId = lead.MetaLeadId,
            CampaignId = lead.CampaignId,
            CampaignName = lead.CampaignName,
            AdId = lead.AdId,
            AdName = lead.AdName,
            AdsetId = lead.AdsetId,
            AdSetName = lead.AdSetName,
            FormId = lead.FormId,
            FormName = lead.FormName,
            Platform = lead.Platform,
            IsOrganic = lead.IsOrganic,
            InboxUrl = lead.InboxUrl,
            FormLanguage = lead.FormLanguage,
            ProjectType = lead.ProjectType,
            ProjectStage = lead.ProjectStage,
            Budget = lead.Budget,
            DecisionMaker = lead.DecisionMaker,
            NextAction = lead.NextAction,
            BestTimeToReach = lead.BestTimeToReach,
            ProjectStageAlt = lead.ProjectStageAlt,
            BudgetAlt = lead.BudgetAlt,
            LeadDate = lead.LeadDate,
            LeadStatus = lead.LeadStatus,
            IsConverted = lead.IsConverted,
            ConvertedPartyId = lead.ConvertedPartyId,
            ConvertedOpportunityId = lead.ConvertedOpportunityId,
            ConvertedDate = lead.ConvertedDate,
            ConvertedBy = lead.ConvertedBy,
            IsDuplicate = lead.IsDuplicate,
            DuplicateOfPhone = lead.DuplicateOfPhone,
            SheetTabName = lead.SheetTabName,
            SheetRowNumber = lead.SheetRowNumber,
            Notes = lead.Notes,
            AssignedEmployeeId = lead.AssignedEmployeeId,
            AssignedEmployeeName = assignedName,
            OpportunityEmployeeId = opportunityEmployeeId,
            OpportunityEmployeeName = opportunityEmployeeName,
            Feedback = lead.Feedback,
            RejectedReason = lead.RejectedReason,
            LastContactDate = lead.LastContactDate,
            QualifiedDate = lead.QualifiedDate,
            ExtraData = lead.ExtraData,
            CreatedAt = lead.CreatedAt,
            CreatedBy = lead.CreatedBy
        };
    }
    public async Task<List<string>> GetDistinctProjectsAsync()
{
    return await _db.LeadsCRMs
        .Where(l => !string.IsNullOrEmpty(l.ProjectType))
        .Select(l => l.ProjectType!)
        .Distinct()
        .OrderBy(p => p)
        .ToListAsync();
}
    

    // ═══════════════════════════════════════════════════════════
//  تواصلات / حركات الـ Lead
// ═══════════════════════════════════════════════════════════
public async Task<List<LeadInteractionDto>> GetLeadInteractionsAsync(int leadId)
{
    var interactions = await _db.LeadInteractions
        .AsNoTracking()
        .Where(i => i.LeadId == leadId)
        .OrderByDescending(i => i.InteractionDate)
        .ThenByDescending(i => i.LeadInteractionId)
        .Select(i => new
        {
            i.LeadInteractionId,
            i.LeadId,
            i.EmployeeId,
            i.InteractionType,
            i.InteractionDate,
            i.Summary,
            i.Notes,
            i.OldLeadStatus,
            i.NewLeadStatus,
            i.NextFollowUpDate,
            i.IsCompleted,            // ⭐ أضف هذا السطر
            i.CompletedByEmployeeId,  // ⭐ أضف هذا السطر
            i.CompletedDate,          // ⭐ أضف هذا السطر
            i.IsSystemGenerated,
            i.CreatedBy,
            i.CreatedAt
        })
        .ToListAsync();

    var employeeIds = interactions
        .Where(i => i.EmployeeId.HasValue)
        .Select(i => i.EmployeeId!.Value)
        .Distinct()
        .ToList();

    var employeeNames = new Dictionary<int, string>();

    if (employeeIds.Any())
    {
        employeeNames = await _db.Employees
            .AsNoTracking()
            .Where(e => employeeIds.Contains(e.EmployeeId))
            .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName);
    }

    return interactions.Select(i => new LeadInteractionDto
    {
        LeadInteractionId = i.LeadInteractionId,
        LeadId = i.LeadId,
        EmployeeId = i.EmployeeId,
        EmployeeName = i.EmployeeId.HasValue && employeeNames.TryGetValue(i.EmployeeId.Value, out var empName)
            ? empName
            : null,
        InteractionType = i.InteractionType,
        InteractionDate = i.InteractionDate,
        Summary = i.Summary,
        Notes = i.Notes,
        OldLeadStatus = i.OldLeadStatus,
        NewLeadStatus = i.NewLeadStatus,
        NextFollowUpDate = i.NextFollowUpDate,
        IsCompleted = i.IsCompleted,
        CompletedByEmployeeId = i.CompletedByEmployeeId,
        CompletedDate = i.CompletedDate,
        IsSystemGenerated = i.IsSystemGenerated,
        CreatedBy = i.CreatedBy,
        CreatedAt = i.CreatedAt
    }).ToList();
}

public async Task<(bool Success, string Message)> AddLeadInteractionAsync(
    LeadInteractionCreateDto dto, string userName)
{
    var lead = await _db.LeadsCRMs.FindAsync(dto.LeadId);
    if (lead == null)
        return (false, "Lead غير موجود");

    var now = DateTime.Now;
    var oldStatus = lead.LeadStatus;

    var interactionType = string.IsNullOrWhiteSpace(dto.InteractionType)
        ? LeadInteractionTypes.Note
        : dto.InteractionType.Trim();

    var newStatus = string.IsNullOrWhiteSpace(dto.NewLeadStatus)
        ? null
        : dto.NewLeadStatus.Trim();

    var userEmpId = await _db.Users.AsNoTracking()
        .Where(u => u.Username == userName && u.EmployeeId != null)
        .Select(u => u.EmployeeId)
        .FirstOrDefaultAsync();

    var empId = dto.EmployeeId ?? lead.AssignedEmployeeId ?? userEmpId;

    // ⭐ إغلاق وتوثيق أي مهام متابعة مفتوحة سابقة لهذا الليد
    var openFollowUps = await _db.LeadInteractions
        .Where(i => i.LeadId == dto.LeadId && i.NextFollowUpDate != null && !i.IsCompleted)
        .ToListAsync();
    foreach (var open in openFollowUps)
    {
        open.IsCompleted = true;
        open.CompletedByEmployeeId = empId;
        open.CompletedDate = now;
    }

    var interaction = new LeadInteraction
    {
        LeadId = dto.LeadId,
        EmployeeId = empId,
        InteractionType = interactionType,
        InteractionDate = now,
        Summary = dto.Summary?.Trim(),
        Notes = dto.Notes?.Trim(),
        OldLeadStatus = oldStatus,
        NewLeadStatus = newStatus,
        NextFollowUpDate = dto.NextFollowUpDate,
        IsSystemGenerated = false,
        CreatedBy = userName,
        CreatedAt = now
    };

    _db.LeadInteractions.Add(interaction);

    // لو فيه موظف في التفاعل والـ Lead غير مسند، نسنده لنفس الموظف
    if (!lead.AssignedEmployeeId.HasValue && interaction.EmployeeId.HasValue)
    {
        lead.AssignedEmployeeId = interaction.EmployeeId.Value;

        if (lead.LeadStatus == "جديد")
            lead.LeadStatus = "تم الإسناد";
    }

    // تحديث الحالة لو المستخدم اختار حالة جديدة
    if (!string.IsNullOrWhiteSpace(newStatus))
    {
        lead.LeadStatus = newStatus;

        if (newStatus == "تم التواصل")
        {
            lead.LastContactDate = now;
        }
        else if (newStatus == "مرفوض")
        {
            lead.LastContactDate = now;

            if (!string.IsNullOrWhiteSpace(dto.RejectedReason))
                lead.RejectedReason = dto.RejectedReason.Trim();
        }
        else if (newStatus == "محول")
        {
            lead.LastContactDate = now;
        }
    }
    else
    {
        // لو لم يحدد حالة، لكن نوع التفاعل يدل على تواصل فعلي
        if (interactionType == LeadInteractionTypes.Call ||
            interactionType == LeadInteractionTypes.WhatsApp ||
            interactionType == LeadInteractionTypes.FollowUp ||
            interactionType == LeadInteractionTypes.Note)
        {
            lead.LastContactDate = now;

            if (lead.LeadStatus == "جديد" || lead.LeadStatus == "تم الإسناد")
                lead.LeadStatus = "تم التواصل";
        }
    }

    await _db.SaveChangesAsync();

    await _audit.LogAsync("LeadInteractions", "Insert",
        interaction.LeadInteractionId.ToString(), null, interaction, userName);

    return (true, "تم تسجيل التواصل بنجاح");
}

    // ═══════════════════════════════════════════════════════════
    //  تحديث حالة Lead أو فيدباك
    // ═══════════════════════════════════════════════════════════
    public async Task<(bool Success, string Message)> UpdateLeadAsync(
    LeadsCrmUpdateDto dto, string userName)
{
    var lead = await _db.LeadsCRMs.FindAsync(dto.LeadId);
    if (lead == null) return (false, "Lead غير موجود");

    var now = DateTime.Now;

    var oldStatus = lead.LeadStatus;
    var oldAssignedEmployeeId = lead.AssignedEmployeeId;

    var hasContactAction = false;

    // 1) تحديث الحالة لو المستخدم اختار حالة
    if (!string.IsNullOrWhiteSpace(dto.LeadStatus))
    {
        lead.LeadStatus = dto.LeadStatus;

        if (dto.LeadStatus == "تم التواصل")
            hasContactAction = true;

        // نترك مؤهل مؤقتًا للداتا القديمة لو موجودة، لكن لن نعتمد عليها في الواجهة الجديدة
        if (dto.LeadStatus == "مؤهل")
        {
            hasContactAction = true;

            if (oldStatus != "مؤهل")
                lead.QualifiedDate = now;
        }

        if (dto.LeadStatus == "مرفوض")
        {
            if (!string.IsNullOrWhiteSpace(dto.RejectedReason))
                lead.RejectedReason = dto.RejectedReason;

            hasContactAction = true;
        }
    }

    // 2) تحديث الفيدباك
    if (dto.Feedback != null)
    {
        lead.Feedback = dto.Feedback;

        if (!string.IsNullOrWhiteSpace(dto.Feedback))
            hasContactAction = true;
    }

    // 2b) تحديث الملاحظات
    if (dto.Notes != null)
    {
        lead.Notes = dto.Notes;
    }

    // 3) تحديث الموظف المسؤول
    var assignmentChanged = oldAssignedEmployeeId != dto.AssignedEmployeeId;
    var assignmentComment = string.IsNullOrWhiteSpace(dto.AssignmentComment) ? null : dto.AssignmentComment.Trim();

    lead.AssignedEmployeeId = dto.AssignedEmployeeId;

    // 4) لو تم إسناد Lead جديد لموظف، نحوله إلى "تم الإسناد"
    if (assignmentChanged && dto.AssignedEmployeeId.HasValue)
    {
        var statusBeforeAssignment = lead.LeadStatus;

        if (lead.LeadStatus == "جديد")
            lead.LeadStatus = "تم الإسناد";

        _db.LeadInteractions.Add(new LeadInteraction
        {
            LeadId = lead.LeadId,
            EmployeeId = dto.AssignedEmployeeId.Value,
            InteractionType = LeadInteractionTypes.Assigned,
            InteractionDate = now,
            Summary = "تم إسناد الـ Lead إلى موظف مسؤول.",
            Notes = assignmentComment,
            OldLeadStatus = statusBeforeAssignment,
            NewLeadStatus = lead.LeadStatus,
            IsSystemGenerated = true,
            CreatedBy = userName,
            CreatedAt = now
        });
    }

    // 5) لو تم مسح الإسناد، نسجل حركة اختيارية
    if (assignmentChanged && !dto.AssignedEmployeeId.HasValue && oldAssignedEmployeeId.HasValue)
    {
        _db.LeadInteractions.Add(new LeadInteraction
        {
            LeadId = lead.LeadId,
            EmployeeId = oldAssignedEmployeeId.Value,
            InteractionType = LeadInteractionTypes.Note,
            InteractionDate = now,
            Summary = "تم إلغاء إسناد الـ Lead من الموظف السابق.",
            OldLeadStatus = oldStatus,
            NewLeadStatus = lead.LeadStatus,
            IsSystemGenerated = true,
            CreatedBy = userName,
            CreatedAt = now
        });
    }

    // 6) تحديث آخر تواصل لو حصل تواصل فعلي
    if (hasContactAction)
        lead.LastContactDate = now;

    if (dto.LeadStatus == "مرفوض")
    {
        await CloseOpenLeadTasksAsync(lead.LeadId, userName, now, "تم إغلاق تكاليف الليد تلقائياً لأن حالة الليد أصبحت مرفوض.");
    }

    await _db.SaveChangesAsync();

    await _audit.LogAsync("LeadsCRM", "Update",
        dto.LeadId.ToString(), null, dto, userName);

    // 7) إشعار الموظف الجديد لو تم الإسناد
    if (assignmentChanged && dto.AssignedEmployeeId.HasValue)
    {
        await NotifyLeadAssignedAsync(lead, dto.AssignedEmployeeId.Value, userName, assignmentComment);
    }

    return (true, "تم التحديث بنجاح");
}

private async Task CloseOpenLeadTasksAsync(int leadId, string userName, DateTime now, string notes)
{
    var openTasks = await _db.CrmTasks
        .Where(t => t.LeadId == leadId
            && t.IsActive
            && t.TaskScope == TaskScopes.Lead
            && (t.Status == "Pending" || t.Status == "In Progress"))
        .ToListAsync();

    foreach (var task in openTasks)
    {
        task.Status = "Completed";
        task.CompletedDate = now;
        task.CompletedBy = userName;
        task.CompletionNotes = notes;
    }
}

private async Task NotifyLeadAssignedAsync(LeadsCrm lead, int employeeId, string assignedBy, string? assignmentComment = null)
{
    try
    {
        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.EmployeeId == employeeId && u.IsActive == true)
            .Select(u => new
            {
                u.Username,
                u.FullName
            })
            .FirstOrDefaultAsync();

        if (user == null || string.IsNullOrWhiteSpace(user.Username))
        {
            _logger.LogWarning(
                "Lead {LeadId} assigned to Employee {EmployeeId}, but no active user is linked to this employee.",
                lead.LeadId,
                employeeId);

            return;
        }

        var title = "📌 تم إسناد Lead جديد لك";

        var campaignPart = string.IsNullOrWhiteSpace(lead.CampaignName)
            ? ""
            : $" من حملة: {lead.CampaignName}";

        var message =
            $"تم إسناد Lead لك: {lead.FullName} - {lead.Phone}{campaignPart}. " +
            "برجاء المتابعة واتخاذ إجراء.";

        if (!string.IsNullOrWhiteSpace(assignmentComment))
            message += $"\nتعليمات / ملاحظة: {assignmentComment}";

        await _notify.AddAsync(
            title: title,
            message: message,
            recipientUser: user.Username,
            createdBy: assignedBy,
            formName: "crm/leads/my",
            relatedTable: "LeadsCRM",
            relatedId: lead.LeadId);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(
            ex,
            "Failed to send lead assignment notification. LeadId={LeadId}, EmployeeId={EmployeeId}",
            lead.LeadId,
            employeeId);
    }
}

private async Task NotifyOpportunityAssignedFromLeadConversionAsync(LeadsCrm lead, int opportunityId, int employeeId, string actor)
{
    var user = await _db.Users
        .AsNoTracking()
        .Where(u => u.EmployeeId == employeeId && u.IsActive == true)
        .Select(u => new { u.Username, u.FullName })
        .FirstOrDefaultAsync();

    if (user == null || string.IsNullOrWhiteSpace(user.Username))
    {
        _logger.LogWarning(
            "Opportunity {OpportunityId} created from Lead {LeadId}, but no active user is linked to employee {EmployeeId}.",
            opportunityId,
            lead.LeadId,
            employeeId);
        return;
    }

    var title = "🎯 تم تحويل Lead إلى فرصة بيع لك";
    var message = $"تم تحويل Lead العميل {lead.FullName} إلى فرصة بيع رقم #{opportunityId} وتم إسنادها لك. برجاء البدء في المتابعة.";

    await _notify.AddAsync(
        title: title,
        message: message,
        recipientUser: user.Username,
        createdBy: actor,
        formName: "crm/opportunities",
        relatedTable: "SalesOpportunities",
        relatedId: opportunityId);
}

    // ═══════════════════════════════════════════════════════════
    //  تحويل Lead لعميل (Party + Opportunity + Task)
    // ═══════════════════════════════════════════════════════════
    public async Task<(bool Success, string Message, int PartyId, int OpportunityId)>
        ConvertLeadToClientAsync(LeadConvertDto dto, string userName)
    {
        var lead = await _db.LeadsCRMs.FindAsync(dto.LeadId);
        if (lead == null) return (false, "Lead غير موجود", 0, 0);

        if (lead.IsConverted)
            return (false, "الـ Lead ده اتحول لعميل قبل كده", 0, 0);

        if (!dto.EmployeeId.HasValue || dto.EmployeeId.Value <= 0)
            return (false, "يجب اختيار الموظف الذي ستُسند إليه الفرصة قبل التحويل", 0, 0);

        var assignee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EmployeeId == dto.EmployeeId.Value
                                   && (e.Status == "نشط" || e.Status == "Active"));
        if (assignee == null)
            return (false, "الموظف المختار غير موجود أو غير نشط", 0, 0);

        if (string.IsNullOrWhiteSpace(lead.FullName) || string.IsNullOrWhiteSpace(lead.Phone))
            return (false, "بيانات الـ Lead ناقصة (الاسم أو الموبايل)", 0, 0);

        var candidatePhones = new[] { lead.Phone?.Trim(), lead.Phone2?.Trim() }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .ToList();

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var now = DateTime.Now;
            var oldLeadStatus = lead.LeadStatus;

            var initialStageId = await _db.SalesStages
                .AsNoTracking()
                .Where(s => s.IsActive && s.StageId == 1)
                .Select(s => s.StageId)
                .FirstOrDefaultAsync();

            if (initialStageId == 0)
                return (false, "مرحلة البداية رقم 1 غير موجودة أو غير مفعّلة.", 0, 0);

            var openFollowUps = await _db.LeadInteractions
                .Where(i => i.LeadId == lead.LeadId && i.NextFollowUpDate != null && !i.IsCompleted)
                .ToListAsync();

            foreach (var open in openFollowUps)
            {
                open.IsCompleted = true;
                open.CompletedByEmployeeId = dto.EmployeeId ?? lead.AssignedEmployeeId;
                open.CompletedDate = now;
                open.Notes = string.IsNullOrWhiteSpace(open.Notes)
                    ? "أغلقت تلقائياً بسبب تحويل الـ Lead إلى فرصة"
                    : (open.Notes + " | أغلقت تلقائياً بسبب تحويل الـ Lead إلى فرصة");
            }

            await CloseOpenLeadTasksAsync(lead.LeadId, userName, now, "تم إغلاق تكاليف الليد تلقائياً بسبب تحويل الليد إلى فرصة بيع.");

            int? resolvedSourceId = dto.SourceId;
            if (!resolvedSourceId.HasValue
                && string.Equals(lead.FormName, "manual", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(lead.FormId, out var parsedManualSourceId))
            {
                resolvedSourceId = parsedManualSourceId;
            }

            int? resolvedAdTypeId = dto.AdTypeId;
            if (!resolvedAdTypeId.HasValue
                && string.Equals(lead.FormName, "manual", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(lead.CampaignId, out var parsedLeadAdTypeId))
            {
                resolvedAdTypeId = parsedLeadAdTypeId;
            }

            var existingParty = candidatePhones.Count > 0
                ? await _db.Parties
                    .FirstOrDefaultAsync(p =>
                        (p.Phone != null && candidatePhones.Contains(p.Phone)) ||
                        (p.Phone2 != null && candidatePhones.Contains(p.Phone2)))
                : null;

            var isExistingParty = existingParty != null;

            // 1. استخدام العميل الموجود أو إنشاء عميل جديد
            var party = existingParty ?? new Party
            {
                PartyName = lead.FullName.Trim(),
                Phone = lead.Phone.Trim(),
                Phone2 = lead.Phone2?.Trim(),
                Address = lead.Address?.Trim(),
                PartyType = 1,
                IsActive = true,
                ReferralSourceId = resolvedSourceId ?? 2,
                CreatedBy = userName,
                CreatedAt = now
            };

            if (!isExistingParty)
            {
                _db.Parties.Add(party);
                await _db.SaveChangesAsync();
            }

            // 2. إنشاء فرصة بيع
            var opportunity = new SalesOpportunity
            {
                PartyId = party.PartyId,
                EmployeeId = dto.EmployeeId,
                SourceId = resolvedSourceId,
                AdTypeId = resolvedAdTypeId,
                //StageId = 1,
                StageId = initialStageId,
                CategoryId = dto.CategoryId,
                InterestedProduct = lead.ProjectType,
                FirstContactDate = lead.LeadDate ?? now,
                NextFollowUpDate = now.AddDays(1),
                Notes = dto.Notes ?? lead.Notes,
                ExpectedValue = dto.ExpectedValue,
                Guidance = BuildGuidanceFromLead(lead),
                IsActive = true,
                CreatedBy = userName,
                CreatedAt = now
            };
            _db.SalesOpportunities.Add(opportunity);
            await _db.SaveChangesAsync();

            // 3. إنشاء سجل تواصل
            var sourceLabel = string.IsNullOrWhiteSpace(lead.Platform) ? "غير محدد" : lead.Platform;
            var campaignLabel = string.IsNullOrWhiteSpace(lead.CampaignName) ? null : lead.CampaignName;

            var interaction = new CustomerInteraction
            {
                OpportunityId = opportunity.OpportunityId,
                PartyId = party.PartyId,
                EmployeeId = dto.EmployeeId,
                SourceId = resolvedSourceId,
                InteractionDate = now,
                Summary = campaignLabel == null
                    ? $"تحويل Lead إلى فرصة بيع — المصدر: {sourceLabel}"
                    : $"تحويل Lead إلى فرصة بيع — المصدر: {sourceLabel} — الحملة: {campaignLabel}",
                StageBeforeId = null,
                //StageAfterId = 1,
                StageAfterId = initialStageId,
                NextFollowUpDate = now.AddDays(1),
                Notes = dto.Notes ?? lead.Notes,
                CreatedBy = userName,
                CreatedAt = now
            };
            _db.CustomerInteractions.Add(interaction);

            // 4. إنشاء مهمة متابعة
            if (dto.EmployeeId.HasValue && dto.EmployeeId.Value > 0)
            {
                var task = new CrmTask
                {
                    OpportunityId = opportunity.OpportunityId,
                    PartyId = party.PartyId,
                    AssignedTo = dto.EmployeeId.Value,
                    TaskTypeId = dto.TaskTypeId,
                    TaskDescription = isExistingParty
                        ? $"متابعة فرصة جديدة لعميل موجود: {lead.FullName}"
                        : $"متابعة عميل جديد محول من Lead: {lead.FullName}",
                    DueDate = now.AddDays(1),
                    Priority = "Normal",
                    Status = "Pending",
                    ReminderEnabled = true,
                    IsActive = true,
                    CreatedBy = userName,
                    CreatedAt = now
                };
                _db.CrmTasks.Add(task);
            }

            // 5. تحديث الـ Lead
            lead.IsConverted = true;
            lead.ConvertedPartyId = party.PartyId;
            lead.ConvertedOpportunityId = opportunity.OpportunityId;
            lead.ConvertedDate = now;
            lead.ConvertedBy = userName;
            lead.LeadStatus = "محول";
            lead.LastContactDate = now;
            _db.LeadInteractions.Add(new LeadInteraction
{
    LeadId = lead.LeadId,
    EmployeeId = dto.EmployeeId ?? lead.AssignedEmployeeId,
    InteractionType = LeadInteractionTypes.Converted,
    InteractionDate = now,
    Summary = isExistingParty
        ? $"تم تحويل الـ Lead إلى فرصة بيع جديدة #{opportunity.OpportunityId} على عميل موجود #{party.PartyId}"
        : $"تم تحويل الـ Lead إلى عميل جديد #{party.PartyId} وفرصة بيع #{opportunity.OpportunityId}",
    Notes = dto.Notes ?? lead.Notes,
    OldLeadStatus = oldLeadStatus,
    NewLeadStatus = "محول",
    IsSystemGenerated = true,
    CreatedBy = userName,
    CreatedAt = now
});

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            await _audit.LogAsync("LeadsCRM", "Convert",
                lead.LeadId.ToString(), null, dto, userName);

            try
            {
                await NotifyOpportunityAssignedFromLeadConversionAsync(lead, opportunity.OpportunityId, dto.EmployeeId.Value, userName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send conversion assignment notification. LeadId={LeadId}, OpportunityId={OpportunityId}, EmployeeId={EmployeeId}",
                    lead.LeadId, opportunity.OpportunityId, dto.EmployeeId.Value);
            }

            return (true, "تم تحويل Lead لعميل بنجاح", party.PartyId, opportunity.OpportunityId);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Lead conversion failed for LeadId={LeadId}", dto.LeadId);
            return (false, $"خطأ: {ex.InnerException?.Message ?? ex.Message}", 0, 0);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  إحصائيات — مُحسّن (1 استعلام بدل 12!)
    // ═══════════════════════════════════════════════════════════
    public async Task<LeadsCrmStatsDto> GetStatsAsync(LeadsCrmFilterDto? filter = null)
{
    var today = DateTime.Today;
    var weekStart = today.AddDays(-(int)today.DayOfWeek);
    var monthStart = new DateTime(today.Year, today.Month, 1);
    var lateCutoff = DateTime.Now.AddHours(-1);

    // بناء الاستعلام مع الفلتر
    var query = _db.LeadsCRMs.AsNoTracking().AsQueryable();

    if (filter != null)
    {
        if (!string.IsNullOrWhiteSpace(filter.ProjectType))
            query = query.Where(l => l.ProjectType == filter.ProjectType);

        if (!string.IsNullOrWhiteSpace(filter.LeadStatus))
            query = query.Where(l => l.LeadStatus == filter.LeadStatus);

        if (!string.IsNullOrWhiteSpace(filter.Platform))
            query = query.Where(l => l.Platform == filter.Platform);

        if (filter.AssignedEmployeeId.HasValue)
            query = query.Where(l => l.AssignedEmployeeId == filter.AssignedEmployeeId.Value);

        if (filter.DateFrom.HasValue)
            query = query.Where(l => l.CreatedAt >= filter.DateFrom.Value);

        if (filter.DateTo.HasValue)
            query = query.Where(l => l.CreatedAt <= filter.DateTo.Value);
    }

    var leadsData = await query
        .Select(l => new
        {
            l.LeadStatus,
            l.IsDuplicate,
            l.IsConverted,
            l.LastContactDate,
            l.CreatedAt
        })
        .ToListAsync();

    var campaignData = await query
        .Where(l => l.CampaignName != null)
        .GroupBy(l => l.CampaignName)
        .Select(g => new LeadsByCampaignDto
        {
            CampaignName = g.Key!,
            Count = g.Count()
        })
        .OrderByDescending(x => x.Count)
        .Take(10)
        .ToListAsync();

    var platformData = await query
        .Where(l => l.Platform != null)
        .GroupBy(l => l.Platform)
        .Select(g => new LeadsByPlatformDto
        {
            Platform = g.Key!,
            Count = g.Count()
        })
        .OrderByDescending(x => x.Count)
        .ToListAsync();

    return new LeadsCrmStatsDto
    {
        TotalLeads = leadsData.Count,
        NewLeads = leadsData.Count(l => l.LeadStatus == "جديد"),
        AssignedLeads = leadsData.Count(l => l.LeadStatus == "تم الإسناد"),
        ContactedLeads = leadsData.Count(l => l.LeadStatus == "تم التواصل"),
        QualifiedLeads = leadsData.Count(l => l.LeadStatus == "مؤهل"),
        ConvertedLeads = leadsData.Count(l => l.LeadStatus == "محول"),
        RejectedLeads = leadsData.Count(l => l.LeadStatus == "مرفوض"),
        LateFollowUpLeads = leadsData.Count(l =>
            !l.IsConverted &&
            l.LeadStatus != "محول" &&
            l.LeadStatus != "مرفوض" &&
            !l.LastContactDate.HasValue &&
            l.CreatedAt <= lateCutoff),
        DuplicateLeads = leadsData.Count(l => l.IsDuplicate),
        TodayLeads = leadsData.Count(l => l.CreatedAt >= today),
        ThisWeekLeads = leadsData.Count(l => l.CreatedAt >= weekStart),
        ThisMonthLeads = leadsData.Count(l => l.CreatedAt >= monthStart),
        ByCampaign = campaignData,
        ByPlatform = platformData
    };
}

    // ═══════════════════════════════════════════════════════════
    //  لوحة تحليلات الـ Leads — 3 استعلامات بس!
    // ═══════════════════════════════════════════════════════════
    // ═══════════════════════════════════════════════════════════
//  لوحة تحليلات الـ Leads — 3 استعلامات بس!
// ═══════════════════════════════════════════════════════════
public async Task<LeadsDashboardDataDto> GetDashboardDataAsync(LeadsDashboardFilterDto filter)
{
    var result = new LeadsDashboardDataDto();

    try
    {
        var crmAccessFrom = _httpContext.GetCrmAccessFrom();
        if (crmAccessFrom.HasValue)
        {
            if (!filter.DateFrom.HasValue || filter.DateFrom < crmAccessFrom.Value)
                filter.DateFrom = crmAccessFrom.Value;
        }

        var currentQuery = _db.LeadsCRMs.AsNoTracking().AsQueryable();
        currentQuery = ApplyDashboardFilter(currentQuery, filter);

        var leads = await currentQuery.Select(l => new
        {
            l.LeadId,
            l.LeadStatus,
            l.Platform,
            l.City,
            l.Budget,
            l.ProjectType,
            l.ProjectStage,
            l.CampaignName,
            l.AdName,
            l.AdSetName,
            l.FullName,
            l.AssignedEmployeeId,
            l.IsDuplicate,
            l.IsConverted,
            l.ConvertedOpportunityId,
            l.ConvertedDate,
            l.CreatedAt
        }).ToListAsync();

        var prevFilter = filter.GetPreviousPeriod();
        var prevQuery = _db.LeadsCRMs.AsNoTracking().AsQueryable();
        prevQuery = ApplyDashboardFilter(prevQuery, prevFilter);

        var prevLeads = await prevQuery.Select(l => new
        {
            l.LeadStatus,
            l.IsDuplicate,
            l.IsConverted,
            l.ConvertedDate,
            l.CreatedAt
        }).ToListAsync();

        var empIds = leads
            .Where(l => l.AssignedEmployeeId.HasValue)
            .Select(l => l.AssignedEmployeeId!.Value)
            .Distinct()
            .ToList();

        var empNames = empIds.Count > 0
            ? await _db.Employees.AsNoTracking()
                .Where(e => empIds.Contains(e.EmployeeId))
                .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName ?? "")
            : new Dictionary<int, string>();

        const int closedDealStageId = 3;
        var lostKeywords = new[] { "خسارة", "Lost", "غير مهتم", "Not Interested" };
        var lostStageIds = await _db.SalesStages.AsNoTracking()
            .Where(s => lostKeywords.Any(k => (s.StageNameAr ?? "").Contains(k) || (s.StageName ?? "").Contains(k)))
            .Select(s => s.StageId)
            .ToListAsync();
        var lostIdSet = lostStageIds.ToHashSet();

        var trendFrom = filter.DateFrom ?? DateTime.Today.AddDays(-30);
        var trendTo = filter.DateTo ?? DateTime.Today;

        var hasLeadOnlyFilters =
            !string.IsNullOrWhiteSpace(filter.Platform) ||
            !string.IsNullOrWhiteSpace(filter.City) ||
            !string.IsNullOrWhiteSpace(filter.ProjectType) ||
            !string.IsNullOrWhiteSpace(filter.ProjectStage) ||
            !string.IsNullOrWhiteSpace(filter.CampaignName);

        var opportunityAnalyticsQuery = _db.SalesOpportunities.AsNoTracking().Where(o => o.IsActive);

        if (filter.EmployeeId.HasValue)
            opportunityAnalyticsQuery = opportunityAnalyticsQuery.Where(o => o.EmployeeId == filter.EmployeeId.Value);

        if (filter.DateFrom.HasValue)
            opportunityAnalyticsQuery = opportunityAnalyticsQuery.Where(o => o.CreatedAt >= filter.DateFrom.Value.Date);

        if (filter.DateTo.HasValue)
        {
            var oppEnd = filter.DateTo.Value.Date.AddDays(1).AddTicks(-1);
            opportunityAnalyticsQuery = opportunityAnalyticsQuery.Where(o => o.CreatedAt <= oppEnd);
        }

        if (hasLeadOnlyFilters)
        {
            var leadScopedQuery = _db.LeadsCRMs.AsNoTracking().Where(l => l.ConvertedOpportunityId.HasValue);

            if (!string.IsNullOrWhiteSpace(filter.Platform))
            {
                var platformAliases = GetPlatformAliases(filter.Platform);
                leadScopedQuery = platformAliases.Count == 0
                    ? leadScopedQuery.Where(l => l.Platform == filter.Platform)
                    : leadScopedQuery.Where(l => l.Platform != null && platformAliases.Contains(l.Platform));
            }
            if (!string.IsNullOrWhiteSpace(filter.City))
                leadScopedQuery = leadScopedQuery.Where(l => l.City == filter.City);
            if (!string.IsNullOrWhiteSpace(filter.ProjectType))
                leadScopedQuery = leadScopedQuery.Where(l => l.ProjectType == filter.ProjectType);
            if (!string.IsNullOrWhiteSpace(filter.ProjectStage))
                leadScopedQuery = leadScopedQuery.Where(l => l.ProjectStage == filter.ProjectStage);
            if (!string.IsNullOrWhiteSpace(filter.CampaignName))
                leadScopedQuery = leadScopedQuery.Where(l => l.CampaignName == filter.CampaignName);

            var filteredLeadOppIds = await leadScopedQuery
                .Select(l => l.ConvertedOpportunityId!.Value)
                .Distinct()
                .ToListAsync();

            opportunityAnalyticsQuery = filteredLeadOppIds.Any()
                ? opportunityAnalyticsQuery.Where(o => filteredLeadOppIds.Contains(o.OpportunityId))
                : opportunityAnalyticsQuery.Where(o => false);
        }

        var allOppDetails = await opportunityAnalyticsQuery.ToListAsync();

        var allOppIds = allOppDetails
            .Select(o => o.OpportunityId)
            .Distinct()
            .ToList();

        var allOppPartyIds = allOppDetails
            .Select(o => o.PartyId)
            .Distinct()
            .ToList();

        var allOppSourceIds = allOppDetails
            .Where(o => o.SourceId.HasValue)
            .Select(o => o.SourceId!.Value)
            .Distinct()
            .ToList();

        var opportunityPartyNames = allOppPartyIds.Count > 0
            ? await _db.Parties.AsNoTracking()
                .Where(p => allOppPartyIds.Contains(p.PartyId))
                .ToDictionaryAsync(p => p.PartyId, p => p.PartyName ?? $"عميل #{p.PartyId}")
            : new Dictionary<int, string>();

        var opportunitySourceNames = allOppSourceIds.Count > 0
            ? await _db.ContactSources.AsNoTracking()
                .Where(s => allOppSourceIds.Contains(s.SourceId))
                .ToDictionaryAsync(
                    s => s.SourceId,
                    s => string.IsNullOrWhiteSpace(s.SourceNameAr) ? (s.SourceName ?? "غير محدد") : s.SourceNameAr!)
            : new Dictionary<int, string>();

        var leadOriginOppIdsInScope = leads
            .Where(l => l.LeadStatus == "محول"
                && l.ConvertedOpportunityId.HasValue
                && allOppIds.Contains(l.ConvertedOpportunityId.Value))
            .Select(l => l.ConvertedOpportunityId!.Value)
            .Distinct()
            .ToHashSet();

        var primaryQuotationIds = allOppDetails
            .Where(o => o.QuotationId.HasValue)
            .Select(o => o.QuotationId!.Value)
            .Distinct()
            .ToList();

        var primaryTransactionIds = allOppDetails
            .Where(o => o.TransactionId.HasValue)
            .Select(o => o.TransactionId!.Value)
            .Distinct()
            .ToList();

        var quotations = (allOppIds.Count > 0 || primaryQuotationIds.Count > 0)
            ? await _db.Quotations.AsNoTracking()
                .Where(q =>
                    (q.OpportunityId.HasValue && allOppIds.Contains(q.OpportunityId.Value)) ||
                    primaryQuotationIds.Contains(q.QuotationId))
                .ToListAsync()
            : new List<Quotation>();

        quotations = quotations
            .GroupBy(q => q.QuotationId)
            .Select(g => g.First())
            .ToList();

        var saleTransactions = (allOppIds.Count > 0 || primaryTransactionIds.Count > 0)
            ? await _db.Transactions.AsNoTracking()
                .Where(t => t.TransactionType == TransactionTypes.Sale &&
                    ((t.OpportunityId.HasValue && allOppIds.Contains(t.OpportunityId.Value)) ||
                     primaryTransactionIds.Contains(t.TransactionId)))
                .ToListAsync()
            : new List<Transaction>();

        saleTransactions = saleTransactions
            .GroupBy(t => t.TransactionId)
            .Select(g => g.First())
            .ToList();

        var oppExpectedById = allOppDetails.ToDictionary(o => o.OpportunityId, o => o.ExpectedValue ?? 0m);
        var oppEmployeeById = allOppDetails.ToDictionary(o => o.OpportunityId, o => o.EmployeeId);
        var actualValueByOpp = saleTransactions
            .Where(t => t.OpportunityId.HasValue)
            .GroupBy(t => t.OpportunityId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.GrandTotal));
        var firstInvoiceDateByOpp = saleTransactions
            .Where(t => t.OpportunityId.HasValue)
            .GroupBy(t => t.OpportunityId!.Value)
            .ToDictionary(g => g.Key, g => g.Min(x => x.TransactionDate));

        var invoicedOppIds = saleTransactions
            .Where(t => t.OpportunityId.HasValue)
            .Select(t => t.OpportunityId!.Value)
            .Distinct()
            .ToHashSet();

        var wonDeals = allOppDetails
            .Where(o => o.StageId == closedDealStageId || invoicedOppIds.Contains(o.OpportunityId) || o.TransactionId.HasValue)
            .ToList();

        var marketingWonDeals = wonDeals
            .Where(o => !IsExcludedMarketingSourceName(o.SourceId.HasValue
                ? opportunitySourceNames.GetValueOrDefault(o.SourceId.Value)
                : null))
            .ToList();

        var marketingWonDealIds = marketingWonDeals
            .Select(o => o.OpportunityId)
            .Distinct()
            .ToHashSet();

        var invoiceRefsByOpp = saleTransactions
            .Where(t => t.OpportunityId.HasValue)
            .GroupBy(t => t.OpportunityId!.Value)
            .ToDictionary(
                g => g.Key,
                g => string.Join("، ", g.Select(x => x.ReferenceNumber).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()));

        result.ClosedDealsDetails = marketingWonDeals
            .Select(o => new ClosedDealDetailDto
            {
                OpportunityId = o.OpportunityId,
                ClientName = opportunityPartyNames.GetValueOrDefault(o.PartyId, $"عميل #{o.PartyId}"),
                InvoiceReference = invoiceRefsByOpp.TryGetValue(o.OpportunityId, out var invoiceRef) && !string.IsNullOrWhiteSpace(invoiceRef)
                    ? invoiceRef
                    : "بدون فاتورة",
                ActualValue = actualValueByOpp.TryGetValue(o.OpportunityId, out var oppActual)
                    ? oppActual
                    : (o.ActualValue ?? o.ExpectedValue ?? 0m),
                SourceName = o.SourceId.HasValue
                    ? opportunitySourceNames.GetValueOrDefault(o.SourceId.Value, "غير محدد")
                    : "غير محدد"
            })
            .OrderByDescending(x => x.ActualValue)
            .ThenBy(x => x.ClientName)
            .ToList();

        var closedDealCount = marketingWonDeals.Select(o => o.OpportunityId).Distinct().Count();
        var closedDealValue = marketingWonDeals.Sum(o => actualValueByOpp.TryGetValue(o.OpportunityId, out var oppActual) ? oppActual : (o.ActualValue ?? o.ExpectedValue ?? 0m));
        var closedDealExpectedValue = marketingWonDeals.Sum(o => o.ExpectedValue ?? 0m);
        var valueVariance = closedDealExpectedValue - closedDealValue;

        var closeStageIds = lostIdSet.Concat(new[] { closedDealStageId }).ToHashSet();
        var closeInteractionDates = allOppIds.Any()
            ? await _db.CustomerInteractions.AsNoTracking()
                .Where(ci => allOppIds.Contains(ci.OpportunityId)
                             && ci.StageAfterId.HasValue
                             && closeStageIds.Contains(ci.StageAfterId.Value))
                .GroupBy(ci => ci.OpportunityId)
                .Select(g => new { OpportunityId = g.Key, CloseDate = g.Min(x => x.InteractionDate) })
                .ToDictionaryAsync(x => x.OpportunityId, x => x.CloseDate)
            : new Dictionary<int, DateTime>();

        var closedOppsForVelocity = allOppDetails
            .Where(o => closeStageIds.Contains(o.StageId) || actualValueByOpp.ContainsKey(o.OpportunityId) || o.TransactionId.HasValue)
            .Select(o =>
            {
                DateTime closeDate;
                if (!closeInteractionDates.TryGetValue(o.OpportunityId, out closeDate))
                {
                    if (!firstInvoiceDateByOpp.TryGetValue(o.OpportunityId, out closeDate))
                        closeDate = o.LastUpdatedAt ?? o.CreatedAt;
                }

                var days = Math.Max(0, (closeDate.Date - o.CreatedAt.Date).Days);
                return new { o.OpportunityId, Days = days };
            })
            .ToList();

        result.OpportunityClosureMetrics = new OpportunityClosureMetricsDto
        {
            ClosedCount = closedOppsForVelocity.Count,
            ClosureRate = allOppDetails.Count == 0 ? 0m : Math.Round((decimal)closedOppsForVelocity.Count / allOppDetails.Count * 100m, 1),
            AvgDaysToClose = closedOppsForVelocity.Any() ? Math.Round(closedOppsForVelocity.Average(x => x.Days), 1) : 0,
            MinDaysToClose = closedOppsForVelocity.Any() ? closedOppsForVelocity.Min(x => x.Days) : null,
            MaxDaysToClose = closedOppsForVelocity.Any() ? closedOppsForVelocity.Max(x => x.Days) : null
        };

        var totalLeads = leads.Count;
        var convertedCount = leads.Count(l => l.LeadStatus == "محول");
        var duplicateCount = leads.Count(l => l.IsDuplicate);
        var rejectedCount = leads.Count(l => l.LeadStatus == "مرفوض");

        var convertedWithDate = leads
            .Where(l => l.LeadStatus == "محول" && l.ConvertedDate.HasValue && l.CreatedAt != default)
            .ToList();
        double avgDays = convertedWithDate.Count > 0
            ? convertedWithDate.Average(l => (l.ConvertedDate!.Value - l.CreatedAt).TotalDays)
            : 0;

        var prevTotal = prevLeads.Count;
        var prevConverted = prevLeads.Count(l => l.LeadStatus == "محول");
        var prevDuplicate = prevLeads.Count(l => l.IsDuplicate);
        var prevRejected = prevLeads.Count(l => l.LeadStatus == "مرفوض");

        var prevConvertedWithDate = prevLeads
            .Where(l => l.LeadStatus == "محول" && l.ConvertedDate.HasValue && l.CreatedAt != default)
            .ToList();
        double prevAvgDays = prevConvertedWithDate.Count > 0
            ? prevConvertedWithDate.Average(l => (l.ConvertedDate!.Value - l.CreatedAt).TotalDays)
            : 0;

        var convRate = totalLeads > 0 ? Math.Round((decimal)convertedCount / totalLeads * 100, 1) : 0;
        var prevConvRate = prevTotal > 0 ? Math.Round((decimal)prevConverted / prevTotal * 100, 1) : 0;
        var dupRate = totalLeads > 0 ? Math.Round((decimal)duplicateCount / totalLeads * 100, 1) : 0;
        var prevDupRate = prevTotal > 0 ? Math.Round((decimal)prevDuplicate / prevTotal * 100, 1) : 0;
        var rejRate = totalLeads > 0 ? Math.Round((decimal)rejectedCount / totalLeads * 100, 1) : 0;
        var prevRejRate = prevTotal > 0 ? Math.Round((decimal)prevRejected / prevTotal * 100, 1) : 0;

        result.Kpis = new LeadsDashboardKpisDto
        {
            TotalLeads = totalLeads,
            ConversionRate = convRate,
            AvgConversionDays = Math.Round(avgDays, 1),
            ConvertedCount = convertedCount,
            RejectedCount = rejectedCount,
            LeadOriginOpportunitiesCount = leadOriginOppIdsInScope.Count,
            LeadOriginLostCount = allOppDetails.Count(o => leadOriginOppIdsInScope.Contains(o.OpportunityId) && lostIdSet.Contains(o.StageId)),
            ClosedDealCount = closedDealCount,
            ClosedDealValue = closedDealValue,
            ClosedDealExpectedValue = closedDealExpectedValue,
            ValueVariance = valueVariance,
            DuplicateRate = dupRate,
            RejectionRate = rejRate,
            TotalLeadsChange = CalcChange(totalLeads, prevTotal),
            ConversionRateChange = CalcChange(convRate, prevConvRate),
            AvgConversionDaysChange = CalcChangeDouble(avgDays, prevAvgDays),
            DuplicateRateChange = CalcChange(dupRate, prevDupRate),
            RejectionRateChange = CalcChange(rejRate, prevRejRate)
        };

        var statusColors = new Dictionary<string, string>
        {
            { "جديد", "#3b82f6" },
            { "تم الإسناد", "#0ea5e9" },
            { "تم التواصل", "#f59e0b" },
            { "محول", "#8b5cf6" },
            { "مرفوض", "#ef4444" }
        };
        var statusDisplayNames = new Dictionary<string, string>
        {
            { "تم التواصل", "تفاوض" }
        };
        var statusOrder = new[] { "جديد", "تم الإسناد", "تم التواصل", "محول", "مرفوض" };
        var statusCounts = leads
            .GroupBy(l => l.LeadStatus ?? "غير محدد")
            .ToDictionary(g => g.Key, g => g.Count());

        result.StatusDistribution = statusOrder
            .Where(statusCounts.ContainsKey)
            .Select(k => new ChartItemDto
            {
                Label = statusDisplayNames.GetValueOrDefault(k, k),
                Value = statusCounts[k],
                Color = statusColors.GetValueOrDefault(k, "#6b7280")
            })
            .ToList();

        result.StatusDistribution.AddRange(
            statusCounts
                .Where(x => !statusOrder.Contains(x.Key))
                .Select(x => new ChartItemDto
                {
                    Label = x.Key,
                    Value = x.Value,
                    Color = "#6b7280"
                }));

        result.PlatformData = leads
            .Select(l => NormalizeLeadPlatformLabel(l.Platform))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .GroupBy(p => p!)
            .Select(g => new ChartItemDto
            {
                Label = g.Key,
                Value = g.Count()
            })
            .OrderByDescending(x => x.Value)
            .ToList();

        var dailyGroups = leads
            .Where(l => l.CreatedAt.Date >= trendFrom.Date && l.CreatedAt.Date <= trendTo.Date)
            .GroupBy(l => l.CreatedAt.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        var allDates = Enumerable.Range(0, (trendTo.Date - trendFrom.Date).Days + 1)
            .Select(d => trendFrom.Date.AddDays(d));

        result.DailyTrend = allDates.Select(d =>
        {
            var dayLeads = dailyGroups.GetValueOrDefault(d);
            return new DailyTrendItemDto
            {
                Date = d,
                Leads = dayLeads?.Count ?? 0,
                Contacted = dayLeads?.Count(l => l.LeadStatus == "تم التواصل") ?? 0,
                Converted = dayLeads?.Count(l => l.LeadStatus == "محول") ?? 0
            };
        }).ToList();

        result.BudgetDistribution = leads
            .GroupBy(l => MapBudgetCategory(l.Budget))
            .Select(g => new ChartItemDto { Label = g.Key, Value = g.Count() })
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Label)
            .Take(8)
            .ToList();

        result.TopCities = leads
            .Where(l => !string.IsNullOrWhiteSpace(l.City))
            .GroupBy(l => l.City)
            .Select(g => new ChartItemDto { Label = g.Key!, Value = g.Count() })
            .OrderByDescending(x => x.Value)
            .Take(8)
            .ToList();

        var empSales = saleTransactions
            .Where(t => t.OpportunityId.HasValue && marketingWonDealIds.Contains(t.OpportunityId.Value))
            .Select(t => new
            {
                EmpId = t.EmpId ?? (t.OpportunityId.HasValue ? oppEmployeeById.GetValueOrDefault(t.OpportunityId.Value) : null),
                t.GrandTotal,
                t.OpportunityId
            })
            .Where(x => x.EmpId.HasValue)
            .GroupBy(x => x.EmpId!.Value)
            .ToDictionary(g => g.Key, g => new
            {
                Count = g.Select(x => x.OpportunityId ?? 0).Where(x => x > 0).Distinct().Count(),
                Value = g.Sum(x => x.GrandTotal)
            });

        if (empSales.Count == 0)
        {
            empSales = marketingWonDeals
                .Where(o => o.EmployeeId.HasValue)
                .GroupBy(o => o.EmployeeId!.Value)
                .ToDictionary(g => g.Key, g => new
                {
                    Count = g.Count(),
                    Value = g.Sum(o => o.ActualValue ?? o.ExpectedValue ?? 0m)
                });
        }

        result.EmployeePerformance = leads
            .Where(l => l.AssignedEmployeeId.HasValue)
            .GroupBy(l => l.AssignedEmployeeId!.Value)
            .Select(g => new DashboardEmployeeDto
            {
                Name = empNames.GetValueOrDefault(g.Key, $"موظف {g.Key}"),
                NewCount = g.Count(l => l.LeadStatus == "جديد"),
                ContactedCount = g.Count(l => l.LeadStatus == "تم التواصل"),
                ConvertedCount = g.Count(l => l.LeadStatus == "محول"),
                RejectedCount = g.Count(l => l.LeadStatus == "مرفوض"),
                ClosedDealCount = empSales.ContainsKey(g.Key) ? empSales[g.Key].Count : 0,
                ClosedDealValue = empSales.ContainsKey(g.Key) ? empSales[g.Key].Value : 0
            })
            .Select(e => { e.Total = e.NewCount + e.ContactedCount + e.ConvertedCount + e.RejectedCount + e.ClosedDealCount; return e; })
            .OrderByDescending(e => e.Total)
            .Take(10)
            .ToList();

        var newCount = leads.Count(l => l.LeadStatus == "جديد");
        var contactedCount = leads.Count(l => l.LeadStatus == "تم التواصل");
        var lostDealCount = allOppDetails.Count(o => lostIdSet.Contains(o.StageId));

        result.FunnelData = new List<FunnelItemDto>
        {
            new() { Stage = "جديد", Count = newCount, Percentage = totalLeads > 0 ? Math.Round((decimal)newCount / totalLeads * 100, 1) : 0, Color = "#3b82f6" },
            new() { Stage = "تفاوض", Count = contactedCount, Percentage = totalLeads > 0 ? Math.Round((decimal)contactedCount / totalLeads * 100, 1) : 0, Color = "#f59e0b" },
            new() { Stage = "محول", Count = convertedCount, Percentage = totalLeads > 0 ? Math.Round((decimal)convertedCount / totalLeads * 100, 1) : 0, Color = "#8b5cf6" },
            new() { Stage = "صفقة مغلقة", Count = closedDealCount, Percentage = totalLeads > 0 ? Math.Round((decimal)closedDealCount / totalLeads * 100, 1) : 0, Color = "#10b981" },
            new() { Stage = "مرفوض/خسارة", Count = rejectedCount + lostDealCount, Percentage = totalLeads > 0 ? Math.Round((decimal)(rejectedCount + lostDealCount) / totalLeads * 100, 1) : 0, Color = "#ef4444" }
        };

        var allWonOppIds = wonDeals.Select(o => o.OpportunityId).ToHashSet();
        var allLostOppIds = allOppDetails
            .Where(o => lostIdSet.Contains(o.StageId))
            .Select(o => o.OpportunityId)
            .ToHashSet();

        var opportunityStageIds = allOppDetails
            .Select(o => o.StageId)
            .Distinct()
            .ToList();

        var opportunityStagesById = opportunityStageIds.Any()
            ? await _db.SalesStages.AsNoTracking()
                .Where(s => opportunityStageIds.Contains(s.StageId))
                .ToDictionaryAsync(s => s.StageId)
            : new Dictionary<int, SalesStage>();

        var newLeadCount = leads.Count(l => l.LeadStatus == "جديد");
        var assignedLeadCount = leads.Count(l => l.LeadStatus == "تم الإسناد");
        var untouchedLeadCount = newLeadCount + assignedLeadCount;
        var contactedOnlyCount = leads.Count(l =>
            (l.LeadStatus == "تم التواصل" || l.LeadStatus == "مؤهل")
            && !(l.IsConverted || l.LeadStatus == "محول")
            && l.LeadStatus != "مرفوض");
        var contactedStageCount = leads.Count(l =>
            l.LeadStatus == "تم التواصل"
            && !(l.IsConverted || l.LeadStatus == "محول")
            && l.LeadStatus != "مرفوض");
        var qualifiedStageCount = leads.Count(l =>
            l.LeadStatus == "مؤهل"
            && !(l.IsConverted || l.LeadStatus == "محول")
            && l.LeadStatus != "مرفوض");
        var convertedLeadCountForTree = leads.Count(l =>
            (l.IsConverted || l.LeadStatus == "محول")
            && l.ConvertedOpportunityId.HasValue
            && allOppIds.Contains(l.ConvertedOpportunityId.Value));
        var rejectedLeadCountForTree = leads.Count(l => l.LeadStatus == "مرفوض");

        var convertedOppsForTree = allOppDetails
            .Where(o => leadOriginOppIdsInScope.Contains(o.OpportunityId))
            .ToList();

        var convertedStageNodes = convertedOppsForTree
            .GroupBy(o => o.StageId)
            .OrderBy(g => opportunityStagesById.TryGetValue(g.Key, out var stage) ? stage.StageOrder : int.MaxValue)
            .ThenBy(g => g.Key)
            .Select(g =>
            {
                opportunityStagesById.TryGetValue(g.Key, out var stage);
                var stageTitle = stage?.StageNameAr ?? stage?.StageName ?? $"مرحلة {g.Key}";
                var stageAccent = stage?.StageColor ?? "#6366f1";
                var stageBg = IsExitStageId(g.Key)
                    ? "#fef2f2"
                    : g.Key == closedDealStageId ? "#ecfdf5" : "#eef2ff";
                var stageBorder = IsExitStageId(g.Key)
                    ? "#fca5a5"
                    : g.Key == closedDealStageId ? "#86efac" : "#c7d2fe";
                var stageHover = IsExitStageId(g.Key)
                    ? "#ef4444"
                    : g.Key == closedDealStageId ? "#10b981" : "#6366f1";
                var stageValue = g.Sum(o => actualValueByOpp.TryGetValue(o.OpportunityId, out var oppActual) ? oppActual : (o.ActualValue ?? o.ExpectedValue ?? 0m));

                var stageCount = g.Select(x => x.OpportunityId).Distinct().Count();
                return CreateLeadJourneyNode(
                    id: $"opp-stage-{g.Key}",
                    title: stageTitle,
                    count: stageCount,
                    bgColor: stageBg,
                    borderColor: stageBorder,
                    borderHoverColor: stageHover,
                    accentColor: stageAccent,
                    subLabel: BuildLeadJourneyStageSub(stageCount, leadOriginOppIdsInScope.Count, stageValue));
            })
            .ToList();

        var leadJourneyChildren = new List<LeadJourneyTreeNodeDto>
        {
            CreateLeadJourneyNode(
                id: "lead-untouched",
                title: "لم يتم التواصل",
                count: untouchedLeadCount,
                bgColor: "#eff6ff",
                borderColor: "#93c5fd",
                borderHoverColor: "#3b82f6",
                accentColor: "#3b82f6",
                subLabel: BuildLeadJourneyLeadSub(untouchedLeadCount, totalLeads),
                children: new List<LeadJourneyTreeNodeDto>
                {
                    CreateLeadJourneyNode("lead-new", "جديد", newLeadCount, "#eff6ff", "#bfdbfe", "#60a5fa", "#2563eb", BuildLeadJourneyLeadSub(newLeadCount, totalLeads)),
                    CreateLeadJourneyNode("lead-assigned", "تم الإسناد", assignedLeadCount, "#e0f2fe", "#7dd3fc", "#0ea5e9", "#0284c7", BuildLeadJourneyLeadSub(assignedLeadCount, totalLeads))
                }),
            CreateLeadJourneyNode(
                id: "lead-contacted",
                title: "تم التواصل",
                count: contactedOnlyCount,
                bgColor: "#fff7ed",
                borderColor: "#fdba74",
                borderHoverColor: "#f97316",
                accentColor: "#f97316",
                subLabel: BuildLeadJourneyLeadSub(contactedOnlyCount, totalLeads),
                children: new List<LeadJourneyTreeNodeDto>
                {
                    CreateLeadJourneyNode("lead-contacted-only", "تم التواصل فقط", contactedStageCount, "#fff7ed", "#fdba74", "#fb923c", "#ea580c", BuildLeadJourneyLeadSub(contactedStageCount, totalLeads)),
                    CreateLeadJourneyNode("lead-qualified", "مؤهل", qualifiedStageCount, "#faf5ff", "#d8b4fe", "#a855f7", "#9333ea", BuildLeadJourneyLeadSub(qualifiedStageCount, totalLeads))
                }),
            CreateLeadJourneyNode(
                id: "lead-rejected",
                title: "مرفوض",
                count: rejectedLeadCountForTree,
                bgColor: "#fef2f2",
                borderColor: "#fca5a5",
                borderHoverColor: "#ef4444",
                accentColor: "#dc2626",
                subLabel: BuildLeadJourneyLeadSub(rejectedLeadCountForTree, totalLeads)),
            CreateLeadJourneyNode(
                id: "lead-converted",
                title: "محول إلى فرصة",
                count: convertedLeadCountForTree,
                bgColor: "#f5f3ff",
                borderColor: "#c4b5fd",
                borderHoverColor: "#8b5cf6",
                accentColor: "#7c3aed",
                subLabel: BuildLeadJourneyLeadSub(convertedLeadCountForTree, totalLeads),
                children: convertedStageNodes)
        };

        var classifiedLeadCount = untouchedLeadCount + contactedOnlyCount + rejectedLeadCountForTree + convertedLeadCountForTree;
        var remainingLeadCount = Math.Max(0, totalLeads - classifiedLeadCount);
        if (remainingLeadCount > 0)
        {
            leadJourneyChildren.Add(CreateLeadJourneyNode(
                id: "lead-other",
                title: "حالات أخرى",
                count: remainingLeadCount,
                bgColor: "#f8fafc",
                borderColor: "#cbd5e1",
                borderHoverColor: "#64748b",
                accentColor: "#475569",
                subLabel: BuildLeadJourneyLeadSub(remainingLeadCount, totalLeads)));
        }

        result.LeadJourneyTree = CreateLeadJourneyNode(
            id: "lead-root",
            title: "إجمالي العملاء المحتملين",
            count: totalLeads,
            bgColor: "#f8fafc",
            borderColor: "#94a3b8",
            borderHoverColor: "#475569",
            accentColor: "#475569",
            subLabel: "الأساس من LeadsCRM",
            children: leadJourneyChildren);

        var openStageOutcomeById = new Dictionary<int, string>();
        var openStagesInScope = opportunityStagesById.Values
            .Where(s => s.StageId != closedDealStageId && !lostIdSet.Contains(s.StageId))
            .OrderBy(s => s.StageOrder)
            .ThenBy(s => s.StageId)
            .ToList();

        foreach (var stage in openStagesInScope)
        {
            var explicitLabel = TryMapOpportunityOpenOutcomeLabel(stage);
            if (!string.IsNullOrWhiteSpace(explicitLabel))
                openStageOutcomeById[stage.StageId] = explicitLabel!;
        }

        var unresolvedOpenStages = openStagesInScope
            .Where(s => !openStageOutcomeById.ContainsKey(s.StageId))
            .ToList();

        for (var i = 0; i < unresolvedOpenStages.Count; i++)
            openStageOutcomeById[unresolvedOpenStages[i].StageId] = ResolveFallbackOpportunityOpenOutcomeLabel(i, unresolvedOpenStages.Count);

        var opportunityOutcomeCounts = new Dictionary<string, int>
        {
            ["محتمل"] = 0,
            ["مهتم"] = 0,
            ["عالي الاهتمام"] = 0,
            ["تم البيع"] = 0,
            ["خسارة"] = 0
        };

        foreach (var opp in allOppDetails)
        {
            string bucket;

            if (allWonOppIds.Contains(opp.OpportunityId))
            {
                bucket = "تم البيع";
            }
            else if (allLostOppIds.Contains(opp.OpportunityId))
            {
                bucket = "خسارة";
            }
            else if (openStageOutcomeById.TryGetValue(opp.StageId, out var resolvedBucket))
            {
                bucket = resolvedBucket;
            }
            else
            {
                bucket = "مهتم";
            }

            opportunityOutcomeCounts[bucket] = opportunityOutcomeCounts.GetValueOrDefault(bucket) + 1;
        }

        result.ConvertedLeadOutcomeDistribution = new List<ChartItemDto>
        {
            new() { Label = "محتمل", Value = opportunityOutcomeCounts.GetValueOrDefault("محتمل"), Color = "#3b82f6" },
            new() { Label = "مهتم", Value = opportunityOutcomeCounts.GetValueOrDefault("مهتم"), Color = "#f59e0b" },
            new() { Label = "عالي الاهتمام", Value = opportunityOutcomeCounts.GetValueOrDefault("عالي الاهتمام"), Color = "#8b5cf6" },
            new() { Label = "تم البيع", Value = opportunityOutcomeCounts.GetValueOrDefault("تم البيع"), Color = "#10b981" },
            new() { Label = "خسارة", Value = opportunityOutcomeCounts.GetValueOrDefault("خسارة"), Color = "#ef4444" }
        }
        .Where(x => x.Value > 0)
        .ToList();

        var arMonths = new[] { "", "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };

        var salesPeriods = marketingWonDeals
            .GroupBy(o => new { o.CreatedAt.Year, o.CreatedAt.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new SalesByPeriodDto
            {
                Period = $"{arMonths[g.Key.Month]} {g.Key.Year}",
                TotalValue = g.Sum(o => actualValueByOpp.TryGetValue(o.OpportunityId, out var oppActual) ? oppActual : (o.ActualValue ?? o.ExpectedValue ?? 0m)),
                ExpectedTotalValue = g.Sum(o => o.ExpectedValue ?? 0m),
                DealCount = g.Count()
            })
            .ToList();

        {
            var allMonths = new List<SalesByPeriodDto>();
            var m = new DateTime(trendFrom.Year, trendFrom.Month, 1);
            var end = new DateTime(trendTo.Year, trendTo.Month, 1);
            while (m <= end)
            {
                var period = $"{arMonths[m.Month]} {m.Year}";
                var existing = salesPeriods.FirstOrDefault(p => p.Period == period);
                allMonths.Add(existing ?? new SalesByPeriodDto
                {
                    Period = period,
                    TotalValue = 0,
                    ExpectedTotalValue = 0,
                    DealCount = 0
                });
                m = m.AddMonths(1);
            }
            result.SalesByPeriod = allMonths;
        }

        var valueComparisonRows = new List<ValueComparisonDto>();
        {
            var m = new DateTime(trendFrom.Year, trendFrom.Month, 1);
            var end = new DateTime(trendTo.Year, trendTo.Month, 1);
            while (m <= end)
            {
                var monthWonDeals = marketingWonDeals.Where(o => o.CreatedAt.Year == m.Year && o.CreatedAt.Month == m.Month).ToList();
                var actualValue = monthWonDeals.Sum(o => actualValueByOpp.TryGetValue(o.OpportunityId, out var oppActual) ? oppActual : (o.ActualValue ?? 0m));
                var expectedValue = monthWonDeals.Sum(o => o.ExpectedValue ?? 0m);

                valueComparisonRows.Add(new ValueComparisonDto
                {
                    Period = $"{arMonths[m.Month]} {m.Year}",
                    ExpectedValue = expectedValue,
                    ActualValue = actualValue
                });

                m = m.AddMonths(1);
            }
        }
        result.ValueComparison = valueComparisonRows;

        var packageColors = new Dictionary<string, string>
        {
            { PricingTiers.CClass, "#6366f1" },
            { PricingTiers.Premium, "#0ea5e9" },
            { PricingTiers.Elite, "#f59e0b" },
            { QuotationPricingModes.Mixed, "#8b5cf6" }
        };
        var packageLabels = new Dictionary<string, string>
        {
            { PricingTiers.CClass, QuotationPricingModes.All.GetValueOrDefault(PricingTiers.CClass, "ستاندرد") },
            { PricingTiers.Premium, QuotationPricingModes.All.GetValueOrDefault(PricingTiers.Premium, "بريميوم") },
            { PricingTiers.Elite, QuotationPricingModes.All.GetValueOrDefault(PricingTiers.Elite, "إيليت") },
            { QuotationPricingModes.Mixed, QuotationPricingModes.All.GetValueOrDefault(QuotationPricingModes.Mixed, "مختلط") }
        };
        var packageOrder = new[] { PricingTiers.CClass, PricingTiers.Premium, PricingTiers.Elite, QuotationPricingModes.Mixed };
        var packageMetrics = quotations
            .GroupBy(q => string.IsNullOrWhiteSpace(q.PricingType) ? PricingTiers.Premium : q.PricingType!)
            .ToDictionary(
                g => g.Key,
                g => new QuotationPackageMetricDto
                {
                    PackageKey = g.Key,
                    PackageName = packageLabels.GetValueOrDefault(g.Key, g.Key),
                    Count = g.Count(),
                    TotalValue = g.Sum(x => x.GrandTotal ?? x.TotalAmount),
                    RejectedCount = g.Count(x => string.Equals(x.Status, QuotationStatuses.Rejected, StringComparison.OrdinalIgnoreCase)),
                    Color = packageColors.GetValueOrDefault(g.Key, "#94a3b8")
                });

        result.QuotationPackageMetrics = packageOrder
            .Where(packageMetrics.ContainsKey)
            .Select(k => packageMetrics[k])
            .ToList();

        result.QuotationPackageDistribution = result.QuotationPackageMetrics
            .Select(x => new ChartItemDto
            {
                Label = x.PackageName,
                Value = x.TotalValue,
                Color = x.Color
            })
            .ToList();

        var visitStatusIds = await _db.ContactStatuses.AsNoTracking()
            .Where(s =>
                (s.StatusNameAr != null && (s.StatusNameAr.Contains("زيارة") || s.StatusNameAr.Contains("المعرض"))) ||
                (s.StatusName != null && (s.StatusName.Contains("Visit") || s.StatusName.Contains("Show Room") || s.StatusName.Contains("ShowRoom"))))
            .Select(s => s.StatusId)
            .Distinct()
            .ToListAsync();

        if (visitStatusIds.Any())
        {
            var showroomVisits = await _db.CustomerInteractions.AsNoTracking()
                .Where(ci => allOppIds.Contains(ci.OpportunityId)
                             && ci.StatusId.HasValue
                             && visitStatusIds.Contains(ci.StatusId.Value))
                .Select(ci => new { ci.OpportunityId, ci.PartyId })
                .ToListAsync();

            result.ShowroomVisitMetrics = new ShowroomVisitMetricsDto
            {
                TotalVisits = showroomVisits.Count,
                UniqueVisitors = showroomVisits.Select(v => v.PartyId).Distinct().Count(),
                LeadOriginVisits = showroomVisits.Count(v => leadOriginOppIdsInScope.Contains(v.OpportunityId)),
                DirectVisits = showroomVisits.Count(v => !leadOriginOppIdsInScope.Contains(v.OpportunityId)),
                RepeatVisitors = showroomVisits.GroupBy(v => v.PartyId).Count(g => g.Count() > 1)
            };

            result.ShowroomVisitOriginDistribution = new List<ChartItemDto>
            {
                new() { Key = "lead", Label = "من الليدز", Value = result.ShowroomVisitMetrics.LeadOriginVisits, Color = "#7c3aed" },
                new() { Key = "direct", Label = "مباشر", Value = result.ShowroomVisitMetrics.DirectVisits, Color = "#0ea5e9" }
            }
            .Where(x => x.Value > 0)
            .ToList();
        }

        // مصادر الفرص البيعية خارج Leads: المصدر الرسمي هو Opportunity.SourceId فقط.
        // لا نستخدم Party.ContactSourceId هنا، لأن هذا الرسم يقيس مصدر الفرصة البيعية.
        var directOpportunityIds = allOppDetails
            .Where(o => !leadOriginOppIdsInScope.Contains(o.OpportunityId))
            .Select(o => o.OpportunityId)
            .Distinct()
            .ToList();

        var directSourceRows = new List<DirectSourceRow>();
        if (directOpportunityIds.Count > 0)
        {
            directSourceRows = await _db.SalesOpportunities.AsNoTracking()
                .Where(o => directOpportunityIds.Contains(o.OpportunityId))
                .Select(o => new DirectSourceRow
                {
                    SourceId = o.SourceId
                })
                .ToListAsync();
        }

        var directSourceIds = directSourceRows
            .Where(o => o.SourceId.HasValue)
            .Select(o => o.SourceId!.Value)
            .Distinct()
            .ToList();

        var sourceNames = directSourceIds.Count > 0
            ? await _db.ContactSources.AsNoTracking()
                .Where(s => directSourceIds.Contains(s.SourceId))
                .ToDictionaryAsync(
                    s => s.SourceId,
                    s => string.IsNullOrWhiteSpace(s.SourceNameAr)
                        ? s.SourceName
                        : s.SourceNameAr!)
            : new Dictionary<int, string>();

        result.ExternalCustomerSourceDistribution = directSourceRows
            .GroupBy(o => o.SourceId)
            .Select(g =>
            {
                var sourceKey = g.Key.HasValue
                    ? $"source:{g.Key.Value}"
                    : "source:unknown";
                var sourceLabel = g.Key.HasValue
                    ? sourceNames.GetValueOrDefault(g.Key.Value, "مصدر غير معروف")
                    : "مصدر غير محدد";

                return new ChartItemDto
                {
                    Key = sourceKey,
                    Label = sourceLabel,
                    Value = g.Count(),
                    Color = "#0ea5e9"
                };
            })
            .OrderByDescending(x => x.Value)
            .Take(10)
            .ToList();

        var totalQuotations = quotations.Count;
        var acceptedQuotations = quotations.Count(q => string.Equals(q.Status, QuotationStatuses.Accepted, StringComparison.OrdinalIgnoreCase));
        var rejectedQuotations = quotations.Count(q => string.Equals(q.Status, QuotationStatuses.Rejected, StringComparison.OrdinalIgnoreCase));
        var convertedQuotations = quotations.Count(q => string.Equals(q.Status, QuotationStatuses.Converted, StringComparison.OrdinalIgnoreCase) || q.InvoiceId.HasValue);
        var openQuotations = Math.Max(totalQuotations - acceptedQuotations - rejectedQuotations - convertedQuotations, 0);

        result.QuotationStatusDistribution = new List<ChartItemDto>
        {
            new() { Label = "قيد المتابعة", Value = openQuotations, Color = "#3b82f6" },
            new() { Label = "مقبول", Value = acceptedQuotations, Color = "#10b981" },
            new() { Label = "مرفوض", Value = rejectedQuotations, Color = "#ef4444" },
            new() { Label = "تحوّل لفاتورة", Value = convertedQuotations, Color = "#8b5cf6" }
        }
        .Where(x => x.Value > 0)
        .ToList();

        result.QuotationStatusSummary = new List<QuotationStatusSummaryDto>
        {
            new()
            {
                StatusKey = "total",
                StatusName = "إجمالي عروض الأسعار",
                Count = totalQuotations,
                Percent = totalQuotations > 0 ? 100m : 0m,
                Color = "#6366f1"
            },
            new()
            {
                StatusKey = QuotationStatuses.Accepted,
                StatusName = "مقبول",
                Count = acceptedQuotations,
                Percent = totalQuotations > 0 ? Math.Round((decimal)acceptedQuotations / totalQuotations * 100m, 1) : 0m,
                Color = "#10b981"
            },
            new()
            {
                StatusKey = QuotationStatuses.Rejected,
                StatusName = "مرفوض",
                Count = rejectedQuotations,
                Percent = totalQuotations > 0 ? Math.Round((decimal)rejectedQuotations / totalQuotations * 100m, 1) : 0m,
                Color = "#ef4444"
            },
            new()
            {
                StatusKey = QuotationStatuses.Converted,
                StatusName = "تحوّل لفاتورة",
                Count = convertedQuotations,
                Percent = totalQuotations > 0 ? Math.Round((decimal)convertedQuotations / totalQuotations * 100m, 1) : 0m,
                Color = "#8b5cf6"
            }
        };

        result.TopCampaigns = leads
            .Select(l => new
            {
                CampaignLabel = BuildCampaignDisplayName(l.AdSetName, l.AdName, l.CampaignName),
                Platform = NormalizeLeadPlatformLabel(l.Platform) ?? string.Empty,
                l.LeadStatus
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.CampaignLabel))
            .GroupBy(x => new { x.CampaignLabel, x.Platform })
            .Select(g => new CampaignPerformanceDto
            {
                CampaignName = g.Key.CampaignLabel!,
                Platform = g.Key.Platform,
                TotalLeads = g.Count(),
                ConvertedLeads = g.Count(l => l.LeadStatus == "محول"),
                ConversionRate = g.Count() > 0
                    ? Math.Round((decimal)g.Count(l => l.LeadStatus == "محول") / g.Count() * 100, 1) : 0
            })
            .OrderByDescending(x => x.ConvertedLeads)
            .ThenByDescending(x => x.ConversionRate)
            .ThenByDescending(x => x.TotalLeads)
            .Take(10)
            .ToList();

        result.ProjectSummary = leads
            .Where(l => l.ProjectType != null)
            .GroupBy(l => l.ProjectType)
            .Select(g => new ProjectTypeSummaryDto
            {
                ProjectType = g.Key!,
                TotalLeads = g.Count(),
                ConvertedLeads = g.Count(l => l.LeadStatus == "محول"),
                ConversionRate = g.Count() > 0
                    ? Math.Round((decimal)g.Count(l => l.LeadStatus == "محول") / g.Count() * 100, 1) : 0
            })
            .OrderByDescending(x => x.TotalLeads)
            .Take(10)
            .ToList();

        result.RecentConverted = leads
            .Where(l => l.LeadStatus == "محول" && l.ConvertedDate.HasValue)
            .OrderByDescending(l => l.ConvertedDate)
            .Take(10)
            .Select(l => new RecentConvertedDto
            {
                FullName = l.FullName,
                CampaignName = l.CampaignName ?? "",
                EmployeeName = l.AssignedEmployeeId.HasValue
                    && empNames.TryGetValue(l.AssignedEmployeeId.Value, out var ename) ? ename : "",
                ConvertedDate = l.ConvertedDate!.Value,
                Budget = ""
            }).ToList();

        result.AvailableCities = leads
            .Where(l => l.City != null).Select(l => l.City!).Distinct().OrderBy(c => c).ToList();

        result.AvailableProjectTypes = leads
            .Where(l => l.ProjectType != null).Select(l => l.ProjectType!).Distinct().OrderBy(p => p).ToList();

        result.AvailableProjectStages = leads
            .Where(l => l.ProjectStage != null).Select(l => l.ProjectStage!).Distinct().OrderBy(p => p).ToList();

        result.AvailableCampaigns = leads
            .Where(l => l.CampaignName != null).Select(l => l.CampaignName!).Distinct().OrderBy(c => c).ToList();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Leads Dashboard load failed: {Msg}", ex.Message);
        throw;
    }

    return result;
}



    public async Task<List<ShowroomVisitDetailDto>> GetShowroomVisitDetailsAsync(
        LeadsDashboardFilterDto filter,
        string originKey)
    {
        if (originKey is not ("lead" or "direct"))
            return new List<ShowroomVisitDetailDto>();

        var visitStatusIds = await _db.ContactStatuses
            .AsNoTracking()
            .Where(s =>
                (s.StatusNameAr != null &&
                    (s.StatusNameAr.Contains("زيارة") || s.StatusNameAr.Contains("المعرض"))) ||
                (s.StatusName != null &&
                    (s.StatusName.Contains("Visit") ||
                     s.StatusName.Contains("Show Room") ||
                     s.StatusName.Contains("ShowRoom"))))
            .Select(s => s.StatusId)
            .Distinct()
            .ToListAsync();

        if (visitStatusIds.Count == 0)
            return new List<ShowroomVisitDetailDto>();

        var scopedLeadsQuery = ApplyDashboardFilter(
            _db.LeadsCRMs.AsNoTracking(), filter);

        var leadOriginOppIds = await scopedLeadsQuery
            .Where(l => l.ConvertedOpportunityId.HasValue)
            .Select(l => l.ConvertedOpportunityId!.Value)
            .Distinct()
            .ToListAsync();

        var hasLeadOnlyFilters =
            !string.IsNullOrWhiteSpace(filter.Platform) ||
            !string.IsNullOrWhiteSpace(filter.City) ||
            !string.IsNullOrWhiteSpace(filter.ProjectType) ||
            !string.IsNullOrWhiteSpace(filter.ProjectStage) ||
            !string.IsNullOrWhiteSpace(filter.CampaignName);

        var opportunitiesQuery = _db.SalesOpportunities
            .AsNoTracking()
            .Where(o => o.IsActive);

        if (filter.EmployeeId.HasValue)
            opportunitiesQuery = opportunitiesQuery
                .Where(o => o.EmployeeId == filter.EmployeeId.Value);

        if (filter.DateFrom.HasValue)
            opportunitiesQuery = opportunitiesQuery
                .Where(o => o.CreatedAt >= filter.DateFrom.Value.Date);

        if (filter.DateTo.HasValue)
            opportunitiesQuery = opportunitiesQuery
                .Where(o => o.CreatedAt < filter.DateTo.Value.Date.AddDays(1));

        if (hasLeadOnlyFilters)
        {
            opportunitiesQuery = leadOriginOppIds.Count > 0
                ? opportunitiesQuery.Where(o => leadOriginOppIds.Contains(o.OpportunityId))
                : opportunitiesQuery.Where(o => false);
        }

        var opportunityIds = await opportunitiesQuery
            .Select(o => o.OpportunityId)
            .ToListAsync();

        if (opportunityIds.Count == 0)
            return new List<ShowroomVisitDetailDto>();

        var selectedOpportunityIds = originKey == "lead"
            ? opportunityIds.Intersect(leadOriginOppIds).ToList()
            : opportunityIds.Except(leadOriginOppIds).ToList();

        if (selectedOpportunityIds.Count == 0)
            return new List<ShowroomVisitDetailDto>();

        var visits = await _db.CustomerInteractions
            .AsNoTracking()
            .Where(i => selectedOpportunityIds.Contains(i.OpportunityId)
                        && i.StatusId.HasValue
                        && visitStatusIds.Contains(i.StatusId.Value)
                        && (!filter.DateFrom.HasValue ||
                            i.InteractionDate >= filter.DateFrom.Value.Date)
                        && (!filter.DateTo.HasValue ||
                            i.InteractionDate < filter.DateTo.Value.Date.AddDays(1)))
            .OrderByDescending(i => i.InteractionDate)
            .Take(500)
            .Select(i => new ShowroomVisitDetailDto
            {
                InteractionId = i.InteractionId,
                LeadId = null,
                OpportunityId = i.OpportunityId,
                PartyId = i.PartyId,
                CustomerName = i.Party.PartyName,
                Phone = i.Party.Phone ?? "",
                VisitDate = i.InteractionDate,
                EmployeeName = i.Employee != null ? i.Employee.FullName : null,
                OpportunityStage = i.Opportunity.Stage.StageNameAr ?? i.Opportunity.Stage.StageName,
                OriginKey = originKey,
                OriginName = originKey == "lead" ? "من الليدز" : "مباشر"
            })
            .ToListAsync();

        if (visits.Count == 0)
            return visits;

        var visitOpportunityIds = visits
            .Select(v => v.OpportunityId)
            .Distinct()
            .ToList();

        var leadInfoByOpportunity = await _db.LeadsCRMs
            .AsNoTracking()
            .Where(l => l.ConvertedOpportunityId.HasValue
                        && visitOpportunityIds.Contains(l.ConvertedOpportunityId.Value))
            .Select(l => new
            {
                LeadId = l.LeadId,
                OpportunityId = l.ConvertedOpportunityId!.Value,
                l.CampaignName,
                l.Platform
            })
            .ToDictionaryAsync(x => x.OpportunityId);

        foreach (var visit in visits)
        {
            if (leadInfoByOpportunity.TryGetValue(visit.OpportunityId, out var leadInfo))
            {
                visit.LeadId = leadInfo.LeadId;
                visit.CampaignName = leadInfo.CampaignName;
                visit.Platform = leadInfo.Platform;
            }
        }

        return visits;
    }

    public async Task<List<ShowroomVisitDetailDto>> GetExternalCustomerSourceDetailsAsync(
        LeadsDashboardFilterDto filter,
        int? sourceId)
    {
        var leadOriginOpportunityIds = await _db.LeadsCRMs
            .AsNoTracking()
            .Where(l => l.ConvertedOpportunityId.HasValue)
            .Select(l => l.ConvertedOpportunityId!.Value)
            .Distinct()
            .ToListAsync();

        var query = _db.SalesOpportunities
            .AsNoTracking()
            .Where(o => o.IsActive
                        && !leadOriginOpportunityIds.Contains(o.OpportunityId));

        if (filter.EmployeeId.HasValue)
            query = query.Where(o => o.EmployeeId == filter.EmployeeId.Value);

        if (filter.DateFrom.HasValue)
            query = query.Where(o => o.CreatedAt >= filter.DateFrom.Value.Date);

        if (filter.DateTo.HasValue)
            query = query.Where(o => o.CreatedAt < filter.DateTo.Value.Date.AddDays(1));

        query = sourceId.HasValue
            ? query.Where(o => o.SourceId == sourceId.Value)
            : query.Where(o => !o.SourceId.HasValue);

        var sourceName = sourceId.HasValue
            ? await _db.ContactSources.AsNoTracking()
                .Where(s => s.SourceId == sourceId.Value)
                .Select(s => string.IsNullOrWhiteSpace(s.SourceNameAr)
                    ? s.SourceName
                    : s.SourceNameAr!)
                .FirstOrDefaultAsync() ?? "مصدر غير معروف"
            : "مصدر غير محدد";

        return await query
            .OrderByDescending(o => o.CreatedAt)
            .Take(500)
            .Select(o => new ShowroomVisitDetailDto
            {
                InteractionId = 0,
                LeadId = null,
                OpportunityId = o.OpportunityId,
                PartyId = o.PartyId,
                CustomerName = o.Party.PartyName,
                Phone = o.Party.Phone ?? "",
                VisitDate = o.CreatedAt,
                EmployeeName = o.Employee != null ? o.Employee.FullName : null,
                OpportunityStage = o.Stage.StageNameAr ?? o.Stage.StageName,
                OriginKey = sourceId.HasValue
                    ? $"source:{sourceId.Value}"
                    : "source:unknown",
                OriginName = sourceName
            })
            .ToListAsync();
    }

        // ═══════════════════════════════════════════════════════════
    //  إنشاء Lead يدوياً
    // ═══════════════════════════════════════════════════════════
    public async Task<(bool Success, string Message, int LeadId)> CreateLeadAsync(LeadsCrmCreateDto dto, string userName)
    {
        if (string.IsNullOrWhiteSpace(dto.FullName))
            return (false, "اسم العميل مطلوب", 0);

        if (string.IsNullOrWhiteSpace(dto.Phone))
            return (false, "رقم الهاتف مطلوب", 0);

        if (!dto.SourceId.HasValue)
            return (false, "مصدر الـ Lead مطلوب", 0);

        if (!dto.NextFollowUpDate.HasValue)
            return (false, "تاريخ المتابعة مطلوب", 0);

        var phone = dto.Phone.Trim();

        // التحقق من التكرار بناءً على رقم الهاتف
        var existingPhone = await _db.LeadsCRMs
            .AnyAsync(l => l.Phone == phone);
        if (existingPhone)
            return (false, "رقم الهاتف موجود بالفعل في الـ Leads", 0);

        var source = await _db.ContactSources.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SourceId == dto.SourceId.Value && s.IsActive);
        if (source == null)
            return (false, "المصدر المختار غير موجود أو غير مفعل", 0);

        AdType? adType = null;
        if (dto.AdTypeId.HasValue)
        {
            adType = await _db.AdTypes.AsNoTracking()
                .FirstOrDefaultAsync(a => a.AdTypeId == dto.AdTypeId.Value && a.IsActive);

            if (adType == null)
                return (false, "الحملة المختارة غير موجودة أو غير مفعلة", 0);
        }

        var now = DateTime.Now;
        var followUpDate = dto.NextFollowUpDate.Value;

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var lead = new LeadsCrm
            {
                FullName = dto.FullName.Trim(),
                Phone = phone,
                Phone2 = dto.Phone2?.Trim(),
                Email = dto.Email?.Trim(),
                City = dto.City?.Trim(),
                Area = dto.Area?.Trim(),
                Address = dto.Address?.Trim(),
                FormId = source.SourceId.ToString(),
                FormName = "manual",
                Platform = string.IsNullOrWhiteSpace(source.SourceNameAr)
                    ? source.SourceName?.Trim()
                    : source.SourceNameAr!.Trim(),
                CampaignId = adType?.AdTypeId.ToString(),
                CampaignName = string.IsNullOrWhiteSpace(adType?.AdTypeNameAr)
                    ? adType?.AdTypeName?.Trim()
                    : adType!.AdTypeNameAr!.Trim(),
                ProjectType = dto.ProjectType?.Trim(),
                ProjectStage = dto.ProjectStage?.Trim(),
                Budget = dto.Budget?.Trim(),
                DecisionMaker = dto.DecisionMaker?.Trim(),
                NextAction = dto.NextAction?.Trim(),
                BestTimeToReach = dto.BestTimeToReach?.Trim(),
                AssignedEmployeeId = dto.AssignedEmployeeId,
                Notes = dto.Notes?.Trim(),
                LeadDate = now,
                LeadStatus = "جديد",
                IsDuplicate = false,
                IsConverted = false,
                CreatedAt = now,
                CreatedBy = userName
            };

            _db.LeadsCRMs.Add(lead);
            await _db.SaveChangesAsync();

            var initialFollowUp = new LeadInteraction
            {
                LeadId = lead.LeadId,
                EmployeeId = dto.AssignedEmployeeId,
                InteractionType = LeadInteractionTypes.FollowUp,
                InteractionDate = now,
                Summary = "تحديد متابعة أولية عند إنشاء الـ Lead",
                Notes = "تم إنشاء متابعة أولية تلقائياً من شاشة إضافة الـ Leads.",
                OldLeadStatus = lead.LeadStatus,
                NewLeadStatus = null,
                NextFollowUpDate = followUpDate,
                IsCompleted = false,
                IsSystemGenerated = true,
                CreatedBy = userName,
                CreatedAt = now
            };

            _db.LeadInteractions.Add(initialFollowUp);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            await _audit.LogAsync("LeadsCRM", "Create",
                lead.LeadId.ToString(), null, dto, userName);

            // إرسال إشعار للموظف المسؤول
            if (lead.AssignedEmployeeId.HasValue)
            {
                try
                {
                    var emp = await _db.Employees.FindAsync(lead.AssignedEmployeeId.Value);
                    if (emp != null)
                    {
                        var user = await _db.Users.AsNoTracking()
                            .FirstOrDefaultAsync(u => u.EmployeeId == emp.EmployeeId);

                        if (user != null)
                        {
                            await _notify.AddAsync(
                                title: "📌 تم إسناد Lead جديد لك",
                                message: $"تم إسناد Lead لك: {lead.FullName} - {lead.Phone}. موعد المتابعة: {followUpDate:yyyy/MM/dd}.",
                                recipientUser: user.Username,
                                createdBy: userName,
                                formName: "crm/leads/my",
                                relatedTable: "LeadsCRM",
                                relatedId: lead.LeadId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to send lead assignment notification. LeadId={LeadId}",
                        lead.LeadId);
                }
            }

            return (true, "تم إنشاء الـ Lead بنجاح", lead.LeadId);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
    

    // ═══════════════════════════════════════════════════════════
    //  حذف Lead
    // ═══════════════════════════════════════════════════════════
    public async Task<(bool Success, string Message)> DeleteLeadAsync(int leadId, string userName)
    {
        var lead = await _db.LeadsCRMs.FindAsync(leadId);
        if (lead == null) return (false, "Lead غير موجود");

        if (lead.IsConverted)
            return (false, "لا يمكن حذف Lead تم تحويله لعميل");

        _db.LeadsCRMs.Remove(lead);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("LeadsCRM", "Delete",
            leadId.ToString(), null, lead, userName);

        return (true, "تم الحذف بنجاح");
    }

    // ═══════════════════════════════════════════════════════════
    //  الموظفين
    // ═══════════════════════════════════════════════════════════
    public async Task<List<Employee>> GetEmployeesAsync()
    {
        return await _db.Employees
            .Where(e => e.Status == "نشط" || e.Status == "Active")
            .OrderBy(e => e.FullName)
            .ToListAsync();
    }
    public async Task<List<Employee>> GetAssignableEmployeesAsync()
{
    var allowedDepartments = new[]
    {
        "المبيعات",
        "إدارة العلاقات العامة"
    };

    return await _db.Employees
        .AsNoTracking()
        .Where(e =>
            (e.Status == "نشط" || e.Status == "Active") &&
            e.Department != null &&
            allowedDepartments.Contains(e.Department))
        .OrderBy(e => e.FullName)
        .Select(e => new Employee
        {
            EmployeeId = e.EmployeeId,
            FullName = e.FullName,
            Department = e.Department,
            Status = e.Status
        })
        .ToListAsync();
}

    // ═══════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════

    private static IQueryable<LeadsCrm> ApplyDashboardFilter(IQueryable<LeadsCrm> q, LeadsDashboardFilterDto filter)
    {
        if (filter.DateFrom.HasValue)
            q = q.Where(l => l.CreatedAt >= filter.DateFrom.Value);
        if (filter.DateTo.HasValue)
            q = q.Where(l => l.CreatedAt <= filter.DateTo.Value.AddDays(1));
        if (!string.IsNullOrWhiteSpace(filter.Platform))
        {
            var platformAliases = GetPlatformAliases(filter.Platform);
            q = platformAliases.Count == 0
                ? q.Where(l => l.Platform == filter.Platform)
                : q.Where(l => l.Platform != null && platformAliases.Contains(l.Platform));
        }
        if (filter.EmployeeId.HasValue)
            q = q.Where(l => l.AssignedEmployeeId == filter.EmployeeId);
        if (!string.IsNullOrWhiteSpace(filter.City))
            q = q.Where(l => l.City == filter.City);
        if (!string.IsNullOrWhiteSpace(filter.ProjectType))
            q = q.Where(l => l.ProjectType == filter.ProjectType);
        if (!string.IsNullOrWhiteSpace(filter.ProjectStage))
            q = q.Where(l => l.ProjectStage == filter.ProjectStage);
        if (!string.IsNullOrWhiteSpace(filter.CampaignName))
            q = q.Where(l => l.CampaignName == filter.CampaignName);
        return q;
    }

    private static string BuildGuidanceFromLead(LeadsCrm lead)
    {
        var parts = new List<string>();
        parts.Add($"مصدر: إعلان Meta - التاب: {lead.SheetTabName ?? "غير محدد"}");
        if (!string.IsNullOrEmpty(lead.BestTimeToReach))
            parts.Add($"أفضل وقت للتواصل: {lead.BestTimeToReach}");
        if (!string.IsNullOrEmpty(lead.DecisionMaker))
            parts.Add($"صاحب القرار: {lead.DecisionMaker}");
        if (!string.IsNullOrEmpty(lead.NextAction))
            parts.Add($"الاحتياج: {lead.NextAction}");
        return string.Join(" | ", parts);
    }

    private static string? TryMapOpportunityOpenOutcomeLabel(SalesStage? stage)
    {
        if (stage == null)
            return null;

        var raw = $"{stage.StageNameAr} {stage.StageName}".Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var normalized = raw.ToLowerInvariant()
            .Replace("أ", "ا")
            .Replace("إ", "ا")
            .Replace("آ", "ا")
            .Replace("ى", "ي")
            .Replace("ة", "ه");

        if (normalized.Contains("عالي الاهتمام") || normalized.Contains("عالى الاهتمام") ||
            (normalized.Contains("high") && normalized.Contains("interest")) ||
            normalized.Contains("hot") || normalized.Contains("negotiat") || normalized.Contains("تفاوض") ||
            normalized.Contains("عرض سعر") || normalized.Contains("quotation") || normalized.Contains("proposal"))
            return "عالي الاهتمام";

        if (normalized.Contains("مهتم") || normalized.Contains("interested") || normalized.Contains("qualified") || normalized.Contains("مؤهل"))
            return "مهتم";

        if (normalized.Contains("محتمل") || normalized.Contains("potential") || normalized.Contains("new") || normalized.Contains("lead"))
            return "محتمل";

        return null;
    }

    private static string ResolveFallbackOpportunityOpenOutcomeLabel(int index, int total)
    {
        if (total <= 1)
            return "مهتم";

        if (index <= 0)
            return "محتمل";

        if (index >= total - 1)
            return "عالي الاهتمام";

        return "مهتم";
    }

    private static bool IsExitStageId(int stageId) => stageId == 4 || stageId == 5;

    private static LeadJourneyTreeNodeDto CreateLeadJourneyNode(
        string id,
        string title,
        int count,
        string bgColor,
        string borderColor,
        string borderHoverColor,
        string accentColor,
        string? subLabel = null,
        List<LeadJourneyTreeNodeDto>? children = null)
    {
        return new LeadJourneyTreeNodeDto
        {
            Id = id,
            Title = title,
            CountLabel = count.ToString("N0"),
            SubLabel = subLabel,
            BgColor = bgColor,
            BorderColor = borderColor,
            BorderHoverColor = borderHoverColor,
            AccentColor = accentColor,
            Children = children ?? new List<LeadJourneyTreeNodeDto>()
        };
    }

    private static string BuildLeadJourneyLeadSub(int count, int total)
    {
        if (total <= 0) return "0% من الإجمالي";
        var pct = Math.Round((decimal)count / total * 100m, 1);
        return $"{pct:F1}% من الإجمالي";
    }

    private static string BuildLeadJourneyStageSub(int count, int totalConverted, decimal totalValue)
    {
        var pctText = totalConverted > 0
            ? $"{Math.Round((decimal)count / totalConverted * 100m, 1):F1}% من المحولين"
            : "0% من المحولين";

        if (totalValue <= 0)
            return pctText;

        return $"{pctText} • {FormatCompactMoney(totalValue)}";
    }

    private static string FormatCompactMoney(decimal value)
    {
        if (value >= 1_000_000m) return $"{(value / 1_000_000m):0.#}M ج.م";
        if (value >= 1_000m) return $"{(value / 1_000m):0.#}K ج.م";
        return $"{value:N0} ج.م";
    }

    private static string MapBudgetCategory(string? budget)
    {
        if (string.IsNullOrWhiteSpace(budget) ||
            string.Equals(budget.Trim(), "null", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(budget.Trim(), "n/a", StringComparison.OrdinalIgnoreCase) ||
            budget.Trim() == "-")
            return "بدون ميزانية";

        var raw = budget.Trim();
        var normalized = raw.ToLowerInvariant()
            .Replace("أ", "ا")
            .Replace("إ", "ا")
            .Replace("آ", "ا")
            .Replace("جنيه", "جنيه")
            .Replace("  ", " ");

        if (normalized.Contains("above egp 1 million") || normalized.Contains("اكثر من مليون"))
            return "أكثر من مليون جنيه";

        if (normalized.Contains("below 500k") || normalized.Contains("اقل من 500") || normalized.Contains("أقل من 500"))
            return "أقل من 500 ألف جنيه";

        if (normalized.Contains("500") && normalized.Contains("1 مليون") && !normalized.Contains("700"))
            return "من 500 ألف لـ 1 مليون جنيه";

        if (normalized.Contains("500") && normalized.Contains("1 million") && !normalized.Contains("700"))
            return "من 500 ألف لـ 1 مليون جنيه";

        return raw;
    }

    private static string BuildCampaignDisplayName(string? adSetName, string? adName, string? campaignName)
    {
        var adSet = string.IsNullOrWhiteSpace(adSetName) ? null : adSetName.Trim();
        var ad = string.IsNullOrWhiteSpace(adName) ? null : adName.Trim();
        var campaign = string.IsNullOrWhiteSpace(campaignName) ? null : campaignName.Trim();

        if (!string.IsNullOrWhiteSpace(adSet) && !string.IsNullOrWhiteSpace(ad))
            return $"{adSet} / {ad}";
        if (!string.IsNullOrWhiteSpace(ad))
            return ad;
        if (!string.IsNullOrWhiteSpace(adSet))
            return adSet;
        return campaign ?? string.Empty;
    }

    private static List<string> GetPlatformAliases(string? platformFilter)
    {
        if (string.IsNullOrWhiteSpace(platformFilter))
            return new List<string>();

        var normalized = NormalizeLooseArabic(platformFilter);
        return normalized switch
        {
            "fb" or "facebook" or "فيسبوك" => new List<string> { "fb", "facebook", "Facebook", "فيسبوك" },
            "ig" or "instagram" or "انستجرام" or "انستغرام" => new List<string> { "ig", "instagram", "Instagram", "انستجرام", "إنستجرام", "انستغرام", "إنستغرام" },
            _ => new List<string> { platformFilter }
        };
    }

    private static string? NormalizeLeadPlatformLabel(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var normalized = NormalizeLooseArabic(raw);

        if (normalized is "fb" or "facebook" or "فيسبوك")
            return "Facebook";
        if (normalized is "ig" or "instagram" or "انستجرام" or "انستغرام")
            return "Instagram";
        if (normalized.Contains("tiktok") || normalized.Contains("تيك توك") || normalized.Contains("تيكتوك"))
            return "TikTok";
        if (normalized.Contains("google") || normalized.Contains("جوجل"))
            return "Google";
        if (normalized.Contains("website") || normalized.Contains("site") || normalized.Contains("ويب") || normalized.Contains("موقع"))
            return "Website";
        if (normalized.Contains("whatsapp") || normalized.Contains("واتساب"))
            return "WhatsApp";

        if (IsExcludedMarketingSourceName(raw))
            return null;

        return raw.Trim();
    }

    private static bool IsExcludedMarketingSourceName(string? sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
            return false;

        var normalized = NormalizeLooseArabic(sourceName);

        return normalized.Contains("عميل قديم")
            || normalized.Contains("العميل القديم")
            || normalized.Contains("توصية عميل")
            || normalized.Contains("توصيه عميل")
            || normalized.Contains("توصية")
            || normalized.Contains("توصيه")
            || normalized.Contains("طرف المهندس حسن")
            || normalized.Contains("المهندس حسن")
            || normalized.Contains("مهندس حسن");
    }

    private static string NormalizeLooseArabic(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var s = input.Trim().ToLowerInvariant()
            .Replace("أ", "ا")
            .Replace("إ", "ا")
            .Replace("آ", "ا")
            .Replace("ى", "ي")
            .Replace("ة", "ه")
            .Replace("ـ", string.Empty)
            .Replace("  ", " ");

        return s;
    }

    private sealed class DirectSourceRow
    {
        public int? SourceId { get; set; }
    }

    private static decimal CalcChange(decimal current, decimal previous)
    {
        if (previous == 0) return current > 0 ? 100m : 0m;
        return Math.Round((current - previous) / previous * 100, 1);
    }

    private static double? CalcChangeDouble(double current, double previous)
    {
        if (previous == 0) return current > 0 ? 100.0 : 0.0;
        return Math.Round((current - previous) / previous * 100, 1);
    }
}
