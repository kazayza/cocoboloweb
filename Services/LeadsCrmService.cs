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
    // ═══ لوحة تحليلات الـ Leads (مُحسّن - 3 استعلامات بس بدل 30+) ═══
public async Task<LeadsDashboardDataDto> GetDashboardDataAsync(LeadsDashboardFilterDto filter)
{
    var result = new LeadsDashboardDataDto();

    // ─── QUERY 1: تحميل كل Leads الفترة الحالية في استعلام واحد ───
    var currentQuery = _db.LeadsCRMs.AsNoTracking().AsQueryable();
    if (filter.DateFrom.HasValue)
        currentQuery = currentQuery.Where(l => l.CreatedAt >= filter.DateFrom.Value);
    if (filter.DateTo.HasValue)
        currentQuery = currentQuery.Where(l => l.CreatedAt <= filter.DateTo.Value.AddDays(1));
    if (!string.IsNullOrWhiteSpace(filter.Platform))
        currentQuery = currentQuery.Where(l => l.Platform == filter.Platform);
    if (filter.EmployeeId.HasValue)
        currentQuery = currentQuery.Where(l => l.AssignedEmployeeId == filter.EmployeeId);
    if (!string.IsNullOrWhiteSpace(filter.City))
        currentQuery = currentQuery.Where(l => l.City == filter.City);
    if (!string.IsNullOrWhiteSpace(filter.ProjectType))
        currentQuery = currentQuery.Where(l => l.ProjectType == filter.ProjectType);
    if (!string.IsNullOrWhiteSpace(filter.ProjectStage))
        currentQuery = currentQuery.Where(l => l.ProjectStage == filter.ProjectStage);
    if (!string.IsNullOrWhiteSpace(filter.CampaignName))
        currentQuery = currentQuery.Where(l => l.CampaignName == filter.CampaignName);

    // نحمل بس الأعمدة اللي محتاجينها - مش كل الجدول
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

    // ─── QUERY 2: تحميل Leads الفترة السابقة (للمقارنة) ───
    var prevFilter = filter.GetPreviousPeriod();
    var prevQuery = _db.LeadsCRMs.AsNoTracking().AsQueryable();
    if (prevFilter.DateFrom.HasValue)
        prevQuery = prevQuery.Where(l => l.CreatedAt >= prevFilter.DateFrom.Value);
    if (prevFilter.DateTo.HasValue)
        prevQuery = prevQuery.Where(l => l.CreatedAt <= prevFilter.DateTo.Value.AddDays(1));
    if (!string.IsNullOrWhiteSpace(prevFilter.Platform))
        prevQuery = prevQuery.Where(l => l.Platform == prevFilter.Platform);
    if (prevFilter.EmployeeId.HasValue)
        prevQuery = prevQuery.Where(l => l.AssignedEmployeeId == prevFilter.EmployeeId);
    if (!string.IsNullOrWhiteSpace(prevFilter.City))
        prevQuery = prevQuery.Where(l => l.City == prevFilter.City);
    if (!string.IsNullOrWhiteSpace(prevFilter.ProjectType))
        prevQuery = prevQuery.Where(l => l.ProjectType == prevFilter.ProjectType);
    if (!string.IsNullOrWhiteSpace(prevFilter.ProjectStage))
        prevQuery = prevQuery.Where(l => l.ProjectStage == prevFilter.ProjectStage);
    if (!string.IsNullOrWhiteSpace(prevFilter.CampaignName))
        prevQuery = prevQuery.Where(l => l.CampaignName == prevFilter.CampaignName);

    var prevLeads = await prevQuery.Select(l => new
    {
        l.LeadStatus,
        l.IsDuplicate,
        l.IsConverted,
        l.ConvertedDate,
        l.CreatedAt
    }).ToListAsync();

    // ─── QUERY 3: تحميل أسماء الموظفين ───
    var empIds = leads
        .Where(l => l.AssignedEmployeeId.HasValue)
        .Select(l => l.AssignedEmployeeId!.Value)
        .Distinct()
        .ToList();

    var empNames = empIds.Count > 0
        ? await _db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.EmployeeId))
            .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName)
        : new Dictionary<int, string>();

    // ═══════════════════════════════════════════
    //  كل الحسابات بعد كده في الذاكرة - صفر استعلامات
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
    decimal CalcChange(decimal current, decimal previous)
    {
        if (previous == 0) return current > 0 ? 100m : 0m;
        return Math.Round((current - previous) / previous * 100, 1);
    }
    double? CalcChangeDouble(double current, double previous)
    {
        if (previous == 0) return current > 0 ? 100.0 : 0.0;
        return Math.Round((current - previous) / previous * 100, 1);
    }

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

    // ─── Status Distribution (Donut) - في الذاكرة ───
    var statusColors = new Dictionary<string, string>
    {
        { "جديد", "#3b82f6" },
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

    // ─── Platform Comparison (Bar) - في الذاكرة ───
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

    // ─── Daily Trend (Area) - في الذاكرة ───
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

    // ─── Budget Distribution (Bar) - في الذاكرة ───
    result.BudgetDistribution = leads
        .Where(l => l.Budget != null)
        .GroupBy(l => l.Budget)
        .Select(g => new ChartItemDto { Label = g.Key!, Value = g.Count() })
        .OrderByDescending(x => x.Value)
        .Take(8)
        .ToList();

    // ─── Top Cities (Horizontal Bar) - في الذاكرة ───
    result.TopCities = leads
        .Where(l => l.City != null)
        .GroupBy(l => l.City)
        .Select(g => new ChartItemDto { Label = g.Key!, Value = g.Count() })
        .OrderByDescending(x => x.Value)
        .Take(8)
        .ToList();

    // ─── Employee Performance (Stacked Bar) - في الذاكرة (بدون N+1) ───
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

    // ─── Funnel Data - في الذاكرة ───
    var newCount = leads.Count(l => l.LeadStatus == "جديد");
    var contactedCount = leads.Count(l => l.LeadStatus == "تم التواصل");
    var qualifiedCount = leads.Count(l => l.LeadStatus == "مؤهل");
    var rejeCount = leads.Count(l => l.LeadStatus == "مرفوض");

    result.FunnelData = new List<FunnelItemDto>
    {
        new() { Stage = "جديد", Count = newCount, Percentage = totalLeads > 0 ? Math.Round((decimal)newCount / totalLeads * 100, 1) : 0, Color = "#3b82f6" },
        new() { Stage = "تم التواصل", Count = contactedCount, Percentage = totalLeads > 0 ? Math.Round((decimal)contactedCount / totalLeads * 100, 1) : 0, Color = "#f59e0b" },
        new() { Stage = "مؤهل", Count = qualifiedCount, Percentage = totalLeads > 0 ? Math.Round((decimal)qualifiedCount / totalLeads * 100, 1) : 0, Color = "#10b981" },
        new() { Stage = "محوّل", Count = convertedCount, Percentage = totalLeads > 0 ? Math.Round((decimal)convertedCount / totalLeads * 100, 1) : 0, Color = "#8b5cf6" },
        new() { Stage = "مرفوض", Count = rejeCount, Percentage = totalLeads > 0 ? Math.Round((decimal)rejeCount / totalLeads * 100, 1) : 0, Color = "#ef4444" }
    };

    // ─── Top Campaigns - في الذاكرة ───
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

    // ─── Project Type Summary - في الذاكرة ───
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

    // ─── Recent Converted - في الذاكرة ───
    result.RecentConverted = leads
        .Where(l => l.LeadStatus == "محوّل" && l.ConvertedDate.HasValue)
        .OrderByDescending(l => l.ConvertedDate)
        .Take(10)
        .Select(l => new RecentConvertedDto
        {
            FullName = l.FullName,
            CampaignName = l.CampaignName ?? "",
            EmployeeName = l.AssignedEmployeeId.HasValue
                && empNames.TryGetValue(l.AssignedEmployeeId.Value, out var name) ? name : "",
            ConvertedDate = l.ConvertedDate!.Value,
            Budget = "" // Budget مش محملة في الاستعلام الخفيف
        }).ToList();

    // ─── Filter dropdown options - في الذاكرة ───
    result.AvailableCities = leads
        .Where(l => l.City != null).Select(l => l.City!).Distinct().OrderBy(c => c).ToList();

    result.AvailableProjectTypes = leads
        .Where(l => l.ProjectType != null).Select(l => l.ProjectType!).Distinct().OrderBy(p => p).ToList();

    result.AvailableProjectStages = leads
        .Where(l => l.ProjectStage != null).Select(l => l.ProjectStage!).Distinct().OrderBy(p => p).ToList();

    result.AvailableCampaigns = leads
        .Where(l => l.CampaignName != null).Select(l => l.CampaignName!).Distinct().OrderBy(c => c).ToList();

    return result;
}
}

