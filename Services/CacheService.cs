using Microsoft.Extensions.Caching.Memory;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// خدمة التخزين المؤقت المركزية
/// </summary>
public class CacheService
{
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ShortDuration   = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LongDuration    = TimeSpan.FromMinutes(30);

    public static class Keys
    {
        public const string Products         = "products_all";
        public const string ProductGroups    = "product_groups";
        public const string Parties          = "parties_all";
        public const string Warehouses       = "warehouses";
        public const string CashBoxes        = "cashboxes";
        public const string ExpenseGroups    = "expense_groups";
        public const string Permissions      = "permissions_all";
        public const string CompanyInfo      = "company_info";
        public const string PricingMargins   = "pricing_margins";

        public static string ProductsSearch(string search) => $"products_search_{search}";
        public static string PartyBalance(int partyId, string type) => $"party_balance_{partyId}_{type}";
        public static string CashBoxBalance(int cashBoxId) => $"cashbox_balance_{cashBoxId}";
    }

    public CacheService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public T GetOrCreate<T>(string key, Func<T> factory, TimeSpan? duration = null)
    {
        return _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = duration ?? DefaultDuration;
            return factory();
        })!;
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? duration = null)
    {
        return (await _cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = duration ?? DefaultDuration;
            return await factory();
        }))!;
    }

    public void Remove(string key)
    {
        _cache.Remove(key);
    }

    public void Set<T>(string key, T value, TimeSpan? duration = null)
    {
        _cache.Set(key, value, duration ?? DefaultDuration);
    }

    public static TimeSpan GetDefaultDuration() => DefaultDuration;
    public static TimeSpan GetShortDuration()   => ShortDuration;
    public static TimeSpan GetLongDuration()    => LongDuration;
}
