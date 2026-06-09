using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class LeadsCrmService : ILeadsCrmService
{
    private readonly db24804Context _db;
    private readonly IAuditService _audit;

    public LeadsCrmService(db24804Context db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    // ═══ عرض كل الـ Leads مع فلترة وصفحات ═══
public async Task<PagedResult<LeadsCrmListDto>> GetLeadsAsync(LeadsCrmFilterDto filter)
{
    var query = _db.LeadsCRMs.AsQueryable();

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

    var totalCount = await query.CountAsync();

    // الخطوة 1: جيب الـ Leads بس (من غير sub-query)
    var leads = await query
        .OrderByDescending(l => l.CreatedAt)
        .Skip((filter.PageNumber - 1) * filter.PageSize)
        .Take(filter.PageSize)
        .ToListAsync();

    // الخطوة 2: جيب أسماء الموظفين لوحدها
    var employeeIds = leads
        .Where(l => l.AssignedEmployeeId.HasValue)
        .Select(l => l.AssignedEmployeeId!.Value)
        .Distinct()
        .ToList();

    Dictionary<int, string> employeeNames = new();
    if (employeeIds.Count > 0)
    {
        employeeNames = await _db.Employees
            .Where(e => employeeIds.Contains(e.EmployeeId))
            .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName);
    }

    // الخطوة 3: ادمجهم
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
        CreatedAt = l.CreatedAt
    }).ToList();

    return new PagedResult<LeadsCrmListDto>
    {
        Items = items,
        TotalCount = totalCount,
        PageNumber = filter.PageNumber,
        PageSize = filter.PageSize
    };
}

    // ═══ تفاصيل Lead واحد ═══
    public async Task<LeadsCrmDetailDto?> GetLeadByIdAsync(int leadId)
    {
        var lead = await _db.LeadsCRMs.FindAsync(leadId);
        if (lead == null) return null;

        string? assignedName = null;
        if (lead.AssignedEmployeeId.HasValue)
        {
            assignedName = await _db.Employees
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
            Feedback = lead.Feedback,
            AssignedEmployeeId = lead.AssignedEmployeeId,
            AssignedEmployeeName = assignedName,
            LastContactDate = lead.LastContactDate,
            QualifiedDate = lead.QualifiedDate,
            RejectedReason = lead.RejectedReason,
            ExtraData = lead.ExtraData,
            CreatedAt = lead.CreatedAt,
            CreatedBy = lead.CreatedBy
        };
    }

    // ═══ تحديث حالة Lead أو فيدباك ═══
    public async Task<(bool Success, string Message)> UpdateLeadAsync(
        LeadsCrmUpdateDto dto, string userName)
    {
        var lead = await _db.LeadsCRMs.FindAsync(dto.LeadId);
        if (lead == null) return (false, "Lead غير موجود");

        var oldStatus = lead.LeadStatus;

        if (!string.IsNullOrWhiteSpace(dto.LeadStatus))
        {
            lead.LeadStatus = dto.LeadStatus;

            if (dto.LeadStatus == "مؤهل" && oldStatus != "مؤهل")        // ← عربي
                lead.QualifiedDate = DateTime.Now;

            if (dto.LeadStatus == "تم التواصل")                         // ← عربي
                lead.LastContactDate = DateTime.Now;

            if (dto.LeadStatus == "مرفوض" && !string.IsNullOrWhiteSpace(dto.RejectedReason))  // ← عربي
                lead.RejectedReason = dto.RejectedReason;
        }

        if (dto.Feedback != null)
            lead.Feedback = dto.Feedback;

        if (dto.AssignedEmployeeId.HasValue)
            lead.AssignedEmployeeId = dto.AssignedEmployeeId;

        await _db.SaveChangesAsync();

        await _audit.LogAsync("LeadsCRM", "Update",
            dto.LeadId.ToString(), null, dto, userName);

        return (true, "تم التحديث بنجاح");
    }

    // ═══ تحويل Lead لعميل (Party + Opportunity + Task) ═══
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

        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var now = DateTime.Now;

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
                StageId = 1,
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
                StageAfterId = 1,
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
            lead.LeadStatus = "محوّل";          // ← عربي بدل Converted
            lead.LastContactDate = now;

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            await _audit.LogAsync("LeadsCRM", "Convert",
                lead.LeadId.ToString(), null, dto, userName);

            return (true, "تم تحويل Lead لعميل بنجاح", party.PartyId, opportunity.OpportunityId);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return (false, $"خطأ: {ex.InnerException?.Message ?? ex.Message}", 0, 0);
        }
    }

    // ═══ إحصائيات ═══
    public async Task<LeadsCrmStatsDto> GetStatsAsync()
    {
        var today = DateTime.Today;
        var weekStart = today.AddDays(-(int)today.DayOfWeek);
        var monthStart = new DateTime(today.Year, today.Month, 1);

        var total = await _db.LeadsCRMs.CountAsync();

        return new LeadsCrmStatsDto
        {
            TotalLeads = total,
            NewLeads = await _db.LeadsCRMs.CountAsync(l => l.LeadStatus == "جديد"),         // ← عربي
            ContactedLeads = await _db.LeadsCRMs.CountAsync(l => l.LeadStatus == "تم التواصل"), // ← عربي
            QualifiedLeads = await _db.LeadsCRMs.CountAsync(l => l.LeadStatus == "مؤهل"),     // ← عربي
            ConvertedLeads = await _db.LeadsCRMs.CountAsync(l => l.LeadStatus == "محوّل"),    // ← عربي
            RejectedLeads = await _db.LeadsCRMs.CountAsync(l => l.LeadStatus == "مرفوض"),     // ← عربي
            DuplicateLeads = await _db.LeadsCRMs.CountAsync(l => l.IsDuplicate),
            TodayLeads = await _db.LeadsCRMs.CountAsync(l => l.CreatedAt >= today),
            ThisWeekLeads = await _db.LeadsCRMs.CountAsync(l => l.CreatedAt >= weekStart),
            ThisMonthLeads = await _db.LeadsCRMs.CountAsync(l => l.CreatedAt >= monthStart),
            ByCampaign = await _db.LeadsCRMs
                .Where(l => l.CampaignName != null)
                .GroupBy(l => l.CampaignName)
                .Select(g => new LeadsByCampaignDto
                {
                    CampaignName = g.Key!,
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync(),
            ByPlatform = await _db.LeadsCRMs
                .Where(l => l.Platform != null)
                .GroupBy(l => l.Platform)
                .Select(g => new LeadsByPlatformDto
                {
                    Platform = g.Key!,
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .ToListAsync()
        };
    }

    // ═══ حذف Lead ═══
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

    // ═══ الموظفين ═══
    public async Task<List<Employee>> GetEmployeesAsync()
    {
        return await _db.Employees                                 // ← _db مش _context
            .Where(e => e.Status == "نشط")                        // ← IsActive مش status
            .OrderBy(e => e.FullName)
            .ToListAsync();
    }

    // ═══ Helper ═══
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
}