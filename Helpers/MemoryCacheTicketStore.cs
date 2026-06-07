using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace COCOBOLOERPNEW.Helpers;

/// <summary>
/// مخزن لإدارة تذاكر المصادقة والكوكيز الكبيرة في ذاكرة السيرفر.
/// يحل مشكلة ERR_HTTP2_PROTOCOL_ERROR نهائياً عن طريق تخزين الـ Claims الضخمة (مثل الصلاحيات الكثيرة للأدمن)
/// في الـ Memory Cache، وإرسال معرف بسيط (Session Key) فقط للمتصفح.
/// </summary>
public class MemoryCacheTicketStore : ITicketStore
{
    private const string KeyPrefix = "COCOBOLO_SESSION_";
    private readonly IMemoryCache _cache;

    public MemoryCacheTicketStore(IMemoryCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var guid = Guid.NewGuid().ToString("N");
        var key = KeyPrefix + guid;
        await RenewAsync(key, ticket);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        if (ticket == null)
        {
            throw new ArgumentNullException(nameof(ticket));
        }

        var options = new MemoryCacheEntryOptions();
        
        var expiresUtc = ticket.Properties.ExpiresUtc;
        if (expiresUtc.HasValue)
        {
            options.SetAbsoluteExpiration(expiresUtc.Value);
        }
        else
        {
            options.SetAbsoluteExpiration(DateTimeOffset.UtcNow.AddDays(7));
        }

        // الحفاظ على الجلسة نشطة طالما المستخدم يتفاعل
        options.SetSlidingExpiration(TimeSpan.FromMinutes(30));

        _cache.Set(key, ticket, options);

        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        _cache.TryGetValue(key, out AuthenticationTicket? ticket);
        return Task.FromResult(ticket);
    }

    public Task RemoveAsync(string key)
    {
        _cache.Remove(key);
        return Task.FromResult(0);
    }
}