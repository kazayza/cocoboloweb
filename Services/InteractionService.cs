using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class InteractionService : IInteractionService
{
    private readonly db24804Context _db;
    private readonly IHttpContextAccessor _http;

    public InteractionService(db24804Context db, IHttpContextAccessor http)
    { _db = db; _http = http; }

    public async Task<PagedResult<InteractionListDto>> GetInteractionsAsync(InteractionFilterDto filter)
    {
        var crmAccess = _http.GetCrmAccessFrom();
        var query = _db.VwCustomerInteractions.AsNoTracking().AsQueryable();

        if (crmAccess.HasValue)
            query = query.Where(i => i.InteractionDate >= crmAccess.Value);
        if (filter.OpportunityId.HasValue)
            query = query.Where(i => i.OpportunityId == filter.OpportunityId.Value);
        if (filter.EmployeeId.HasValue)
            query = query.Where(i => i.EmployeeId == filter.EmployeeId.Value);
        if (filter.SourceId.HasValue)
            query = query.Where(i => i.SourceId == filter.SourceId.Value);
        if (filter.DateFrom.HasValue)
            query = query.Where(i => i.InteractionDate >= filter.DateFrom.Value);
        if (filter.DateTo.HasValue)
            query = query.Where(i => i.InteractionDate <= filter.DateTo.Value);
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();
            query = query.Where(i =>
                (i.ClientName != null && i.ClientName.Contains(s)) ||
                (i.Summary != null && i.Summary.Contains(s)));
        }

        var total = await query.CountAsync();

        query = filter.SortDescending
            ? query.OrderByDescending(i => i.InteractionDate).ThenByDescending(i => i.InteractionId)
            : query.OrderBy(i => i.InteractionDate).ThenBy(i => i.InteractionId);

        var items = await query.Skip((filter.PageNumber - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(i => new InteractionListDto
            {
                InteractionId = i.InteractionId, OpportunityId = i.OpportunityId, PartyId = i.PartyId,
                ClientName = i.ClientName ?? "", Phone = i.Phone,
                EmployeeId = i.EmployeeId, EmployeeName = i.EmployeeName,
                SourceId = i.SourceId, SourceName = i.SourceName, SourceIcon = i.SourceIcon,
                StatusId = i.StatusId, StatusName = i.StatusName,
                InteractionDate = i.InteractionDate, InteractionTime = i.InteractionTime,
                Summary = i.Summary, StageBeforeId = i.StageBeforeId, StageBeforeName = i.StageBeforeName,
                StageAfterId = i.StageAfterId, StageAfterName = i.StageAfterName,
                NextFollowUpDate = i.NextFollowUpDate, Notes = i.Notes,
                CreatedBy = i.CreatedBy, CreatedAt = i.CreatedAt
            }).ToListAsync();

        return new PagedResult<InteractionListDto> { Items = items, TotalCount = total, PageNumber = filter.PageNumber, PageSize = filter.PageSize };
    }

    public async Task<List<InteractionListDto>> GetByOpportunityAsync(int opportunityId)
    {
        return await _db.VwCustomerInteractions.AsNoTracking()
            .Where(i => i.OpportunityId == opportunityId)
            .OrderByDescending(i => i.InteractionDate).ThenByDescending(i => i.InteractionId)
            .Select(i => new InteractionListDto
            {
                InteractionId = i.InteractionId, OpportunityId = i.OpportunityId, PartyId = i.PartyId,
                ClientName = i.ClientName ?? "", Phone = i.Phone,
                EmployeeId = i.EmployeeId, EmployeeName = i.EmployeeName,
                SourceId = i.SourceId, SourceName = i.SourceName,
                InteractionDate = i.InteractionDate, InteractionTime = i.InteractionTime,
                Summary = i.Summary, StageBeforeId = i.StageBeforeId, StageBeforeName = i.StageBeforeName,
                StageAfterId = i.StageAfterId, StageAfterName = i.StageAfterName,
                NextFollowUpDate = i.NextFollowUpDate, Notes = i.Notes,
                CreatedBy = i.CreatedBy, CreatedAt = i.CreatedAt
            }).ToListAsync();
    }

    public async Task<(bool Success, string Message)> AddQuickAsync(QuickInteractionDto dto, string userName)
    {
        try
        {
            var opp = await _db.SalesOpportunities.FindAsync(dto.OpportunityId);
            if (opp == null) return (false, "الفرصة غير موجودة");

            var oldStageId = opp.StageId;

            var interaction = new CustomerInteraction
            {
                OpportunityId = dto.OpportunityId, PartyId = dto.PartyId,
                Summary = dto.Summary, StageBeforeId = oldStageId,
                StageAfterId = dto.StageAfterId ?? oldStageId,
                NextFollowUpDate = dto.NextFollowUpDate, Notes = dto.Notes,
                InteractionDate = DateTime.Now, CreatedBy = userName, CreatedAt = DateTime.Now
            };
            _db.CustomerInteractions.Add(interaction);

            // Update opportunity stage + followup
            if (dto.StageAfterId.HasValue && dto.StageAfterId != oldStageId)
                opp.StageId = dto.StageAfterId.Value;
            if (dto.NextFollowUpDate.HasValue)
                opp.NextFollowUpDate = dto.NextFollowUpDate.Value;
            opp.LastContactDate = DateTime.Now;
            opp.LastUpdatedBy = userName;
            opp.LastUpdatedAt = DateTime.Now;

            // Update party
            var party = await _db.Parties.FindAsync(dto.PartyId);
            if (party != null) party.LastContactDate = DateTime.Now;

            await _db.SaveChangesAsync();
            return (true, "تم إضافة التفاعل بنجاح");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.InnerException?.Message ?? ex.Message}");
        }
    }
}