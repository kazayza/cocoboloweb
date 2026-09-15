using System.IO;
using System.Security.Claims;
using ClosedXML.Excel;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// موديول خدمة العملاء — فيدباك ما بعد البيع.
/// يجمّع قائمة مكالمات من 3 مصادر (زيارة معرض / تسليم فاتورة / شكوى)
/// ويسجّل نتيجتها + تقرير/تصدير.
/// </summary>
public class CustomerCareService : ICustomerCareService
{
    private readonly IDbContextFactory<db24804Context> _factory;

    private static List<byte> OpenComplaintStatuses() => new()
    {
        ComplaintStatus.New,
        ComplaintStatus.InProgress,
        ComplaintStatus.AwaitingClient,
        ComplaintStatus.Escalated
    };

    public CustomerCareService(IDbContextFactory<db24804Context> factory)
    {
        _factory = factory;
    }

    // ═══════════════════════════════════════════════════════════
    //  لوحة المكالمات (3 مصادر + KPI)
    // ═══════════════════════════════════════════════════════════

    public async Task<CustomerCareDashboardDto> GetDashboardAsync(
        CustomerCareDashboardFilterDto filter,
        ClaimsPrincipal? user)
    {
        var result = new CustomerCareDashboardDto();
        if (!CustomerCarePermissions.CanView(user))
            return result;

        var window = filter.DaysBack > 0 ? filter.DaysBack : 30;
        var since = DateTime.Today.AddDays(-window);
        var fromDate = filter.DateFrom ?? since;
        var hasTo = filter.DateTo.HasValue;
        var toEx = hasTo ? filter.DateTo!.Value.Date.AddDays(1) : DateTime.MaxValue;

        await using var db = await _factory.CreateDbContextAsync();

        // 🏢 قرار العميل: عملاء B2B لا يظهرون في موديول الفيدباك إطلاقاً (حتى للمدراء)
        //    — فيدباك الشركات يُؤخذ خارج هذا الموديول. استبعاد من كل المصادر بالـ PartyId.
        var excludedB2bPartyIds = await B2bPartyIdsAsync(db);

        // ── الزيارات: CustomerInteractions بحالة زيارة/ShowRoom
        var visitStatusIds = await db.ContactStatuses.AsNoTracking()
            .Where(s => s.StatusName != null
                && (s.StatusName.Contains("Visit")
                    || s.StatusName.Contains("ShowRoom")
                    || s.StatusName.Contains("Show Room")))
            .Select(s => s.StatusId)
            .ToListAsync();

        if (visitStatusIds.Any())
        {
            var visits = await (from v in db.VwCustomerInteractions.AsNoTracking()
                                where v.StatusId.HasValue
                                      && visitStatusIds.Contains(v.StatusId.Value)
                                      && v.InteractionDate >= fromDate
                                      && (!hasTo || v.InteractionDate < toEx)
                                      && !excludedB2bPartyIds.Contains(v.PartyId)
                                orderby v.InteractionDate descending
                                select new
                                {
                                    v.InteractionId,
                                    v.PartyId,
                                    v.InteractionDate,
                                    v.ClientName,
                                    v.Phone
                                }).Take(400).ToListAsync();

            if (visits.Any())
            {
                var existing = await ExistingByKeyAsync(db, CustomerCareKind.Visit, "Interaction", visits.Select(x => (int?)x.InteractionId).ToList());

                result.Visits = visits.Select(x =>
                {
                    existing.TryGetValue(x.InteractionId, out var fb);
                    return new CustomerCareQueueItemDto
                    {
                        Kind = CustomerCareKind.Visit,
                        PartyId = x.PartyId,
                        VisitInteractionId = x.InteractionId,
                        ClientName = x.ClientName ?? "عميل",
                        Phone = x.Phone,
                        SourceDate = x.InteractionDate,
                        Title = $"زيارة معرض — {x.InteractionDate:yyyy-MM-dd}",
                        ExistingFeedbackId = fb?.FeedbackId,
                        Satisfaction = fb?.Satisfaction,
                        Channel = fb?.Channel,
                        FeedbackStatus = fb?.Status,
                        FeedbackOutcome = fb?.Outcome,
                        NextCallDate = fb?.NextCallDate
                    };
                }).Where(OkToShow(filter)).ToList();
            }
        }

        // ── التسليمات: فواتير تم تسليمها خلال النافذة
        var deliveries = await (from d in db.VwSalesDeliveryStatuses.AsNoTracking()
                                where d.DeliveredAt.HasValue
                                      && d.DeliveredAt >= fromDate
                                      && (!hasTo || d.DeliveredAt < toEx)
                                      && !excludedB2bPartyIds.Contains(d.PartyId)
                                orderby d.DeliveredAt descending
                                select new
                                {
                                    d.TransactionId,
                                    d.PartyId,
                                    d.PartyName,
                                    d.DeliveredAt
                                }).Take(400).ToListAsync();

        if (deliveries.Any())
        {
            var phones = await PartyPhonesAsync(db, deliveries.Select(x => x.PartyId).Distinct().ToList());
            var existing = await ExistingByKeyAsync(db, CustomerCareKind.Delivery, "Transaction", deliveries.Select(x => (int?)x.TransactionId).ToList());

            result.Deliveries = deliveries.Select(x =>
            {
                existing.TryGetValue(x.TransactionId, out var fb);
                phones.TryGetValue(x.PartyId, out var ph);
                return new CustomerCareQueueItemDto
                {
                    Kind = CustomerCareKind.Delivery,
                    PartyId = x.PartyId,
                    TransactionId = x.TransactionId,
                    ClientName = x.PartyName ?? "عميل",
                    Phone = ph,
                    SourceDate = x.DeliveredAt,
                    Title = $"فاتورة #{x.TransactionId} — سُلّمت {x.DeliveredAt:yyyy-MM-dd}",
                    ExistingFeedbackId = fb?.FeedbackId,
                    Satisfaction = fb?.Satisfaction,
                    Channel = fb?.Channel,
                    FeedbackStatus = fb?.Status,
                    FeedbackOutcome = fb?.Outcome,
                    NextCallDate = fb?.NextCallDate
                };
            }).Where(OkToShow(filter)).ToList();
        }

        // ── الشكاوى: مفتوحة + مَغلقة حديثاً بلا تقييم رضا
        var openComplaints = OpenComplaintStatuses();
        var complaints = await (from c in db.VwComplaintsLists.AsNoTracking()
                                where c.ComplaintId > 0
                                      && !excludedB2bPartyIds.Contains(c.PartyId)
                                      && ((c.Status.HasValue && openComplaints.Contains(c.Status.GetValueOrDefault()))
                                          || (c.Status == ComplaintStatus.Resolved
                                              && c.SolvedDate.HasValue
                                              && c.SolvedDate >= fromDate
                                              && (!hasTo || c.SolvedDate < toEx)
                                              && !c.SatisfactionLevel.HasValue))
                                orderby (c.ComplaintDate ?? c.SolvedDate) descending
                                select new
                                {
                                    c.ComplaintId,
                                    c.PartyId,
                                    c.TransactionId,
                                    c.ComplaintDate,
                                    c.SolvedDate,
                                    c.ClientName,
                                    c.ClientPhone,
                                    c.Subject
                                }).Take(400).ToListAsync();

        if (complaints.Any())
        {
            var existing = await ExistingByKeyAsync(db, CustomerCareKind.ComplaintFollowUp, "Complaint", complaints.Select(x => (int?)x.ComplaintId).ToList());

            result.Complaints = complaints.Select(c =>
            {
                existing.TryGetValue(c.ComplaintId, out var fb);
                return new CustomerCareQueueItemDto
                {
                    Kind = CustomerCareKind.ComplaintFollowUp,
                    PartyId = c.PartyId,
                    TransactionId = c.TransactionId,
                    ComplaintId = c.ComplaintId,
                    ClientName = c.ClientName ?? "عميل",
                    Phone = c.ClientPhone,
                    SourceDate = c.ComplaintDate ?? c.SolvedDate,
                    Title = string.IsNullOrWhiteSpace(c.Subject) ? "شكوى" : c.Subject,
                    ExistingFeedbackId = fb?.FeedbackId,
                    Satisfaction = fb?.Satisfaction,
                    Channel = fb?.Channel,
                    FeedbackStatus = fb?.Status,
                    FeedbackOutcome = fb?.Outcome,
                    NextCallDate = fb?.NextCallDate
                };
            }).Where(OkToShow(filter)).ToList();
        }

        // ── المتابعات المجدولة («يتصل لاحقاً») — المتأخرة أولاً
        var cbRaw = await db.CustomerFeedbacks.AsNoTracking()
            .Where(f => f.IsActive
                        && f.Status == CustomerCareStatus.Open
                        && f.Outcome == CustomerCareOutcome.CallbackLater
                        && f.NextCallDate.HasValue
                        && !excludedB2bPartyIds.Contains(f.PartyId))
            .OrderBy(f => f.NextCallDate)
            .Take(12)
            .Select(f => new
            {
                f.FeedbackId, f.FeedbackType, f.PartyId, f.ClientName, f.Phone,
                f.TransactionId, f.ComplaintId, f.VisitInteractionId,
                f.SourceDate, f.NextCallDate, f.Satisfaction, f.Channel
            })
            .ToListAsync();

        result.Callbacks = cbRaw.Select(f => new CustomerCareQueueItemDto
        {
            Kind = f.FeedbackType,
            PartyId = f.PartyId,
            TransactionId = f.TransactionId,
            ComplaintId = f.ComplaintId,
            VisitInteractionId = f.VisitInteractionId,
            ClientName = f.ClientName ?? "عميل",
            Phone = f.Phone,
            SourceDate = f.SourceDate,
            Title = CustomerCareKindText.ToText(f.FeedbackType) + " — متابعة مجدولة",
            ExistingFeedbackId = f.FeedbackId,
            Satisfaction = f.Satisfaction,
            Channel = f.Channel,
            FeedbackStatus = CustomerCareStatus.Open,
            FeedbackOutcome = CustomerCareOutcome.CallbackLater,
            NextCallDate = f.NextCallDate
        }).ToList();

        // ── KPI
        var mine = !CustomerCarePermissions.CanViewAll(user);
        var myName = user?.Identity?.Name;
        var employeeId = mine ? await ResolveEmployeeByUsernameAsync(myName) : 0;

        var fbQuery = db.CustomerFeedbacks.AsNoTracking()
            .Where(f => f.IsActive && f.Status == CustomerCareStatus.Done && !excludedB2bPartyIds.Contains(f.PartyId));
        if (mine)
            fbQuery = fbQuery.Where(f => f.CreatedBy == myName || (f.AssignedTo.HasValue && f.AssignedTo == employeeId));

        var doneTodayStart = DateTime.Today;
        var doneTotal = await fbQuery.CountAsync();
        var doneToday = await fbQuery.CountAsync(f => f.CompletedAt.HasValue && f.CompletedAt >= doneTodayStart);
        var positive = await fbQuery.CountAsync(f => f.Outcome == CustomerCareOutcome.Positive);

        var satisfactionList = await fbQuery
            .Where(f => f.Satisfaction.HasValue)
            .Select(f => f.Satisfaction!.Value)
            .ToListAsync();

        result.Kpis = new CustomerCareKpiDto
        {
            OpenVisits = result.Visits.Count,
            OpenDeliveries = result.Deliveries.Count,
            OpenComplaints = result.Complaints.Count,
            DoneToday = doneToday,
            DoneTotal = doneTotal,
            AvgSatisfaction = satisfactionList.Count > 0 ? satisfactionList.Average(s => (double)s) : null,
            PositiveCount = positive
        };

        return result;
    }

