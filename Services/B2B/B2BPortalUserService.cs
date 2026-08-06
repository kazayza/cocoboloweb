using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class B2BPortalUserService : IB2BPortalUserService
{
    private readonly IDbContextFactory<db24804Context> _factory;

    public B2BPortalUserService(IDbContextFactory<db24804Context> factory)
    {
        _factory = factory;
    }

    public async Task<List<B2BPortalUserListDto>> GetUsersAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();

        var users = await db.B2BPortalUsers.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new B2BPortalUserListDto
            {
                PortalUserId = x.PortalUserId,
                PartyId = x.PartyId,
                PartyName = x.Party.PartyName,
                ResponsibleEmployeeId = x.ResponsibleEmployeeId,
                ResponsibleEmployeeName = x.ResponsibleEmployee != null ? x.ResponsibleEmployee.FullName : null,
                FullName = x.FullName,
                UserName = x.UserName,
                Email = x.Email,
                Mobile = x.Mobile,
                IsActive = x.IsActive,
                CanViewPrices = x.CanViewPrices,
                CanViewFinancials = x.CanViewFinancials,
                CanRequestQuotation = x.CanRequestQuotation,
                CanUploadPaymentProof = x.CanUploadPaymentProof,
                LastLogin = x.LastLogin,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync();

        return users;
    }

    public async Task<B2BPortalUserFormDto?> GetForEditAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();

        return await db.B2BPortalUsers.AsNoTracking()
            .Where(x => x.PortalUserId == id)
            .Select(x => new B2BPortalUserFormDto
            {
                PortalUserId = x.PortalUserId,
                PartyId = x.PartyId,
                ResponsibleEmployeeId = x.ResponsibleEmployeeId,
                FullName = x.FullName,
                UserName = x.UserName,
                Email = x.Email,
                Mobile = x.Mobile,
                IsActive = x.IsActive,
                CanViewPrices = x.CanViewPrices,
                CanViewFinancials = x.CanViewFinancials,
                CanRequestQuotation = x.CanRequestQuotation,
                CanUploadPaymentProof = x.CanUploadPaymentProof
            })
            .FirstOrDefaultAsync();
    }

    public async Task<List<B2BLookupDto>> SearchPartyLookupsAsync(string? searchText, int take = 20)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var query = db.Parties.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var s = searchText.Trim();
            query = query.Where(x =>
                (x.PartyName != null && x.PartyName.Contains(s)) ||
                (x.Phone != null && x.Phone.Contains(s)) ||
                (x.Phone2 != null && x.Phone2.Contains(s)));
        }

        return await query
            .OrderBy(x => x.PartyName)
            .Take(take)
            .Select(x => new B2BLookupDto { Id = x.PartyId, Name = x.PartyName ?? $"عميل #{x.PartyId}" })
            .ToListAsync();
    }

    public async Task<B2BLookupDto?> GetPartyLookupByIdAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Parties.AsNoTracking()
            .Where(x => x.PartyId == id)
            .Select(x => new B2BLookupDto { Id = x.PartyId, Name = x.PartyName ?? $"عميل #{x.PartyId}" })
            .FirstOrDefaultAsync();
    }

    public async Task<List<B2BLookupDto>> GetEmployeeLookupsAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Employees.AsNoTracking()
            .Where(x => x.Status == "نشط")
            .OrderBy(x => x.FullName)
            .Select(x => new B2BLookupDto { Id = x.EmployeeId, Name = x.FullName })
            .ToListAsync();
    }

    public async Task<List<B2BLookupDto>> GetPortalUserLookupsByPartyAsync(int partyId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.B2BPortalUsers.AsNoTracking()
            .Where(x => x.PartyId == partyId && x.IsActive)
            .OrderBy(x => x.FullName)
            .Select(x => new B2BLookupDto { Id = x.PortalUserId, Name = x.FullName + " — " + x.UserName })
            .ToListAsync();
    }

    public async Task<(bool Success, string Message, int? Id)> SaveAsync(B2BPortalUserFormDto dto, string currentUserName)
    {
        if (!dto.PartyId.HasValue)
            return (false, "اختر العميل أولاً", null);

        if (string.IsNullOrWhiteSpace(dto.FullName) || string.IsNullOrWhiteSpace(dto.UserName))
            return (false, "الاسم واسم المستخدم مطلوبان", null);

        await using var db = await _factory.CreateDbContextAsync();

        var normalizedUserName = dto.UserName.Trim();
        var exists = await db.B2BPortalUsers.AsNoTracking()
            .AnyAsync(x => x.UserName == normalizedUserName && x.PortalUserId != dto.PortalUserId);
        if (exists)
            return (false, "اسم المستخدم مستخدم بالفعل", null);

        B2BPortalUser entity;
        if (dto.PortalUserId > 0)
        {
            entity = await db.B2BPortalUsers.FirstOrDefaultAsync(x => x.PortalUserId == dto.PortalUserId)
                     ?? new B2BPortalUser();
            if (entity.PortalUserId == 0)
                return (false, "الحساب غير موجود", null);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(dto.Password))
                return (false, "كلمة المرور مطلوبة عند إنشاء الحساب", null);

            entity = new B2BPortalUser
            {
                CreatedAt = DateTime.Now,
                CreatedBy = currentUserName
            };
            db.B2BPortalUsers.Add(entity);
        }

        entity.PartyId = dto.PartyId.Value;
        entity.ResponsibleEmployeeId = dto.ResponsibleEmployeeId;
        entity.FullName = dto.FullName.Trim();
        entity.UserName = normalizedUserName;
        entity.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
        entity.Mobile = string.IsNullOrWhiteSpace(dto.Mobile) ? null : dto.Mobile.Trim();
        entity.IsActive = dto.IsActive;
        entity.CanViewPrices = dto.CanViewPrices;
        entity.CanViewFinancials = dto.CanViewFinancials;
        entity.CanRequestQuotation = dto.CanRequestQuotation;
        entity.CanUploadPaymentProof = dto.CanUploadPaymentProof;

        if (!string.IsNullOrWhiteSpace(dto.Password))
            entity.HashedPassword = PasswordHasher.HashPassword(dto.Password.Trim());

        await db.SaveChangesAsync();
        return (true, dto.PortalUserId > 0 ? "تم تحديث الحساب" : "تم إنشاء الحساب", entity.PortalUserId);
    }

    public async Task<(bool Success, string Message)> SetActiveAsync(int id, bool isActive, string currentUserName)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var entity = await db.B2BPortalUsers.FirstOrDefaultAsync(x => x.PortalUserId == id);
        if (entity == null)
            return (false, "الحساب غير موجود");

        entity.IsActive = isActive;
        await db.SaveChangesAsync();
        return (true, isActive ? "تم تفعيل الحساب" : "تم إيقاف الحساب");
    }
}
