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

    private static readonly HashSet<string> WonKeywords  = new() { "تم البيع", "بيع", "Closed Deal" };
    private static readonly HashSet<string> LostKeywords = new() { "خسارة", "Lost", "غير مهتم", "Not Interested" };

    public OpportunityService(db24804Context db, IHttpContextAccessor http, ILogger<OpportunityService> logger)
    { _db = db; _http = http; _logger = logger; }

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
        var opps = await query.Select(o => new { o.OpportunityId, o.PartyId, o.StageId, o.ExpectedValue, o.EmployeeId, o.NextFollowUpDate, o.InterestedProduct, o.SourceId, o.CreatedAt }).ToListAsync();
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
        return new KanbanBoardDto { Columns = stages.Select(s => { var cards = opps.Where(o => o.StageId == s.StageId).Select(o => { parties.TryGetValue(o.PartyId, out var p); emps.TryGetValue(o.EmployeeId ?? 0, out var en); srcs.TryGetValue(o.SourceId ?? 0, out var sn); return new KanbanCardDto { OpportunityId = o.OpportunityId, PartyId = o.PartyId, ClientName = p.PartyName ?? "—", Phone = p.Phone, ExpectedValue = o.ExpectedValue, EmployeeId = o.EmployeeId, EmployeeName = en, InterestedProduct = o.InterestedProduct, SourceId = o.SourceId, SourceName = sn, NextFollowUpDate = o.NextFollowUpDate, StageId = s.StageId, InteractionsCount = icDict.TryGetValue(o.OpportunityId, out var ic) ? ic : 0, TasksCount = tcDict.TryGetValue(o.OpportunityId, out var tc) ? tc : 0, CreatedAt = o.CreatedAt,IsOverdue = o.NextFollowUpDate.HasValue && o.NextFollowUpDate.Value < today }; }).ToList(); return new KanbanColumnDto { StageId = s.StageId, StageName = s.StageName ?? "", StageNameAr = s.StageNameAr ?? s.StageName ?? "", StageColor = s.StageColor ?? "#94a3b8", StageOrder = s.StageOrder, Count = cards.Count, Value = cards.Sum(c => c.ExpectedValue ?? 0), Cards = cards }; }).ToList() };
    }

    // ════════════════════ MOVE STAGE ════════════════════
    public async Task<(bool Success, string Message)> MoveStageAsync(int opportunityId, int newStageId, string userName)
{
    try
    {
        var opp = await _db.SalesOpportunities.FindAsync(opportunityId);
        if (opp == null) return (false, "الفرصة غير موجودة");
        var oldStageId = opp.StageId;
        if (oldStageId == newStageId) return (true, "لم يتغير شيء");
        var oldStage = await _db.SalesStages.FindAsync(oldStageId);
        var newStage = await _db.SalesStages.FindAsync(newStageId);
        opp.StageId = newStageId; opp.LastUpdatedBy = userName; opp.LastUpdatedAt = DateTime.Now; opp.LastContactDate = DateTime.Now;

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
        _db.CustomerInteractions.Add(new CustomerInteraction { OpportunityId = opportunityId, PartyId = opp.PartyId, StageBeforeId = oldStageId, StageAfterId = newStageId, Summary = $"نقل تلقائي: {(oldStage?.StageNameAr ?? "—")} → {(newStage?.StageNameAr ?? "—")}", InteractionDate = DateTime.Now, CreatedBy = userName, CreatedAt = DateTime.Now, NextFollowUpDate = opp.NextFollowUpDate });
        var party = await _db.Parties.FindAsync(opp.PartyId);
        if (party != null) party.LastContactDate = DateTime.Now;
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

            if (dto.StageId != 3 && dto.StageId != 4)
            {
                if (!dto.NextFollowUpDate.HasValue)
                    return (false, "تاريخ المتابعة القادم إجباري لهذه المرحلة", 0);
            }

            SalesOpportunity opp; bool isNew = dto.OpportunityId == 0;
            if (isNew) { opp = new SalesOpportunity { PartyId = dto.PartyId, CreatedBy = userName, CreatedAt = DateTime.Now, IsActive = true }; _db.SalesOpportunities.Add(opp); }
            else { opp = await _db.SalesOpportunities.FindAsync(dto.OpportunityId); if (opp == null) return (false, "الفرصة غير موجودة", 0); opp.LastUpdatedBy = userName; opp.LastUpdatedAt = DateTime.Now; }
            opp.EmployeeId = dto.EmployeeId; opp.SourceId = dto.SourceId; opp.AdTypeId = dto.AdTypeId; opp.StageId = dto.StageId; opp.StatusId = dto.StatusId; opp.CategoryId = dto.CategoryId; opp.InterestedProduct = dto.InterestedProduct; opp.ExpectedValue = dto.ExpectedValue; opp.Location = dto.Location; opp.FirstContactDate = dto.FirstContactDate; opp.NextFollowUpDate = dto.NextFollowUpDate; opp.LostReasonId = dto.LostReasonId; opp.LostNotes = dto.LostNotes; opp.Notes = dto.Notes; opp.Guidance = dto.Guidance; opp.IsActive = dto.IsActive;
                        // لو الفرصة في مرحلة رابحة والقيمة الفعلية فاضية → املأها
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
            if (dto.NextFollowUpDate.HasValue) { var party = await _db.Parties.FindAsync(dto.PartyId); if (party != null) party.LastContactDate = DateTime.Now; }
            await _db.SaveChangesAsync();
            return (true, isNew ? "تم إضافة الفرصة بنجاح" : "تم تعديل الفرصة بنجاح", opp.OpportunityId);
        }
        catch (Exception ex) { _logger.LogError(ex, "Save failed"); return (false, $"خطأ: {ex.InnerException?.Message ?? ex.Message}", 0); }
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
    public async Task<List<Employee>> GetActiveEmployeesAsync() => await _db.Employees.AsNoTracking().Where(e => e.Status == "نشط").Select(e => new Employee { EmployeeId = e.EmployeeId, FullName = e.FullName }).ToListAsync();

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
    private static OpportunityListDto MapToListDto(VwSalesOpportunity o) => new() { OpportunityId = o.OpportunityId, PartyId = o.PartyId, ClientName = o.ClientName ?? "", Phone = o.Phone1, Phone2 = o.Phone2, Address = o.Address, EmployeeId = o.EmployeeId, EmployeeName = o.EmployeeName, SourceId = o.SourceId, SourceName = o.SourceName, SourceIcon = o.SourceIcon, StageId = o.StageId, StageName = o.StageName ?? "", StageNameAr = o.StageNameAr, StageColor = o.StageColor, StageOrder = o.StageOrder ?? 0, StatusId = o.StatusId, StatusName = o.StatusName, CategoryId = o.CategoryId, CategoryName = o.CategoryName, InterestedProduct = o.InterestedProduct, ExpectedValue = o.ExpectedValue, Location = o.Location, FirstContactDate = o.FirstContactDate, NextFollowUpDate = o.NextFollowUpDate, LastContactDate = o.LastContactDate, LostReasonId = o.LostReasonId, LostReasonName = o.LostReasonName, LostNotes = o.LostNotes, Notes = o.Notes, Guidance = o.Guidance, QuotationId = o.QuotationId, TransactionId = o.TransactionId, IsActive = o.IsActive, CreatedBy = o.CreatedBy, CreatedAt = o.CreatedAt, DaysSinceFirstContact = o.DaysSinceFirstContact ?? 0, FollowUpStatus = o.FollowUpStatus ?? "" };
    private async Task EnrichCounts(List<OpportunityListDto> items)
    {
        var oppIds = items.Select(i => i.OpportunityId).ToList(); if (!oppIds.Any()) return;
        var ic = (await _db.CustomerInteractions.AsNoTracking().Where(ci => oppIds.Contains(ci.OpportunityId)).GroupBy(ci => ci.OpportunityId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync()).ToDictionary(x => x.Key, x => x.Count);
        var tc = (await _db.CrmTasks.AsNoTracking().Where(t => t.OpportunityId != null && oppIds.Contains(t.OpportunityId.Value)).GroupBy(t => t.OpportunityId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync()).ToDictionary(x => x.Key, x => x.Count);
        foreach (var item in items) { item.InteractionsCount = ic.TryGetValue(item.OpportunityId, out var a) ? a : 0; item.TasksCount = tc.TryGetValue(item.OpportunityId, out var b) ? b : 0; }
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
        if (targetStageId != 3 && targetStageId != 4)
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
                    StageId = dto.StageId ?? 1,
                    StatusId = dto.StatusId,
                    CategoryId = dto.CategoryId,
                    InterestedProduct = dto.InterestedProduct,
                    FirstContactDate = dto.FirstContactDate ?? now,
                    NextFollowUpDate = dto.NextFollowUpDate,
                    ExpectedValue = dto.ExpectedValue,
                    Location = dto.Location,
                    LostReasonId = dto.LostReasonId,
                    LostNotes = dto.LostNotes,
                    Notes = dto.Summary,
                    Guidance = dto.Guidance,
                    IsActive = true,
                    CreatedBy = userName,
                    CreatedAt = now
                };
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

                if (dto.StageId.HasValue) opp.StageId = dto.StageId.Value;
                opp.StatusId = dto.StatusId ?? opp.StatusId;
                opp.ExpectedValue = dto.ExpectedValue ?? opp.ExpectedValue;
                opp.LostReasonId = dto.LostReasonId;
                opp.LostNotes = dto.LostNotes;
                opp.CategoryId = dto.CategoryId ?? opp.CategoryId;
                opp.InterestedProduct = dto.InterestedProduct ?? opp.InterestedProduct;
                opp.NextFollowUpDate = dto.NextFollowUpDate;
                opp.LastContactDate = now;
                opp.Notes = dto.Summary;
                opp.Guidance = dto.Guidance;
                opp.LastUpdatedBy = userName;
                opp.LastUpdatedAt = now;

                await _db.SaveChangesAsync();
                opportunityId = opp.OpportunityId;
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

            if (stageId == 4 || stageId == 5)
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
            else if (dto.NextFollowUpDate.HasValue)
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