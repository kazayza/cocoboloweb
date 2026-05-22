using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace COCOBOLOERPNEW.Helpers;

/// <summary>
/// خدمة توليد روابط عامة موقّعة (HMAC) لمشاركة الملفات.
/// مميزاتها:
///   - Stateless (مفيش جدول في DB)
///   - تنتهي تلقائيًا بعد المدة المحددة
///   - مستحيلة التزوير (تحتاج SecretKey)
///   - تشتغل من غير login
/// </summary>
public class ShareTokenService
{
    private readonly byte[] _key;
    private readonly ILogger<ShareTokenService> _logger;

    public ShareTokenService(IConfiguration config, ILogger<ShareTokenService> logger)
    {
        _logger = logger;

        var secret = config["Security:ShareSecret"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
        {
            throw new InvalidOperationException(
                "Security:ShareSecret يجب أن يكون موجود في appsettings ولا يقل عن 32 حرف. " +
                "ولّد قيمة عشوائية: Guid.NewGuid().ToString(\"N\") + Guid.NewGuid().ToString(\"N\")");
        }

        _key = Encoding.UTF8.GetBytes(secret);
    }

    public class TokenPayload
    {
        public string Type { get; set; } = "";      // "quotation" / "invoice" ...
        public int Id { get; set; }
        public long ExpiresAt { get; set; }          // Unix seconds
    }

    /// <summary>
    /// توليد رابط عام يحمل النوع والـ ID وتاريخ الانتهاء، موقّع بالـ HMAC.
    /// </summary>
    public string GenerateToken(string type, int id, TimeSpan validity)
    {
        var payload = new TokenPayload
        {
            Type = type,
            Id = id,
            ExpiresAt = DateTimeOffset.UtcNow.Add(validity).ToUnixTimeSeconds()
        };

        var json = JsonSerializer.Serialize(payload);
        var payloadBytes = Encoding.UTF8.GetBytes(json);
        var payloadB64 = Base64UrlEncode(payloadBytes);

        var signature = ComputeHmac(payloadB64);
        var sigB64 = Base64UrlEncode(signature);

        return $"{payloadB64}.{sigB64}";
    }

    /// <summary>
    /// التحقق من الـ Token وإرجاع البيانات لو صالح.
    /// </summary>
    public TokenPayload? ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var parts = token.Split('.');
        if (parts.Length != 2) return null;

        try
        {
            // تحقق من التوقيع
            var expectedSig = ComputeHmac(parts[0]);
            var providedSig = Base64UrlDecode(parts[1]);

            if (!CryptographicOperations.FixedTimeEquals(expectedSig, providedSig))
            {
                _logger.LogWarning("Invalid share token signature");
                return null;
            }

            // قراءة الـ payload
            var payloadBytes = Base64UrlDecode(parts[0]);
            var json = Encoding.UTF8.GetString(payloadBytes);
            var payload = JsonSerializer.Deserialize<TokenPayload>(json);

            if (payload == null) return null;

            // تحقق من انتهاء الصلاحية
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > payload.ExpiresAt)
            {
                _logger.LogInformation("Share token expired for {Type} {Id}", payload.Type, payload.Id);
                return null;
            }

            return payload;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate share token");
            return null;
        }
    }

    private byte[] ComputeHmac(string data)
    {
        using var hmac = new HMACSHA256(_key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
