using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Hosting;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;

namespace COCOBOLOERPNEW.Services;

public class ProductService : IProductService
{
    private readonly db24804Context _context;
    private readonly IAuditService _auditService;
    private readonly IWebHostEnvironment _env;

    public ProductService(db24804Context context, IAuditService auditService, IWebHostEnvironment env)
    {
        _context = context;
        _auditService = auditService;
        _env = env;
    }

        public async Task<List<ProductListDto>> GetProductsAsync(string? search)
    {
        var query = from p in _context.Products.AsNoTracking()
                    join c in _context.Parties.AsNoTracking()
                        on p.Customer equals c.PartyId into pc
                    from customer in pc.DefaultIfEmpty()
                    select new
                    {
                        p.ProductId,
                        p.ProductName,
                        p.ProductDescription,
                        p.Customer,
                        CustomerName = customer != null ? customer.PartyName : null,
                        p.PricingType,
                        p.PricingStatusId,
                        p.SuggestedSalePriceCClass,
                        p.SuggestedSalePrice,
                        p.SuggestedSalePriceElite,
                        p.PdfPath,
                        p.CreatedAt
                    };

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();

            query = query.Where(x =>
                x.ProductId.ToString().Contains(search) ||
                x.ProductName.Contains(search) ||
                (x.CustomerName != null && x.CustomerName.Contains(search))
            );
        }

        var rawProducts = await query
            .OrderByDescending(x => x.ProductId)
            .ToListAsync();

        var productIds = rawProducts.Select(p => p.ProductId).ToList();

        var idsWithOldPdf = await _context.Products
            .Where(p => p.Pdffile != null && productIds.Contains(p.ProductId))
            .Select(p => p.ProductId)
            .ToListAsync();

        var factoryPricedDates = await _context.PriceHistories
            .AsNoTracking()
            .Where(h => productIds.Contains(h.ProductId))
            .GroupBy(h => h.ProductId)
            .Select(g => new { ProductId = g.Key, PricedAt = g.Min(h => h.ChangedAt) })
            .ToDictionaryAsync(x => x.ProductId, x => x.PricedAt);

        var now = DateTime.Now;
        var products = rawProducts.Select(p =>
        {
            DateTime? pricedAt = factoryPricedDates.TryGetValue(p.ProductId, out var dt) ? dt : null;
            if (!pricedAt.HasValue && (p.PricingStatusId == 3 ||
                (p.SuggestedSalePriceCClass.HasValue && p.SuggestedSalePriceCClass.Value > 0) ||
                (p.SuggestedSalePrice.HasValue && p.SuggestedSalePrice.Value > 0) ||
                (p.SuggestedSalePriceElite.HasValue && p.SuggestedSalePriceElite.Value > 0)))
            {
                pricedAt = p.CreatedAt;
            }

            string? delayText = null;
            string? delayClass = null;

            if (p.CreatedAt.HasValue)
            {
                if (pricedAt.HasValue)
                {
                    var span = pricedAt.Value - p.CreatedAt.Value;
                    if (span.TotalMinutes < 60)
                        delayText = $"تم في {(int)span.TotalMinutes} دقيقة ⚡";
                    else if (span.TotalHours < 24)
                        delayText = $"تم في {(int)span.TotalHours} ساعة";
                    else
                        delayText = $"استغرق {Math.Round(span.TotalDays, 1)} يوم";

                    delayClass = span.TotalHours <= 6 ? "badge-success" : (span.TotalHours <= 24 ? "badge-info" : "badge-warning");
                }
                else if (p.PricingStatusId == 2) // SentForPricing
                {
                    var waitSpan = now - p.CreatedAt.Value;
                    if (waitSpan.TotalHours < 24)
                        delayText = $"بانتظار المصنع منذ {(int)waitSpan.TotalHours} ساعة ⏳";
                    else
                        delayText = $"بانتظار المصنع منذ {Math.Round(waitSpan.TotalDays, 1)} يوم ⏳";

                    delayClass = waitSpan.TotalHours > 24 ? "badge-danger" : "badge-warning";
                }
            }

            return new ProductListDto
            {
                ProductId = p.ProductId,
                ProductName = p.ProductName,
                ProductDescription = p.ProductDescription,
                Customer = p.Customer,
                CustomerName = p.CustomerName,
                PricingType = p.PricingType,
                PricingStatusId = p.PricingStatusId,
                SuggestedSalePriceCClass = p.SuggestedSalePriceCClass,
                SuggestedSalePrice = p.SuggestedSalePrice,
                SuggestedSalePriceElite = p.SuggestedSalePriceElite,
                PdfPath = p.PdfPath,
                HasOldPdf = idsWithOldPdf.Contains(p.ProductId),
                CreatedAt = p.CreatedAt,
                FactoryPricedAt = pricedAt,
                ResponseTimeText = delayText,
                ResponseTimeClass = delayClass
            };
        }).ToList();