    private static Func<CustomerCareQueueItemDto, bool> OkToShow(CustomerCareDashboardFilterDto filter) =>
        item => item.FeedbackStatus is null
                or CustomerCareStatus.Open
                or CustomerCareStatus.Skipped
                || filter.IncludeContacted;

    // ═══════════════════════════════════════════════════════════
    //  تسجيل/تحديث نتيجة المكالمة
    // ═══════════════════════════════════════════════════════════

    public async Task<(bool Success, string Message, int? FeedbackId)> LogOutcomeAsync(
        CustomerCareLogDto dto,
        ClaimsPrincipal? user)
    {
        if (!CustomerCarePermissions.CanLog(user))
            return (false, "لا تملك صلاحية تسجيل فيدباك.", null);
        if (dto.PartyId <= 0)
            return (false, "بيانات العميل غير صالحة.", null);
        if (string.IsNullOrWhiteSpace(dto.Outcome))
            return (false, "اختر نتيجة المكالمة.", null);

        await using var db = await _factory.CreateDbContextAsync();
        var now = DateTime.Now;
        var myName = user?.Identity?.Name;
        var myEmpId = await ResolveEmployeeByUsernameAsync(myName);

        // 🏢 منع تسجيل فيدباك على عميل B2B نهائياً (قرار العميل: يُؤخذ خارج هذا الموديول)
        var partyIsB2B = await db.Parties.AsNoTracking().AnyAsync(p => p.PartyId == dto.PartyId && p.IsB2B);
        if (partyIsB2B)
            return (false, "هذا العميل من ملفات B2B — فيدباكه لا يُسجّل في هذا الموديول.", null);

        CustomerFeedback? entity;
        if (dto.FeedbackId.HasValue)
        {
            entity = await db.CustomerFeedbacks.FirstOrDefaultAsync(f =>
                f.FeedbackId == dto.FeedbackId.Value && f.IsActive);
            if (entity is null)
                return (false, "السجل غير موجود.", null);
        }
        else
        {
            entity = new CustomerFeedback
            {
                FeedbackType = dto.Kind,
                PartyId = dto.PartyId,
                OpportunityId = dto.OpportunityId,
                TransactionId = dto.TransactionId,
                ComplaintId = dto.ComplaintId,
                VisitInteractionId = dto.VisitInteractionId,
                SourceDate = dto.SourceDate,
                ClientName = dto.ClientName,
                Phone = dto.Phone,
                Channel = string.IsNullOrWhiteSpace(dto.Channel) ? "اتصال" : dto.Channel.Trim(),
                AssignedTo = myEmpId > 0 ? myEmpId : null,
                CreatedBy = myName,
                CreatedAt = now,
                IsActive = true
            };
            db.CustomerFeedbacks.Add(entity);
        }

        if (entity is null)
            return (false, "سجل غير معروف.", null);
        entity.Outcome = dto.Outcome;
        entity.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
        entity.EmployeeNotes = string.IsNullOrWhiteSpace(dto.EmployeeNotes) ? null : dto.EmployeeNotes.Trim();
        entity.Channel = string.IsNullOrWhiteSpace(dto.Channel) ? "اتصال" : dto.Channel.Trim();
        entity.Satisfaction = dto.Satisfaction is >= 1 and <= 5 ? dto.Satisfaction : null;
        entity.UpdatedBy = myName;
        entity.UpdatedAt = now;

        if (dto.MarkSkipped)
        {
            entity.Status = CustomerCareStatus.Skipped;
            entity.NextCallDate = null;
        }
        else if (dto.Outcome == CustomerCareOutcome.CallbackLater)
        {
            entity.Status = CustomerCareStatus.Open;
            entity.NextCallDate = dto.NextCallDate ?? DateTime.Today.AddDays(1);
        }
        else
        {
            entity.Status = CustomerCareStatus.Done;
            entity.CompletedBy = myName;
            entity.CompletedAt = now;
            entity.NextCallDate = null;
        }

        await db.SaveChangesAsync();
        return (true, "تم حفظ الفيدباك.", entity.FeedbackId);
    }

