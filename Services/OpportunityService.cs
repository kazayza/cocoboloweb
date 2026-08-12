using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace COCOBOLOERPNEW.Services;

public class OpportunityService : IOpportunityService
{
    private readonly db24804Context _db;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<OpportunityService> _logger;
    private readonly NotificationService _notify;

    private static readonly HashSet<string> WonKeywords  = new() { "تم البيع", "بيع", "Closed Deal" };
    private static readonly HashSet<string> LostKeywords = new() { "خسارة", "Lost", "غير مهتم", "Not Interested" };

    public OpportunityService(db24804Context db, IHttpContextAccessor http, ILogger<OpportunityService> logger, NotificationService notify)
    { _db = db; _http = http; _logger = logger; _notify = notify; }

    // ════════════════════ LIST ════════════════════
    public async Task<PagedResult<OpportunityListDto>> GetOpportunitiesAsync(OpportunityFilterDto filter)
    {
        var crmAccess = _http.GetCrmAccessFrom();
        var query = _db.VwSalesOpportunities.AsNoTracking().AsQueryable();
        if (crmAccess.HasValue) query = query.Where(o => o.CreatedAt >= crmAccess.Value);
        query = ApplyVwFilters(query, filter);
        var totalCount = await query.CountAsync();
        query = ApplySorting(query, filter);
        var items = await query.Skip((filter.PageNumber - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(o => MapToListDto(o)).ToListAsync();
        await EnrichCounts(items);
        await EnrichLifecycleDataAsync(items);
        return new PagedResult<OpportunityListDto> { Items = items, TotalCount = totalCount, PageNumber = filter.PageNumber, PageSize = filter.PageSize };
    }

    // ════════════════════ KANBAN BOARD ════════════════════
    public async Task<KanbanBoardDto> GetKanbanBoardAsync(OpportunityFilterDto filter)
    {
        var crmAccess = _http.GetCrmAccessFrom();
        var stages = await _db.SalesStages.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.StageOrder).ToListAsync();
        var query = _db.SalesOpportunities.AsNoTracking().Where(o => o.IsActive);
        if (crmAccess.HasValue) query = query.Where(o => o.CreatedAt >= crmAccess.Value);
        query = ApplyOppFilters(query, filter);
        var opps = await query.Select(o => new { o.OpportunityId, o.PartyId, o.StageId, o.ExpectedValue, o.EmployeeId, o.NextFollowUpDate, o.InterestedProduct, o.SourceId, o.CreatedAt, o.ClosedAt }).ToListAsync();
        var partyIds = opps.Select(o => o.PartyId).Distinct().ToList();
        var parties = partyIds.Any() ? (await _db.Parties.AsNoTracking().Where(p => partyIds.Contains(p.PartyId)).Select(p => new { p.PartyId, p.PartyName, p.Phone }).ToListAsync()).ToDictionary(p => p.PartyId, p => (p.PartyName, p.Phone)) : new();
        var empIds = opps.Where(o => o.EmployeeId.HasValue).Select(o => o.EmployeeId!.Value).Distinct().ToList();
        var emps = empIds.Any() ? (await _db.Employees.AsNoTracking().Where(e => empIds.Contains(e.EmployeeId)).Select(e => new { e.EmployeeId, e.FullName }).ToListAsync()).ToDictionary(e => e.EmployeeId, e => e.FullName ?? "—") : new();
        var srcIds = opps.Where(o => o.SourceId.HasValue).Select(o => o.SourceId!.Value).Distinct().ToList();
        var srcs = srcIds.Any() ? (await _db.ContactSources.AsNoTracking().Where(s => srcIds.Contains(s.SourceId)).Select(s => new { s.SourceId, s.SourceName }).ToListAsync()).ToDictionary(s => s.SourceId, s => s.SourceName ?? "—") : new();
        var oppIds = opps.Select(o => o.OpportunityId).ToList();
        var icDict = oppIds.Any() ? (await _db.CustomerInteractions.AsNoTracking().Where(ci => oppIds.Contains(ci.OpportunityId)).GroupBy(ci => ci.OpportunityId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync()).ToDictionary(x => x.Key, x => x.Count) : new();
        var tcDict = oppIds.Any() ? (await _db.CrmTasks.AsNoTracking().Where(t => t.OpportunityId != null && oppIds.Contains(t.OpportunityId.Value)).GroupBy(t => t.OpportunityId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync()).ToDictionary(x => x.Key, x => x.Count) : new();
        var today = DateTime.Today;
        return new KanbanBoardDto { Columns = stages.Select(s => { var cards = opps.Where(o => o.StageId == s.StageId).Select(o => { parties.TryGetValue(o.PartyId, out var p); emps.TryGetValue(o.EmployeeId ?? 0, out var en); srcs.TryGetValue(o.SourceId ?? 0, out var sn); return new KanbanCardDto { OpportunityId = o.OpportunityId, PartyId = o.PartyId, ClientName = p.PartyName ?? "—", Phone = p.Phone, ExpectedValue = o.ExpectedValue, EmployeeId = o.EmployeeId, EmployeeName = en, InterestedProduct = o.InterestedProduct, SourceId = o.SourceId, SourceName = sn, NextFollowUpDate = o.NextFollowUpDate, StageId = s.StageId, InteractionsCount = icDict.TryGetValue(o.OpportunityId, out var ic) ? ic : 0, TasksCount = tcDict.TryGetValue(o.OpportunityId, out var tc) ? tc : 0, CreatedAt = o.CreatedAt, ClosedAt = o.ClosedAt, LifecycleDays = CalculateLifecycleDays(o.CreatedAt, o.ClosedAt), IsOverdue = o.NextFollowUpDate.HasValue && o.NextFollowUpDate.Value < today }; }).ToList(); return new KanbanColumnDto { StageId = s.StageId, StageName = s.StageName ?? "", StageNameAr = s.StageNameAr ?? s.StageName ?? "", StageColor = s.StageColor ?? "#94a3b8", StageOrder = s.StageOrder, Count = cards.Count, Value = cards.Sum(c => c.ExpectedValue ?? 0), Cards = cards }; }).ToList() };
    }

    // ════════════════════ MOVE STAGE ════════════════════
    public async Task<(bool Success, string Message)> MoveStageAsync(int opportunityId, int newStageId, string userName)
{
    try
    {
        var opp = await _db.SalesOpportunities.FindAsync(opportunityId);
        if (opp == null) return (false, "الفرصة غير موجودة");

        if (IsExitStageId(newStageId) && !CanDirectCloseOpportunities())
            return (false, "تحويل الفرصة إلى خسارة أو غير مهتم يحتاج طلب موافقة من مدير المبيعات من داخل شاشة الفرصة.");
        var oldStageId = opp.StageId;
        if (oldStageId == newStageId) return (true, "لم يتغير شيء");
        var oldStage = await _db.SalesStages.FindAsync(oldStageId);
        var newStage = await _db.SalesStages.FindAsync(newStageId);
        var now = DateTime.Now;
        opp.StageId = newStageId; opp.LastUpdatedBy = userName; opp.LastUpdatedAt = now; opp.LastContactDate = now;
        ApplyClosureState(opp, oldStageId, newStageId, userName, now);

        var stages = await _db.SalesStages.AsNoTracking().ToListAsync();
        var wonIds = stages.Where(s => WonKeywords.Any(k => (s.StageNameAr ?? "").Contains(k) || (s.StageName ?? "").Contains(k))).Select(s => s.StageId).ToHashSet();

        // لو المرحلة الجديدة رابحة → املأ القيمة الفعلية
        if (wonIds.Contains(newStageId) && opp.ActualValue == null)
        {
            if (opp.TransactionId.HasValue)
            {
                var txn = await _db.Transactions.AsNoTracking()
                    .Where(t => t.TransactionId == opp.TransactionId.Value && t.TransactionType == "Sale")
                    .Select(t => new { t.GrandTotal })
                    .FirstOrDefaultAsync();
                opp.ActualValue = txn?.GrandTotal ?? opp.ExpectedValue;
            }
            else
            {
                opp.ActualValue = opp.ExpectedValue;
            }
        }

        opp.NextFollowUpDate = wonIds.Contains(newStageId) ? DateTime.Today.AddDays(7) : (!opp.NextFollowUpDate.HasValue || opp.NextFollowUpDate.Value < DateTime.Today ? DateTime.Today.AddDays(3) : opp.NextFollowUpDate);
        _db.CustomerInteractions.Add(new CustomerInteraction { OpportunityId = opportunityId, PartyId = opp.PartyId, StageBeforeId = oldStageId, StageAfterId = newStageId, Summary = $"نقل تلقائي: {(oldStage?.StageNameAr ?? "—")} → {(newStage?.StageNameAr ?? "—")}", InteractionDate = now, CreatedBy = userName, CreatedAt = now, NextFollowUpDate = opp.NextFollowUpDate });
        var party = await _db.Parties.FindAsync(opp.PartyId);
        if (party != null) party.LastContactDate = now;
        await _db.SaveChangesAsync();
        return (true, $"تم النقل إلى {(newStage?.StageNameAr ?? newStage?.StageName ?? "—")}");
    }
    catch (Exception ex) { _logger.LogError(ex, "MoveStage failed"); return (false, $"خطأ: {ex.Message}"); }
}

    // ════════════════════ GET FOR EDIT ════════════════════
    public async Task<OpportunityFormDto?> GetOpportunityForEditAsync(int opportunityId)
{
    var opp = await _db.SalesOpportunities.AsNoTracking().FirstOrDefaultAsync(o => o.OpportunityId == opportunityId);
    if (opp == null) return null;
    
    var partyInfo = await _db.Parties.AsNoTracking()
        .Where(p => p.PartyId == opp.PartyId)
        .Select(p => new { p.PartyName, p.Phone })
        .FirstOrDefaultAsync();

    return new OpportunityFormDto
    {
        OpportunityId = opp.OpportunityId,
        PartyId = opp.PartyId,
        PartyName = partyInfo?.PartyName,
        Phone = partyInfo?.Phone,
        EmployeeId = opp.EmployeeId,
        SourceId = opp.SourceId,
        AdTypeId = opp.AdTypeId,
        StageId = opp.StageId,
        StatusId = opp.StatusId,
        CategoryId = opp.CategoryId,
        InterestedProduct = opp.InterestedProduct,
        ExpectedValue = opp.ExpectedValue,
        Location = opp.Location,
        FirstContactDate = opp.FirstContactDate,
        NextFollowUpDate = opp.NextFollowUpDate,
        LostReasonId = opp.LostReasonId,
        LostNotes = opp.LostNotes,
        Notes = opp.Notes,
        Guidance = opp.Guidance,
        IsActive = opp.IsActive,
        CreatedBy = opp.CreatedBy,
        CreatedAt = opp.CreatedAt
    };
}

    // ════════════════════ GET OPPORTUNITY DETAIL ════════════════════
    public async Task<OpportunityListDto?> GetOpportunityDetailAsync(int opportunityId)
{
    var opp = await _db.VwSalesOpportunities
        .AsNoTracking()
        .FirstOrDefaultAsync(o => o.OpportunityId == opportunityId);

    if (opp == null) return null;

    var dto = MapToListDto(opp);
    await EnrichLifecycleDataAsync(new List<OpportunityListDto> { dto });

    var sourceLead = await _db.LeadsCRMs
        .AsNoTracking()
        .Where(l => l.ConvertedOpportunityId == opportunityId)
        .Select(l => new
        {
            l.LeadId,
            l.FullName,
            l.Phone,
            l.CampaignName,
            l.Platform
        })
        .FirstOrDefaultAsync();

    if (sourceLead != null)
    {
        dto.SourceLeadId = sourceLead.LeadId;
        dto.SourceLeadName = sourceLead.FullName;
        dto.SourceLeadPhone = sourceLead.Phone;
        dto.SourceLeadCampaign = sourceLead.CampaignName;
        dto.SourceLeadPlatform = sourceLead.Platform;
    }

    return dto;
}

    // ════════════════════ GET ALL EMPLOYEES ════════════════════
    public async Task<List<Employee>> GetEmployeesAsync()
    {
        return await _db.Employees.AsNoTracking()
            .Where(e =>  e.Status == "نشط")
            .Select(e => new Employee { EmployeeId = e.EmployeeId, FullName = e.FullName })
            .ToListAsync();
    }

    // ════════════════════ STATS ════════════════════
    // ✅ الكود المعدل
public async Task<OpportunityStatsDto> GetStatsAsync(OpportunityFilterDto filter)
{
    try
    {
        // ═══ 1. CRM Access Date ═══
        var crmAccess = _http.GetCrmAccessFrom();
        
        var stages = await _db.SalesStages
            .AsNoTracking()
            .Where(s => s.IsActive)
            .ToListAsync();

        var wonIds = stages
            .Where(s => WonKeywords.Any(k => 
                (s.StageNameAr ?? "").Contains(k) || 
                (s.StageName ?? "").Contains(k)))
            .Select(s => s.StageId)
            .ToHashSet();

        var lostIds = stages
            .Where(s => LostKeywords.Any(k => 
                (s.StageNameAr ?? "").Contains(k) || 
                (s.StageName ?? "").Contains(k)))
            .Select(s => s.StageId)
            .ToHashSet();
        lostIds.ExceptWith(wonIds);

        // ═══ 2. Base Query مع كل الفلاتر ═══
        var q = _db.SalesOpportunities
            .AsNoTracking()
            .Where(o => o.IsActive == (filter.IsActive ?? true));

        // ⭐ فلتر التاريخ حسب صلاحية المستخدم
        if (crmAccess.HasValue)
            q = q.Where(o => o.CreatedAt >= crmAccess.Value);

        // ⭐ كل الفلاتر المتقدمة
        q = ApplyStatsFilters(q, filter);

        var opps = await q
            .Select(o => new 
            { 
                o.StageId, 
                o.ExpectedValue, 
                o.ActualValue,
                o.NextFollowUpDate,
                o.TransactionId
            })
            .ToListAsync();

        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);

        return new OpportunityStatsDto
        {
            TotalCount = opps.Count,
            OpenCount = opps.Count(o => 
                !wonIds.Contains(o.StageId) && 
                !lostIds.Contains(o.StageId)),
            WonCount = opps.Count(o => wonIds.Contains(o.StageId)),
            LostCount = opps.Count(o => lostIds.Contains(o.StageId)),
            PipelineValue = opps
                .Where(o => !wonIds.Contains(o.StageId) && 
                            !lostIds.Contains(o.StageId))
                .Sum(o => o.ExpectedValue ?? 0),
            ActualValue = opps
    .Where(o => wonIds.Contains(o.StageId))
    .Sum(o => o.ExpectedValue ?? 0),
            OverdueFollowUpCount = opps.Count(o => 
                o.NextFollowUpDate.HasValue && 
                o.NextFollowUpDate.Value < today),
            TodayFollowUpCount = opps.Count(o => 
                o.NextFollowUpDate.HasValue && 
                o.NextFollowUpDate.Value >= today && 
                o.NextFollowUpDate.Value < tomorrow)
        };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "GetStatsAsync failed");
        return new();
    }
}

// ═══ Helper Method جديدة ═══
private static IQueryable<SalesOpportunity> ApplyStatsFilters(
    IQueryable<SalesOpportunity> q, OpportunityFilterDto f)
{
    if (f.StageId.HasValue)
        q = q.Where(o => o.StageId == f.StageId.Value);
    if (f.EmployeeId.HasValue)
        q = q.Where(o => o.EmployeeId == f.EmployeeId.Value);
    if (f.SourceId.HasValue)
        q = q.Where(o => o.SourceId == f.SourceId.Value);
    if (f.CategoryId.HasValue)
        q = q.Where(o => o.CategoryId == f.CategoryId.Value);
    if (f.MinValue.HasValue)
        q = q.Where(o => o.ExpectedValue >= f.MinValue.Value);
    if (f.MaxValue.HasValue)
        q = q.Where(o => o.ExpectedValue <= f.MaxValue.Value);
    if (f.DateFrom.HasValue)
{
    var from = f.DateFrom.Value.Date;
    q = q.Where(o => o.CreatedAt >= from);
}

if (f.DateTo.HasValue)
{
    var to = f.DateTo.Value.Date.AddDays(1).AddTicks(-1);
    q = q.Where(o => o.CreatedAt <= to);
}
    if (f.IsOverdueFollowUp == true)
        q = q.Where(o => o.NextFollowUpDate.HasValue && 
                         o.NextFollowUpDate.Value < DateTime.Today);
    if (f.HasFollowUp == true)
        q = q.Where(o => o.NextFollowUpDate.HasValue);
    
    return q;
}

    // ════════════════════ SAVE ════════════════════
    public async Task<(bool Success, string Message, int OpportunityId)> SaveOpportunityAsync(OpportunityFormDto dto, string userName)
    {
        try
        {
            if (!dto.SourceId.HasValue)
                return (false, "برجاء تحديد طريقة / مصدر التواصل أولاً", 0);

            if (!IsClosedStageId(dto.StageId))
            {
                if (!dto.NextFollowUpDate.HasValue)
                    return (false, "تاريخ المتابعة القادم إجباري لهذه المرحلة", 0);
            }

            if (IsExitStageId(dto.StageId) && !CanDirectCloseOpportunities())
            {
                if (dto.OpportunityId <= 0)
                    return (false, "لا يمكن إرسال طلب إغلاق قبل حفظ الفرصة أولاً.", 0);

                var currentStageId = await _db.SalesOpportunities
                    .AsNoTracking()
                    .Where(o => o.OpportunityId == dto.OpportunityId)
                    .Select(o => o.StageId)
                    .FirstOrDefaultAsync();

                if (currentStageId != dto.StageId)
                {
                    var requestResult = await CreateClosureApprovalRequestInternalAsync(
                        dto.OpportunityId,
                        dto.PartyId,
                        0,
                        dto.StageId,
                        dto.LostReasonId,
                        dto.LostNotes,
                        "OpportunityDetail",
                        userName);

                    if (!requestResult.Success || requestResult.Request == null)
                        return (false, requestResult.Message, dto.OpportunityId);

                    if (requestResult.IsNewRequest)
                    {
                        await NotifyClosureApprovalRequestedAsync(requestResult.Request, requestResult.ClientName, requestResult.RequestedStageName, userName);
                    }

                    return (true, requestResult.Message, dto.OpportunityId);
                }
            }

            SalesOpportunity opp;
            bool isNew = dto.OpportunityId == 0;
            int? oldEmployeeId = null;
            int oldStageId = 0;
            var now = DateTime.Now;

            if (isNew)
            {
                opp = new SalesOpportunity
                {
                    PartyId = dto.PartyId,
                    CreatedBy = userName,
                    CreatedAt = now,
                    IsActive = true
                };
                _db.SalesOpportunities.Add(opp);
            }
            else
            {
                var existingOpp = await _db.SalesOpportunities.FindAsync(dto.OpportunityId);
                if (existingOpp == null) return (false, "الفرصة غير موجودة", 0);

                opp = existingOpp;
                oldEmployeeId = opp.EmployeeId;
                oldStageId = opp.StageId;
                opp.LastUpdatedBy = userName;
                opp.LastUpdatedAt = now;
            }

            opp.EmployeeId = dto.EmployeeId;
            opp.SourceId = dto.SourceId;
            opp.AdTypeId = dto.AdTypeId;
            opp.StageId = dto.StageId;
            opp.StatusId = dto.StatusId;
            opp.CategoryId = dto.CategoryId;
            opp.InterestedProduct = dto.InterestedProduct;
            opp.ExpectedValue = dto.ExpectedValue;
            opp.Location = dto.Location;
            opp.FirstContactDate = dto.FirstContactDate;
            opp.NextFollowUpDate = dto.NextFollowUpDate;
            opp.LostReasonId = dto.LostReasonId;
            opp.LostNotes = dto.LostNotes;
            opp.Notes = dto.Notes;
            opp.Guidance = dto.Guidance;
            opp.IsActive = dto.IsActive;
            ApplyClosureState(opp, isNew ? 0 : oldStageId, dto.StageId, userName, now);

            var stages = await _db.SalesStages.AsNoTracking().ToListAsync();
            var wonIds = stages.Where(s => WonKeywords.Any(k => (s.StageNameAr ?? "").Contains(k) || (s.StageName ?? "").Contains(k))).Select(s => s.StageId).ToHashSet();
            if (wonIds.Contains(opp.StageId) && opp.ActualValue == null)
            {
                if (opp.TransactionId.HasValue)
                {
                    var txn = await _db.Transactions.AsNoTracking()
                        .Where(t => t.TransactionId == opp.TransactionId.Value && t.TransactionType == "Sale")
                        .Select(t => new { t.GrandTotal })
                        .FirstOrDefaultAsync();
                    opp.ActualValue = txn?.GrandTotal ?? opp.ExpectedValue;
                }
                else
                {
                    opp.ActualValue = opp.ExpectedValue;
                }
            }

            if (dto.NextFollowUpDate.HasValue)
            {
                var party = await _db.Parties.FindAsync(dto.PartyId);
                if (party != null) party.LastContactDate = now;
            }

            await _db.SaveChangesAsync();

            if (!isNew && oldEmployeeId != dto.EmployeeId && dto.EmployeeId.HasValue)
            {
                await AddOpportunityReassignmentInteractionAsync(
                    opp.OpportunityId,
                    dto.PartyId,
                    oldEmployeeId,
                    dto.EmployeeId.Value,
                    dto.SourceId,
                    dto.StatusId,
                    dto.NextFollowUpDate,
                    userName,
                    dto.ReassignmentComment);

                await NotifyOpportunityReassignedAsync(opp.OpportunityId, dto.PartyId, oldEmployeeId, dto.EmployeeId.Value, userName, dto.ReassignmentComment);
            }

            return (true, isNew ? "تم إضافة الفرصة بنجاح" : "تم تعديل الفرصة بنجاح", opp.OpportunityId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save failed");
            return (false, $"خطأ: {ex.InnerException?.Message ?? ex.Message}", 0);
        }
    }

    // ════════════════════ DELETE ════════════════════
    public async Task<(bool Success, string Message)> DeleteOpportunityAsync(int opportunityId, string userName)
    {
        try
        {
            var opp = await _db.SalesOpportunities.FindAsync(opportunityId);
            if (opp == null) return (false, "الفرصة غير موجودة");
            var hasInteractions = await _db.CustomerInteractions.AnyAsync(ci => ci.OpportunityId == opportunityId);
            if (hasInteractions) { opp.IsActive = false; opp.LastUpdatedBy = userName; opp.LastUpdatedAt = DateTime.Now; await _db.SaveChangesAsync(); return (true, "تم تعطيل الفرصة (يوجد تفاعلات)"); }
            _db.SalesOpportunities.Remove(opp); await _db.SaveChangesAsync();
            return (true, "تم حذف الفرصة");
        }
        catch (Exception ex) { return (false, $"خطأ: {ex.Message}"); }
    }

    // ════════════════════ LOOKUPS ════════════════════
    public async Task<List<SalesStage>> GetStagesAsync() => await _db.SalesStages.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.StageOrder).ToListAsync();
    public async Task<List<ContactSource>> GetSourcesAsync() => await _db.ContactSources.AsNoTracking().Where(s => s.IsActive).ToListAsync();
    public async Task<List<InterestCategory>> GetCategoriesAsync() => await _db.InterestCategories.AsNoTracking().Where(c => c.IsActive).ToListAsync();
    public async Task<List<LostReason>> GetLostReasonsAsync() => await _db.LostReasons.AsNoTracking().Where(r => r.IsActive).ToListAsync();
    public async Task<List<AdType>> GetAdTypesAsync() => await _db.AdTypes.AsNoTracking().ToListAsync();
    public async Task<List<Employee>> GetActiveEmployeesAsync() => await _db.Employees.AsNoTracking().Where(e => e.Status == "نشط").Select(e => new Employee { EmployeeId = e.EmployeeId, FullName = e.FullName, Department = e.Department }).ToListAsync();

    public async Task<(bool Success, string Message, int? RequestId)> RequestClosureApprovalAsync(OpportunityClosureApprovalCreateDto dto, string userName)
    {
        var result = await CreateClosureApprovalRequestInternalAsync(
            dto.OpportunityId,
            dto.PartyId,
            dto.CurrentStageId,
            dto.RequestedStageId,
            dto.LostReasonId,
            dto.RequestReasonNotes,
            dto.RequestSource,
            userName);

        if (!result.Success || result.Request == null)
            return (false, result.Message, result.Request?.RequestId);

        if (result.IsNewRequest)
            await NotifyClosureApprovalRequestedAsync(result.Request, result.ClientName, result.RequestedStageName, userName);

        return (true, result.Message, result.Request.RequestId);
    }

    public async Task<List<OpportunityClosureApprovalRequestDto>> GetClosureApprovalRequestsAsync(string? status = null)
    {
        var query = _db.OpportunityClosureApprovalRequests
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);

        return await query
            .OrderBy(r => r.Status == OpportunityClosureApprovalStatuses.Pending ? 0 : 1)
            .ThenByDescending(r => r.RequestedAt)
            .Select(r => new OpportunityClosureApprovalRequestDto
            {
                RequestId = r.RequestId,
                OpportunityId = r.OpportunityId,
                PartyId = r.PartyId,
                ClientName = r.Party.PartyName ?? "",
                CurrentStageId = r.CurrentStageId,
                CurrentStageName = r.CurrentStage.StageNameAr ?? r.CurrentStage.StageName,
                RequestedStageId = r.RequestedStageId,
                RequestedStageName = r.RequestedStage.StageNameAr ?? r.RequestedStage.StageName,
                LostReasonId = r.LostReasonId,
                LostReasonName = r.LostReason != null ? (r.LostReason.ReasonNameAr ?? r.LostReason.ReasonName) : null,
                RequestReasonNotes = r.RequestReasonNotes,
                RequestSource = r.RequestSource,
                Status = r.Status,
                RequestedBy = r.RequestedBy,
                RequestedAt = r.RequestedAt,
                ReviewedBy = r.ReviewedBy,
                ReviewedAt = r.ReviewedAt,
                ReviewNotes = r.ReviewNotes
            })
            .ToListAsync();
    }

