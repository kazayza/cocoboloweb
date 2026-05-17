using Microsoft.EntityFrameworkCore;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;

namespace COCOBOLOERPNEW.Services;

public class ProductService : IProductService
{
    private readonly db24804Context _context;

    public ProductService(db24804Context context)
    {
        _context = context;
    }

        public async Task<List<ProductListDto>> GetProductsAsync(string? search)
    {
        var query = from p in _context.Products.AsNoTracking()
                    join c in _context.Parties.AsNoTracking()
                        on p.Customer equals c.PartyId into pc
                    from customer in pc.DefaultIfEmpty()
                    select new ProductListDto
                    {
                        ProductId = p.ProductId,
                        ProductName = p.ProductName,
                        ProductDescription = p.ProductDescription,
                        CustomerName = customer != null ? customer.PartyName : null,
                        PricingType = p.PricingType,
                        PricingStatusId = p.PricingStatusId,
                        SuggestedSalePrice = p.SuggestedSalePrice,
                        PdfPath = p.PdfPath // ✅ إضافة مسار الـ PDF الجديد
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

        var products = await query
            .OrderByDescending(x => x.ProductId)
            .ToListAsync();

        // ✅ جلب أرقام المنتجات اللي فيها ملفات PDF قديمة فقط (بدون تحميل الملفات نفسها)
                var idsWithOldPdf = await _context.Products
            .Where(p => p.Pdffile != null)
            .Select(p => p.ProductId)
            .ToListAsync();

        foreach (var p in products)
        {
            p.HasOldPdf = idsWithOldPdf.Contains(p.ProductId);
        }

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
            FormName = "frm_Products"
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
        FormName = "frm_Products"
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
        FormName = "frm_Products"
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
        FormName = "frm_Products"
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
        FormName = "frm_Products"
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
        FormName = "frm_Products"
    });

    await _context.SaveChangesAsync();
    await transaction.CommitAsync();
}
}