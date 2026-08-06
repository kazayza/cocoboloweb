using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class B2BRequestService : IB2BRequestService
{
    private readonly IDbContextFactory<db24804Context> _factory;
    private readonly IWebHostEnvironment _env;

    public B2BRequestService(IDbContextFactory<db24804Context> factory, IWebHostEnvironment env)
    {
        _factory = factory;
        _env = env;
    }

    public async Task<List<B2BProductLookupDto>> SearchProductsAsync(string? searchText, int take = 50)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var query = db.Products.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var s = searchText.Trim();
            query = query.Where(x => x.ProductName.Contains(s) || x.ProductDescription.Contains(s));
        }

        return await query
            .OrderBy(x => x.ProductName)
            .Take(take)
            .Select(x => new B2BProductLookupDto
            {
                ProductId = x.ProductId,
                ProductName = x.ProductName,
                ProductDescription = string.IsNullOrWhiteSpace(x.ProductDescription) ? null : x.ProductDescription,
                ImageUrl = db.ProductImages.Any(pi => pi.ProductId == x.ProductId)
                    ? $"/api/product-images/{x.ProductId}"
                    : null
            })
            .ToListAsync();
    }

    public async Task<List<B2BQuotationLookupDto>> SearchQuotationsAsync(int partyId, string? searchText, int take = 20)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var query = db.Quotations.AsNoTracking().Where(x => x.PartyId == partyId);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var s = searchText.Trim();
            if (int.TryParse(s, out var quoteId))
                query = query.Where(x => x.QuotationId == quoteId || (x.ReferenceNumber != null && x.ReferenceNumber.Contains(s)));
            else
                query = query.Where(x => x.ReferenceNumber != null && x.ReferenceNumber.Contains(s));
        }

        return await query
            .OrderByDescending(x => x.QuotationDate)
            .ThenByDescending(x => x.QuotationId)
            .Take(take)
            .Select(x => new B2BQuotationLookupDto
            {
                QuotationId = x.QuotationId,
                ReferenceNumber = x.ReferenceNumber ?? ("#" + x.QuotationId),
                QuotationDate = x.QuotationDate,
                PartyId = x.PartyId,
                PartyName = db.Parties.Where(p => p.PartyId == x.PartyId).Select(p => p.PartyName).FirstOrDefault() ?? string.Empty,
                Status = x.Status,
                GrandTotal = x.GrandTotal ?? x.TotalAmount,
                InvoiceId = x.InvoiceId,
                InvoiceReferenceNumber = x.InvoiceId.HasValue
                    ? db.Transactions.Where(t => t.TransactionId == x.InvoiceId.Value).Select(t => t.ReferenceNumber).FirstOrDefault()
                    : null
            })
            .ToListAsync();
    }

    public async Task<List<B2BInvoiceLookupDto>> SearchInvoicesAsync(int partyId, string? searchText, int take = 20)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var query = db.Transactions.AsNoTracking()
            .Where(x => x.TransactionType == TransactionTypes.Sale && x.PartyId == partyId && x.InvoiceStatus != "Cancelled");

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var s = searchText.Trim();
            if (int.TryParse(s, out var invoiceIdSearch))
                query = query.Where(x => x.TransactionId == invoiceIdSearch || (x.ReferenceNumber != null && x.ReferenceNumber.Contains(s)));
            else
                query = query.Where(x => x.ReferenceNumber != null && x.ReferenceNumber.Contains(s));
        }

        return await query
            .OrderByDescending(x => x.TransactionDate)
            .ThenByDescending(x => x.TransactionId)
            .Take(take)
            .Select(x => new B2BInvoiceLookupDto
            {
                TransactionId = x.TransactionId,
                ReferenceNumber = x.ReferenceNumber ?? ("#" + x.TransactionId),
                TransactionDate = x.TransactionDate,
                PartyId = x.PartyId,
                PartyName = db.Parties.Where(p => p.PartyId == x.PartyId).Select(p => p.PartyName).FirstOrDefault() ?? string.Empty,
                Status = x.InvoiceStatus,
                GrandTotal = x.GrandTotal,
                PaidAmount = x.PaidAmount,
                QuotationId = db.Quotations.Where(q => q.InvoiceId == x.TransactionId).Select(q => (int?)q.QuotationId).FirstOrDefault(),
                QuotationReferenceNumber = db.Quotations.Where(q => q.InvoiceId == x.TransactionId).Select(q => q.ReferenceNumber).FirstOrDefault()
            })
            .ToListAsync();
    }

    public async Task<B2BQuotationLookupDto?> GetQuotationLookupByIdAsync(int quotationId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Quotations.AsNoTracking()
            .Where(x => x.QuotationId == quotationId)
            .Select(x => new B2BQuotationLookupDto
            {
                QuotationId = x.QuotationId,
                ReferenceNumber = x.ReferenceNumber ?? ("#" + x.QuotationId),
                QuotationDate = x.QuotationDate,
                PartyId = x.PartyId,
                PartyName = db.Parties.Where(p => p.PartyId == x.PartyId).Select(p => p.PartyName).FirstOrDefault() ?? string.Empty,
                Status = x.Status,
                GrandTotal = x.GrandTotal ?? x.TotalAmount,
                InvoiceId = x.InvoiceId,
                InvoiceReferenceNumber = x.InvoiceId.HasValue
                    ? db.Transactions.Where(t => t.TransactionId == x.InvoiceId.Value).Select(t => t.ReferenceNumber).FirstOrDefault()
                    : null
            })
            .FirstOrDefaultAsync();
    }

    public async Task<B2BInvoiceLookupDto?> GetInvoiceLookupByIdAsync(int invoiceId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Transactions.AsNoTracking()
            .Where(x => x.TransactionId == invoiceId && x.TransactionType == TransactionTypes.Sale)
            .Select(x => new B2BInvoiceLookupDto
            {
                TransactionId = x.TransactionId,
                ReferenceNumber = x.ReferenceNumber ?? ("#" + x.TransactionId),
                TransactionDate = x.TransactionDate,
                PartyId = x.PartyId,
                PartyName = db.Parties.Where(p => p.PartyId == x.PartyId).Select(p => p.PartyName).FirstOrDefault() ?? string.Empty,
                Status = x.InvoiceStatus,
                GrandTotal = x.GrandTotal,
                PaidAmount = x.PaidAmount,
                QuotationId = db.Quotations.Where(q => q.InvoiceId == x.TransactionId).Select(q => (int?)q.QuotationId).FirstOrDefault(),
                QuotationReferenceNumber = db.Quotations.Where(q => q.InvoiceId == x.TransactionId).Select(q => q.ReferenceNumber).FirstOrDefault()
            })
            .FirstOrDefaultAsync();
    }

    public async Task<List<B2BRequestListDto>> GetRequestsAsync(int? partyId = null, string? status = null, int? responsibleEmployeeId = null)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var query = db.B2BRequests.AsNoTracking().AsQueryable();

        if (partyId.HasValue)
            query = query.Where(x => x.PartyId == partyId.Value);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);
        if (responsibleEmployeeId.HasValue)
            query = query.Where(x => x.ResponsibleEmployeeId == responsibleEmployeeId.Value);

        return await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new B2BRequestListDto
            {
                RequestId = x.RequestId,
                RequestType = x.RequestType,
                Status = x.Status,
                RequestSource = x.RequestSource,
                PartyId = x.PartyId,
                PartyName = x.Party.PartyName,
                PortalUserId = x.PortalUserId,
                PortalUserName = x.PortalUser != null ? x.PortalUser.FullName : string.Empty,
                RequestedContactName = x.RequestedContactName,
                RequestedContactPhone = x.RequestedContactPhone,
                ResponsibleEmployeeId = x.ResponsibleEmployeeId,
                ResponsibleEmployeeName = x.ResponsibleEmployee != null ? x.ResponsibleEmployee.FullName : null,
                RelatedQuotationId = x.RelatedQuotationId,
                RelatedInvoiceId = x.RelatedInvoiceId,
                ItemsCount = x.Items.Count,
                Notes = x.Notes,
                InternalNotes = x.InternalNotes,
                CustomerResponse = x.CustomerResponse,
                CreatedAt = x.CreatedAt,
                CreatedBy = x.CreatedBy,
                HandledAt = x.HandledAt,
                HandledBy = x.HandledBy
            })
            .ToListAsync();
    }

    public async Task<B2BRequestDetailDto?> GetByIdAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var request = await db.B2BRequests.AsNoTracking()
            .Where(x => x.RequestId == id)
            .Select(x => new B2BRequestDetailDto
            {
                RequestId = x.RequestId,
                RequestType = x.RequestType,
                Status = x.Status,
                RequestSource = x.RequestSource,
                PartyId = x.PartyId,
                PartyName = x.Party.PartyName,
                PortalUserId = x.PortalUserId,
                PortalUserName = x.PortalUser != null ? x.PortalUser.FullName : string.Empty,
                RequestedContactName = x.RequestedContactName,
                RequestedContactPhone = x.RequestedContactPhone,
                ResponsibleEmployeeId = x.ResponsibleEmployeeId,
                ResponsibleEmployeeName = x.ResponsibleEmployee != null ? x.ResponsibleEmployee.FullName : null,
                RelatedQuotationId = x.RelatedQuotationId,
                RelatedInvoiceId = x.RelatedInvoiceId,
                ItemsCount = x.Items.Count,
                Notes = x.Notes,
                InternalNotes = x.InternalNotes,
                CustomerResponse = x.CustomerResponse,
                CreatedAt = x.CreatedAt,
                CreatedBy = x.CreatedBy,
                HandledAt = x.HandledAt,
                HandledBy = x.HandledBy,
                Items = x.Items.Select(i => new B2BRequestItemDto
                {
                    RequestItemId = i.RequestItemId,
                    ProductId = i.ProductId,
                    ProductName = i.Product != null ? i.Product.ProductName : null,
                    ProductImageUrl = i.ProductId.HasValue && db.ProductImages.Any(pi => pi.ProductId == i.ProductId.Value)
                        ? $"/api/product-images/{i.ProductId.Value}"
                        : null,
                    Quantity = i.Quantity,
                    Notes = i.Notes
                }).ToList(),
                Attachments = x.Attachments.Select(a => new B2BRequestAttachmentDto
                {
                    AttachmentId = a.AttachmentId,
                    FileName = a.FileName,
                    RelativePath = a.RelativePath,
                    ContentType = a.ContentType,
                    FileSizeBytes = a.FileSizeBytes,
                    UploadedAt = a.UploadedAt,
                    UploadedBy = a.UploadedBy
                }).ToList()
            })
            .FirstOrDefaultAsync();

        return request;
    }

    public async Task<(bool Success, string Message, int? Id)> CreateAsync(B2BCreateRequestDto dto, int portalUserId, int partyId, int? responsibleEmployeeId, string currentUserName)
    {
        if (!B2BRequestTypes.All.Contains(dto.RequestType))
            return (false, "نوع الطلب غير صحيح", null);

        await using var db = await _factory.CreateDbContextAsync();

        var request = new B2BRequest
        {
            PartyId = partyId,
            PortalUserId = portalUserId,
            ResponsibleEmployeeId = responsibleEmployeeId,
            RequestType = dto.RequestType,
            Status = B2BRequestStatuses.New,
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
            CreatedAt = DateTime.Now,
            CreatedBy = currentUserName
        };

        foreach (var item in dto.Items.Where(x => x.ProductId.HasValue || x.SelectedProduct != null || !string.IsNullOrWhiteSpace(x.Notes)))
        {
            request.Items.Add(new B2BRequestItem
            {
                ProductId = item.ProductId ?? item.SelectedProduct?.ProductId,
                Quantity = item.Quantity <= 0 ? 1 : item.Quantity,
                Notes = string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes.Trim()
            });
        }

        db.B2BRequests.Add(request);
        await db.SaveChangesAsync();

        await NotifyNewRequestAsync(db, request);

        return (true, "تم إرسال الطلب بنجاح", request.RequestId);
    }

    public async Task<(bool Success, string Message, int? Id)> CreateInternalAsync(B2BInternalCreateRequestDto dto, string currentUserName)
    {
        if (!dto.PartyId.HasValue)
            return (false, "اختر العميل أولاً", null);

        if (!B2BRequestTypes.All.Contains(dto.RequestType))
            return (false, "نوع الطلب غير صحيح", null);

        await using var db = await _factory.CreateDbContextAsync();

        var request = new B2BRequest
        {
            PartyId = dto.PartyId.Value,
            PortalUserId = dto.PortalUserId,
            ResponsibleEmployeeId = dto.ResponsibleEmployeeId,
            RequestType = dto.RequestType,
            Status = B2BRequestStatuses.New,
            RequestSource = string.IsNullOrWhiteSpace(dto.RequestSource) ? "Internal" : dto.RequestSource.Trim(),
            RequestedContactName = string.IsNullOrWhiteSpace(dto.RequestedContactName) ? null : dto.RequestedContactName.Trim(),
            RequestedContactPhone = string.IsNullOrWhiteSpace(dto.RequestedContactPhone) ? null : dto.RequestedContactPhone.Trim(),
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
            CreatedAt = DateTime.Now,
            CreatedBy = currentUserName
        };

        foreach (var item in dto.Items.Where(x => x.ProductId.HasValue || x.SelectedProduct != null || !string.IsNullOrWhiteSpace(x.Notes)))
        {
            request.Items.Add(new B2BRequestItem
            {
                ProductId = item.ProductId ?? item.SelectedProduct?.ProductId,
                Quantity = item.Quantity <= 0 ? 1 : item.Quantity,
                Notes = string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes.Trim()
            });
        }

        db.B2BRequests.Add(request);
        await db.SaveChangesAsync();
        return (true, "تم إنشاء الطلب نيابة عن العميل", request.RequestId);
    }

    public async Task<(bool Success, string Message)> UploadAttachmentsAsync(int requestId, IReadOnlyList<IBrowserFile> files, string currentUserName)
    {
        if (files == null || files.Count == 0)
            return (true, "لا توجد مرفقات للرفع");

        await using var db = await _factory.CreateDbContextAsync();
        var requestExists = await db.B2BRequests.AsNoTracking().AnyAsync(x => x.RequestId == requestId);
        if (!requestExists)
            return (false, "الطلب غير موجود");

        var webRoot = _env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var relativeFolder = Path.Combine("uploads", "b2b-requests", requestId.ToString());
        var absoluteFolder = Path.Combine(webRoot, relativeFolder);
        Directory.CreateDirectory(absoluteFolder);

        foreach (var file in files.Take(10))
        {
            if (file.Size <= 0) continue;
            if (file.Size > 10 * 1024 * 1024)
                return (false, $"الملف {file.Name} يتجاوز الحد الأقصى 10MB");

            var safeOriginal = Path.GetFileName(file.Name);
            var ext = Path.GetExtension(safeOriginal);
            var stored = $"{Guid.NewGuid():N}{ext}";
            var absolutePath = Path.Combine(absoluteFolder, stored);

            await using var stream = file.OpenReadStream(10 * 1024 * 1024);
            await using var fs = new FileStream(absolutePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fs);

            db.B2BRequestAttachments.Add(new B2BRequestAttachment
            {
                RequestId = requestId,
                FileName = safeOriginal,
                StoredFileName = stored,
                RelativePath = "/" + relativeFolder.Replace('\\', '/') + "/" + stored,
                ContentType = file.ContentType,
                FileSizeBytes = file.Size,
                UploadedAt = DateTime.Now,
                UploadedBy = currentUserName
            });
        }

        await db.SaveChangesAsync();
        return (true, "تم رفع المرفقات بنجاح");
    }

    public async Task<(bool Success, string Message)> UpdateStatusAsync(int requestId, string newStatus, string? handledBy, string? internalNotes = null, string? customerResponse = null, int? quotationId = null, int? invoiceId = null)
    {
        if (!B2BRequestStatuses.All.Contains(newStatus))
            return (false, "الحالة غير صحيحة");

        await using var db = await _factory.CreateDbContextAsync();
        var request = await db.B2BRequests.FirstOrDefaultAsync(x => x.RequestId == requestId);
        if (request == null)
            return (false, "الطلب غير موجود");

        Quotation? selectedQuotation = null;
        Transaction? selectedInvoice = null;

        if (quotationId.HasValue)
        {
            selectedQuotation = await db.Quotations.FirstOrDefaultAsync(x => x.QuotationId == quotationId.Value);
            if (selectedQuotation == null)
                return (false, "عرض السعر المختار غير موجود");

            if (selectedQuotation.PartyId != request.PartyId)
                return (false, "عرض السعر المختار لا يخص نفس العميل المرتبط بالطلب");
        }

        if (invoiceId.HasValue)
        {
            selectedInvoice = await db.Transactions.FirstOrDefaultAsync(x => x.TransactionId == invoiceId.Value && x.TransactionType == TransactionTypes.Sale);
            if (selectedInvoice == null)
                return (false, "الفاتورة المختارة غير موجودة");

            if (selectedInvoice.PartyId != request.PartyId)
                return (false, "الفاتورة المختارة لا تخص نفس العميل المرتبط بالطلب");
        }

        if (selectedQuotation?.InvoiceId is int quotationInvoiceId)
        {
            if (!invoiceId.HasValue)
            {
                invoiceId = quotationInvoiceId;
                selectedInvoice = await db.Transactions.FirstOrDefaultAsync(x => x.TransactionId == quotationInvoiceId && x.TransactionType == TransactionTypes.Sale);
            }
            else if (invoiceId.Value != quotationInvoiceId)
            {
                return (false, "الفاتورة المختارة لا تطابق الفاتورة المرتبطة بعرض السعر المحدد");
            }
        }

        if (selectedInvoice?.PartyId == request.PartyId && !quotationId.HasValue)
        {
            var quotationFromInvoice = await db.Quotations.AsNoTracking()
                .Where(x => x.InvoiceId == selectedInvoice.TransactionId)
                .Select(x => (int?)x.QuotationId)
                .FirstOrDefaultAsync();

            if (quotationFromInvoice.HasValue)
                quotationId = quotationFromInvoice.Value;
        }

        request.Status = newStatus;
        request.HandledAt = DateTime.Now;
        request.HandledBy = handledBy;
        request.InternalNotes = string.IsNullOrWhiteSpace(internalNotes) ? null : internalNotes.Trim();
        request.CustomerResponse = string.IsNullOrWhiteSpace(customerResponse) ? null : customerResponse.Trim();
        request.RelatedQuotationId = quotationId;
        request.RelatedInvoiceId = invoiceId;

        await db.SaveChangesAsync();
        await NotifyPortalStatusChangeAsync(db, request, customerResponse);
        return (true, "تم تحديث حالة الطلب");
    }

    private static string GetRequestTypeText(string requestType) => requestType switch
    {
        B2BRequestTypes.Quotation => "طلب عرض سعر",
        B2BRequestTypes.Reorder => "إعادة طلب",
        B2BRequestTypes.PaymentProof => "إثبات دفع",
        B2BRequestTypes.Support => "دعم / استفسار",
        _ => requestType
    };

    private async Task NotifyNewRequestAsync(db24804Context db, B2BRequest request)
    {
        var partyName = await db.Parties.AsNoTracking()
            .Where(x => x.PartyId == request.PartyId)
            .Select(x => x.PartyName)
            .FirstOrDefaultAsync() ?? $"عميل #{request.PartyId}";

        var portalUserName = await db.B2BPortalUsers.AsNoTracking()
            .Where(x => x.PortalUserId == request.PortalUserId)
            .Select(x => x.FullName)
            .FirstOrDefaultAsync() ?? request.CreatedBy;

        var title = $"طلب B2B جديد — {GetRequestTypeText(request.RequestType)}";
        var message = $"العميل {partyName} أرسل {GetRequestTypeText(request.RequestType)} عبر البوابة بواسطة {portalUserName}.";

        db.Notifications.Add(new Notification
        {
            Title = title,
            Message = message,
            RecipientUser = "Admin",
            CreatedBy = request.CreatedBy,
            FormName = "admin/b2b/requests",
            RelatedTable = "B2BRequests",
            RelatedId = request.RequestId,
            CreatedAt = DateTime.Now
        });

        var b2bUsers = await (
            from u in db.Users.AsNoTracking()
            join up in db.UserPermissions.AsNoTracking() on u.UserId equals up.UserId
            join p in db.Permissions.AsNoTracking() on up.PermissionId equals p.PermissionId
            where u.IsActive == true
               && up.CanView
               && p.FormName == B2BPermissions.FormName
               && (u.Role == null || u.Role != "Admin")
            select u.Username
        )
        .Distinct()
        .ToListAsync();

        foreach (var recipient in b2bUsers)
        {
            db.Notifications.Add(new Notification
            {
                Title = title,
                Message = message,
                RecipientUser = recipient,
                CreatedBy = request.CreatedBy,
                FormName = "admin/b2b/requests",
                RelatedTable = "B2BRequests",
                RelatedId = request.RequestId,
                CreatedAt = DateTime.Now
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task NotifyPortalStatusChangeAsync(db24804Context db, B2BRequest request, string? notes)
    {
        var portalRecipientKeys = new List<string>();

        if (request.PortalUserId.HasValue)
        {
            portalRecipientKeys.Add(B2BPermissions.GetPortalRecipientKey(request.PortalUserId.Value));
        }
        else
        {
            var partyPortalUsers = await db.B2BPortalUsers.AsNoTracking()
                .Where(x => x.PartyId == request.PartyId && x.IsActive)
                .Select(x => x.PortalUserId)
                .ToListAsync();

            portalRecipientKeys.AddRange(partyPortalUsers.Select(B2BPermissions.GetPortalRecipientKey));
        }

        if (!portalRecipientKeys.Any())
            return;

        var title = request.Status switch
        {
            B2BRequestStatuses.UnderReview => "تمت مراجعة طلبك",
            B2BRequestStatuses.Converted => "تم تحويل طلبك",
            B2BRequestStatuses.Closed => "تم إغلاق طلبك",
            B2BRequestStatuses.Rejected => "تم رفض طلبك",
            _ => "تحديث على طلبك"
        };

        var message = request.Status switch
        {
            B2BRequestStatuses.UnderReview => "طلبك الآن تحت المراجعة من الفريق المختص.",
            B2BRequestStatuses.Converted => "تم تحويل طلبك إلى إجراء داخلي وسيتم استكمال المتابعة.",
            B2BRequestStatuses.Closed => "تم إغلاق طلبك. يمكنك مراجعة التفاصيل من داخل البوابة.",
            B2BRequestStatuses.Rejected => "تم تحديث طلبك إلى مرفوض. راجع التفاصيل أو تواصل معنا.",
            _ => "تم تحديث حالة طلبك."
        };

        if (request.RelatedQuotationId.HasValue)
            message += $" مرتبط بعرض سعر رقم {request.RelatedQuotationId.Value}.";

        if (request.RelatedInvoiceId.HasValue)
            message += $" مرتبط بفاتورة رقم {request.RelatedInvoiceId.Value}.";

        if (!string.IsNullOrWhiteSpace(notes))
            message += $" ملاحظة: {notes.Trim()}";

        foreach (var recipientKey in portalRecipientKeys.Distinct())
        {
            db.Notifications.Add(new Notification
            {
                Title = title,
                Message = message,
                RecipientUser = recipientKey,
                CreatedBy = request.HandledBy ?? request.CreatedBy,
                FormName = "b2b/requests",
                RelatedTable = "B2BRequests",
                RelatedId = request.RequestId,
                CreatedAt = DateTime.Now
            });
        }

        await db.SaveChangesAsync();
    }
}