        return products;
    }

    

    public async Task FactorySetCostAsync(
        int ProductId,
        decimal? cClassCost,
        decimal? premiumCost,
        decimal? eliteCost,
        string currentUsername)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();

        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.ProductId == ProductId);

        if (product == null)
            throw new Exception("المنتج غير موجود");

        var margin = await _context.PricingMargins
            .Where(m => m.IsActive)
            .OrderByDescending(m => m.MarginId)
            .FirstOrDefaultAsync();

        if (margin == null)
            throw new Exception("لا توجد نسب ربح مفعلة");

        // ✅ C Class
        if (cClassCost.HasValue)
        {
            var oldPriceCClass = product.SuggestedSalePriceCClass;

            product.PurchasePriceCClass = cClassCost.Value;

            var newSaleCClass = cClassCost.Value +
                                (cClassCost.Value * margin.CClassMargin / 100);

            product.SuggestedSalePriceCClass = newSaleCClass;

            _context.PriceHistories.Add(new PriceHistory
            {
                ProductId = product.ProductId,
                PriceType = "CClass",
                OldPrice = oldPriceCClass,
                NewPrice = newSaleCClass,
                ChangedBy = currentUsername,
                ChangedAt = DateTime.Now,
                ChangeReason = "تسعير من المصنع"
            });
        }

        // ✅ Premium
        if (premiumCost.HasValue)
        {
            var oldPrice = product.SuggestedSalePrice;

            product.PurchasePrice = premiumCost.Value;

            var newSale = premiumCost.Value +
                          (premiumCost.Value * margin.PremiumMargin / 100);

            product.SuggestedSalePrice = newSale;

            _context.PriceHistories.Add(new PriceHistory
            {
                ProductId = product.ProductId,
                PriceType = "Premium",
                OldPrice = oldPrice,
                NewPrice = newSale,
                ChangedBy = currentUsername,
                ChangedAt = DateTime.Now,
                ChangeReason = "تسعير من المصنع"
            });
        }

        // ✅ Elite
        if (eliteCost.HasValue)
        {
            var oldPriceElite = product.SuggestedSalePriceElite;

            product.PurchasePriceElite = eliteCost.Value;

            var newSaleElite = eliteCost.Value +
                               (eliteCost.Value * margin.EliteMargin / 100);

            product.SuggestedSalePriceElite = newSaleElite;

            _context.PriceHistories.Add(new PriceHistory
            {
                ProductId = product.ProductId,
                PriceType = "Elite",
                OldPrice = oldPriceElite,
                NewPrice = newSaleElite,
                ChangedBy = currentUsername,
                ChangedAt = DateTime.Now,
                ChangeReason = "تسعير من المصنع"
            });
        }

        // ✅ تغيير الحالة
        product.PricingStatusId = 3; // Priced

        // ✅ إشعار للبائع
        _context.Notifications.Add(new Notification
        {
            Title = "تم تسعير المنتج",
            Message = "تم إدخال التكلفة وتحديد سعر البيع.",
            RelatedTable = "Products",
            RelatedId = product.ProductId,
            RecipientUser = product.CreatedBy,
            CreatedBy = currentUsername,
            CreatedAt = DateTime.Now,
            FormName = "products/form"
        });

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
    }
    public async Task RequestSalePriceChangeAsync(
    int ProductId,
    decimal? newCClassSalePrice,
    decimal newPremiumSalePrice,
    decimal? newEliteSalePrice,
    string currentUsername)
{
    using var transaction = await _context.Database.BeginTransactionAsync();

    var product = await _context.Products
        .FirstOrDefaultAsync(p => p.ProductId == ProductId);

    if (product == null)
        throw new Exception("المنتج غير موجود");

    if (product.PricingStatusId != 3) // لازم يكون Priced
        throw new Exception("لا يمكن طلب تعديل في هذه الحالة");

    // ✅ تسجيل طلب تعديل C Class (لو موجود)
    if (newCClassSalePrice.HasValue)
    {
        _context.PriceChangeRequests.Add(new PriceChangeRequest
        {
            ProductId = product.ProductId,
            PriceType = "CClass",
            CurrentPrice = product.SuggestedSalePriceCClass ?? 0,
            RequestedPrice = newCClassSalePrice.Value,
            Reason = "طلب تعديل من البائع",
            Status = "Pending",
            RequestedBy = currentUsername,
            RequestedAt = DateTime.Now
        });
    }

    // ✅ تسجيل طلب تعديل Premium
    _context.PriceChangeRequests.Add(new PriceChangeRequest
    {
        ProductId = product.ProductId,
        PriceType = "Premium",
        CurrentPrice = product.SuggestedSalePrice ?? 0,
        RequestedPrice = newPremiumSalePrice,
        Reason = "طلب تعديل من البائع",
        Status = "Pending",
        RequestedBy = currentUsername,
        RequestedAt = DateTime.Now
    });

    // ✅ تسجيل طلب تعديل Elite (لو موجود)
    if (newEliteSalePrice.HasValue)
    {
        _context.PriceChangeRequests.Add(new PriceChangeRequest
        {
            ProductId = product.ProductId,
            PriceType = "Elite",
            CurrentPrice = product.SuggestedSalePriceElite ?? 0,
            RequestedPrice = newEliteSalePrice.Value,
            Reason = "طلب تعديل من البائع",
            Status = "Pending",
            RequestedBy = currentUsername,
            RequestedAt = DateTime.Now
        });
    }

    // ✅ تغيير الحالة
    product.PricingStatusId = 7; // SalePriceChangeRequested

    // ✅ إشعار لمدير المبيعات
    _context.Notifications.Add(new Notification
    {
        Title = "طلب تعديل سعر",
        Message = "يوجد طلب تعديل سعر يحتاج موافقة.",
        RelatedTable = "Products",
        RelatedId = product.ProductId,
        RecipientUser = "SalesManager", // مؤقتًا – هنحسنها بعدين
        CreatedBy = currentUsername,
        CreatedAt = DateTime.Now,
        FormName = "products/form"
    });

    await _context.SaveChangesAsync();
    await transaction.CommitAsync();
} 
public async Task ApproveSalePriceChangeAsync(
    int ProductId,
    string currentUsername)
{
    using var transaction = await _context.Database.BeginTransactionAsync();

    var product = await _context.Products
        .FirstOrDefaultAsync(p => p.ProductId == ProductId);

    if (product == null)
        throw new Exception("المنتج غير موجود");

    if (product.PricingStatusId != 7) // لازم يكون SalePriceChangeRequested
        throw new Exception("لا يوجد طلب تعديل معلق");

    var pendingRequests = await _context.PriceChangeRequests
        .Where(r => r.ProductId == ProductId && r.Status == "Pending")
        .ToListAsync();

    if (!pendingRequests.Any())
        throw new Exception("لا يوجد طلبات تعديل");

    foreach (var request in pendingRequests)
    {
        if (request.PriceType == "CClass")
        {
            var oldPriceCClass = product.SuggestedSalePriceCClass;

            product.SuggestedSalePriceCClass = request.RequestedPrice;

            _context.PriceHistories.Add(new PriceHistory
            {
                ProductId = product.ProductId,
                PriceType = "CClass",
                OldPrice = oldPriceCClass,
                NewPrice = request.RequestedPrice,
                ChangedBy = currentUsername,
                ChangedAt = DateTime.Now,
                ChangeReason = "موافقة مدير المبيعات على تعديل السعر"
            });
        }
        else if (request.PriceType == "Premium")
        {
            var oldPrice = product.SuggestedSalePrice;

            product.SuggestedSalePrice = request.RequestedPrice;

            _context.PriceHistories.Add(new PriceHistory
            {
                ProductId = product.ProductId,
                PriceType = "Premium",
                OldPrice = oldPrice,
                NewPrice = request.RequestedPrice,
                ChangedBy = currentUsername,
                ChangedAt = DateTime.Now,
                ChangeReason = "موافقة مدير المبيعات على تعديل السعر"
            });
        }
        else if (request.PriceType == "Elite")
        {
            var oldPriceElite = product.SuggestedSalePriceElite;

            product.SuggestedSalePriceElite = request.RequestedPrice;

            _context.PriceHistories.Add(new PriceHistory
            {
                ProductId = product.ProductId,
                PriceType = "Elite",
                OldPrice = oldPriceElite,
                NewPrice = request.RequestedPrice,
                ChangedBy = currentUsername,
                ChangedAt = DateTime.Now,
                ChangeReason = "موافقة مدير المبيعات على تعديل السعر"
            });
        }

        request.Status = "Approved";
        request.ReviewedBy = currentUsername;
        request.ReviewedAt = DateTime.Now;
    }

    // ✅ رجوع الحالة إلى Priced
    product.PricingStatusId = 3;

    // ✅ إشعار للبائع
    _context.Notifications.Add(new Notification
    {
        Title = "تمت الموافقة على تعديل السعر",
        Message = "تم اعتماد السعر الجديد للمنتج.",
        RelatedTable = "Products",
        RelatedId = product.ProductId,
        RecipientUser = product.CreatedBy,
        CreatedBy = currentUsername,
        CreatedAt = DateTime.Now,
        FormName = "products/form"
    });

    await _context.SaveChangesAsync();
    await transaction.CommitAsync();
}
public async Task RejectSalePriceChangeAsync(
    int ProductId,
    string currentUsername,
    string? rejectReason = null)
{
    using var transaction = await _context.Database.BeginTransactionAsync();

    var product = await _context.Products
        .FirstOrDefaultAsync(p => p.ProductId == ProductId);

    if (product == null)
        throw new Exception("المنتج غير موجود");

    if (product.PricingStatusId != 7)
        throw new Exception("لا يوجد طلب تعديل معلق");

    var pendingRequests = await _context.PriceChangeRequests
        .Where(r => r.ProductId == ProductId && r.Status == "Pending")
        .ToListAsync();

    if (!pendingRequests.Any())
        throw new Exception("لا يوجد طلبات تعديل");

    foreach (var request in pendingRequests)
    {
        request.Status = "Rejected";
        request.ReviewedBy = currentUsername;
        request.ReviewedAt = DateTime.Now;
        request.ReviewNotes = rejectReason;
    }

    // ✅ رجوع الحالة إلى Priced
    product.PricingStatusId = 3;

    // ✅ إشعار للبائع
    _context.Notifications.Add(new Notification
    {
        Title = "تم رفض تعديل السعر",
        Message = "تم رفض طلب تعديل السعر للمنتج.",
        RelatedTable = "Products",
        RelatedId = product.ProductId,
        RecipientUser = product.CreatedBy,
        CreatedBy = currentUsername,
        CreatedAt = DateTime.Now,
        FormName = "products/form"
    });

    await _context.SaveChangesAsync();
    await transaction.CommitAsync();
}
public async Task RequestCostChangeAsync(
    int ProductId,
    string currentUsername)
{
    using var transaction = await _context.Database.BeginTransactionAsync();

    var product = await _context.Products
        .FirstOrDefaultAsync(p => p.ProductId == ProductId);

    if (product == null)
        throw new Exception("المنتج غير موجود");

    if (product.PricingStatusId != 3)
        throw new Exception("لا يمكن طلب تعديل تكلفة في هذه الحالة");

    // ✅ تغيير الحالة
    product.PricingStatusId = 8; // CostChangeRequested

    // ✅ إشعار للمصنع
    _context.Notifications.Add(new Notification
    {
        Title = "طلب تعديل تكلفة",
        Message = "يوجد طلب لتعديل تكلفة المنتج.",
        RelatedTable = "Products",
        RelatedId = product.ProductId,
        RecipientUser = "factory", // مؤقتًا
        CreatedBy = currentUsername,
        CreatedAt = DateTime.Now,
        FormName = "products/form"
    });

    await _context.SaveChangesAsync();
    await transaction.CommitAsync();
}
public async Task ApproveCostChangeAsync(
    int ProductId,
    decimal? newCClassCost,
    decimal? newPremiumCost,
    decimal? newEliteCost,
    string currentUsername)
{
    using var transaction = await _context.Database.BeginTransactionAsync();

    var product = await _context.Products
        .FirstOrDefaultAsync(p => p.ProductId == ProductId);

    if (product == null)
        throw new Exception("المنتج غير موجود");

    if (product.PricingStatusId != 8)
        throw new Exception("لا يوجد طلب تعديل تكلفة");

    var margin = await _context.PricingMargins
        .Where(m => m.IsActive)
        .OrderByDescending(m => m.MarginId)
        .FirstOrDefaultAsync();

    if (margin == null)
        throw new Exception("لا توجد نسب ربح مفعلة");

    // ✅ C Class
    if (newCClassCost.HasValue)
    {
        var oldSaleCClass = product.SuggestedSalePriceCClass;

        product.PurchasePriceCClass = newCClassCost.Value;

        var newSaleCClass = newCClassCost.Value +
                           (newCClassCost.Value * margin.CClassMargin / 100);

        product.SuggestedSalePriceCClass = newSaleCClass;

        _context.PriceHistories.Add(new PriceHistory
        {
            ProductId = product.ProductId,
            PriceType = "CClass",
            OldPrice = oldSaleCClass,
            NewPrice = newSaleCClass,
            ChangedBy = currentUsername,
            ChangedAt = DateTime.Now,
            ChangeReason = "تعديل تكلفة بواسطة المصنع"
        });
    }

    // ✅ Premium
    if (newPremiumCost.HasValue)
    {
        var oldSale = product.SuggestedSalePrice;

        product.PurchasePrice = newPremiumCost.Value;

        var newSale = newPremiumCost.Value +
                      (newPremiumCost.Value * margin.PremiumMargin / 100);

        product.SuggestedSalePrice = newSale;

        _context.PriceHistories.Add(new PriceHistory
        {
            ProductId = product.ProductId,
            PriceType = "Premium",
            OldPrice = oldSale,
            NewPrice = newSale,
            ChangedBy = currentUsername,
            ChangedAt = DateTime.Now,
            ChangeReason = "تعديل تكلفة بواسطة المصنع"
        });
    }

    // ✅ Elite
    if (newEliteCost.HasValue)
    {
        var oldSaleElite = product.SuggestedSalePriceElite;

        product.PurchasePriceElite = newEliteCost.Value;

        var newSaleElite = newEliteCost.Value +
                           (newEliteCost.Value * margin.EliteMargin / 100);

        product.SuggestedSalePriceElite = newSaleElite;

        _context.PriceHistories.Add(new PriceHistory
        {
            ProductId = product.ProductId,
            PriceType = "Elite",
            OldPrice = oldSaleElite,
            NewPrice = newSaleElite,
            ChangedBy = currentUsername,
            ChangedAt = DateTime.Now,
            ChangeReason = "تعديل تكلفة بواسطة المصنع"
        });
    }

    // ✅ رجوع الحالة إلى Priced
    product.PricingStatusId = 3;

    // ✅ إشعار للبائع
    _context.Notifications.Add(new Notification
    {
        Title = "تم تحديث التكلفة",
        Message = "تم تعديل التكلفة وتحديث سعر البيع.",
        RelatedTable = "Products",
        RelatedId = product.ProductId,
        RecipientUser = product.CreatedBy,
        CreatedBy = currentUsername,
        CreatedAt = DateTime.Now,
        FormName = "products/form"
    });

    await _context.SaveChangesAsync();
    await transaction.CommitAsync();
}
// ============================
// ✅ دوال مدة التصنيع وملاحظات التصنيع
// ============================
public async Task RequestPeriodChangeAsync(
    int productId,
    int? newPeriod,
    string? newManufacturingNotes,
    string reason,
    string currentUsername)
{
    var product = await _context.Products
        .FirstOrDefaultAsync(p => p.ProductId == productId);

    if (product == null)
        throw new Exception("المنتج غير موجود");

    try
    {
        _context.PriceChangeRequests.Add(new PriceChangeRequest
        {
            ProductId = product.ProductId,
            PriceType = "Manufacturing_Period",
            CurrentPrice = product.Period.HasValue ? (decimal)product.Period.Value : 0,
            RequestedPrice = newPeriod.HasValue ? (decimal)newPeriod.Value : 0,
            Reason = reason,
            Status = "Pending",
            RequestedBy = currentUsername,
            RequestedAt = DateTime.Now,
            ReviewNotes = newManufacturingNotes
        });

        _context.Notifications.Add(new Notification
        {
            Title = "طلب تعديل مدة التصنيع",
            Message = $"يوجد طلب لتعديل مدة التصنيع من {currentUsername}. السبب: {reason}",
            RelatedTable = "Products",
            RelatedId = product.ProductId,
            RecipientUser = "Admin",
            CreatedBy = currentUsername,
            CreatedAt = DateTime.Now,
            FormName = "products/form"
        });

    // ✅ تسجيل المراجعة
    try
    {
        await _auditService.LogAsync(
            "Products",
            "إرسال طلب تعديل مدة التصنيع",
            productId.ToString(),
            oldData: (object?)null,
            newData: (object)new { Period = newPeriod, ManufacturingDescription = newManufacturingNotes, Reason = reason },
            currentUsername
        );
    }
    catch (Exception auditEx)
    {
        Console.WriteLine($"[ProductService] Audit Warning: {auditEx.Message}");
    }

    await _context.SaveChangesAsync();
}
    catch (Exception ex)
    {
        Console.WriteLine("==================== ERROR ====================");
        Console.WriteLine("Message: " + ex.Message);
        Console.WriteLine("InnerException: " + ex.InnerException?.Message);
        throw new Exception("خطأ أثناء حفظ الطلب: " + (ex.InnerException?.Message ?? ex.Message));
    }
}

