using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IPasswordService
{
    /// <summary>تغيير الباسورد بواسطة المستخدم نفسه (مع التحقق من القديم)</summary>
    Task<(bool Success, string Message)> ChangePasswordAsync(int userId, ChangePasswordDto dto);

    /// <summary>إعادة تعيين الباسورد بواسطة الأدمن (بدون تحقق من القديم)</summary>
    Task<(bool Success, string Message)> ResetPasswordAsync(ResetUserPasswordDto dto, string adminUserName);

    /// <summary>قائمة المستخدمين للأدمن</summary>
    Task<List<UserLookupDto>> SearchUsersAsync(string? search, int max = 20);
}