    // ═══════════════════════════════════════════════════════════
    //  السجل + التقرير
    // ═══════════════════════════════════════════════════════════

    public async Task<CustomerCareHistoryDto> GetHistoryAsync(
        CustomerCareHistoryFilterDto filter,
        ClaimsPrincipal? user)
    {
        if (!CustomerCarePermissions.CanView(user))
            return new CustomerCareHistoryDto();

        await using var db = await _factory.CreateDbContextAsync();

        // 🏢 استبعاد عملاء B2B نهائياً من سجل الفيدباك
        var excludedB2bPartyIds = await B2bPartyIdsAsync(db);

        var q = db.CustomerFeedbacks.AsNoTracking().Where(f => f.IsActive && !excludedB2bPartyIds.Contains(f.PartyId));

        if (filter.DateFrom.HasValue)
            q = q.Where(f => f.CreatedAt >= filter.DateFrom.Value.Date);
        if (filter.DateTo.HasValue)
            q = q.Where(f => f.CreatedAt <= filter.DateTo.Value.Date.AddDays(1).AddTicks(-1));

        if (!string.IsNullOrWhiteSpace(filter.Kind))
            q = q.Where(f => f.FeedbackType == filter.Kind);
        if (!string.IsNullOrWhiteSpace(filter.Status))
            q = q.Where(f => f.Status == filter.Status);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            q = q.Where(f => (f.ClientName != null && f.ClientName.Contains(s))
                             || (f.Phone != null && f.Phone.Contains(s))
                             || (f.Notes != null && f.Notes.Contains(s)));
        }

