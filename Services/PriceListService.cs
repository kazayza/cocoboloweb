using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// خدمة قائمة الأسعار — قراءة الكتالوج العام + تعديل/طلب تعديل بنفس منظومة التسعير الموجودة
/// (Product / PriceHistory / PriceChangeRequests) — بدون أي جداول جديدة.
/// </summary>
public class PriceListService : IPriceListService
{
    private readonly IDbContextFactory<db24804Context> _factory;
    private readonly IHttpContextAccessor _http;
    private readonly IAuditService _audit;
    private readonly NotificationService _notify;
    private readonly ILogger<PriceListService> _logger;

    public PriceListService(
        IDbContextFactory<db24804Context> factory,
        IHttpContextAccessor http,
        IAuditService audit,
        NotificationService notify,
        ILogger<PriceListService> logger)
    {
        _factory = factory;
        _http = http;
        _audit = audit;
        _notify = notify;
        _logger = logger;
    }

    // ═══════════════ القراءة ═══════════════

    public async Task<List<PriceListRowDto>> GetRowsAsync()
    {
        using var db = await _factory.CreateDbContextAsync();

        return await db.Products.AsNoTracking()
            .Where(p => p.Customer == null)               // الكتالوج العام فقط — منتجات أوامر الشغل مستبعدة
            .OrderBy(p => p.ProductName)
            .Select(p => new PriceListRowDto
            {
                ProductId = p.ProductId,
                ProductName = p.ProductName,
                GroupName = p.ProductGroup.GroupName,
                StdPrice = p.SuggestedSalePriceCClass,
                PremiumPrice = p.SuggestedSalePrice,
                ElitePrice = p.SuggestedSalePriceElite,
                PricingStatusId = p.PricingStatusId
            })
            .ToListAsync();
    }

    public async Task<Dictionary<int, int>> GetStockMapAsync(int? branchId)
    {
        using var db = await _factory.CreateDbContextAsync();

        var q = db.StockLevels.AsNoTracking();
        if (branchId.HasValue)
            q = q.Where(sl => sl.Warehouse.BranchId == branchId.Value);

        return await q
            .GroupBy(sl => sl.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Qty);
    }

    public async Task<List<PriceListBranchDto>> GetBranchesAsync()
    {
        using var db = await _factory.CreateDbContextAsync();

        return await db.Branches.AsNoTracking()
            .OrderBy(b => b.BranchNameAr)
            .Select(b => new PriceListBranchDto { BranchId = b.BranchId, BranchName = b.BranchNameAr })
            .ToListAsync();
    }

    public async Task<HashSet<int>> GetPendingRequestProductIdsAsync()
    {
        using var db = await _factory.CreateDbContextAsync();

        var ids = await db.PriceChangeRequests.AsNoTracking()
            .Where(r => r.Status == "Pending")
            .Select(r => r.ProductId)
            .Distinct()
            .ToListAsync();
        return ids.ToHashSet();
    }

    public async Task<List<PriceHistoryRowDto>> GetHistoryAsync(int productId, int take = 15)
    {
        using var db = await _factory.CreateDbContextAsync();

        return await db.PriceHistories.AsNoTracking()
            .Where(h => h.ProductId == productId)
            .OrderByDescending(h => h.ChangedAt)
            .ThenByDescending(h => h.HistoryId)
            .Take(take)
            .Select(h => new PriceHistoryRowDto
            {
                PriceType = h.PriceType,
                OldPrice = h.OldPrice,
                NewPrice = h.NewPrice,
                ChangedBy = h.ChangedBy,
                ChangedAt = h.ChangedAt,
                ChangeReason = h.ChangeReason
            })
            .ToListAsync();
    }

    public async Task<List<int>> GetImageIdsAsync(int productId)
    {
        using var db = await _factory.CreateDbContextAsync();

        return await db.ProductImages.AsNoTracking()
            .Where(im => im.ProductId == productId)
            .OrderByDescending(im => im.IsPrimary)
            .ThenByDescending(im => im.CreatedAt)
            .ThenByDescending(im => im.ProductImagesId)
            .Select(im => im.ProductImagesId)
            .ToListAsync();
    }

    // ═══════════════ التعديل المباشر (Admin/AccountManager) ═══════════════