public async Task ApprovePeriodChangeAsync(int productId, string currentUsername)
{
    var product = await _context.Products
        .FirstOrDefaultAsync(p => p.ProductId == productId);

    if (product == null)
        throw new Exception("المنتج غير موجود");

    var pendingRequest = await _context.PriceChangeRequests
        .FirstOrDefaultAsync(r => r.ProductId == productId 
                               && r.Status == "Pending" 
                               && r.PriceType == "Manufacturing_Period");

    if (pendingRequest == null)
        throw new Exception("لا يوجد طلب تعديل معلق");

    // تطبيق التعديل
    product.Period = (int)pendingRequest.RequestedPrice;
    product.ManufacturingDescription = pendingRequest.ReviewNotes;

    _context.PriceHistories.Add(new PriceHistory
    {
        ProductId = product.ProductId,
        PriceType = "Manufacturing_Period",
        OldPrice = pendingRequest.CurrentPrice,
        NewPrice = pendingRequest.RequestedPrice,
        ChangedBy = currentUsername,
        ChangedAt = DateTime.Now,
        ChangeReason = "موافقة الأدمن على تعديل مدة التصنيع"
    });

    pendingRequest.Status = "Approved";
    pendingRequest.ReviewedBy = currentUsername;
    pendingRequest.ReviewedAt = DateTime.Now;

    _context.Notifications.Add(new Notification
    {
        Title = "تمت الموافقة على طلب تعديل مدة التصنيع",
        Message = "تم اعتماد تعديل مدة التصنيع وملاحظات التصنيع.",
        RelatedTable = "Products",
        RelatedId = product.ProductId,
        RecipientUser = pendingRequest.RequestedBy,
        CreatedBy = currentUsername,
        CreatedAt = DateTime.Now,
        FormName = "products/form"
    });

    // ✅ تسجيل المراجعة
    try
    {
        await _auditService.LogAsync(
            "Products",
            "موافقة على تعديل مدة التصنيع",
            productId.ToString(),
            oldData: (object)new { Period = (int)pendingRequest.CurrentPrice },
            newData: (object)new { Period = (int)pendingRequest.RequestedPrice, ManufacturingDescription = pendingRequest.ReviewNotes },
            currentUsername
        );
    }
    catch (Exception auditEx)
    {
        Console.WriteLine($"[ProductService] Audit Warning: {auditEx.Message}");
    }

    await _context.SaveChangesAsync();
}