        var mine = !CustomerCarePermissions.CanViewAll(user);
        if (mine)
        {
            var myName = user?.Identity?.Name;
            var empId = await ResolveEmployeeByUsernameAsync(myName);
            q = q.Where(f => f.CreatedBy == myName || (f.AssignedTo.HasValue && f.AssignedTo == empId));
        }

        var total = await q.CountAsync();
        var pageIndex = filter.PageIndex < 1 ? 1 : filter.PageIndex;
        var pageSize = filter.PageSize < 1 ? 50 : filter.PageSize;

        var items = await q
            .OrderByDescending(f => f.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new CustomerCareHistoryItemDto
            {
                FeedbackId = f.FeedbackId,
                Kind = f.FeedbackType,
                ClientName = f.ClientName ?? "عميل",
                Phone = f.Phone,
                Status = f.Status,
                Outcome = f.Outcome ?? "",
                Satisfaction = f.Satisfaction,
                Notes = f.Notes,
                EmployeeNotes = f.EmployeeNotes,
                SourceDate = f.SourceDate,
                NextCallDate = f.NextCallDate,
                CreatedBy = f.CreatedBy,
                CreatedAt = f.CreatedAt,
                CompletedBy = f.CompletedBy,
                CompletedAt = f.CompletedAt
            })
            .ToListAsync();

