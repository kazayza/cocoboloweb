using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class B2BRequestService : IB2BRequestService
{
    private readonly IDbContextFactory<db24804Context> _factory;

    public B2BRequestService(IDbContextFactory<db24804Context> factory)
    {
        _factory = factory;
    }

    public async Task<List<B2BProductLookupDto>> SearchProductsAsync(string? searchText, int take = 20)
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
                ProductDescription = string.IsNullOrWhiteSpace(x.ProductDescription) ? null : x.ProductDescription
            })
            .ToListAsync();
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
                PartyId = x.PartyId,
                PartyName = x.Party.PartyName,
                PortalUserId = x.PortalUserId,
                PortalUserName = x.PortalUser.FullName,
                ResponsibleEmployeeId = x.ResponsibleEmployeeId,
                ResponsibleEmployeeName = x.ResponsibleEmployee != null ? x.ResponsibleEmployee.FullName : null,
                RelatedQuotationId = x.RelatedQuotationId,
                RelatedInvoiceId = x.RelatedInvoiceId,
                ItemsCount = x.Items.Count,
                Notes = x.Notes,
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
                PartyId = x.PartyId,
                PartyName = x.Party.PartyName,
                PortalUserId = x.PortalUserId,
                PortalUserName = x.PortalUser.FullName,
                ResponsibleEmployeeId = x.ResponsibleEmployeeId,
                ResponsibleEmployeeName = x.ResponsibleEmployee != null ? x.ResponsibleEmployee.FullName : null,
                RelatedQuotationId = x.RelatedQuotationId,
                RelatedInvoiceId = x.RelatedInvoiceId,
                ItemsCount = x.Items.Count,
                Notes = x.Notes,
                CreatedAt = x.CreatedAt,
                CreatedBy = x.CreatedBy,
                HandledAt = x.HandledAt,
                HandledBy = x.HandledBy,
                Items = x.Items.Select(i => new B2BRequestItemDto
                {
                    RequestItemId = i.RequestItemId,
                    ProductId = i.ProductId,
                    ProductName = i.Product != null ? i.Product.ProductName : null,
                    Quantity = i.Quantity,
                    Notes = i.Notes
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

    public async Task<(bool Success, string Message)> UpdateStatusAsync(int requestId, string newStatus, string? handledBy, string? notes = null, int? quotationId = null, int? invoiceId = null)
    {
        if (!B2BRequestStatuses.All.Contains(newStatus))
            return (false, "الحالة غير صحيحة");

        await using var db = await _factory.CreateDbContextAsync();
        var request = await db.B2BRequests.FirstOrDefaultAsync(x => x.RequestId == requestId);
        if (request == null)
            return (false, "الطلب غير موجود");

        request.Status = newStatus;
        request.HandledAt = DateTime.Now;
        request.HandledBy = handledBy;
        if (!string.IsNullOrWhiteSpace(notes))
            request.Notes = notes.Trim();
        if (quotationId.HasValue)
            request.RelatedQuotationId = quotationId.Value;
        if (invoiceId.HasValue)
            request.RelatedInvoiceId = invoiceId.Value;

        await db.SaveChangesAsync();
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
                CreatedAt = DateTime.Now
            });
        }

        await db.SaveChangesAsync();
    }
}