public async Task RejectPeriodChangeAsync(
    int productId,
    string currentUsername,
    string? rejectReason = null)
{
    var pendingRequest = await _context.PriceChangeRequests
        .FirstOrDefaultAsync(r => r.ProductId == productId 
                               && r.Status == "Pending" 
                               && r.PriceType == "Manufacturing_Period");

    if (pendingRequest == null)
        throw new Exception("لا يوجد طلب تعديل معلق");

    pendingRequest.Status = "Rejected";
    pendingRequest.ReviewedBy = currentUsername;
    pendingRequest.ReviewedAt = DateTime.Now;
    pendingRequest.ReviewNotes = rejectReason;

    _context.Notifications.Add(new Notification
    {
        Title = "تم رفض طلب تعديل مدة التصنيع",
        Message = $"تم رفض طلب تعديل مدة التصنيع. {(string.IsNullOrWhiteSpace(rejectReason) ? "" : $"السبب: {rejectReason}")}",
        RelatedTable = "Products",
        RelatedId = productId,
        RecipientUser = pendingRequest.RequestedBy,
        CreatedBy = currentUsername,
        CreatedAt = DateTime.Now,
        FormName = "products/form"
    });

    // ✅ تسجيل المراجعة
    try
    {
        await _auditService.LogAsync(
            "Products",
            "رفض تعديل مدة التصنيع",
            productId.ToString(),
            oldData: (object)new { Period = (int?)pendingRequest.RequestedPrice, ManufacturingNotes = pendingRequest.ReviewNotes },
            newData: (object?)null,
            currentUsername
        );
    }
    catch (Exception auditEx)
    {
        Console.WriteLine($"[ProductService] Audit Warning: {auditEx.Message}");
    }

    await _context.SaveChangesAsync();
}