    public async Task<(bool Success, string Message)> UpdatePriceAsync(
        int productId, string tier, decimal newPrice, string reason, string currentUser)
    {
        // 🔒 مصدر الحقيقة: المدير ومدير الحسابات فقط — زي صلاحية التصدير/الحذف في شاشة المنتجات
        var user = _http.HttpContext?.User;
        if (user == null || !(user.IsInRole("Admin") || user.IsInRole(SystemRoles.Admin)
            || user.IsInRole("AccountManager") || user.IsInRole(SystemRoles.AccountManager)))
            return (false, "التعديل المباشر متاح للمدير ومدير الحسابات فقط.");

        if (string.IsNullOrWhiteSpace(reason))
            return (false, "سبب التعديل إلزامي.");
        if (newPrice < 0)
            return (false, "السعر لا يمكن أن يكون سالباً.");
        if (tier != "CClass" && tier != "Premium" && tier != "Elite")
            return (false, "فئة سعر غير معروفة.");

        using var db = await _factory.CreateDbContextAsync();
        var product = await db.Products.FirstOrDefaultAsync(p => p.ProductId == productId);
        if (product == null) return (false, "المنتج غير موجود.");

        var oldPrice = tier switch
        {
            "CClass" => product.SuggestedSalePriceCClass,
            "Premium" => product.SuggestedSalePrice,
            _ => product.SuggestedSalePriceElite
        };
        if ((oldPrice ?? 0) == newPrice)
            return (false, "السعر الجديد مطابق للقديم.");

        using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            switch (tier)
            {
                case "CClass": product.SuggestedSalePriceCClass = newPrice; break;
                case "Premium": product.SuggestedSalePrice = newPrice; break;
                case "Elite": product.SuggestedSalePriceElite = newPrice; break;
            }

            db.PriceHistories.Add(new PriceHistory
            {
                ProductId = productId,
                PriceType = tier,
                OldPrice = oldPrice,
                NewPrice = newPrice,
                ChangedBy = currentUser,
                ChangedAt = DateTime.Now,
                ChangeReason = reason.Trim()
            });

            await db.SaveChangesAsync();
            await tx.CommitAsync();

            await _audit.LogAsync<object>("Products", "PriceUpdate",
                productId.ToString(),
                new { Tier = tier, OldPrice = oldPrice },
                new { Tier = tier, NewPrice = newPrice, Reason = reason.Trim() },
                currentUser);

            _logger.LogInformation("Price updated: product {Pid} tier {Tier} {Old}→{New} by {User}",
                productId, tier, oldPrice, newPrice, currentUser);
            return (true, $"تم تحديث سعر {TierName(tier)} إلى {newPrice:N2} ج — وتم توثيقه في تاريخ الأسعار.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "UpdatePriceAsync failed for {Pid}", productId);
            return (false, "حدث خطأ أثناء حفظ السعر: " + ex.Message);
        }
    }

    // ═══════════════ طلب تعديل (نفس وورك فلو المراجعة الموجود) ═══════════════

    public async Task<(bool Success, string Message)> RequestPriceChangeAsync(
        int productId, string tier, decimal newPrice, string reason, string currentUser)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return (false, "سبب الطلب إلزامي.");
        if (newPrice < 0)
            return (false, "السعر لا يمكن أن يكون سالباً.");
        if (tier != "CClass" && tier != "Premium" && tier != "Elite")
            return (false, "فئة سعر غير معروفة.");

        using var db = await _factory.CreateDbContextAsync();
        var product = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProductId == productId);
        if (product == null) return (false, "المنتج غير موجود.");

        var currentPrice = tier switch
        {
            "CClass" => product.SuggestedSalePriceCClass,
            "Premium" => product.SuggestedSalePrice,
            _ => product.SuggestedSalePriceElite
        } ?? 0m;
        if (currentPrice == newPrice)
            return (false, "السعر الجديد مطابق للحالي.");

        // منع تكرار نفس الطلب المعلق على نفس الصنف والفئة
        var duplicate = await db.PriceChangeRequests.AsNoTracking()
            .AnyAsync(r => r.ProductId == productId
                        && r.PriceType == tier
                        && r.Status == "Pending"
                        && r.RequestedPrice == newPrice);
        if (duplicate)
            return (false, "يوجد طلب معلق بنفس القيمة لهذا الصنف — بانتظار المراجعة.");

        db.PriceChangeRequests.Add(new PriceChangeRequest
        {
            ProductId = productId,
            PriceType = tier,
            CurrentPrice = currentPrice,
            RequestedPrice = newPrice,
            Reason = reason.Trim(),
            Status = "Pending",
            RequestedBy = currentUser,
            RequestedAt = DateTime.Now
        });
        await db.SaveChangesAsync();

        var msg = $"طلب تعديل سعر {TierName(tier)} للمنتج «{product.ProductName}» " +
                  $"من {currentPrice:N0} إلى {newPrice:N0} ج — بواسطة {currentUser}";
        await _notify.NotifyRoleAsync("طلب تعديل سعر", msg, "Admin",
            currentUser, formName: "sales/price-requests", relatedTable: "Products", relatedId: productId);
        await _notify.NotifyRoleAsync("طلب تعديل سعر", msg, "AccountManager",
            currentUser, formName: "sales/price-requests", relatedTable: "Products", relatedId: productId);

        await _audit.LogAsync<object>("Products", "PriceChangeRequest",
            productId.ToString(), null,
            new { Tier = tier, From = currentPrice, To = newPrice, Reason = reason.Trim() },
            currentUser);

        return (true, "تم إرسال طلب تعديل السعر للمراجعة — يظهر الآن في شاشة طلبات تعديل الأسعار.");
    }

    private static string TierName(string tier) => tier switch
    {
        "CClass" => "ستاندرد",
        "Premium" => "بريميوم",
        "Elite" => "إيليت",
        _ => tier
    };
}
