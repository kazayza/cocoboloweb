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
        var opps = await query.Select(o => new { o.OpportunityId, o.PartyId, o.StageId, o.ExpectedValue, o.EmployeeId, o.NextFollowUpDate, o.InterestedProduct, o.SourceId }).ToListAsync();
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
        return new KanbanBoardDto { Columns = stages.Select(s => { var cards = opps.Where(o => o.StageId == s.StageId).Select(o => { parties.TryGetValue(o.PartyId, out var p); emps.TryGetValue(o.EmployeeId ?? 0, out var en); srcs.TryGetValue(o.SourceId ?? 0, out var sn); return new KanbanCardDto { OpportunityId = o.OpportunityId, PartyId = o.PartyId, ClientName = p.PartyName ?? "—", Phone = p.Phone, ExpectedValue = o.ExpectedValue, EmployeeId = o.EmployeeId, EmployeeName = en, InterestedProduct = o.InterestedProduct, SourceId = o.SourceId, SourceName = sn, NextFollowUpDate = o.NextFollowUpDate, StageId = s.StageId, InteractionsCount = icDict.TryGetValue(o.OpportunityId, out var ic) ? ic : 0, TasksCount = tcDict.TryGetValue(o.OpportunityId, out var tc) ? tc : 0, IsOverdue = o.NextFollowUpDate.HasValue && o.NextFollowUpDate.Value < today }; }).ToList(); return new KanbanColumnDto { StageId = s.StageId, StageName = s.StageName ?? "", StageNameAr = s.StageNameAr ?? s.StageName ?? "", StageColor = s.StageColor ?? "#94a3b8", StageOrder = s.StageOrder, Count = cards.Count, Value = cards.Sum(c => c.ExpectedValue ?? 0), Cards = cards }; }).ToList() };
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
        var partyName = await _db.Parties.AsNoTracking().Where(p => p.PartyId == opp.PartyId).Select(p => p.PartyName).FirstOrDefaultAsync();
        return new OpportunityFormDto { OpportunityId = opp.OpportunityId, PartyId = opp.PartyId, PartyName = partyName, EmployeeId = opp.EmployeeId, SourceId = opp.SourceId, AdTypeId = opp.AdTypeId, StageId = opp.StageId, StatusId = opp.StatusId, CategoryId = opp.CategoryId, InterestedProduct = opp.InterestedProduct, ExpectedValue = opp.ExpectedValue, Location = opp.Location, FirstContactDate = opp.FirstContactDate, NextFollowUpDate = opp.NextFollowUpDate, LostReasonId = opp.LostReasonId, LostNotes = opp.LostNotes, Notes = opp.Notes, Guidance = opp.Guidance, IsActive = opp.IsActive, CreatedBy = opp.CreatedBy, CreatedAt = opp.CreatedAt };
    }

    // ════════════════════ GET OPPORTUNITY DETAIL ════════════════════
    public async Task<OpportunityListDto?> GetOpportunityDetailAsync(int opportunityId)
    {
        var opp = await _db.VwSalesOpportunities.AsNoTracking().FirstOrDefaultAsync(o => o.OpportunityId == opportunityId);
        if (opp == null) return null;
        return MapToListDto(opp);
    }

    // ════════════════════ GET ALL EMPLOYEES ════════════════════
    public async Task<List<Employee>> GetEmployeesAsync()
    {
        return await _db.Employees.AsNoTracking().Where(e => e.Status == "نشط" || e.Status == "Active").ToListAsync();
    }

    // ════════════════════ STATS ════════════════════
    public async Task<OpportunityStatsDto> GetStatsAsync(OpportunityFilterDto filter)
    {
        try
        {
            var stages = await _db.SalesStages.AsNoTracking().Where(s => s.IsActive).ToListAsync();
            var wonIds  = stages.Where(s => WonKeywords.Any(k => (s.StageNameAr ?? "").Contains(k) || (s.StageName ?? "").Contains(k))).Select(s => s.StageId).ToHashSet();
            var lostIds = stages.Where(s => LostKeywords.Any(k => (s.StageNameAr ?? "").Contains(k) || (s.StageName ?? "").Contains(k))).Select(s => s.StageId).ToHashSet();
            lostIds.ExceptWith(wonIds);
            var q = _db.SalesOpportunities.AsNoTracking().AsQueryable();
            if (filter.StageId.HasValue) q = q.Where(o => o.StageId == filter.StageId.Value);
            if (filter.EmployeeId.HasValue) q = q.Where(o => o.EmployeeId == filter.EmployeeId.Value);
            if (filter.IsActive.HasValue) q = q.Where(o => o.IsActive == filter.IsActive.Value);
            var opps = await q.Select(o => new { o.StageId, o.ExpectedValue, o.NextFollowUpDate }).ToListAsync();
            var today = DateTime.Today; var tomorrow = today.AddDays(1);
            return new OpportunityStatsDto { TotalCount = opps.Count, OpenCount = opps.Count(o => !wonIds.Contains(o.StageId) && !lostIds.Contains(o.StageId)), WonCount = opps.Count(o => wonIds.Contains(o.StageId)), LostCount = opps.Count(o => lostIds.Contains(o.StageId)), PipelineValue = opps.Where(o => !wonIds.Contains(o.StageId) && !lostIds.Contains(o.StageId)).Sum(o => o.ExpectedValue ?? 0), OverdueFollowUpCount = opps.Count(o => o.NextFollowUpDate.HasValue && o.NextFollowUpDate.Value < today), TodayFollowUpCount = opps.Count(o => o.NextFollowUpDate.HasValue && o.NextFollowUpDate.Value >= today && o.NextFollowUpDate.Value < tomorrow) };
        }
        catch { return new(); }
    }

    // ════════════════════ SAVE ════════════════════
    public async Task<(bool Success, string Message, int OpportunityId)> SaveOpportunityAsync(OpportunityFormDto dto, string userName)
    {
        try
        {
            SalesOpportunity opp; bool isNew = dto.OpportunityId == 0;
            if (isNew) { opp = new SalesOpportunity { PartyId = dto.PartyId, CreatedBy = userName, CreatedAt = DateTime.Now, IsActive = true }; _db.SalesOpportunities.Add(opp); }
            else { opp = await _db.SalesOpportunities.FindAsync(dto.OpportunityId); if (opp == null) return (false, "الفرصة غير موجودة", 0); opp.LastUpdatedBy = userName; opp.LastUpdatedAt = DateTime.Now; }
            opp.EmployeeId = dto.EmployeeId; opp.SourceId = dto.SourceId; opp.AdTypeId = dto.AdTypeId; opp.StageId = dto.StageId; opp.StatusId = dto.StatusId; opp.CategoryId = dto.CategoryId; opp.InterestedProduct = dto.InterestedProduct; opp.ExpectedValue = dto.ExpectedValue; opp.Location = dto.Location; opp.FirstContactDate = dto.FirstContactDate; opp.NextFollowUpDate = dto.NextFollowUpDate; opp.LostReasonId = dto.LostReasonId; opp.LostNotes = dto.LostNotes; opp.Notes = dto.Notes; opp.Guidance = dto.Guidance; opp.IsActive = dto.IsActive;
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
        if (f.DateFrom.HasValue) q = q.Where(o => o.CreatedAt >= f.DateFrom.Value);
        if (f.DateTo.HasValue) q = q.Where(o => o.CreatedAt <= f.DateTo.Value);
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
        if (f.DateFrom.HasValue) q = q.Where(o => o.CreatedAt >= f.DateFrom.Value);
        if (f.DateTo.HasValue) q = q.Where(o => o.CreatedAt <= f.DateTo.Value);
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
}