public async Task<List<ProductFactoryAlternativeDto>> GetFactoryAlternativesAsync(int productId)
{
    return await _context.ProductFactoryAlternatives
        .AsNoTracking()
        .Where(x => x.ProductId == productId)
        .OrderByDescending(x => x.IsPrimary)
        .ThenByDescending(x => x.CreatedAt)
        .Select(x => new ProductFactoryAlternativeDto
        {
            AlternativeId = x.AlternativeId,
            ProductId = x.ProductId,
            AlternativeName = x.AlternativeName,
            SpecificationSummary = x.SpecificationSummary,
            ManufacturingDescription = x.ManufacturingDescription,
            Period = x.Period,
            PurchasePriceCClass = x.PurchasePriceCClass,
            PurchasePricePremium = x.PurchasePricePremium,
            PurchasePriceElite = x.PurchasePriceElite,
            SuggestedSalePriceCClass = x.SuggestedSalePriceCClass,
            SuggestedSalePricePremium = x.SuggestedSalePricePremium,
            SuggestedSalePriceElite = x.SuggestedSalePriceElite,
            Status = x.Status,
            IsPrimary = x.IsPrimary,
            CreatedBy = x.CreatedBy,
            CreatedAt = x.CreatedAt,
            ReviewedBy = x.ReviewedBy,
            ReviewedAt = x.ReviewedAt,
            Images = x.Images
                .OrderByDescending(i => i.IsPrimary)
                .ThenBy(i => i.AlternativeImageId)
                .Select(i => new ProductFactoryAlternativeImageDto
                {
                    AlternativeImageId = i.AlternativeImageId,
                    AlternativeId = i.AlternativeId,
                    ImagePath = i.ImagePath,
                    Caption = i.Caption,
                    IsPrimary = i.IsPrimary,
                    CreatedAt = i.CreatedAt
                }).ToList()
        })
        .ToListAsync();
}