    public async Task<OpportunityClosureApprovalRequestDto?> GetPendingClosureApprovalByOpportunityAsync(int opportunityId)
    {
        if (opportunityId <= 0) return null;

        return await _db.OpportunityClosureApprovalRequests
            .AsNoTracking()
            .Where(r => r.OpportunityId == opportunityId && r.Status == OpportunityClosureApprovalStatuses.Pending)
            .OrderByDescending(r => r.RequestedAt)
            .Select(r => new OpportunityClosureApprovalRequestDto
            {
                RequestId = r.RequestId,
                OpportunityId = r.OpportunityId,
                PartyId = r.PartyId,
                ClientName = r.Party.PartyName ?? "",
                CurrentStageId = r.CurrentStageId,
                CurrentStageName = r.CurrentStage.StageNameAr ?? r.CurrentStage.StageName,
                RequestedStageId = r.RequestedStageId,
                RequestedStageName = r.RequestedStage.StageNameAr ?? r.RequestedStage.StageName,
                LostReasonId = r.LostReasonId,
                LostReasonName = r.LostReason != null ? (r.LostReason.ReasonNameAr ?? r.LostReason.ReasonName) : null,
                RequestReasonNotes = r.RequestReasonNotes,
                RequestSource = r.RequestSource,
                Status = r.Status,
                RequestedBy = r.RequestedBy,
                RequestedAt = r.RequestedAt,
                ReviewedBy = r.ReviewedBy,
                ReviewedAt = r.ReviewedAt,
                ReviewNotes = r.ReviewNotes
            })
            .FirstOrDefaultAsync();
    }