        var outcomeGroups = await q
            .Where(f => f.Status == CustomerCareStatus.Done)
            .GroupBy(f => f.Outcome)
            .Select(g => new { Outcome = g.Key, Count = g.Count() })
            .ToListAsync();

        return new CustomerCareHistoryDto
        {
            Items = items,
            TotalCount = total,
            OutcomeStats = outcomeGroups
                .Select(g => new CustomerCareOutcomeStatDto { Outcome = g.Outcome ?? "", Count = g.Count })
                .ToList(),
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    public async Task<(bool Success, string Message)> HideAsync(int feedbackId, ClaimsPrincipal? user)
    {
        if (!CustomerCarePermissions.CanView(user))
            return (false, "لا تملك صلاحية.");

        await using var db = await _factory.CreateDbContextAsync();
        var entity = await db.CustomerFeedbacks.FirstOrDefaultAsync(f => f.FeedbackId == feedbackId);
        if (entity is null)
            return (false, "السجل غير موجود.");
        entity.IsActive = false;
        entity.UpdatedBy = user?.Identity?.Name;
        entity.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
        return (true, "تم الإخفاء.");
    }

    public async Task<(byte[] Content, string Mime, string FileName)> ExportAsync(
        CustomerCareReportFilterDto filter,
        ClaimsPrincipal? user)
    {
        if (!CustomerCarePermissions.CanExport(user))
            throw new UnauthorizedAccessException("لا تملك صلاحية التصدير.");

        await using var db = await _factory.CreateDbContextAsync();

        // 🏢 استبعاد عملاء B2B نهائياً من تصدير الفيدباك
        var excludedB2bPartyIds = await B2bPartyIdsAsync(db);

        var q = db.CustomerFeedbacks.AsNoTracking().Where(f => f.IsActive && !excludedB2bPartyIds.Contains(f.PartyId));

        if (filter.DateFrom.HasValue)
            q = q.Where(f => f.CreatedAt >= filter.DateFrom.Value.Date);
        if (filter.DateTo.HasValue)
            q = q.Where(f => f.CreatedAt <= filter.DateTo.Value.Date.AddDays(1).AddTicks(-1));
        if (!string.IsNullOrWhiteSpace(filter.Kind))
            q = q.Where(f => f.FeedbackType == filter.Kind);

        var mine = !CustomerCarePermissions.CanViewAll(user);
        if (mine)
        {
            var myName = user?.Identity?.Name;
            var empId = await ResolveEmployeeByUsernameAsync(myName);
            q = q.Where(f => f.CreatedBy == myName || (f.AssignedTo.HasValue && f.AssignedTo == empId));
        }

        var rows = await q
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new
            {
                f.FeedbackId, f.FeedbackType, f.ClientName, f.Phone, f.Status,
                f.Outcome, f.Satisfaction, f.Notes, f.EmployeeNotes, f.SourceDate, f.CreatedBy, f.CreatedAt,
                f.CompletedBy, f.CompletedAt, f.NextCallDate
            })
            .ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("فيدباك");

        var headers = new[]
        {
            "رقم", "المصدر", "العميل", "الجوال", "الحالة", "النتيجة", "الرضا",
            "ملاحظات", "انطباع الموظف", "تاريخ الحدث", "سجّله", "تاريخ التسجيل", "أكمله", "تاريخ الإنجاز"
        };
        for (int c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];

        var r = 2;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.FeedbackId;
            ws.Cell(r, 2).Value = CustomerCareKindText.ToText(row.FeedbackType);
            ws.Cell(r, 3).Value = row.ClientName ?? "";
            ws.Cell(r, 4).Value = row.Phone ?? "";
            ws.Cell(r, 5).Value = row.Status == CustomerCareStatus.Done ? "تم" : row.Status == CustomerCareStatus.Open ? "مفتوح" : "مُسقط";
            ws.Cell(r, 6).Value = CustomerCareOutcome.ToText(row.Outcome);
            ws.Cell(r, 7).Value = row.Satisfaction?.ToString() ?? "";
            ws.Cell(r, 8).Value = row.Notes ?? "";
            ws.Cell(r, 9).Value = row.EmployeeNotes ?? "";
            ws.Cell(r, 10).Value = row.SourceDate?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(r, 11).Value = row.CreatedBy ?? "";
            ws.Cell(r, 12).Value = row.CreatedAt.ToString("yyyy-MM-dd HH:mm");
            ws.Cell(r, 13).Value = row.CompletedBy ?? "";
            ws.Cell(r, 14).Value = row.CompletedAt?.ToString("yyyy-MM-dd HH:mm") ?? "";
            r++;
        }