public async Task<(bool Success, string Message, int? AlternativeId)> SaveFactoryAlternativeAsync(ProductFactoryAlternativeDto dto, IReadOnlyList<IBrowserFile> files, string currentUsername)
{
    if (dto.ProductId <= 0)
        return (false, "احفظ المنتج أولاً قبل إضافة البدائل.", null);

    if (string.IsNullOrWhiteSpace(dto.AlternativeName))
        return (false, "اسم البديل مطلوب.", null);

    if (!dto.Period.HasValue || dto.Period.Value <= 0)
        return (false, "مدة التصنيع للبديل مطلوبة ويجب أن تكون أكبر من صفر.", null);

    var hasAnyCost = (dto.PurchasePriceCClass ?? 0) > 0 || (dto.PurchasePricePremium ?? 0) > 0 || (dto.PurchasePriceElite ?? 0) > 0;
    if (!hasAnyCost)
        return (false, "أدخل تكلفة واحدة على الأقل للبديل.", null);

    var product = await _context.Products.FirstOrDefaultAsync(x => x.ProductId == dto.ProductId);
    if (product == null)
        return (false, "المنتج غير موجود.", null);

    var margin = await _context.PricingMargins
        .AsNoTracking()
        .Where(m => m.IsActive)
        .OrderByDescending(m => m.MarginId)
        .FirstOrDefaultAsync();

    if (margin == null)
        return (false, "لا توجد نسب ربح مفعلة.", null);

    ProductFactoryAlternative entity;
    var isNew = dto.AlternativeId == 0;

    if (isNew)
    {
        entity = new ProductFactoryAlternative
        {
            ProductId = dto.ProductId,
            CreatedBy = currentUsername,
            CreatedAt = DateTime.Now,
            Status = ProductFactoryAlternativeStatuses.Proposed
        };
        _context.ProductFactoryAlternatives.Add(entity);
    }
    else
    {
        var existingEntity = await _context.ProductFactoryAlternatives
            .Include(x => x.Images)
            .FirstOrDefaultAsync(x => x.AlternativeId == dto.AlternativeId && x.ProductId == dto.ProductId);

        if (existingEntity == null)
            return (false, "البديل غير موجود.", null);

        entity = existingEntity;

        if (entity.Status == ProductFactoryAlternativeStatuses.Approved)
            return (false, "لا يمكن تعديل بديل معتمد. أنشئ بديلًا جديدًا أو غيّر حالته أولاً.", null);
    }

    entity.AlternativeName = dto.AlternativeName.Trim();
    entity.SpecificationSummary = string.IsNullOrWhiteSpace(dto.SpecificationSummary) ? null : dto.SpecificationSummary.Trim();
    entity.ManufacturingDescription = string.IsNullOrWhiteSpace(dto.ManufacturingDescription) ? null : dto.ManufacturingDescription.Trim();
    entity.Period = dto.Period;
    entity.PurchasePriceCClass = NormalizePrice(dto.PurchasePriceCClass);
    entity.PurchasePricePremium = NormalizePrice(dto.PurchasePricePremium);
    entity.PurchasePriceElite = NormalizePrice(dto.PurchasePriceElite);
    entity.SuggestedSalePriceCClass = CalculateSuggestedSale(entity.PurchasePriceCClass, margin.CClassMargin);
    entity.SuggestedSalePricePremium = CalculateSuggestedSale(entity.PurchasePricePremium, margin.PremiumMargin);
    entity.SuggestedSalePriceElite = CalculateSuggestedSale(entity.PurchasePriceElite, margin.EliteMargin);
    entity.Status = entity.Status == ProductFactoryAlternativeStatuses.Rejected
        ? ProductFactoryAlternativeStatuses.Proposed
        : entity.Status;

    await _context.SaveChangesAsync();

    if (files != null && files.Count > 0)
    {
        var webRoot = _env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var relativeFolder = Path.Combine("uploads", "product-alternative-images", entity.ProductId.ToString(), entity.AlternativeId.ToString());
        var absoluteFolder = Path.Combine(webRoot, relativeFolder);
        Directory.CreateDirectory(absoluteFolder);

        var hasPrimary = await _context.ProductFactoryAlternativeImages.AnyAsync(i => i.AlternativeId == entity.AlternativeId && i.IsPrimary);

        foreach (var file in files.Take(6))
        {
            if (file.Size <= 0)
                continue;

            if (file.Size > 5 * 1024 * 1024)
                return (false, $"الصورة {file.Name} تتجاوز الحد الأقصى 5MB", entity.AlternativeId);

            var ext = Path.GetExtension(file.Name);
            var storedName = $"{Guid.NewGuid():N}{ext}";
            var absolutePath = Path.Combine(absoluteFolder, storedName);

            await using var source = file.OpenReadStream(5 * 1024 * 1024);
            await using var target = new FileStream(absolutePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(target);

            _context.ProductFactoryAlternativeImages.Add(new ProductFactoryAlternativeImage
            {
                AlternativeId = entity.AlternativeId,
                ImagePath = "/" + relativeFolder.Replace('\\', '/') + "/" + storedName,
                Caption = null,
                IsPrimary = !hasPrimary,
                CreatedAt = DateTime.Now
            });

            hasPrimary = true;
        }

        await _context.SaveChangesAsync();
    }

    foreach (var role in new[] { "Admin", "SalesManager", "AccountManager", "Sales" })
    {
        _context.Notifications.Add(new Notification
        {
            Title = isNew ? "بديل مصنع جديد" : "تحديث بديل مصنع",
            Message = isNew
                ? $"أضاف المصنع بديلاً جديدًا للمنتج {product.ProductName}: {entity.AlternativeName}"
                : $"تم تحديث بديل المصنع للمنتج {product.ProductName}: {entity.AlternativeName}",
            RelatedTable = "Products",
            RelatedId = entity.ProductId,
            RecipientUser = role,
            CreatedBy = currentUsername,
            CreatedAt = DateTime.Now,
            FormName = "products/form"
        });
    }
    await _context.SaveChangesAsync();

    return (true, isNew ? "تم حفظ البديل بنجاح." : "تم تحديث البديل بنجاح.", entity.AlternativeId);
}

public async Task<(bool Success, string Message)> ApproveFactoryAlternativeAsync(int alternativeId, string currentUsername)
{
    var alternative = await _context.ProductFactoryAlternatives
        .FirstOrDefaultAsync(x => x.AlternativeId == alternativeId);

    if (alternative == null)
        return (false, "البديل غير موجود.");

    var product = await _context.Products.FirstOrDefaultAsync(x => x.ProductId == alternative.ProductId);
    if (product == null)
        return (false, "المنتج الأساسي غير موجود.");

    var oldProduct = new ProductApprovalSnapshot(
        product.PurchasePriceCClass,
        product.PurchasePrice,
        product.PurchasePriceElite,
        product.SuggestedSalePriceCClass,
        product.SuggestedSalePrice,
        product.SuggestedSalePriceElite,
        product.Period,
        product.ManufacturingDescription);

    var siblings = await _context.ProductFactoryAlternatives
        .Where(x => x.ProductId == alternative.ProductId)
        .ToListAsync();

    foreach (var item in siblings)
    {
        item.IsPrimary = item.AlternativeId == alternativeId;
        if (item.AlternativeId != alternativeId && item.Status == ProductFactoryAlternativeStatuses.Approved)
            item.Status = ProductFactoryAlternativeStatuses.Proposed;
    }

    alternative.Status = ProductFactoryAlternativeStatuses.Approved;
    alternative.IsPrimary = true;
    alternative.ReviewedBy = currentUsername;
    alternative.ReviewedAt = DateTime.Now;

    product.PurchasePriceCClass = alternative.PurchasePriceCClass;
    product.PurchasePrice = alternative.PurchasePricePremium;
    product.PurchasePriceElite = alternative.PurchasePriceElite;
    product.SuggestedSalePriceCClass = alternative.SuggestedSalePriceCClass;
    product.SuggestedSalePrice = alternative.SuggestedSalePricePremium;
    product.SuggestedSalePriceElite = alternative.SuggestedSalePriceElite;
    product.Period = alternative.Period;
    product.ManufacturingDescription = BuildApprovedManufacturingNotes(alternative);
    product.PricingStatusId = 3;

    TrackAlternativeApprovalHistory(product.ProductId, oldProduct, product, currentUsername, alternative.AlternativeName);

    _context.Notifications.Add(new Notification
    {
        Title = "تم اعتماد بديل مصنع",
        Message = $"تم اعتماد البديل {alternative.AlternativeName} للمنتج {product.ProductName}. وأصبح جاهزًا للاستخدام وأمر التشغيل الحالي سيعكس هذا الاعتماد.",
        RelatedTable = "Products",
        RelatedId = product.ProductId,
        RecipientUser = product.CreatedBy ?? "Admin",
        CreatedBy = currentUsername,
        CreatedAt = DateTime.Now,
        FormName = "products/form"
    });

    await _auditService.LogAsync<object>(
        "Products",
        "اعتماد بديل مصنع",
        product.ProductId.ToString(),
        oldProduct,
        new
        {
            product.PurchasePriceCClass,
            product.PurchasePrice,
            product.PurchasePriceElite,
            product.SuggestedSalePriceCClass,
            product.SuggestedSalePrice,
            product.SuggestedSalePriceElite,
            product.Period,
            product.ManufacturingDescription,
            ApprovedAlternative = alternative.AlternativeName
        },
        currentUsername);

    await _context.SaveChangesAsync();
    return (true, "تم اعتماد البديل وتحديث المنتج الأساسي بنجاح.");
}

public async Task<(bool Success, string Message)> RejectFactoryAlternativeAsync(int alternativeId, string currentUsername, string? reason = null)
{
    var alternative = await _context.ProductFactoryAlternatives.FirstOrDefaultAsync(x => x.AlternativeId == alternativeId);
    if (alternative == null)
        return (false, "البديل غير موجود.");

    alternative.Status = ProductFactoryAlternativeStatuses.Rejected;
    alternative.IsPrimary = false;
    alternative.ReviewedBy = currentUsername;
    alternative.ReviewedAt = DateTime.Now;

    if (await _context.Products.Where(p => p.ProductId == alternative.ProductId).Select(p => p.CreatedBy).FirstOrDefaultAsync() is string creator && !string.IsNullOrWhiteSpace(creator))
    {
        _context.Notifications.Add(new Notification
        {
            Title = "تم رفض بديل مصنع",
            Message = string.IsNullOrWhiteSpace(reason)
                ? $"تم رفض البديل {alternative.AlternativeName}."
                : $"تم رفض البديل {alternative.AlternativeName}. السبب: {reason}",
            RelatedTable = "ProductFactoryAlternatives",
            RelatedId = alternative.AlternativeId,
            RecipientUser = creator,
            CreatedBy = currentUsername,
            CreatedAt = DateTime.Now,
            FormName = "products/form"
        });
    }

    await _context.SaveChangesAsync();
    return (true, "تم رفض البديل.");
}

public async Task<(bool Success, string Message)> DeleteFactoryAlternativeAsync(int alternativeId, string currentUsername)
{
    var alternative = await _context.ProductFactoryAlternatives
        .Include(x => x.Images)
        .FirstOrDefaultAsync(x => x.AlternativeId == alternativeId);

    if (alternative == null)
        return (false, "البديل غير موجود.");

    if (alternative.Status == ProductFactoryAlternativeStatuses.Approved || alternative.IsPrimary)
        return (false, "لا يمكن حذف بديل معتمد. قم برفضه أولاً أو اعتمد بديلًا آخر.");

    _context.ProductFactoryAlternativeImages.RemoveRange(alternative.Images);
    _context.ProductFactoryAlternatives.Remove(alternative);
    await _context.SaveChangesAsync();

    return (true, "تم حذف البديل.");
}

private static decimal? NormalizePrice(decimal? value)
    => value.HasValue && value.Value > 0 ? value.Value : null;

private static decimal? CalculateSuggestedSale(decimal? cost, decimal marginPercent)
{
    if (!cost.HasValue || cost.Value <= 0)
        return null;

    var raw = cost.Value * (1 + marginPercent / 100m);
    return Math.Ceiling(raw / 100m) * 100m;
}

private static string? BuildApprovedManufacturingNotes(ProductFactoryAlternative alternative)
{
    var parts = new List<string>();
    if (!string.IsNullOrWhiteSpace(alternative.SpecificationSummary))
        parts.Add($"المواصفة المعتمدة: {alternative.SpecificationSummary.Trim()}");
    if (!string.IsNullOrWhiteSpace(alternative.ManufacturingDescription))
        parts.Add($"ملاحظات التصنيع: {alternative.ManufacturingDescription.Trim()}");
    return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts);
}