    public async Task<(bool Success, string Message)> ApproveClosureApprovalAsync(int requestId, string userName, string? reviewNotes = null)
    {
        if (!CanApproveClosureRequests())
            return (false, "ليست لديك صلاحية اعتماد طلبات الإغلاق.");

        var request = await _db.OpportunityClosureApprovalRequests
            .FirstOrDefaultAsync(r => r.RequestId == requestId && r.Status == OpportunityClosureApprovalStatuses.Pending);

        if (request == null)
            return (false, "طلب الموافقة غير موجود أو تم التعامل معه بالفعل.");

        var opp = await _db.SalesOpportunities.FindAsync(request.OpportunityId);
        if (opp == null)
            return (false, "الفرصة المرتبطة بالطلب غير موجودة.");

        if (opp.StageId != request.CurrentStageId)
            return (false, "لا يمكن اعتماد الطلب لأن مرحلة الفرصة تغيرت بعد إنشاء الطلب. راجع الفرصة أولاً.");

        var now = DateTime.Now;
        var requestedStageName = await _db.SalesStages.AsNoTracking()
            .Where(s => s.StageId == request.RequestedStageId)
            .Select(s => s.StageNameAr ?? s.StageName)
            .FirstOrDefaultAsync() ?? "الحالة المطلوبة";

        var currentStageName = await _db.SalesStages.AsNoTracking()
            .Where(s => s.StageId == request.CurrentStageId)
            .Select(s => s.StageNameAr ?? s.StageName)
            .FirstOrDefaultAsync() ?? "الحالة الحالية";

        var clientName = await _db.Parties.AsNoTracking()
            .Where(p => p.PartyId == request.PartyId)
            .Select(p => p.PartyName)
            .FirstOrDefaultAsync() ?? $"عميل #{request.PartyId}";

        request.Status = OpportunityClosureApprovalStatuses.Approved;
        request.ReviewedBy = userName;
        request.ReviewedAt = now;
        request.ReviewNotes = string.IsNullOrWhiteSpace(reviewNotes) ? null : reviewNotes.Trim();

        _db.CustomerInteractions.Add(new CustomerInteraction
        {
            OpportunityId = opp.OpportunityId,
            PartyId = opp.PartyId,
            EmployeeId = null,
            SourceId = null,
            StatusId = null,
            InteractionDate = now,
            Summary = $"تم اعتماد طلب تحويل الفرصة من {currentStageName} إلى {requestedStageName} بواسطة {userName}",
            StageBeforeId = null,
            StageAfterId = null,
            NextFollowUpDate = null,
            Notes = string.IsNullOrWhiteSpace(request.ReviewNotes)
                ? "تم اعتماد الطلب وبانتظار تنفيذ الإغلاق بواسطة مقدم الطلب"
                : request.ReviewNotes,
            CreatedBy = userName,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(request.RequestedBy) && !string.Equals(request.RequestedBy, userName, StringComparison.OrdinalIgnoreCase))
            await NotifyClosureApprovalDecisionAsync(request.RequestedBy, opp.OpportunityId, clientName, requestedStageName, true, userName, request.ReviewNotes);

        return (true, $"تم اعتماد طلب تحويل الفرصة إلى {requestedStageName} وإرسال إشعار إلى مقدم الطلب لاستكمال الإغلاق.");
    }

    public async Task<(bool Success, string Message)> RejectClosureApprovalAsync(int requestId, string userName, string? reviewNotes = null)
    {
        if (!CanApproveClosureRequests())
            return (false, "ليست لديك صلاحية رفض طلبات الإغلاق.");

        var request = await _db.OpportunityClosureApprovalRequests
            .FirstOrDefaultAsync(r => r.RequestId == requestId && r.Status == OpportunityClosureApprovalStatuses.Pending);

        if (request == null)
            return (false, "طلب الموافقة غير موجود أو تم التعامل معه بالفعل.");

        var requestedStageName = await _db.SalesStages.AsNoTracking()
            .Where(s => s.StageId == request.RequestedStageId)
            .Select(s => s.StageNameAr ?? s.StageName)
            .FirstOrDefaultAsync() ?? "الحالة المطلوبة";

        var clientName = await _db.Parties.AsNoTracking()
            .Where(p => p.PartyId == request.PartyId)
            .Select(p => p.PartyName)
            .FirstOrDefaultAsync() ?? $"عميل #{request.PartyId}";

        request.Status = OpportunityClosureApprovalStatuses.Rejected;
        request.ReviewedBy = userName;
        request.ReviewedAt = DateTime.Now;
        request.ReviewNotes = string.IsNullOrWhiteSpace(reviewNotes) ? null : reviewNotes.Trim();

        var opportunity = await _db.SalesOpportunities.AsNoTracking()
            .Where(o => o.OpportunityId == request.OpportunityId)
            .Select(o => new { o.PartyId, o.EmployeeId, o.SourceId, o.StatusId })
            .FirstOrDefaultAsync();

        if (opportunity != null)
        {
            _db.CustomerInteractions.Add(new CustomerInteraction
            {
                OpportunityId = request.OpportunityId,
                PartyId = opportunity.PartyId,
                EmployeeId = null,
                SourceId = null,
                StatusId = null,
                InteractionDate = DateTime.Now,
                Summary = $"تم رفض طلب تحويل الفرصة إلى {requestedStageName} بواسطة {userName}",
                StageBeforeId = null,
                StageAfterId = null,
                NextFollowUpDate = null,
                Notes = string.IsNullOrWhiteSpace(request.ReviewNotes) ? "رفض طلب الإغلاق" : request.ReviewNotes,
                CreatedBy = userName,
                CreatedAt = DateTime.Now
            });
        }

        await _db.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(request.RequestedBy) && !string.Equals(request.RequestedBy, userName, StringComparison.OrdinalIgnoreCase))
            await NotifyClosureApprovalDecisionAsync(request.RequestedBy, request.OpportunityId, clientName, requestedStageName, false, userName, request.ReviewNotes);

        return (true, "تم رفض طلب الإغلاق.");
    }

