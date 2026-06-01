using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class PasswordService : IPasswordService
{
    private readonly IDbContextFactory<db24804Context> _factory;
    private readonly IAuditService _audit;
    private readonly ILogger<PasswordService> _logger;

    public PasswordService(
        IDbContextFactory<db24804Context> factory,
        IAuditService audit,
        ILogger<PasswordService> logger)
    {
        _factory = factory;
        _audit   = audit;
        _logger  = logger;
    }

    public async Task<(bool Success, string Message)> ChangePasswordAsync(int userId, ChangePasswordDto dto)
    {
        if (userId <= 0) return (false, "معرّف المستخدم غير صحيح");
        if (string.IsNullOrWhiteSpace(dto.CurrentPassword)) return (false, "الكلمة السرية الحالية مطلوبة");
        if (string.IsNullOrWhiteSpace(dto.NewPassword))     return (false, "الكلمة السرية الجديدة مطلوبة");
        if (dto.NewPassword.Length < 8)                     return (false, "الكلمة السرية يجب أن تكون 8 أحرف على الأقل");
        if (dto.NewPassword != dto.ConfirmPassword)         return (false, "الكلمات السرية غير متطابقة");

        if (dto.CurrentPassword == dto.NewPassword)
            return (false, "الكلمة السرية الجديدة لا يمكن أن تكون مماثلة للحالية");

        using var db = await _factory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == userId);
        if (user is null)                       return (false, "المستخدم غير موجود");
        if (user.IsActive == false)             return (false, "هذا الحساب غير نشط");

        // ─── التحقق من الكلمة السرية الحالية ───
        bool currentValid;
        if (!string.IsNullOrEmpty(user.HashedPassword) && PasswordHasher.IsBcryptHash(user.HashedPassword))
        {
            currentValid = PasswordHasher.VerifyPassword(dto.CurrentPassword, user.HashedPassword);
        }
        else
        {
            currentValid = string.Equals(user.Password, dto.CurrentPassword, StringComparison.Ordinal);
        }

        if (!currentValid)
            return (false, "الكلمة السرية الحالية غير صحيحة");

        // ─── حفظ الكلمة الجديدة ───
        try
        {
            user.HashedPassword = PasswordHasher.HashPassword(dto.NewPassword);
            user.Password = "***";   // مسح القديم
            await db.SaveChangesAsync();

            // Audit log
            try
            {
                await _audit.LogAsync(
                    "Users", "ChangePassword", user.UserId.ToString(),
                    null, new { UserId = user.UserId, Username = user.Username },
                    user.Username);
            }
            catch { /* audit failure shouldn't block */ }

            _logger.LogInformation("Password changed successfully for user {Username}", user.Username);
            return (true, "تم تغيير الكلمة السرية بنجاح");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change password failed for user {UserId}", userId);
            return (false, "حدث خطأ: " + ex.Message);
        }
    }

    public async Task<(bool Success, string Message)> ResetPasswordAsync(ResetUserPasswordDto dto, string adminUserName)
    {
        if (dto.UserId <= 0)                                 return (false, "معرّف المستخدم غير صحيح");
        if (string.IsNullOrWhiteSpace(dto.NewPassword))      return (false, "الكلمة السرية الجديدة مطلوبة");
        if (dto.NewPassword.Length < 8)                      return (false, "الكلمة السرية يجب أن تكون 8 أحرف على الأقل");
        if (dto.NewPassword != dto.ConfirmPassword)          return (false, "الكلمات السرية غير متطابقة");

        using var db = await _factory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == dto.UserId);
        if (user is null) return (false, "المستخدم غير موجود");

        try
        {
            user.HashedPassword = PasswordHasher.HashPassword(dto.NewPassword);
            user.Password = "***";
            await db.SaveChangesAsync();

            try
            {
                await _audit.LogAsync(
                    "Users", "ResetPassword", user.UserId.ToString(),
                    null, new { UserId = user.UserId, Username = user.Username, ResetBy = adminUserName },
                    adminUserName);
            }
            catch { }

            _logger.LogWarning("Admin {Admin} reset password for user {Username}", adminUserName, user.Username);
            return (true, $"تم إعادة تعيين كلمة السر للمستخدم '{user.Username}' بنجاح");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reset password failed");
            return (false, "حدث خطأ: " + ex.Message);
        }
    }

    public async Task<List<UserLookupDto>> SearchUsersAsync(string? search, int max = 20)
    {
        using var db = await _factory.CreateDbContextAsync();
        var q = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(u => u.Username.Contains(s)
                          || u.FullName.Contains(s)
                          || (u.Email != null && u.Email.Contains(s)));
        }

        return await q.OrderBy(u => u.FullName).Take(max)
            .Select(u => new UserLookupDto
            {
                UserId    = u.UserId,
                Username  = u.Username,
                FullName  = u.FullName,
                Email     = u.Email,
                Role      = u.Role,
                IsActive  = u.IsActive ?? false,
                LastLogin = u.LastLogin
            }).ToListAsync();
    }
}