private void TrackAlternativeApprovalHistory(int productId, ProductApprovalSnapshot oldProduct, Product product, string currentUsername, string alternativeName)
{
    void Add(string priceType, decimal? oldValue, decimal? newValue)
    {
        if (oldValue == newValue)
            return;

        _context.PriceHistories.Add(new PriceHistory
        {
            ProductId = productId,
            PriceType = priceType,
            OldPrice = oldValue ?? 0m,
            NewPrice = newValue ?? 0m,
            ChangedBy = currentUsername,
            ChangedAt = DateTime.Now,
            ChangeReason = $"اعتماد بديل مصنع: {alternativeName}"
        });
    }

    Add("Alternative_CClass_Cost", oldProduct.PurchasePriceCClass, product.PurchasePriceCClass);
    Add("Alternative_Premium_Cost", oldProduct.PurchasePrice, product.PurchasePrice);
    Add("Alternative_Elite_Cost", oldProduct.PurchasePriceElite, product.PurchasePriceElite);
    Add("Alternative_CClass_Sale", oldProduct.SuggestedSalePriceCClass, product.SuggestedSalePriceCClass);
    Add("Alternative_Premium_Sale", oldProduct.SuggestedSalePrice, product.SuggestedSalePrice);
    Add("Alternative_Elite_Sale", oldProduct.SuggestedSalePriceElite, product.SuggestedSalePriceElite);

    if (oldProduct.Period != product.Period)
    {
        _context.PriceHistories.Add(new PriceHistory
        {
            ProductId = productId,
            PriceType = "Alternative_Manufacturing_Period",
            OldPrice = oldProduct.Period ?? 0m,
            NewPrice = product.Period ?? 0m,
            ChangedBy = currentUsername,
            ChangedAt = DateTime.Now,
            ChangeReason = $"اعتماد بديل مصنع: {alternativeName}"
        });
    }
}

private sealed record ProductApprovalSnapshot(
    decimal? PurchasePriceCClass,
    decimal? PurchasePrice,
    decimal? PurchasePriceElite,
    decimal? SuggestedSalePriceCClass,
    decimal? SuggestedSalePrice,
    decimal? SuggestedSalePriceElite,
    int? Period,
    string? ManufacturingDescription);

}