    // ════════════════════ PRIVATE HELPERS ════════════════════
    private static IQueryable<VwSalesOpportunity> ApplyVwFilters(IQueryable<VwSalesOpportunity> q, OpportunityFilterDto f)
    {
        if (f.StageId.HasValue) q = q.Where(o => o.StageId == f.StageId.Value);
        if (f.EmployeeId.HasValue) q = q.Where(o => o.EmployeeId == f.EmployeeId.Value);
        if (f.SourceId.HasValue) q = q.Where(o => o.SourceId == f.SourceId.Value);
        if (f.CategoryId.HasValue) q = q.Where(o => o.CategoryId == f.CategoryId.Value);
        if (f.IsActive.HasValue) q = q.Where(o => o.IsActive == f.IsActive.Value);
        if (f.MinValue.HasValue) q = q.Where(o => o.ExpectedValue >= f.MinValue.Value);
        if (f.MaxValue.HasValue) q = q.Where(o => o.ExpectedValue <= f.MaxValue.Value);
        if (f.DateFrom.HasValue)
{
    var from = f.DateFrom.Value.Date;
    q = q.Where(o => o.CreatedAt >= from);
}

if (f.DateTo.HasValue)
{
    var to = f.DateTo.Value.Date.AddDays(1).AddTicks(-1);
    q = q.Where(o => o.CreatedAt <= to);
}
        if (f.IsOverdueFollowUp == true) q = q.Where(o => o.NextFollowUpDate.HasValue && o.NextFollowUpDate.Value < DateTime.Today);
        if (f.HasFollowUp == true) q = q.Where(o => o.NextFollowUpDate.HasValue);
        if (!string.IsNullOrWhiteSpace(f.SearchText)) { var s = f.SearchText.Trim(); q = q.Where(o => (o.ClientName != null && o.ClientName.Contains(s)) || (o.Phone1 != null && o.Phone1.Contains(s)) || (o.InterestedProduct != null && o.InterestedProduct.Contains(s)) || (o.EmployeeName != null && o.EmployeeName.Contains(s))); }
        return q;
    }
    private static IQueryable<SalesOpportunity> ApplyOppFilters(IQueryable<SalesOpportunity> q, OpportunityFilterDto f)
    {
        if (f.StageId.HasValue) q = q.Where(o => o.StageId == f.StageId.Value);
        if (f.EmployeeId.HasValue) q = q.Where(o => o.EmployeeId == f.EmployeeId.Value);
        if (f.SourceId.HasValue) q = q.Where(o => o.SourceId == f.SourceId.Value);
        if (f.CategoryId.HasValue) q = q.Where(o => o.CategoryId == f.CategoryId.Value);
        if (f.MinValue.HasValue) q = q.Where(o => o.ExpectedValue >= f.MinValue);
        if (f.MaxValue.HasValue) q = q.Where(o => o.ExpectedValue <= f.MaxValue);
        if (f.DateFrom.HasValue)
{
    var from = f.DateFrom.Value.Date;
    q = q.Where(o => o.CreatedAt >= from);
}

if (f.DateTo.HasValue)
{
    var to = f.DateTo.Value.Date.AddDays(1).AddTicks(-1);
    q = q.Where(o => o.CreatedAt <= to);
}
        if (f.IsOverdueFollowUp == true) q = q.Where(o => o.NextFollowUpDate.HasValue && o.NextFollowUpDate.Value < DateTime.Today);
        if (f.HasFollowUp == true) q = q.Where(o => o.NextFollowUpDate.HasValue);
        return q;
    }
    private static IQueryable<VwSalesOpportunity> ApplySorting(IQueryable<VwSalesOpportunity> q, OpportunityFilterDto f) => f.SortBy switch { "ClientName" => f.SortDescending ? q.OrderByDescending(o => o.ClientName) : q.OrderBy(o => o.ClientName), "ExpectedValue" => f.SortDescending ? q.OrderByDescending(o => o.ExpectedValue ?? 0) : q.OrderBy(o => o.ExpectedValue ?? 0), "NextFollowUpDate" => f.SortDescending ? q.OrderByDescending(o => o.NextFollowUpDate ?? DateTime.MinValue) : q.OrderBy(o => o.NextFollowUpDate ?? DateTime.MinValue), _ => f.SortDescending ? q.OrderByDescending(o => o.CreatedAt) : q.OrderBy(o => o.CreatedAt) };
    private static OpportunityListDto MapToListDto(VwSalesOpportunity o) => new() { OpportunityId = o.OpportunityId, PartyId = o.PartyId, ClientName = o.ClientName ?? "", Phone = o.Phone1, Phone2 = o.Phone2, Address = o.Address, EmployeeId = o.EmployeeId, EmployeeName = o.EmployeeName, SourceId = o.SourceId, SourceName = o.SourceName, SourceIcon = o.SourceIcon, StageId = o.StageId, StageName = o.StageName ?? "", StageNameAr = o.StageNameAr, StageColor = o.StageColor, StageOrder = o.StageOrder ?? 0, StatusId = o.StatusId, StatusName = o.StatusName, CategoryId = o.CategoryId, CategoryName = o.CategoryName, InterestedProduct = o.InterestedProduct, ExpectedValue = o.ExpectedValue, Location = o.Location, FirstContactDate = o.FirstContactDate, NextFollowUpDate = o.NextFollowUpDate, LastContactDate = o.LastContactDate, LostReasonId = o.LostReasonId, LostReasonName = o.LostReasonName, LostNotes = o.LostNotes, Notes = o.Notes, Guidance = o.Guidance, QuotationId = o.QuotationId, TransactionId = o.TransactionId, IsActive = o.IsActive, CreatedBy = o.CreatedBy, CreatedAt = o.CreatedAt, DaysSinceFirstContact = o.DaysSinceFirstContact ?? 0, LifecycleDays = 0, FollowUpStatus = o.FollowUpStatus ?? "" };
    private async Task EnrichCounts(List<OpportunityListDto> items)
    {
        var oppIds = items.Select(i => i.OpportunityId).ToList(); if (!oppIds.Any()) return;
        var ic = (await _db.CustomerInteractions.AsNoTracking().Where(ci => oppIds.Contains(ci.OpportunityId)).GroupBy(ci => ci.OpportunityId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync()).ToDictionary(x => x.Key, x => x.Count);
        var tc = (await _db.CrmTasks.AsNoTracking().Where(t => t.OpportunityId != null && oppIds.Contains(t.OpportunityId.Value)).GroupBy(t => t.OpportunityId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync()).ToDictionary(x => x.Key, x => x.Count);
        foreach (var item in items) { item.InteractionsCount = ic.TryGetValue(item.OpportunityId, out var a) ? a : 0; item.TasksCount = tc.TryGetValue(item.OpportunityId, out var b) ? b : 0; }
    }

    private async Task EnrichLifecycleDataAsync(List<OpportunityListDto> items)
    {
        var oppIds = items.Select(i => i.OpportunityId).Distinct().ToList();
        if (!oppIds.Any()) return;

        var closureMap = await _db.SalesOpportunities.AsNoTracking()
            .Where(o => oppIds.Contains(o.OpportunityId))
            .Select(o => new { o.OpportunityId, o.ClosedAt, o.ClosedBy })
            .ToDictionaryAsync(x => x.OpportunityId, x => new { x.ClosedAt, x.ClosedBy });

        foreach (var item in items)
        {
            if (closureMap.TryGetValue(item.OpportunityId, out var closure))
            {
                item.ClosedAt = closure.ClosedAt;
                item.ClosedBy = closure.ClosedBy;
            }

            item.LifecycleDays = CalculateLifecycleDays(item.CreatedAt, item.ClosedAt);
        }
    }

        public async Task<List<ContactStatus>> GetContactStatusesAsync()
    {
        return await _db.ContactStatuses
            .Where(s => s.IsActive)
            .OrderBy(s => s.StatusNameAr)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<List<TaskType>> GetTaskTypesAsync()
    {
        return await _db.TaskTypes
            .Where(t => t.IsActive)
            .OrderBy(t => t.TaskTypeNameAr)
            .AsNoTracking()
            .ToListAsync();
    }
        // ════════════════════ WORKFLOW — NEW ════════════════════

    public async Task<OpportunityWorkflowDto?> GetActiveOpportunityByPartyAsync(int partyId)
{
    var opp = await _db.SalesOpportunities
        .AsNoTracking()
        .Where(o => o.PartyId == partyId && o.IsActive)
        .Where(o => !LostKeywords.Any(k =>
            (o.Stage.StageNameAr ?? "").Contains(k) ||
            (o.Stage.StageName ?? "").Contains(k)))
        .OrderByDescending(o => o.CreatedAt)
        .FirstOrDefaultAsync();

    if (opp == null) return null;

    return new OpportunityWorkflowDto
    {
        OpportunityId = opp.OpportunityId,
        EmployeeId = opp.EmployeeId,
        SourceId = opp.SourceId,
        AdTypeId = opp.AdTypeId,
        StageId = opp.StageId,
        StatusId = opp.StatusId,
        CategoryId = opp.CategoryId,
        InterestedProduct = opp.InterestedProduct,
        FirstContactDate = opp.FirstContactDate,
        NextFollowUpDate = opp.NextFollowUpDate,
        LostReasonId = opp.LostReasonId,
        LostNotes = opp.LostNotes,
        StageBeforeId = opp.StageId,
        HasActiveOpportunity = true
    };
}

    public async Task<List<OpportunityLookupDto>> GetOpportunitiesByPartyAsync(int partyId)
    {
        if (partyId <= 0) return new();

        return await _db.SalesOpportunities
            .AsNoTracking()
            .Where(o => o.PartyId == partyId && o.IsActive)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OpportunityLookupDto
            {
                OpportunityId = o.OpportunityId,
                ClientName = o.Party.PartyName ?? "",
                StageNameAr = o.Stage.StageNameAr ?? o.Stage.StageName,
                StageColor = o.Stage.StageColor,
                ExpectedValue = o.ExpectedValue,
                CreatedAt = o.CreatedAt,
                EmployeeId = o.EmployeeId,
                EmployeeName = o.Employee != null ? o.Employee.FullName : null,
                QuotationId = o.QuotationId,
                TransactionId = o.TransactionId,
                IsActive = o.IsActive
            })
            .ToListAsync();
    }

    public async Task<PartySearchDto?> GetPartyByIdAsync(int partyId)
    {
        if (partyId <= 0) return null;

        var party = await _db.Parties
            .AsNoTracking()
            .Where(p => p.PartyId == partyId && p.IsActive == true)
            .Select(p => new PartySearchDto
            {
                PartyId = p.PartyId,
                PartyName = p.PartyName ?? "",
                Phone = p.Phone,
                Phone2 = p.Phone2
            })
            .FirstOrDefaultAsync();

        if (party == null) return null;

        var lastOpp = await _db.SalesOpportunities
            .AsNoTracking()
            .Where(o => o.PartyId == partyId && o.IsActive)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new { StageName = o.Stage.StageNameAr, o.LastContactDate })
            .FirstOrDefaultAsync();

        if (lastOpp != null)
        {
            party.LastStageName = lastOpp.StageName;
            party.LastContactDate = lastOpp.LastContactDate;
        }

        return party;
    }

    public async Task<List<PartySearchDto>> SearchPartiesAsync(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText) || searchText.Trim().Length < 2)
            return new List<PartySearchDto>();

