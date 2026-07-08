using Microsoft.EntityFrameworkCore;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;

namespace COCOBOLOERPNEW.Services;

public class ProductService : IProductService
{
    private readonly db24804Context _context;
    private readonly IAuditService _auditService;

    public ProductService(db24804Context context, IAuditService auditService)
    {
        _context = context;
        _auditService = auditService;
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
                        CustomerName = customer != null ? customer.PartyName : null,
                        p.PricingType,
                        p.PricingStatusId,
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
            if (!pricedAt.HasValue && (p.PricingStatusId == 3 || (p.SuggestedSalePrice.HasValue && p.SuggestedSalePrice.Value > 0)))
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
                CustomerName = p.CustomerName,
                PricingType = p.PricingType,
                PricingStatusId = p.PricingStatusId,
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
        if (request.PriceType == "Premium")
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
}