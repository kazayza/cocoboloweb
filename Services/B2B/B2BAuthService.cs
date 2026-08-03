using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class B2BAuthService : IB2BAuthService
{
    private readonly IDbContextFactory<db24804Context> _factory;

    public B2BAuthService(IDbContextFactory<db24804Context> factory)
    {
        _factory = factory;
    }

    public async Task<B2BLoginResultDto?> ValidateLoginAsync(string username, string password)
    {
        username = username.Trim();

        await using var db = await _factory.CreateDbContextAsync();
        var user = await db.B2BPortalUsers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserName == username && x.IsActive);

        if (user == null)
            return null;

        if (!PasswordHasher.VerifyPassword(password, user.HashedPassword))
            return null;

        var partyName = await db.Parties.AsNoTracking()
            .Where(p => p.PartyId == user.PartyId)
            .Select(p => p.PartyName)
            .FirstOrDefaultAsync();

        return new B2BLoginResultDto
        {
            PortalUserId = user.PortalUserId,
            PartyId = user.PartyId,
            ResponsibleEmployeeId = user.ResponsibleEmployeeId,
            UserName = user.UserName,
            FullName = string.IsNullOrWhiteSpace(user.FullName) ? (partyName ?? user.UserName) : user.FullName
        };
    }
}