        var search = searchText.Trim();

        // Step 1: Basic DB search (name OR phone contains)
        var candidates = await _db.Parties
            .AsNoTracking()
            .Where(p => p.IsActive == true)
            .Where(p =>
                (p.PartyName != null && p.PartyName.Contains(search)) ||
                (p.Phone != null && p.Phone.Contains(search)) ||
                (p.Phone2 != null && p.Phone2.Contains(search)))
            .OrderBy(p => p.PartyName)
            .Take(30)
            .Select(p => new PartySearchDto
            {
                PartyId = p.PartyId,
                PartyName = p.PartyName ?? "",
                Phone = p.Phone,
                Phone2 = p.Phone2
            })
            .ToListAsync();

        if (!candidates.Any()) return candidates;

        // Step 2: Arabic smart filter in memory
        var normalized = NormalizeArabic(search);
        var filtered = candidates
            .Where(p =>
                NormalizeArabic(p.PartyName).Contains(normalized) ||
                NormalizeArabic(p.Phone).Contains(normalized) ||
                NormalizeArabic(p.Phone2).Contains(normalized))
            .ToList();

        // Step 3: Enrich with last stage info
        var partyIds = filtered.Select(p => p.PartyId).ToList();
        var lastOpps = await _db.SalesOpportunities
            .AsNoTracking()
            .Where(o => partyIds.Contains(o.PartyId) && o.IsActive)
            .GroupBy(o => o.PartyId)
            .Select(g => new { g.Key, StageName = g.OrderByDescending(o => o.CreatedAt).FirstOrDefault().Stage.StageNameAr, LastContact = g.OrderByDescending(o => o.CreatedAt).FirstOrDefault().LastContactDate })
            .ToDictionaryAsync(x => x.Key, x => new { Stage = x.StageName, LastContact = x.LastContact });

        foreach (var p in filtered)
        {
            if (lastOpps.TryGetValue(p.PartyId, out var info))
            {
                p.LastStageName = info.Stage;
                p.LastContactDate = info.LastContact;
            }
        }

