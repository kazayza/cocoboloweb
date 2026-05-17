using System.Security.Cryptography;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// خدمة تشفير والتحقق من الباسوردات باستخدام BCrypt
/// </summary>
public static class PasswordHasher
{
    public static string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
    }

    public static bool VerifyPassword(string password, string hashedPassword)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsBcryptHash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.StartsWith("$2") && value.Length >= 50;
    }
}
