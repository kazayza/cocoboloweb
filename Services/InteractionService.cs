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
        if (filter.AdTypeId.HasValue)
            query = query.Where(i => i.AdTypeId == filter.AdTypeId.Value);
        if (filter.StatusId.HasValue)
            query = query.Where(i => i.StatusId == filter.StatusId.Value);
        if (filter.StageBeforeId.HasValue)
            query = query.Where(i => i.StageBeforeId == filter.StageBeforeId.Value);
        if (filter.StageAfterId.HasValue)
            query = query.Where(i => i.StageAfterId == filter.StageAfterId.Value);
        if (filter.PartyId.HasValue)
            query = query.Where(i => i.PartyId == filter.PartyId.Value);
        if (filter.DateFrom.HasValue)
            query = query.Where(i => i.InteractionDate >= filter.DateFrom.Value);
        if (filter.DateTo.HasValue)
            query = query.Where(i => i.InteractionDate <= filter.DateTo.Value.Date.AddDays(1).AddTicks(-1));
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();
            // بحث عربي ذكي سيتم في الذاكرة بعد الجلب الأولي لتجنب مشاكل SQL Collation
            query = query.Where(i =>
                (i.ClientName != null && i.ClientName.Contains(s)) ||
                (i.Phone != null && i.Phone.Contains(s)) ||
                (i.AllPhones != null && i.AllPhones.Contains(s)) ||
                (i.Summary != null && i.Summary.Contains(s)) ||
                (i.Notes != null && i.Notes.Contains(s)));
        }

        var total = await query.CountAsync();

        query = filter.SortDescending
            ? query.OrderByDescending(i => i.InteractionDate).ThenByDescending(i => i.InteractionId)
            : query.OrderBy(i => i.InteractionDate).ThenBy(i => i.InteractionId);

        var items = await query.Skip((filter.PageNumber - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(i => new InteractionListDto
            {
                InteractionId = i.InteractionId, OpportunityId = i.OpportunityId, PartyId = i.PartyId,
                ClientName = i.ClientName ?? "", Phone = i.Phone, AllPhones = i.AllPhones,
                EmployeeId = i.EmployeeId, EmployeeName = i.EmployeeName,
                SourceId = i.SourceId, SourceName = i.SourceName, SourceIcon = i.SourceIcon,
                StatusId = i.StatusId, StatusName = i.StatusName, StatusNameAr = i.StatusNameAr,
                InteractionDate = i.InteractionDate, InteractionTime = i.InteractionTime,
                Summary = i.Summary, 
                StageBeforeId = i.StageBeforeId, StageBeforeName = i.StageBeforeName, 
                StageBeforeNameAr = i.StageBeforeNameAr, StageBeforeColor = i.StageBeforeColor,
                StageAfterId = i.StageAfterId, StageAfterName = i.StageAfterName,
                StageAfterNameAr = i.StageAfterNameAr, StageAfterColor = i.StageAfterColor,
                AdTypeId = i.AdTypeId, AdTypeName = i.AdTypeName, AdTypeNameAr = i.AdTypeNameAr,
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

            if (!dto.SourceId.HasValue)
                return (false, "برجاء تحديد طريقة / مصدر التواصل أولاً");

            var oldStageId = opp.StageId;
            var newStageId = dto.StageAfterId ?? oldStageId;

            if (newStageId != 3 && newStageId != 4)
            {
                if (!dto.TaskTypeId.HasValue)
                    return (false, "برجاء اختيار نوع مهمة المتابعة القادمة (اتصال، اجتماع، إلخ)");
                if (!dto.NextFollowUpDate.HasValue)
                    return (false, "تاريخ المتابعة القادم إجباري لهذه المرحلة");
            }

            var interaction = new CustomerInteraction
            {
                OpportunityId = dto.OpportunityId,
                PartyId = dto.PartyId,
                EmployeeId = dto.EmployeeId,                          // ← كان ناقص
                SourceId = dto.SourceId,                              // ← كان ناقص
                StatusId = dto.StatusId,                              // ← كان ناقص
                Summary = dto.Summary,
                StageBeforeId = dto.StageBeforeId ?? oldStageId,      // ← كان ناقص
                StageAfterId = dto.StageAfterId ?? oldStageId,
                NextFollowUpDate = dto.NextFollowUpDate,
                Notes = dto.Notes,
                InteractionDate = DateTime.Now,
                CreatedBy = userName,
                CreatedAt = DateTime.Now
            };
            _db.CustomerInteractions.Add(interaction);

            // إغلاق مهام المتابعة القديمة وتحديثها
            if (newStageId == 4 || newStageId == 5)
            {
                var label = newStageId == 4 ? "خسارة" : "غير مهتم";
                var tasks = await _db.CrmTasks
                    .Where(t => t.OpportunityId == dto.OpportunityId && (t.Status == "Pending" || t.Status == "In Progress"))
                    .ToListAsync();
                foreach (var t in tasks)
                {
                    t.Status = "Completed";
                    t.CompletedDate = DateTime.Now;
                    t.CompletedBy = userName;
                    t.CompletionNotes = $"تم الإلغاء تلقائياً - العميل {label}";
                }
            }
            else if (newStageId == 3)
            {
                var tasks = await _db.CrmTasks
                    .Where(t => t.OpportunityId == dto.OpportunityId && (t.Status == "Pending" || t.Status == "In Progress"))
                    .ToListAsync();
                foreach (var t in tasks)
                {
                    t.Status = "Completed";
                    t.CompletedDate = DateTime.Now;
                    t.CompletedBy = userName;
                    t.CompletionNotes = "تم الإغلاق تلقائياً - تم البيع بنجاح";
                }
            }
            else if (dto.NextFollowUpDate.HasValue)
            {
                var oldTasks = await _db.CrmTasks
                    .Where(t => t.OpportunityId == dto.OpportunityId && (t.Status == "Pending" || t.Status == "In Progress"))
                    .ToListAsync();
                foreach (var t in oldTasks)
                {
                    t.Status = "Completed";
                    t.CompletedDate = DateTime.Now;
                    t.CompletedBy = userName;
                    t.CompletionNotes = "تمت المتابعة وجدولة موعد جديد";
                }

                // توليد مهمة المتابعة الجديدة في جدول المهام
                var newTask = new CrmTask
                {
                    OpportunityId = dto.OpportunityId,
                    PartyId = dto.PartyId,
                    AssignedTo = dto.EmployeeId ?? opp.EmployeeId ?? 0,
                    TaskTypeId = dto.TaskTypeId ?? 1,
                    TaskDescription = dto.Summary ?? "متابعة تواصل جديد",
                    DueDate = dto.NextFollowUpDate.Value,
                    Priority = (dto.Priority == "Medium" ? "Normal" : dto.Priority) ?? "Normal",
                    Status = "Pending",
                    ReminderEnabled = true,
                    IsActive = true,
                    CreatedBy = userName,
                    CreatedAt = DateTime.Now
                };
                _db.CrmTasks.Add(newTask);
            }

            // Update opportunity stage + followup + lost info
            if (dto.StageAfterId.HasValue && dto.StageAfterId != oldStageId)
                opp.StageId = dto.StageAfterId.Value;

            if (dto.NextFollowUpDate.HasValue)
                opp.NextFollowUpDate = dto.NextFollowUpDate.Value;

            if (dto.LostReasonId.HasValue)
                opp.LostReasonId = dto.LostReasonId;

            if (!string.IsNullOrWhiteSpace(dto.LostNotes))
                opp.LostNotes = dto.LostNotes;

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