        return filtered.Take(20).ToList();
    }

    public async Task<bool> CheckPhoneExistsAsync(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return false;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 8) return false;

        return await _db.Parties
            .AsNoTracking()
            .AnyAsync(p =>
                (p.Phone != null && p.Phone.Contains(digits)) ||
                (p.Phone2 != null && p.Phone2.Contains(digits)));
    }

    public async Task<(bool Success, string Message, int OpportunityId)> SaveWorkflowAsync(
        OpportunityWorkflowDto dto, string userName)
    {
        if (!dto.SourceId.HasValue)
            return (false, "برجاء تحديد طريقة / مصدر التواصل أولاً", 0);

        var targetStageId = dto.StageId ?? 1;
        var requiresClosureApproval = IsExitStageId(targetStageId) && !CanDirectCloseOpportunities();

        if (!IsClosedStageId(targetStageId) && !requiresClosureApproval)
        {
            if (!dto.TaskTypeId.HasValue)
                return (false, "برجاء اختيار نوع مهمة المتابعة القادمة (اتصال، اجتماع، إلخ)", 0);
            if (!dto.NextFollowUpDate.HasValue)
                return (false, "تاريخ المتابعة القادم إجباري لهذه المرحلة", 0);
        }

        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var now = DateTime.Now;
            var partyId = 0;

            // ═══ 1. حفظ العميل الجديد ═══
            if (dto.IsNewClient)
            {
                if (string.IsNullOrWhiteSpace(dto.NewClientName))
                    return (false, "برجاء إدخال اسم العميل", 0);
                if (string.IsNullOrWhiteSpace(dto.NewPhone))
                    return (false, "برجاء إدخال رقم الهاتف", 0);

                var newParty = new Party
                {
                    PartyName = dto.NewClientName.Trim(),
                    Phone = dto.NewPhone.Trim(),
                    Address = dto.NewAddress?.Trim(),
                    PartyType = 1,
                    IsActive = true,
                    ReferralSourceId = 2,
                    CreatedBy = userName,
                    CreatedAt = now
                };
                _db.Parties.Add(newParty);
                await _db.SaveChangesAsync();
                partyId = newParty.PartyId;
            }
            else
            {
                if (!dto.ExistingPartyId.HasValue)
                    return (false, "برجاء اختيار العميل", 0);
                partyId = dto.ExistingPartyId.Value;
            }

            // ═══ 2. إنشاء أو تحديث فرصة البيع ═══
            int opportunityId;
            int stageBefore = dto.StageBeforeId;

            if (!dto.OpportunityId.HasValue || dto.OpportunityId == 0)
            {
                // إنشاء فرصة جديدة
                var newOpp = new SalesOpportunity
                {
                    PartyId = partyId,
                    EmployeeId = dto.EmployeeId,
                    SourceId = dto.SourceId,
                    AdTypeId = dto.AdTypeId,
                    StageId = requiresClosureApproval ? 1 : dto.StageId ?? 1,
                    StatusId = dto.StatusId,
                    CategoryId = dto.CategoryId,
                    InterestedProduct = dto.InterestedProduct,
                    FirstContactDate = dto.FirstContactDate ?? now,
                    NextFollowUpDate = dto.NextFollowUpDate,
                    ExpectedValue = dto.ExpectedValue,
                    Location = dto.Location,
                    LostReasonId = requiresClosureApproval ? null : dto.LostReasonId,
                    LostNotes = requiresClosureApproval ? null : dto.LostNotes,
                    Notes = dto.Summary,
                    Guidance = dto.Guidance,
                    IsActive = true,
                    CreatedBy = userName,
                    CreatedAt = now
                };
                ApplyClosureState(newOpp, 0, newOpp.StageId, userName, now);
                _db.SalesOpportunities.Add(newOpp);
                await _db.SaveChangesAsync();
                opportunityId = newOpp.OpportunityId;
                stageBefore = 0;
            }
            else
            {
                // تحديث فرصة موجودة
                var opp = await _db.SalesOpportunities.FindAsync(dto.OpportunityId.Value);
                if (opp == null) return (false, "الفرصة غير موجودة", 0);

                stageBefore = opp.StageId;
                var oldEmployeeId = opp.EmployeeId;

                if (dto.StageId.HasValue && !requiresClosureApproval) opp.StageId = dto.StageId.Value;
                opp.StatusId = dto.StatusId ?? opp.StatusId;
                opp.ExpectedValue = dto.ExpectedValue ?? opp.ExpectedValue;
                if (!requiresClosureApproval)
                {
                    opp.LostReasonId = dto.LostReasonId;
                    opp.LostNotes = dto.LostNotes;
                }
                opp.CategoryId = dto.CategoryId ?? opp.CategoryId;
                opp.InterestedProduct = dto.InterestedProduct ?? opp.InterestedProduct;
                opp.NextFollowUpDate = dto.NextFollowUpDate;
                opp.LastContactDate = now;
                opp.Notes = dto.Summary;
                opp.Guidance = dto.Guidance;
                opp.LastUpdatedBy = userName;
                opp.LastUpdatedAt = now;
                opp.EmployeeId = dto.EmployeeId ?? opp.EmployeeId;
                ApplyClosureState(opp, stageBefore, opp.StageId, userName, now);

                await _db.SaveChangesAsync();
                opportunityId = opp.OpportunityId;

                if (oldEmployeeId != opp.EmployeeId && opp.EmployeeId.HasValue)
                {
                    await AddOpportunityReassignmentInteractionAsync(
                        opportunityId,
                        partyId,
                        oldEmployeeId,
                        opp.EmployeeId.Value,
                        opp.SourceId,
                        opp.StatusId,
                        opp.NextFollowUpDate,
                        userName,
                        null);

                    await NotifyOpportunityReassignedAsync(opportunityId, partyId, oldEmployeeId, opp.EmployeeId.Value, userName, null);
                }
            }

            if (requiresClosureApproval)
            {
                var currentStageForRequest = stageBefore == 0
                    ? await _db.SalesOpportunities.AsNoTracking()
                        .Where(o => o.OpportunityId == opportunityId)
                        .Select(o => o.StageId)
                        .FirstAsync()
                    : stageBefore;

                var partyForApproval = await _db.Parties.FindAsync(partyId);
                if (partyForApproval != null)
                    partyForApproval.LastContactDate = now;

                await _db.SaveChangesAsync();

                var requestResult = await CreateClosureApprovalRequestInternalAsync(
                    opportunityId,
                    partyId,
                    currentStageForRequest,
                    targetStageId,
                    dto.LostReasonId,
                    dto.LostNotes ?? dto.Summary,
                    "OpportunityForm",
                    userName);

                if (!requestResult.Success || requestResult.Request == null)
                {
                    await transaction.RollbackAsync();
                    return (false, requestResult.Message, opportunityId);
                }

                await transaction.CommitAsync();

                if (requestResult.IsNewRequest)
                {
                    await NotifyClosureApprovalRequestedAsync(requestResult.Request, requestResult.ClientName, requestResult.RequestedStageName, userName);
                }

                return (true, requestResult.Message, opportunityId);
            }

            // ═══ 3. إضافة سجل التواصل (فقط للإضافات الجديدة، أو عند تغيير المرحلة/الملخص) ═══
if (stageBefore == 0 || dto.StageId != (stageBefore == 0 ? null : stageBefore) || !string.IsNullOrWhiteSpace(dto.Summary))
{
    var interaction = new CustomerInteraction
    {
        OpportunityId = opportunityId,
        PartyId = partyId,
        EmployeeId = dto.EmployeeId,
        SourceId = dto.SourceId,
        StatusId = dto.StatusId,
        InteractionDate = now,
        Summary = dto.Summary,
        StageBeforeId = stageBefore == 0 ? (int?)null : stageBefore,
        StageAfterId = dto.StageId,
        NextFollowUpDate = dto.NextFollowUpDate,
        Notes = dto.Guidance,
        CreatedBy = userName,
        CreatedAt = now
    };
    _db.CustomerInteractions.Add(interaction);
}

            // ═══ 4. تحديث آخر تواصل للعميل ═══
            var party = await _db.Parties.FindAsync(partyId);
            if (party != null) party.LastContactDate = now;

            await _db.SaveChangesAsync();

            // ═══ 5. إدارة المهام ═══
            var stageId = dto.StageId ?? 0;

            if (IsExitStageId(stageId))
            {
                // Lost / Not Interested → إلغاء كل المهام
                var reasonText = stageId == 4 ? "Lost" : "Not Interested";
                var tasks = await _db.CrmTasks
                    .Where(t => t.OpportunityId == opportunityId
                             && (t.Status == "Pending" || t.Status == "In Progress"))
                    .ToListAsync();
                foreach (var t in tasks)
                {
                    t.Status = "Completed";
                    t.CompletedDate = now;
                    t.CompletedBy = userName;
                    t.CompletionNotes = $"تم الإلغاء تلقائياً — العميل {reasonText}";
                }

                // ═══ تحديث حالة الـ Lead المرتبط إلى "مرفوض" ═══
                var lostLead = await _db.LeadsCRMs
                    .FirstOrDefaultAsync(l => l.ConvertedOpportunityId == opportunityId);
                if (lostLead != null && lostLead.LeadStatus == "محول")
                {
                    var oldLeadStatus = lostLead.LeadStatus;
                    lostLead.LeadStatus = "مرفوض";
                    lostLead.RejectedReason = $"الفرصة المرتبطة أصبحت خسارة — {reasonText}";

                    _db.LeadInteractions.Add(new LeadInteraction
                    {
                        LeadId = lostLead.LeadId,
                        EmployeeId = lostLead.AssignedEmployeeId,
                        InteractionType = "رفض",
                        InteractionDate = now,
                        Summary = $"تم تحديث حالة الـ Lead تلقائياً — الفرصة #{opportunityId} أصبحت خسارة",
                        Notes = lostLead.RejectedReason,
                        OldLeadStatus = oldLeadStatus,
                        NewLeadStatus = "مرفوض",
                        IsSystemGenerated = true,
                        CreatedBy = userName,
                        CreatedAt = now
                    });
                }
            }
            else if (stageId == 3)
            {
                // Won → إغلاق كل المهام
                var tasks = await _db.CrmTasks
                    .Where(t => t.OpportunityId == opportunityId
                             && (t.Status == "Pending" || t.Status == "In Progress"))
                    .ToListAsync();
                foreach (var t in tasks)
                {
                    t.Status = "Completed";
                    t.CompletedDate = now;
                    t.CompletedBy = userName;
                    t.CompletionNotes = "تم الإغلاق تلقائياً — تم البيع بنجاح";
                }
            }

            // ═══ إرجاع حالة الـ Lead لـ "محول" لو الفرصة رجعت من خسارة لمرحلة نشطة ═══
            if (stageId != 4 && stageId != 5 && stageBefore != stageId)
            {
                var revivedLead = await _db.LeadsCRMs
                    .FirstOrDefaultAsync(l => l.ConvertedOpportunityId == opportunityId);
                if (revivedLead != null && revivedLead.LeadStatus == "مرفوض"
                    && revivedLead.RejectedReason != null
                    && revivedLead.RejectedReason.Contains("خسارة"))
                {
                    var oldLeadStatus = revivedLead.LeadStatus;
                    revivedLead.LeadStatus = "محول";
                    revivedLead.RejectedReason = null;

                    _db.LeadInteractions.Add(new LeadInteraction
                    {
                        LeadId = revivedLead.LeadId,
                        EmployeeId = revivedLead.AssignedEmployeeId,
                        InteractionType = "متابعة",
                        InteractionDate = now,
                        Summary = $"تم إرجاع حالة الـ Lead تلقائياً — الفرصة #{opportunityId} رجعت من خسارة لمرحلة نشطة",
                        OldLeadStatus = oldLeadStatus,
                        NewLeadStatus = "محول",
                        IsSystemGenerated = true,
                        CreatedBy = userName,
                        CreatedAt = now
                    });
                }
            }

            if (dto.NextFollowUpDate.HasValue)
            {
                // مرحلة عادية + متابعة → إغلاق القديمة + إنشاء جديدة

                // إغلاق القديمة
                var oldTasks = await _db.CrmTasks
                    .Where(t => t.OpportunityId == opportunityId
                             && (t.Status == "Pending" || t.Status == "In Progress"))
                    .ToListAsync();
                foreach (var t in oldTasks)
                {
                    t.Status = "Completed";
                    t.CompletedDate = now;
                    t.CompletedBy = userName;
                    t.CompletionNotes = "تم المتابعة وجدولة موعد جديد";
                }

                // إنشاء مهمة جديدة
                if (dto.NextFollowUpDate.Value >= DateTime.Today)
                {
                    var newTask = new CrmTask
                    {
                        OpportunityId = opportunityId,
                        PartyId = partyId,
                        AssignedTo = dto.EmployeeId ?? 0,
                        TaskTypeId = dto.TaskTypeId,
                        TaskDescription = dto.Guidance ?? "متابعة",
                        DueDate = dto.NextFollowUpDate.Value,
                        Priority = (dto.Priority == "Medium" ? "Normal" : dto.Priority) ?? "Normal",
                        Status = "Pending",
                        ReminderEnabled = true,
                        IsActive = true,
                        CreatedBy = userName,
                        CreatedAt = now
                    };
                    _db.CrmTasks.Add(newTask);
                }
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return (true, "تم الحفظ بنجاح", opportunityId);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "SaveWorkflowAsync failed");
            return (false, $"خطأ: {ex.InnerException?.Message ?? ex.Message}", 0);
        }
    }

    private async Task AddOpportunityReassignmentInteractionAsync(
        int opportunityId,
        int partyId,
        int? oldEmployeeId,
        int newEmployeeId,
        int? sourceId,
        int? statusId,
        DateTime? nextFollowUpDate,
        string actor,
        string? reassignmentComment)
    {
        var employeeIds = new List<int>();
        if (oldEmployeeId.HasValue) employeeIds.Add(oldEmployeeId.Value);
        employeeIds.Add(newEmployeeId);

        var employeeNames = await _db.Employees
            .AsNoTracking()
            .Where(e => employeeIds.Contains(e.EmployeeId))
            .Select(e => new { e.EmployeeId, e.FullName })
            .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName ?? "غير محدد");

        var oldEmployeeName = oldEmployeeId.HasValue && employeeNames.TryGetValue(oldEmployeeId.Value, out var oldName)
            ? oldName
            : "غير محدد";

        var newEmployeeName = employeeNames.TryGetValue(newEmployeeId, out var newName)
            ? newName
            : "غير محدد";

        _db.CustomerInteractions.Add(new CustomerInteraction
        {
            OpportunityId = opportunityId,
            PartyId = partyId,
            EmployeeId = newEmployeeId,
            SourceId = sourceId,
            StatusId = statusId,
            InteractionDate = DateTime.Now,
            Summary = $"تم تحويل الفرصة من {oldEmployeeName} إلى {newEmployeeName} بواسطة {actor}",
            StageBeforeId = null,
            StageAfterId = null,
            NextFollowUpDate = nextFollowUpDate,
            Notes = string.IsNullOrWhiteSpace(reassignmentComment) ? "إعادة إسناد داخلي" : reassignmentComment.Trim(),
            CreatedBy = actor,
            CreatedAt = DateTime.Now
        });

        await _db.SaveChangesAsync();
    }

    private async Task NotifyOpportunityReassignedAsync(int opportunityId, int partyId, int? oldEmployeeId, int newEmployeeId, string actor, string? reassignmentComment)
    {
        try
        {
            var newUser = await _db.Users
                .AsNoTracking()
                .Where(u => u.EmployeeId == newEmployeeId && u.IsActive == true)
                .Select(u => new { u.Username, u.FullName })
                .FirstOrDefaultAsync();

            if (newUser == null || string.IsNullOrWhiteSpace(newUser.Username))
            {
                _logger.LogWarning(
                    "Opportunity {OpportunityId} reassigned to Employee {EmployeeId}, but no active user is linked to this employee.",
                    opportunityId,
                    newEmployeeId);
                return;
            }

            var employeeIds = new List<int>();
            if (oldEmployeeId.HasValue) employeeIds.Add(oldEmployeeId.Value);
            employeeIds.Add(newEmployeeId);

            var employeeNames = await _db.Employees
                .AsNoTracking()
                .Where(e => employeeIds.Contains(e.EmployeeId))
                .Select(e => new { e.EmployeeId, e.FullName })
                .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName ?? "غير محدد");

            var oldEmployeeName = oldEmployeeId.HasValue && employeeNames.TryGetValue(oldEmployeeId.Value, out var oldName)
                ? oldName
                : "غير محدد";

            var partyName = await _db.Parties
                .AsNoTracking()
                .Where(p => p.PartyId == partyId)
                .Select(p => p.PartyName)
                .FirstOrDefaultAsync() ?? $"عميل #{partyId}";

            var title = "🎯 تم تحويل فرصة إليك";
            var message = $"تم تغيير الفرصة #{opportunityId} الخاصة بالعميل {partyName} من {oldEmployeeName} إليك بواسطة {actor}.";
            if (!string.IsNullOrWhiteSpace(reassignmentComment))
                message += $"\nتعليمات / ملاحظة: {reassignmentComment.Trim()}";
            if (!string.IsNullOrWhiteSpace(reassignmentComment))
                message += $"\nتعليمات / ملاحظة: {reassignmentComment.Trim()}";

            await _notify.AddAsync(
                title: title,
                message: message,
                recipientUser: newUser.Username,
                createdBy: actor,
                formName: "crm/opportunities",
                relatedTable: "SalesOpportunities",
                relatedId: opportunityId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to send opportunity reassignment notification. OpportunityId={OpportunityId}, NewEmployeeId={EmployeeId}",
                opportunityId,
                newEmployeeId);
        }
    }

    private async Task<(bool Success, string Message, OpportunityClosureApprovalRequest? Request, string ClientName, string RequestedStageName, bool IsNewRequest)> CreateClosureApprovalRequestInternalAsync(
        int opportunityId,
        int partyId,
        int currentStageId,
        int requestedStageId,
        int? lostReasonId,
        string? requestReasonNotes,
        string? requestSource,
        string userName)
    {
        if (opportunityId <= 0)
            return (false, "الفرصة غير موجودة.", null, string.Empty, string.Empty, false);

        if (!IsExitStageId(requestedStageId))
            return (false, "طلبات الموافقة متاحة فقط لخسارة أو غير مهتم.", null, string.Empty, string.Empty, false);

        if (!lostReasonId.HasValue)
            return (false, "سبب الإغلاق مطلوب قبل إرسال طلب الموافقة.", null, string.Empty, string.Empty, false);

        if (string.IsNullOrWhiteSpace(requestReasonNotes))
            return (false, "ملاحظات الإغلاق مطلوبة قبل إرسال طلب الموافقة.", null, string.Empty, string.Empty, false);

        var opp = await _db.SalesOpportunities.AsNoTracking()
            .Where(o => o.OpportunityId == opportunityId)
            .Select(o => new { o.OpportunityId, o.PartyId, o.StageId, ClientName = o.Party.PartyName })
            .FirstOrDefaultAsync();

        if (opp == null)
            return (false, "الفرصة غير موجودة.", null, string.Empty, string.Empty, false);

        var requestedStageName = await _db.SalesStages.AsNoTracking()
            .Where(s => s.StageId == requestedStageId)
            .Select(s => s.StageNameAr ?? s.StageName)
            .FirstOrDefaultAsync() ?? "الحالة المطلوبة";

        var existingPending = await _db.OpportunityClosureApprovalRequests
            .FirstOrDefaultAsync(r => r.OpportunityId == opportunityId && r.Status == OpportunityClosureApprovalStatuses.Pending);

        if (existingPending != null)
        {
            var existingRequestedStageName = await _db.SalesStages.AsNoTracking()
                .Where(s => s.StageId == existingPending.RequestedStageId)
                .Select(s => s.StageNameAr ?? s.StageName)
                .FirstOrDefaultAsync() ?? requestedStageName;

            return (true, $"يوجد بالفعل طلب موافقة معلق لتحويل الفرصة إلى {existingRequestedStageName}.", existingPending, opp.ClientName ?? $"عميل #{opp.PartyId}", existingRequestedStageName, false);
        }

        var entity = new OpportunityClosureApprovalRequest
        {
            OpportunityId = opportunityId,
            PartyId = partyId > 0 ? partyId : opp.PartyId,
            CurrentStageId = currentStageId > 0 ? currentStageId : opp.StageId,
            RequestedStageId = requestedStageId,
            LostReasonId = lostReasonId,
            RequestReasonNotes = requestReasonNotes.Trim(),
            RequestSource = string.IsNullOrWhiteSpace(requestSource) ? null : requestSource.Trim(),
            Status = OpportunityClosureApprovalStatuses.Pending,
            RequestedBy = userName,
            RequestedAt = DateTime.Now
        };

        _db.OpportunityClosureApprovalRequests.Add(entity);

        _db.CustomerInteractions.Add(new CustomerInteraction
        {
            OpportunityId = opportunityId,
            PartyId = partyId > 0 ? partyId : opp.PartyId,
            EmployeeId = null,
            SourceId = null,
            StatusId = null,
            InteractionDate = DateTime.Now,
            Summary = $"تم إرسال طلب موافقة لتحويل الفرصة إلى {requestedStageName} بواسطة {userName}",
            StageBeforeId = null,
            StageAfterId = null,
            NextFollowUpDate = null,
            Notes = requestReasonNotes.Trim(),
            CreatedBy = userName,
            CreatedAt = DateTime.Now
        });

        await _db.SaveChangesAsync();

        return (true, $"تم إرسال طلب موافقة إلى مدير المبيعات لتحويل الفرصة إلى {requestedStageName}.", entity, opp.ClientName ?? $"عميل #{opp.PartyId}", requestedStageName, true);
    }

    private async Task NotifyClosureApprovalRequestedAsync(OpportunityClosureApprovalRequest request, string clientName, string requestedStageName, string actor)
    {
        try
        {
            var message = $"طلب {actor} تحويل الفرصة #{request.OpportunityId} الخاصة بالعميل {clientName} إلى {requestedStageName}.";
            if (!string.IsNullOrWhiteSpace(request.RequestReasonNotes))
                message += $"\nالسبب: {request.RequestReasonNotes}";

            await _notify.NotifyRoleAsync(
                title: "🛑 طلب موافقة إغلاق فرصة",
                message: message,
                role: SystemRoles.SalesManager,
                createdBy: actor,
                formName: "crm/opportunities/closure-requests",
                relatedTable: "OpportunityClosureApprovalRequests",
                relatedId: request.RequestId);

            await _notify.NotifyRoleAsync(
                title: "🛑 طلب موافقة إغلاق فرصة",
                message: message,
                role: SystemRoles.GeneralManager,
                createdBy: actor,
                formName: "crm/opportunities/closure-requests",
                relatedTable: "OpportunityClosureApprovalRequests",
                relatedId: request.RequestId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify sales manager about closure approval request. OpportunityId={OpportunityId}", request.OpportunityId);
        }
    }

    private async Task NotifyClosureApprovalDecisionAsync(string recipientUser, int opportunityId, string clientName, string requestedStageName, bool approved, string reviewer, string? reviewNotes)
    {
        try
        {
            var title = approved ? "✅ تم اعتماد طلب إغلاق الفرصة" : "❌ تم رفض طلب إغلاق الفرصة";
            var message = approved
                ? $"وافق {reviewer} على تحويل الفرصة #{opportunityId} الخاصة بالعميل {clientName} إلى {requestedStageName}. برجاء فتح الفرصة واستكمال الإغلاق بنفسك."
                : $"رفض {reviewer} طلب تحويل الفرصة #{opportunityId} الخاصة بالعميل {clientName} إلى {requestedStageName}.";

            if (!string.IsNullOrWhiteSpace(reviewNotes))
                message += $"\nملاحظات المراجعة: {reviewNotes}";

            await _notify.AddAsync(
                title: title,
                message: message,
                recipientUser: recipientUser,
                createdBy: reviewer,
                formName: "crm/opportunities",
                relatedTable: "SalesOpportunities",
                relatedId: opportunityId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify requester about closure approval decision. OpportunityId={OpportunityId}", opportunityId);
        }
    }

    private async Task CloseTasksForClosedOpportunityAsync(int opportunityId, int stageId, string userName, DateTime now)
    {
        var tasks = await _db.CrmTasks
            .Where(t => t.OpportunityId == opportunityId && (t.Status == "Pending" || t.Status == "In Progress"))
            .ToListAsync();

        if (!tasks.Any())
            return;

        var note = stageId == 4
            ? "تم الإغلاق بعد موافقة مدير المبيعات — خسارة"
            : "تم الإغلاق بعد موافقة مدير المبيعات — غير مهتم";

        foreach (var task in tasks)
        {
            task.Status = "Completed";
            task.CompletedDate = now;
            task.CompletedBy = userName;
            task.CompletionNotes = note;
        }
    }

    private async Task SyncLeadStatusForExitStageAsync(int opportunityId, int stageId, string userName, DateTime now)
    {
        var linkedLead = await _db.LeadsCRMs
            .FirstOrDefaultAsync(l => l.ConvertedOpportunityId == opportunityId);

        if (linkedLead == null || linkedLead.LeadStatus != "محول")
            return;

        var reasonText = stageId == 4 ? "خسارة" : "غير مهتم";
        var oldLeadStatus = linkedLead.LeadStatus;

        linkedLead.LeadStatus = "مرفوض";
        linkedLead.RejectedReason = $"الفرصة المرتبطة أصبحت {reasonText} بعد موافقة مدير المبيعات";

        _db.LeadInteractions.Add(new LeadInteraction
        {
            LeadId = linkedLead.LeadId,
            EmployeeId = linkedLead.AssignedEmployeeId,
            InteractionType = "رفض",
            InteractionDate = now,
            Summary = $"تم تحديث حالة الـ Lead تلقائياً — الفرصة #{opportunityId} أصبحت {reasonText} بعد موافقة مدير المبيعات",
            Notes = linkedLead.RejectedReason,
            OldLeadStatus = oldLeadStatus,
            NewLeadStatus = "مرفوض",
            IsSystemGenerated = true,
            CreatedBy = userName,
            CreatedAt = now
        });
    }

    private bool CanDirectCloseOpportunities()
    {
        var user = _http.HttpContext?.User;
        if (user == null) return false;
        return user.IsInRole("Admin")
            || user.IsInRole(SystemRoles.SalesManager)
            || user.IsInRole(SystemRoles.GeneralManager);
    }

    private bool CanApproveClosureRequests() => CanDirectCloseOpportunities();

    private static bool IsExitStageId(int stageId) => stageId == 4 || stageId == 5;
    private static bool IsClosedStageId(int stageId) => stageId == 3 || stageId == 4 || stageId == 5;

    private static int CalculateLifecycleDays(DateTime createdAt, DateTime? closedAt)
    {
        var endDate = (closedAt ?? DateTime.Today).Date;
        return Math.Max(0, (endDate - createdAt.Date).Days);
    }

    private static void ApplyClosureState(SalesOpportunity opp, int oldStageId, int newStageId, string userName, DateTime now)
    {
        var wasClosed = IsClosedStageId(oldStageId);
        var isClosed = IsClosedStageId(newStageId);

        if (!wasClosed && isClosed)
        {
            opp.ClosedAt = now;
            opp.ClosedBy = userName;
            return;
        }

        if (wasClosed && !isClosed)
        {
            opp.ClosedAt = null;
            opp.ClosedBy = null;
        }
    }

    // ═══ Arabic Normalization Helper ═══
    private static string NormalizeArabic(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var s = input;
        var diacritics = "\u064B\u064C\u064D\u064E\u064F\u0650\u0651\u0652\u0653\u0654\u0655\u0656";
        s = new string(s.Where(c => !diacritics.Contains(c)).ToArray());
        s = s.Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ٱ', 'ا');
        s = s.Replace('ة', 'ه');
        s = s.Replace('ى', 'ي');
        s = s.Replace('\u0640', ' ');
        return s.ToLower().Trim();
    }
}
