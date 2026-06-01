using ClosedXML.Excel;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class ComplaintService : IComplaintService
{
    private readonly IDbContextFactory<db24804Context> _factory;
    private readonly IWebHostEnvironment              _env;
    private readonly ILogger<ComplaintService>        _logger;

    private const string UploadFolder = "uploads/complaints";

    public ComplaintService(
        IDbContextFactory<db24804Context> factory,
        IWebHostEnvironment env,
        ILogger<ComplaintService> logger)
    {
        _factory = factory;
        _env     = env;
        _logger  = logger;
    }

    // ═══════════════════════════════════════════════════════
    //                      LIST & DETAIL
    // ═══════════════════════════════════════════════════════

    public async Task<PagedComplaintsDto> GetComplaintsAsync(
        ComplaintFilterDto filter, string? currentUserName = null)
    {
        using var db = await _factory.CreateDbContextAsync();
        var query = db.VwComplaintsLists.AsNoTracking().AsQueryable();

        // ── Filters ───────────────────────────────────────
        if (filter.DateFrom.HasValue)
            query = query.Where(c => c.ComplaintDate >= filter.DateFrom.Value.Date);
        if (filter.DateTo.HasValue)
            query = query.Where(c => c.ComplaintDate <= filter.DateTo.Value.Date.AddDays(1).AddTicks(-1));

        if (filter.PartyId.HasValue)
            query = query.Where(c => c.PartyId == filter.PartyId.Value);

        if (filter.TypeId.HasValue)
            query = query.Where(c => c.TypeId == filter.TypeId.Value);

        if (filter.Status.HasValue)
            query = query.Where(c => c.Status == filter.Status.Value);

        if (filter.Priority.HasValue)
            query = query.Where(c => c.Priority == filter.Priority.Value);

        if (filter.AssignedTo.HasValue)
            query = query.Where(c => c.AssignedTo == filter.AssignedTo.Value);

        if (filter.OpenOnly)
            query = query.Where(c => c.Status != ComplaintStatus.Resolved
                                  && c.Status != ComplaintStatus.Rejected);

        if (filter.MineOnly && !string.IsNullOrWhiteSpace(currentUserName))
            query = query.Where(c => c.CreatedBy == currentUserName);

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();
            query = query.Where(c =>
                c.Subject.Contains(s) ||
                (c.ClientName != null && c.ClientName.Contains(s)) ||
                (c.ClientPhone != null && c.ClientPhone.Contains(s)));
        }

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(c => c.ComplaintDate)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(c => new ComplaintListItemDto
            {
                ComplaintId       = c.ComplaintId,
                ComplaintDate     = c.ComplaintDate,
                PartyId           = c.PartyId,
                ClientName        = c.ClientName,
                ClientPhone       = c.ClientPhone,
                TypeId            = c.TypeId,
                ComplaintType     = c.ComplaintType,
                Subject           = c.Subject,
                Priority          = c.Priority,
                PriorityName      = c.PriorityName,
                Status            = c.Status,
                StatusName        = c.StatusName,
                AssignedTo        = c.AssignedTo,
                EmployeeName      = c.EmployeeName,
                SolvedDate        = c.SolvedDate,
                DaysOpen          = c.DaysOpen,
                Escalated         = c.Escalated,
                TransactionId     = c.TransactionId,
                ProductId         = c.ProductId,
                ProductName       = c.ProductName,
                SatisfactionLevel = c.SatisfactionLevel,
                AttachmentsCount  = c.AttachmentsCount,
                FollowUpsCount    = c.FollowUpsCount
            })
            .ToListAsync();

        return new PagedComplaintsDto
        {
            Items      = items,
            TotalCount = total,
            Page       = filter.Page,
            PageSize   = filter.PageSize
        };
    }

    public async Task<ComplaintDetailDto?> GetByIdAsync(int complaintId)
    {
        using var db = await _factory.CreateDbContextAsync();

        var c = await db.Complaints.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ComplaintId == complaintId);
        if (c is null) return null;

        var party       = await db.Parties.AsNoTracking()
                            .FirstOrDefaultAsync(p => p.PartyId == c.PartyId);
        var type        = await db.ComplaintTypes.AsNoTracking()
                            .FirstOrDefaultAsync(t => t.TypeId == c.TypeId);
        var assignedTo  = c.AssignedTo.HasValue
                            ? await db.Employees.AsNoTracking()
                                .FirstOrDefaultAsync(e => e.EmployeeId == c.AssignedTo.Value)
                            : null;
        var transaction = c.TransactionId.HasValue
                            ? await db.Transactions.AsNoTracking()
                                .FirstOrDefaultAsync(t => t.TransactionId == c.TransactionId.Value)
                            : null;
        var product     = c.ProductId.HasValue
                            ? await db.Products.AsNoTracking()
                                .FirstOrDefaultAsync(p => p.ProductId == c.ProductId.Value)
                            : null;

        var daysOpen = c.Status == ComplaintStatus.Resolved && c.SolvedDate.HasValue
            ? (int)(c.SolvedDate.Value - (c.ComplaintDate ?? c.SolvedDate.Value)).TotalDays
            : (int)(DateTime.Now - (c.ComplaintDate ?? DateTime.Now)).TotalDays;

        var dto = new ComplaintDetailDto
        {
            ComplaintId       = c.ComplaintId,
            ComplaintDate     = c.ComplaintDate,
            Subject           = c.Subject,
            Details           = c.Details,
            Priority          = c.Priority,
            PriorityName      = ComplaintPriority.ToText(c.Priority),
            Status            = c.Status,
            StatusName        = ComplaintStatus.ToText(c.Status),
            Solution          = c.Solution,
            SolvedDate        = c.SolvedDate,

            PartyId           = c.PartyId,
            ClientName        = party?.PartyName,
            ClientPhone       = party?.Phone,
            ClientEmail       = party?.Email,
            ClientAddress     = party?.Address,

            TypeId            = c.TypeId,
            TypeName          = type?.TypeNameAr ?? type?.TypeName,

            AssignedTo        = c.AssignedTo,
            AssignedToName    = assignedTo?.FullName,

            TransactionId     = c.TransactionId,
            TransactionRef    = transaction?.ReferenceNumber,
            TransactionDate   = transaction?.TransactionDate,
            ProductId         = c.ProductId,
            ProductName       = product?.ProductName,

            Escalated         = c.Escalated,
            EscalatedDate     = c.EscalatedDate,
            EscalatedTo       = c.EscalatedTo,
            EscalationReason  = c.EscalationReason,

            SatisfactionLevel = c.SatisfactionLevel,
            CreatedBy         = c.CreatedBy,
            CreatedAt         = c.CreatedAt,
            DaysOpen          = daysOpen
        };

        // المتابعات
        dto.FollowUps = await db.ComplaintFollowUps.AsNoTracking()
            .Where(f => f.ComplaintId == complaintId)
            .OrderByDescending(f => f.FollowUpDate)
            .Select(f => new FollowUpItemDto
            {
                FollowUpId       = f.FollowUpId,
                FollowUpDate     = f.FollowUpDate,
                FollowUpBy       = f.FollowUpBy,
                FollowUpByName   = f.FollowUpByNavigation != null ? f.FollowUpByNavigation.FullName : null,
                Notes            = f.Notes,
                ActionTaken      = f.ActionTaken,
                NextFollowUpDate = f.NextFollowUpDate
            })
            .ToListAsync();

        // المرفقات
        dto.Attachments = await db.ComplaintAttachments.AsNoTracking()
            .Where(a => a.ComplaintId == complaintId)
            .OrderBy(a => a.UploadedAt)
            .Select(a => new ComplaintAttachmentDto
            {
                AttachmentId     = a.AttachmentId,
                FileName         = a.FileName,
                OriginalFileName = a.OriginalFileName,
                FilePath         = a.FilePath,
                FileSize         = a.FileSize,
                MimeType         = a.MimeType,
                UploadedAt       = a.UploadedAt,
                UploadedByName   = a.UploadedBy != null ? a.UploadedBy.FullName : null
            })
            .ToListAsync();

        return dto;
    }

    // ═══════════════════════════════════════════════════════
    //                  CREATE / UPDATE / DELETE
    // ═══════════════════════════════════════════════════════

    public async Task<(bool Success, string Message, int? ComplaintId)>
        CreateAsync(ComplaintFormDto dto, string currentUserName)
    {
        if (dto.PartyId <= 0)  return (false, "العميل مطلوب", null);
        if (dto.TypeId <= 0)   return (false, "نوع الشكوى مطلوب", null);
        if (string.IsNullOrWhiteSpace(dto.Subject)) return (false, "الموضوع مطلوب", null);
        if (string.IsNullOrWhiteSpace(dto.Details)) return (false, "التفاصيل مطلوبة", null);

        using var db = await _factory.CreateDbContextAsync();
        try
        {
            var entity = new Complaint
            {
                PartyId       = dto.PartyId,
                TypeId        = dto.TypeId,
                TransactionId = dto.TransactionId,
                ProductId     = dto.ProductId,
                OpportunityId = dto.OpportunityId,
                Subject       = dto.Subject.Trim(),
                Details       = dto.Details.Trim(),
                Priority      = dto.Priority,
                Status        = ComplaintStatus.New,
                AssignedTo    = dto.AssignedTo,
                CreatedBy     = currentUserName,
                CreatedAt     = DateTime.Now,
                ComplaintDate = DateTime.Now,
                IsActive      = true,
                Escalated     = false
            };

            db.Complaints.Add(entity);
            await db.SaveChangesAsync();
            return (true, "تم تسجيل الشكوى بنجاح", entity.ComplaintId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create complaint");
            return (false, "حدث خطأ: " + ex.Message, null);
        }
    }

    public async Task<(bool Success, string Message)>
        UpdateAsync(ComplaintFormDto dto, string currentUserName)
    {
        if (dto.ComplaintId is null or 0) return (false, "معرّف غير صحيح");

        using var db = await _factory.CreateDbContextAsync();
        var entity = await db.Complaints
            .FirstOrDefaultAsync(c => c.ComplaintId == dto.ComplaintId.Value);
        if (entity is null) return (false, "الشكوى غير موجودة");

        if (ComplaintStatus.IsClosed(entity.Status))
            return (false, "لا يمكن تعديل شكوى مغلقة");

        try
        {
            entity.PartyId       = dto.PartyId;
            entity.TypeId        = dto.TypeId;
            entity.TransactionId = dto.TransactionId;
            entity.ProductId     = dto.ProductId;
            entity.OpportunityId = dto.OpportunityId;
            entity.Subject       = dto.Subject.Trim();
            entity.Details       = dto.Details.Trim();
            entity.Priority      = dto.Priority;
            entity.AssignedTo    = dto.AssignedTo;

            await db.SaveChangesAsync();
            return (true, "تم التحديث بنجاح");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update complaint failed {Id}", dto.ComplaintId);
            return (false, "حدث خطأ: " + ex.Message);
        }
    }

    public async Task<(bool Success, string Message)> DeleteAsync(int complaintId)
    {
        using var db = await _factory.CreateDbContextAsync();
        var entity = await db.Complaints.FirstOrDefaultAsync(c => c.ComplaintId == complaintId);
        if (entity is null) return (false, "الشكوى غير موجودة");

        try
        {
            // حذف الملفات الفعلية للمرفقات
            var attachments = await db.ComplaintAttachments
                .Where(a => a.ComplaintId == complaintId).ToListAsync();
            foreach (var a in attachments) TryDeleteFile(a.FilePath);

            db.Complaints.Remove(entity);
            await db.SaveChangesAsync();
            return (true, "تم حذف الشكوى");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete complaint failed {Id}", complaintId);
            return (false, "حدث خطأ: " + ex.Message);
        }
    }

    // ═══════════════════════════════════════════════════════
    //                      WORKFLOW
    // ═══════════════════════════════════════════════════════

    public async Task<(bool Success, string Message)>
        ChangeStatusAsync(ChangeStatusDto dto, string currentUserName)
    {
        if (dto.NewStatus is < 1 or > 6) return (false, "حالة غير صالحة");

        using var db = await _factory.CreateDbContextAsync();
        var entity = await db.Complaints
            .FirstOrDefaultAsync(c => c.ComplaintId == dto.ComplaintId);
        if (entity is null) return (false, "الشكوى غير موجودة");

        if (entity.Status == dto.NewStatus) return (true, "الحالة لم تتغير");

        if (dto.NewStatus == ComplaintStatus.Resolved
            && string.IsNullOrWhiteSpace(dto.Solution))
            return (false, "الحل مطلوب عند 'تم الحل'");

        try
        {
            entity.Status = dto.NewStatus;

            if (dto.NewStatus == ComplaintStatus.Resolved)
            {
                entity.SolvedDate = DateTime.Now;
                entity.Solution   = dto.Solution?.Trim();
            }
            else if (dto.NewStatus == ComplaintStatus.New
                  || dto.NewStatus == ComplaintStatus.InProgress)
            {
                // لو رجعها للجديد/قيد الحل امسح تاريخ الحل
                entity.SolvedDate = null;
            }

            await db.SaveChangesAsync();
            return (true, $"تم تغيير الحالة إلى '{ComplaintStatus.ToText(dto.NewStatus)}'");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change status failed {Id}", dto.ComplaintId);
            return (false, "حدث خطأ: " + ex.Message);
        }
    }

    public async Task<(bool Success, string Message)>
        AssignAsync(AssignComplaintDto dto, string currentUserName)
    {
        using var db = await _factory.CreateDbContextAsync();
        var entity = await db.Complaints
            .FirstOrDefaultAsync(c => c.ComplaintId == dto.ComplaintId);
        if (entity is null) return (false, "الشكوى غير موجودة");

        try
        {
            entity.AssignedTo = dto.AssignedTo;

            // لو جديدة وتم إسنادها → قيد الحل
            if (entity.Status == ComplaintStatus.New && dto.AssignedTo.HasValue)
                entity.Status = ComplaintStatus.InProgress;

            await db.SaveChangesAsync();
            return (true, "تم الإسناد بنجاح");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Assign failed {Id}", dto.ComplaintId);
            return (false, "حدث خطأ: " + ex.Message);
        }
    }

    public async Task<(bool Success, string Message)>
        EscalateAsync(EscalateComplaintDto dto, int currentUserId, string currentUserName)
    {
        if (string.IsNullOrWhiteSpace(dto.EscalatedTo))
            return (false, "الجهة المُصعَّد إليها مطلوبة");

        using var db = await _factory.CreateDbContextAsync();
        var entity = await db.Complaints
            .FirstOrDefaultAsync(c => c.ComplaintId == dto.ComplaintId);
        if (entity is null) return (false, "الشكوى غير موجودة");

        try
        {
            entity.Escalated        = true;
            entity.EscalatedDate    = DateTime.Now;
            entity.EscalatedTo      = dto.EscalatedTo.Trim();
            entity.EscalationReason = dto.EscalationReason?.Trim();
            entity.EscalatedBy      = currentUserId;
            entity.Status           = ComplaintStatus.Escalated;

            await db.SaveChangesAsync();
            return (true, "تم تصعيد الشكوى");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Escalate failed {Id}", dto.ComplaintId);
            return (false, "حدث خطأ: " + ex.Message);
        }
    }

    public async Task<(bool Success, string Message)> RateAsync(RateComplaintDto dto)
    {
        if (dto.SatisfactionLevel is < 1 or > 5)
            return (false, "التقييم من 1 إلى 5");

        using var db = await _factory.CreateDbContextAsync();
        var entity = await db.Complaints
            .FirstOrDefaultAsync(c => c.ComplaintId == dto.ComplaintId);
        if (entity is null) return (false, "الشكوى غير موجودة");

        if (!ComplaintStatus.IsClosed(entity.Status))
            return (false, "التقييم متاح فقط بعد حل الشكوى");

        try
        {
            entity.SatisfactionLevel = dto.SatisfactionLevel;
            await db.SaveChangesAsync();
            return (true, "تم حفظ التقييم");
        }
        catch (Exception ex)
        {
            return (false, "حدث خطأ: " + ex.Message);
        }
    }

    // ═══════════════════════════════════════════════════════
    //                       FOLLOW-UPS
    // ═══════════════════════════════════════════════════════

    public async Task<(bool Success, string Message, int? FollowUpId)>
        AddFollowUpAsync(FollowUpFormDto dto, int currentEmployeeId)
    {
        if (dto.ComplaintId <= 0)               return (false, "معرّف الشكوى غير صحيح", null);
        if (currentEmployeeId <= 0)             return (false, "يجب أن يكون لك سجل موظف", null);
        if (string.IsNullOrWhiteSpace(dto.Notes)) return (false, "الملاحظات مطلوبة", null);

        using var db = await _factory.CreateDbContextAsync();
        try
        {
            var entity = new ComplaintFollowUp
            {
                ComplaintId      = dto.ComplaintId,
                FollowUpBy       = currentEmployeeId,
                FollowUpDate     = DateTime.Now,
                Notes            = dto.Notes.Trim(),
                ActionTaken      = string.IsNullOrWhiteSpace(dto.ActionTaken) ? null : dto.ActionTaken.Trim(),
                NextFollowUpDate = dto.NextFollowUpDate
            };
            db.ComplaintFollowUps.Add(entity);
            await db.SaveChangesAsync();
            return (true, "تمت إضافة المتابعة", entity.FollowUpId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Add follow-up failed");
            return (false, "حدث خطأ: " + ex.Message, null);
        }
    }

    public async Task<(bool Success, string Message)> DeleteFollowUpAsync(int followUpId)
    {
        using var db = await _factory.CreateDbContextAsync();
        var entity = await db.ComplaintFollowUps
            .FirstOrDefaultAsync(f => f.FollowUpId == followUpId);
        if (entity is null) return (false, "المتابعة غير موجودة");
        try
        {
            db.ComplaintFollowUps.Remove(entity);
            await db.SaveChangesAsync();
            return (true, "تم الحذف");
        }
        catch (Exception ex) { return (false, "حدث خطأ: " + ex.Message); }
    }

    // ═══════════════════════════════════════════════════════
    //                      ATTACHMENTS
    // ═══════════════════════════════════════════════════════

    public async Task<(bool Success, string Message, int? AttachmentId)>
        UploadAttachmentAsync(int complaintId, string originalFileName,
            byte[] content, string mimeType, int uploadedByUserId)
    {
        try
        {
            if (content.Length == 0) return (false, "الملف فارغ", null);
            const long maxSize = 10 * 1024 * 1024;
            if (content.Length > maxSize) return (false, "الحجم يتجاوز 10 ميجا", null);

            var uploadPath = Path.Combine(_env.WebRootPath, UploadFolder);
            Directory.CreateDirectory(uploadPath);

            var ext      = Path.GetExtension(originalFileName);
            var safeName = $"{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(uploadPath, safeName);
            await File.WriteAllBytesAsync(fullPath, content);

            using var db = await _factory.CreateDbContextAsync();
            var att = new ComplaintAttachment
            {
                ComplaintId      = complaintId,
                FileName         = safeName,
                OriginalFileName = originalFileName,
                FilePath         = $"/{UploadFolder}/{safeName}",
                FileSize         = content.Length,
                MimeType         = mimeType,
                UploadedByUserId = uploadedByUserId,
                UploadedAt       = DateTime.Now
            };
            db.ComplaintAttachments.Add(att);
            await db.SaveChangesAsync();
            return (true, "تم الرفع", att.AttachmentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload attachment failed");
            return (false, "حدث خطأ: " + ex.Message, null);
        }
    }

    public async Task<(bool Success, string Message)> DeleteAttachmentAsync(int attachmentId)
    {
        using var db = await _factory.CreateDbContextAsync();
        var att = await db.ComplaintAttachments
            .FirstOrDefaultAsync(a => a.AttachmentId == attachmentId);
        if (att is null) return (false, "المرفق غير موجود");
        try
        {
            TryDeleteFile(att.FilePath);
            db.ComplaintAttachments.Remove(att);
            await db.SaveChangesAsync();
            return (true, "تم الحذف");
        }
        catch (Exception ex) { return (false, "حدث خطأ: " + ex.Message); }
    }

    public async Task<(byte[] Content, string MimeType, string FileName)?>
        GetAttachmentAsync(int attachmentId)
    {
        using var db = await _factory.CreateDbContextAsync();
        var att = await db.ComplaintAttachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.AttachmentId == attachmentId);
        if (att is null) return null;

        var fullPath = Path.Combine(_env.WebRootPath, att.FilePath.TrimStart('/'));
        if (!File.Exists(fullPath)) return null;

        var content = await File.ReadAllBytesAsync(fullPath);
        return (content, att.MimeType, att.OriginalFileName);
    }

    // ═══════════════════════════════════════════════════════
    //                      DASHBOARD
    // ═══════════════════════════════════════════════════════

    public async Task<ComplaintsDashboardDto> GetDashboardAsync(
        DateTime? from = null, DateTime? to = null)
    {
        using var db = await _factory.CreateDbContextAsync();
        var view = db.VwComplaintsLists.AsNoTracking().AsQueryable();

        if (from.HasValue) view = view.Where(v => v.ComplaintDate >= from.Value.Date);
        if (to.HasValue)   view = view.Where(v => v.ComplaintDate <= to.Value.Date.AddDays(1).AddTicks(-1));

        var all = await view.ToListAsync();

        var openDays = all.Where(c => c.DaysOpen.HasValue).Select(c => (double)c.DaysOpen!.Value).ToList();
        var rated    = all.Where(c => c.SatisfactionLevel.HasValue).Select(c => (double)c.SatisfactionLevel!.Value).ToList();

        return new ComplaintsDashboardDto
        {
            TotalCount          = all.Count,
            NewCount            = all.Count(c => c.Status == ComplaintStatus.New),
            InProgressCount     = all.Count(c => c.Status == ComplaintStatus.InProgress),
            AwaitingClientCount = all.Count(c => c.Status == ComplaintStatus.AwaitingClient),
            ResolvedCount       = all.Count(c => c.Status == ComplaintStatus.Resolved),
            RejectedCount       = all.Count(c => c.Status == ComplaintStatus.Rejected),
            EscalatedCount      = all.Count(c => c.Status == ComplaintStatus.Escalated),

            AverageDaysOpen = openDays.Any() ? openDays.Average() : 0,
            AverageRating   = rated.Any()    ? rated.Average()    : 0,

            RecentComplaints = all.OrderByDescending(c => c.ComplaintDate).Take(5)
                .Select(MapVwToListItem).ToList(),

            OverdueComplaints = all.Where(c => c.Status != ComplaintStatus.Resolved
                                            && c.Status != ComplaintStatus.Rejected
                                            && (c.DaysOpen ?? 0) > 3)
                .OrderByDescending(c => c.DaysOpen ?? 0).Take(5)
                .Select(MapVwToListItem).ToList(),

            ByType = all.GroupBy(c => new { c.TypeId, c.ComplaintType })
                .Select(g => new TypeStatDto
                {
                    TypeId   = g.Key.TypeId,
                    TypeName = g.Key.ComplaintType ?? "—",
                    Count    = g.Count()
                })
                .OrderByDescending(x => x.Count).ToList()
        };
    }

    // ═══════════════════════════════════════════════════════
    //                       LOOKUPS
    // ═══════════════════════════════════════════════════════

    public async Task<List<ComplaintTypeDto>> GetTypesAsync(bool activeOnly = true)
    {
        using var db = await _factory.CreateDbContextAsync();
        var q = db.ComplaintTypes.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(t => t.IsActive == true);
        return await q.OrderBy(t => t.TypeName)
            .Select(t => new ComplaintTypeDto
            {
                TypeId     = t.TypeId,
                TypeName   = t.TypeName,
                TypeNameAr = t.TypeNameAr,
                IsActive   = t.IsActive ?? false
            }).ToListAsync();
    }

    public async Task<List<PartyLookupItemDto>> SearchPartiesAsync(string? search, int max = 20)
    {
        using var db = await _factory.CreateDbContextAsync();
        var q = db.Parties.AsNoTracking().Where(p => p.IsActive == true);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(p => p.PartyName.Contains(s) || (p.Phone != null && p.Phone.Contains(s)));
        }
        return await q.OrderBy(p => p.PartyName).Take(max)
            .Select(p => new PartyLookupItemDto
            {
                PartyId   = p.PartyId,
                PartyName = p.PartyName,
                Phone     = p.Phone
            }).ToListAsync();
    }

    public async Task<List<EmployeeLookupItemDto>> GetEmployeesAsync()
    {
        using var db = await _factory.CreateDbContextAsync();
        return await db.Employees.AsNoTracking()
            .Where(e => e.Status == "نشط" || e.Status == "Working" || e.Status == "Active")
            .OrderBy(e => e.FullName)
            .Select(e => new EmployeeLookupItemDto
            {
                EmployeeId = e.EmployeeId,
                FullName   = e.FullName,
                JobTitle   = e.JobTitle
            }).ToListAsync();
    }

    public async Task<List<TransactionLookupItemDto>> GetCustomerTransactionsAsync(int partyId, int max = 30)
    {
        using var db = await _factory.CreateDbContextAsync();
        return await db.Transactions.AsNoTracking()
            .Where(t => t.PartyId == partyId && t.TransactionType == "Sale")
            .OrderByDescending(t => t.TransactionDate)
            .Take(max)
            .Select(t => new TransactionLookupItemDto
            {
                TransactionId   = t.TransactionId,
                ReferenceNumber = t.ReferenceNumber,
                TransactionDate = t.TransactionDate,
                GrandTotal      = t.GrandTotal
            }).ToListAsync();
    }
    public async Task<List<ProductLookupItemDto>> GetTransactionProductsAsync(int transactionId)
    {
        using var db = await _factory.CreateDbContextAsync();
        return await db.TransactionDetails.AsNoTracking()
            .Where(td => td.TransactionId == transactionId)
            .Select(td => new ProductLookupItemDto
            {
                ProductId   = td.ProductId,
                ProductName = td.Product.ProductName,
                Description = td.Product.ProductDescription,
                Price       = td.UnitPrice
            })
            .Distinct()
            .OrderBy(p => p.ProductName)
            .ToListAsync();
    }
    public async Task<List<ProductLookupItemDto>> GetCustomerProductsAsync(int partyId, int max = 100)
    {
        using var db = await _factory.CreateDbContextAsync();
        // كل المنتجات اللي اشتراها العميل من قبل (من فواتيره)
        return await db.TransactionDetails.AsNoTracking()
            .Where(td => td.Transaction.PartyId == partyId
                      && td.Transaction.TransactionType == "Sale")
            .Select(td => new ProductLookupItemDto
            {
                ProductId   = td.ProductId,
                ProductName = td.Product.ProductName,
                Description = td.Product.ProductDescription,
                Price       = td.UnitPrice
            })
            .Distinct()
            .OrderBy(p => p.ProductName)
            .Take(max)
            .ToListAsync();
    }

    // ═══════════════════════════════════════════════════════
    //                  TYPES MANAGEMENT
    // ═══════════════════════════════════════════════════════

    public async Task<(bool Success, string Message, int? TypeId)>
        SaveTypeAsync(ComplaintTypeDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.TypeName))
            return (false, "اسم النوع مطلوب", null);

        using var db = await _factory.CreateDbContextAsync();
        ComplaintType entity;
        if (dto.TypeId <= 0)
        {
            entity = new ComplaintType { CreatedAt = DateTime.Now, IsActive = true };
            db.ComplaintTypes.Add(entity);
        }
        else
        {
            entity = await db.ComplaintTypes
                .FirstOrDefaultAsync(t => t.TypeId == dto.TypeId)
                ?? throw new Exception("Type not found");
        }

        entity.TypeName   = dto.TypeName.Trim();
        entity.TypeNameAr = dto.TypeNameAr?.Trim();
        entity.IsActive   = dto.IsActive;

        await db.SaveChangesAsync();
        return (true, "تم الحفظ", entity.TypeId);
    }

    public async Task<(bool Success, string Message)> DeleteTypeAsync(int typeId)
    {
        using var db = await _factory.CreateDbContextAsync();
        var hasComplaints = await db.Complaints.AnyAsync(c => c.TypeId == typeId);
        if (hasComplaints) return (false, "لا يمكن حذف نوع مرتبط بشكاوى");

        var entity = await db.ComplaintTypes.FirstOrDefaultAsync(t => t.TypeId == typeId);
        if (entity is null) return (false, "النوع غير موجود");

        db.ComplaintTypes.Remove(entity);
        await db.SaveChangesAsync();
        return (true, "تم الحذف");
    }

    // ═══════════════════════════════════════════════════════
    //                      EXCEL EXPORT
    // ═══════════════════════════════════════════════════════

    public async Task<byte[]> ExportToExcelAsync(ComplaintFilterDto filter)
    {
        filter.Page = 1;
        filter.PageSize = int.MaxValue;
        var result = await GetComplaintsAsync(filter);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("الشكاوى");
        ws.RightToLeft = true;

        var headers = new[] { "رقم", "التاريخ", "العميل", "الهاتف", "النوع",
                              "الموضوع", "الأولوية", "الحالة", "المُسند إليه",
                              "تاريخ الحل", "أيام مفتوحة", "تقييم" };
        for (int i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        ws.Row(1).Style.Font.Bold = true;
        ws.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1a237e");
        ws.Row(1).Style.Font.FontColor = XLColor.White;
        ws.Row(1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        int row = 2;
        foreach (var c in result.Items)
        {
            ws.Cell(row, 1).Value  = c.ComplaintId;
            ws.Cell(row, 2).Value  = c.ComplaintDate?.ToString("yyyy/MM/dd") ?? "";
            ws.Cell(row, 3).Value  = c.ClientName ?? "";
            ws.Cell(row, 4).Value  = c.ClientPhone ?? "";
            ws.Cell(row, 5).Value  = c.ComplaintType ?? "";
            ws.Cell(row, 6).Value  = c.Subject;
            ws.Cell(row, 7).Value  = c.PriorityName ?? "";
            ws.Cell(row, 8).Value  = c.StatusName ?? "";
            ws.Cell(row, 9).Value  = c.EmployeeName ?? "—";
            ws.Cell(row, 10).Value = c.SolvedDate?.ToString("yyyy/MM/dd") ?? "";
            ws.Cell(row, 11).Value = c.DaysOpen ?? 0;
            ws.Cell(row, 12).Value = c.SatisfactionLevel?.ToString() ?? "";
            row++;
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ═══════════════════════════════════════════════════════
    //                        HELPERS
    // ═══════════════════════════════════════════════════════

    private static ComplaintListItemDto MapVwToListItem(VwComplaintsList c) => new()
    {
        ComplaintId       = c.ComplaintId,
        ComplaintDate     = c.ComplaintDate,
        PartyId           = c.PartyId,
        ClientName        = c.ClientName,
        ClientPhone       = c.ClientPhone,
        TypeId            = c.TypeId,
        ComplaintType     = c.ComplaintType,
        Subject           = c.Subject,
        Priority          = c.Priority,
        PriorityName      = c.PriorityName,
        Status            = c.Status,
        StatusName        = c.StatusName,
        AssignedTo        = c.AssignedTo,
        EmployeeName      = c.EmployeeName,
        SolvedDate        = c.SolvedDate,
        DaysOpen          = c.DaysOpen,
        Escalated         = c.Escalated,
        TransactionId     = c.TransactionId,
        ProductId         = c.ProductId,
        ProductName       = c.ProductName,
        SatisfactionLevel = c.SatisfactionLevel,
        AttachmentsCount  = c.AttachmentsCount,
        FollowUpsCount    = c.FollowUpsCount
    };

    private void TryDeleteFile(string relativePath)
    {
        try
        {
            var full = Path.Combine(_env.WebRootPath, relativePath.TrimStart('/'));
            if (File.Exists(full)) File.Delete(full);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Delete file failed {Path}", relativePath);
        }
    }
}
