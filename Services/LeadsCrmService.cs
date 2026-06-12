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

    public LeadsCrmService(
    db24804Context db,
    IAuditService audit,
    NotificationService notify,
    ILogger<LeadsCrmService> logger)
{
    _db = db;
    _audit = audit;
    _notify = notify;
    _logger = logger;
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
        l.LeadStatus != "محوّل" &&
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

        // الخطوة 2: جيب أسماء الموظفين لوحدها (بدل N+1)
        var employeeIds = leads
            .Where(l => l.AssignedEmployeeId.HasValue)
            .Select(l => l.AssignedEmployeeId!.Value)
            .Distinct()
            .ToList();

        Dictionary<int, string> employeeNames = new();
        if (employeeIds.Count > 0)
        {
            employeeNames = await _db.Employees.AsNoTracking()
                .Where(e => employeeIds.Contains(e.EmployeeId))
                .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName);
        }

        // الخطوة 3: ادمجهم في الذاكرة
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
            IsDuplicate = l.IsDuplicate,
            AssignedEmployeeId = l.AssignedEmployeeId,
            AssignedEmployeeName = l.AssignedEmployeeId.HasValue
                && employeeNames.TryGetValue(l.AssignedEmployeeId.Value, out var name)
                ? name : null,
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
            Feedback = lead.Feedback,
            RejectedReason = lead.RejectedReason,
            LastContactDate = lead.LastContactDate,
            QualifiedDate = lead.QualifiedDate,
            ExtraData = lead.ExtraData,
            CreatedAt = lead.CreatedAt,
            CreatedBy = lead.CreatedBy
        };
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

    var interaction = new LeadInteraction
    {
        LeadId = dto.LeadId,
        EmployeeId = dto.EmployeeId ?? lead.AssignedEmployeeId,
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
        else if (newStatus == "محوّل")
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

    // 3) تحديث الموظف المسؤول
    var assignmentChanged = oldAssignedEmployeeId != dto.AssignedEmployeeId;

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

    await _db.SaveChangesAsync();

    await _audit.LogAsync("LeadsCRM", "Update",
        dto.LeadId.ToString(), null, dto, userName);

    // 7) إشعار الموظف الجديد لو تم الإسناد
    if (assignmentChanged && dto.AssignedEmployeeId.HasValue)
    {
        await NotifyLeadAssignedAsync(lead, dto.AssignedEmployeeId.Value, userName);
    }

    return (true, "تم التحديث بنجاح");
}
private async Task NotifyLeadAssignedAsync(LeadsCrm lead, int employeeId, string assignedBy)
{
    try
    {
        // نجيب اليوزر المرتبط بالموظف
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
        // لا نوقف التحديث لو الإشعار فشل
        _logger.LogWarning(
            ex,
            "Failed to send lead assignment notification. LeadId={LeadId}, EmployeeId={EmployeeId}",
            lead.LeadId,
            employeeId);
    }
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

        if (string.IsNullOrWhiteSpace(lead.FullName) || string.IsNullOrWhiteSpace(lead.Phone))
            return (false, "بيانات الـ Lead ناقصة (الاسم أو الموبايل)", 0, 0);

        var phoneExists = await _db.Parties.AnyAsync(p => p.Phone == lead.Phone);
        if (phoneExists)
            return (false, "رقم الهاتف موجود بالفعل في العملاء", 0, 0);

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var now = DateTime.Now;
            var oldLeadStatus = lead.LeadStatus;
            

var initialStageId = await _db.SalesStages
    .AsNoTracking()
    .Where(s => s.IsActive &&
           (s.StageName == "Potential" || s.StageNameAr == "مهتم"))
    .OrderBy(s => s.StageOrder)
    .Select(s => s.StageId)
    .FirstOrDefaultAsync();

if (initialStageId == 0)
{
    initialStageId = await _db.SalesStages
        .AsNoTracking()
        .Where(s => s.IsActive)
        .OrderBy(s => s.StageOrder)
        .Select(s => s.StageId)
        .FirstOrDefaultAsync();
}

if (initialStageId == 0)
    return (false, "لا توجد مراحل بيع مفعّلة.", 0, 0);

            // 1. إنشاء العميل
            var party = new Party
            {
                PartyName = lead.FullName.Trim(),
                Phone = lead.Phone.Trim(),
                Address = lead.Address?.Trim(),
                PartyType = 1,
                IsActive = true,
                ReferralSourceId = dto.SourceId ?? 2,
                CreatedBy = userName,
                CreatedAt = now
            };
            _db.Parties.Add(party);
            await _db.SaveChangesAsync();

            // 2. إنشاء فرصة بيع
            var opportunity = new SalesOpportunity
            {
                PartyId = party.PartyId,
                EmployeeId = dto.EmployeeId,
                SourceId = dto.SourceId,
                AdTypeId = dto.AdTypeId,
                //StageId = 1,
                StageId = initialStageId,
                CategoryId = dto.CategoryId,
                InterestedProduct = lead.ProjectType,
                FirstContactDate = lead.LeadDate ?? now,
                NextFollowUpDate = now.AddDays(1),
                Notes = dto.Notes ?? lead.Notes,
                Guidance = BuildGuidanceFromLead(lead),
                IsActive = true,
                CreatedBy = userName,
                CreatedAt = now
            };
            _db.SalesOpportunities.Add(opportunity);
            await _db.SaveChangesAsync();

            // 3. إنشاء سجل تواصل
            var interaction = new CustomerInteraction
            {
                OpportunityId = opportunity.OpportunityId,
                PartyId = party.PartyId,
                EmployeeId = dto.EmployeeId,
                SourceId = dto.SourceId,
                InteractionDate = now,
                Summary = $"تحويل Lead من إعلان Meta - كامبين: {lead.CampaignName ?? "غير محدد"}",
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
                    TaskDescription = $"متابعة عميل جديد من Meta: {lead.FullName}",
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
            lead.LeadStatus = "محوّل";
            lead.LastContactDate = now;
            _db.LeadInteractions.Add(new LeadInteraction
{
    LeadId = lead.LeadId,
    EmployeeId = dto.EmployeeId ?? lead.AssignedEmployeeId,
    InteractionType = LeadInteractionTypes.Converted,
    InteractionDate = now,
    Summary = $"تم تحويل الـ Lead إلى فرصة بيع #{opportunity.OpportunityId}",
    Notes = dto.Notes ?? lead.Notes,
    OldLeadStatus = oldLeadStatus,
    NewLeadStatus = "محوّل",
    IsSystemGenerated = true,
    CreatedBy = userName,
    CreatedAt = now
});

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            await _audit.LogAsync("LeadsCRM", "Convert",
                lead.LeadId.ToString(), null, dto, userName);

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
    public async Task<LeadsCrmStatsDto> GetStatsAsync()
    {
        var today = DateTime.Today;
        var weekStart = today.AddDays(-(int)today.DayOfWeek);
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var lateCutoff = DateTime.Now.AddHours(-1);

        // ⭐ استعلام واحد: نحمل كل اللي محتاجينه في الذاكرة
        var leadsData = await _db.LeadsCRMs.AsNoTracking()
    .Select(l => new
    {
        l.LeadStatus,
        l.IsDuplicate,
        l.IsConverted,
        l.LastContactDate,
        l.CreatedAt
    })
    .ToListAsync();

        // كمان استعلام واحد للحملات والمنصات
        var campaignData = await _db.LeadsCRMs.AsNoTracking()
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

        var platformData = await _db.LeadsCRMs.AsNoTracking()
            .Where(l => l.Platform != null)
            .GroupBy(l => l.Platform)
            .Select(g => new LeadsByPlatformDto
            {
                Platform = g.Key!,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ToListAsync();

        // كل الحسابات في الذاكرة — صفر استعلامات
        return new LeadsCrmStatsDto
        {
            TotalLeads = leadsData.Count,
            NewLeads = leadsData.Count(l => l.LeadStatus == "جديد"),
            AssignedLeads = leadsData.Count(l => l.LeadStatus == "تم الإسناد"),
            ContactedLeads = leadsData.Count(l => l.LeadStatus == "تم التواصل"),
            QualifiedLeads = leadsData.Count(l => l.LeadStatus == "مؤهل"),
            ConvertedLeads = leadsData.Count(l => l.LeadStatus == "محوّل"),
            RejectedLeads = leadsData.Count(l => l.LeadStatus == "مرفوض"),
            LateFollowUpLeads = leadsData.Count(l =>
    !l.IsConverted &&
    l.LeadStatus != "محوّل" &&
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
    public async Task<LeadsDashboardDataDto> GetDashboardDataAsync(LeadsDashboardFilterDto filter)
    {
        var result = new LeadsDashboardDataDto();

        try
        {
            // ─── QUERY 1: تحميل كل Leads الفترة الحالية ───
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
                l.FullName,
                l.AssignedEmployeeId,
                l.IsDuplicate,
                l.IsConverted,
                l.ConvertedDate,
                l.CreatedAt
            }).ToListAsync();

            // ─── QUERY 2 + 3: بالتوازي — الفترة السابقة + أسماء الموظفين ───
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

            // ═══════════════════════════════════════════
            //  كل الحسابات بعد كده في الذاكرة — صفر استعلامات
            // ═══════════════════════════════════════════

            var totalLeads = leads.Count;
            var convertedCount = leads.Count(l => l.LeadStatus == "محوّل");
            var duplicateCount = leads.Count(l => l.IsDuplicate);
            var rejectedCount = leads.Count(l => l.LeadStatus == "مرفوض");

            // متوسط أيام التحويل
            var convertedWithDate = leads
                .Where(l => l.LeadStatus == "محوّل" && l.ConvertedDate.HasValue && l.CreatedAt != default)
                .ToList();
            double avgDays = convertedWithDate.Count > 0
                ? convertedWithDate.Average(l => (l.ConvertedDate!.Value - l.CreatedAt).TotalDays)
                : 0;

            // ─── KPIs الفترة السابقة ───
            var prevTotal = prevLeads.Count;
            var prevConverted = prevLeads.Count(l => l.LeadStatus == "محوّل");
            var prevDuplicate = prevLeads.Count(l => l.IsDuplicate);
            var prevRejected = prevLeads.Count(l => l.LeadStatus == "مرفوض");

            var prevConvertedWithDate = prevLeads
                .Where(l => l.LeadStatus == "محوّل" && l.ConvertedDate.HasValue && l.CreatedAt != default)
                .ToList();
            double prevAvgDays = prevConvertedWithDate.Count > 0
                ? prevConvertedWithDate.Average(l => (l.ConvertedDate!.Value - l.CreatedAt).TotalDays)
                : 0;

            // حساب التغيير
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
                DuplicateRate = dupRate,
                RejectionRate = rejRate,
                TotalLeadsChange = CalcChange(totalLeads, prevTotal),
                ConversionRateChange = CalcChange(convRate, prevConvRate),
                AvgConversionDaysChange = CalcChangeDouble(avgDays, prevAvgDays),
                DuplicateRateChange = CalcChange(dupRate, prevDupRate),
                RejectionRateChange = CalcChange(rejRate, prevRejRate)
            };

            // ─── Status Distribution (Donut) ───
            var statusColors = new Dictionary<string, string>
            {
                { "جديد", "#3b82f6" },
                { "تم الإسناد", "#0ea5e9" },
                { "تم التواصل", "#f59e0b" },
                { "مؤهل", "#10b981" },
                { "محوّل", "#8b5cf6" },
                { "مرفوض", "#ef4444" }
            };

            result.StatusDistribution = leads
                .GroupBy(l => l.LeadStatus)
                .Select(g => new ChartItemDto
                {
                    Label = g.Key ?? "غير محدد",
                    Value = g.Count(),
                    Color = statusColors.GetValueOrDefault(g.Key ?? "", "#6b7280")
                }).ToList();

            // ─── Platform Comparison (Bar) ───
            var platformLabels = new Dictionary<string, string>
            {
                { "fb", "Facebook" },
                { "ig", "Instagram" }
            };

            result.PlatformData = leads
                .Where(l => l.Platform != null)
                .GroupBy(l => l.Platform)
                .Select(g => new ChartItemDto
                {
                    Label = platformLabels.GetValueOrDefault(g.Key!, g.Key!),
                    Value = g.Count()
                }).ToList();

            // ─── Daily Trend (Area) ───
            var dateFrom = filter.DateFrom ?? DateTime.Today.AddDays(-30);
            var dateTo = filter.DateTo ?? DateTime.Today;

            var dailyGroups = leads
                .Where(l => l.CreatedAt.Date >= dateFrom.Date && l.CreatedAt.Date <= dateTo.Date)
                .GroupBy(l => l.CreatedAt.Date)
                .ToDictionary(g => g.Key, g => g.ToList());

            var allDates = Enumerable.Range(0, (dateTo.Date - dateFrom.Date).Days + 1)
                .Select(d => dateFrom.Date.AddDays(d));

            result.DailyTrend = allDates.Select(d =>
            {
                var dayLeads = dailyGroups.GetValueOrDefault(d);
                return new DailyTrendItemDto
                {
                    Date = d,
                    Leads = dayLeads?.Count ?? 0,
                    Converted = dayLeads?.Count(l => l.LeadStatus == "محوّل") ?? 0
                };
            }).ToList();

            // ─── Budget Distribution (Bar) ───
            result.BudgetDistribution = leads
                .Where(l => l.Budget != null)
                .GroupBy(l => l.Budget)
                .Select(g => new ChartItemDto { Label = g.Key!, Value = g.Count() })
                .OrderByDescending(x => x.Value)
                .Take(8)
                .ToList();

            // ─── Top Cities (Horizontal Bar) ───
            result.TopCities = leads
                .Where(l => l.City != null)
                .GroupBy(l => l.City)
                .Select(g => new ChartItemDto { Label = g.Key!, Value = g.Count() })
                .OrderByDescending(x => x.Value)
                .Take(8)
                .ToList();

            // ─── Employee Performance (Stacked Bar) ───
            result.EmployeePerformance = leads
                .Where(l => l.AssignedEmployeeId.HasValue)
                .GroupBy(l => l.AssignedEmployeeId!.Value)
                .Select(g => new DashboardEmployeeDto
                {
                    Name = empNames.GetValueOrDefault(g.Key, $"موظف {g.Key}"),
                    NewCount = g.Count(l => l.LeadStatus == "جديد"),
                    ContactedCount = g.Count(l => l.LeadStatus == "تم التواصل"),
                    QualifiedCount = g.Count(l => l.LeadStatus == "مؤهل"),
                    ConvertedCount = g.Count(l => l.LeadStatus == "محوّل"),
                    RejectedCount = g.Count(l => l.LeadStatus == "مرفوض")
                })
                .Select(e => { e.Total = e.NewCount + e.ContactedCount + e.QualifiedCount + e.ConvertedCount + e.RejectedCount; return e; })
                .OrderByDescending(e => e.Total)
                .Take(10)
                .ToList();

            // ─── Funnel Data ───
            var newCount = leads.Count(l => l.LeadStatus == "جديد");
            var contactedCount = leads.Count(l => l.LeadStatus == "تم التواصل");
            var qualifiedCount = leads.Count(l => l.LeadStatus == "مؤهل");

            result.FunnelData = new List<FunnelItemDto>
            {
                new() { Stage = "جديد", Count = newCount, Percentage = totalLeads > 0 ? Math.Round((decimal)newCount / totalLeads * 100, 1) : 0, Color = "#3b82f6" },
                new() { Stage = "تم التواصل", Count = contactedCount, Percentage = totalLeads > 0 ? Math.Round((decimal)contactedCount / totalLeads * 100, 1) : 0, Color = "#f59e0b" },
                new() { Stage = "مؤهل", Count = qualifiedCount, Percentage = totalLeads > 0 ? Math.Round((decimal)qualifiedCount / totalLeads * 100, 1) : 0, Color = "#10b981" },
                new() { Stage = "محوّل", Count = convertedCount, Percentage = totalLeads > 0 ? Math.Round((decimal)convertedCount / totalLeads * 100, 1) : 0, Color = "#8b5cf6" },
                new() { Stage = "مرفوض", Count = rejectedCount, Percentage = totalLeads > 0 ? Math.Round((decimal)rejectedCount / totalLeads * 100, 1) : 0, Color = "#ef4444" }
            };

            // ─── Top Campaigns ───
            result.TopCampaigns = leads
                .Where(l => l.CampaignName != null)
                .GroupBy(l => new { l.CampaignName, l.Platform })
                .Select(g => new CampaignPerformanceDto
                {
                    CampaignName = g.Key.CampaignName!,
                    Platform = g.Key.Platform ?? "",
                    TotalLeads = g.Count(),
                    ConvertedLeads = g.Count(l => l.LeadStatus == "محوّل"),
                    ConversionRate = g.Count() > 0
                        ? Math.Round((decimal)g.Count(l => l.LeadStatus == "محوّل") / g.Count() * 100, 1) : 0
                })
                .OrderByDescending(x => x.TotalLeads)
                .Take(10)
                .ToList();

            // ─── Project Type Summary ───
            result.ProjectSummary = leads
                .Where(l => l.ProjectType != null)
                .GroupBy(l => l.ProjectType)
                .Select(g => new ProjectTypeSummaryDto
                {
                    ProjectType = g.Key!,
                    TotalLeads = g.Count(),
                    ConvertedLeads = g.Count(l => l.LeadStatus == "محوّل"),
                    ConversionRate = g.Count() > 0
                        ? Math.Round((decimal)g.Count(l => l.LeadStatus == "محوّل") / g.Count() * 100, 1) : 0
                })
                .OrderByDescending(x => x.TotalLeads)
                .Take(10)
                .ToList();

            // ─── Recent Converted ───
            result.RecentConverted = leads
                .Where(l => l.LeadStatus == "محوّل" && l.ConvertedDate.HasValue)
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

            // ─── Filter dropdown options ───
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
            q = q.Where(l => l.Platform == filter.Platform);
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