        ws.RangeUsed()?.SetAutoFilter();
        ws.Style.Font.FontName = "Cairo";
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return (ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "CustomerCare_Feedback.xlsx");
    }

    // ═══════════════════════════════════════════════════════════
    //  مساعدات
    // ═══════════════════════════════════════════════════════════

    /// <summary>مؤشرات وجود سجلات سابقة حسب المفتاح المرجعي لكل نوع</summary>
    private static async Task<Dictionary<int, CustomerFeedback>> ExistingByKeyAsync(
        db24804Context db, string kind, string keyColumn, List<int?> keys)
    {
        var result = new Dictionary<int, CustomerFeedback>();
        var nonNull = keys.Where(k => k.HasValue).Select(k => k!.Value).Distinct().ToList();
        if (!nonNull.Any()) return result;

        List<CustomerFeedback> rows;
        switch (keyColumn)
        {
            case "Interaction":
                rows = await db.CustomerFeedbacks.AsNoTracking()
                    .Where(f => f.FeedbackType == kind && f.VisitInteractionId.HasValue && nonNull.Contains(f.VisitInteractionId.Value))
                    .ToListAsync();
                foreach (var f in rows) if (f.VisitInteractionId.HasValue) result[f.VisitInteractionId.Value] = f;
                break;
            case "Transaction":
                rows = await db.CustomerFeedbacks.AsNoTracking()
                    .Where(f => f.FeedbackType == kind && f.TransactionId.HasValue && nonNull.Contains(f.TransactionId.Value))
                    .ToListAsync();
                foreach (var f in rows) if (f.TransactionId.HasValue) result[f.TransactionId.Value] = f;
                break;
            default: // Complaint
                rows = await db.CustomerFeedbacks.AsNoTracking()
                    .Where(f => f.FeedbackType == kind && f.ComplaintId.HasValue && nonNull.Contains(f.ComplaintId.Value))
                    .ToListAsync();
                foreach (var f in rows) if (f.ComplaintId.HasValue) result[f.ComplaintId.Value] = f;
                break;
        }
        return result;
    }

    private async Task<Dictionary<int, string?>> PartyPhonesAsync(db24804Context db, List<int> partyIds)
    {
        var result = new Dictionary<int, string?>();
        if (!partyIds.Any()) return result;
        var rows = await db.Parties.AsNoTracking()
            .Where(p => partyIds.Contains(p.PartyId))
            .Select(p => new { p.PartyId, p.Phone })
            .ToListAsync();
        foreach (var r in rows) result[r.PartyId] = r.Phone;
        return result;
    }

    /// <summary>🏢 معرّفات عملاء B2B — يُستبعدون نهائياً من موديول الفيدباك (قرار العميل).</summary>
    private static async Task<List<int>> B2bPartyIdsAsync(db24804Context db)
        => await db.Parties.AsNoTracking()
            .Where(p => p.IsB2B)
            .Select(p => p.PartyId)
            .ToListAsync();

    private async Task<int> ResolveEmployeeByUsernameAsync(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return 0;
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Users.AsNoTracking()
            .Where(u => u.Username == username && u.EmployeeId.HasValue)
            .Select(u => u.EmployeeId!.Value)
            .FirstOrDefaultAsync();
    }
}
