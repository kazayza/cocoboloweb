using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IPriceListService
{
    /// <summary>صفوف قائمة الأسعار — الكتالوج العام فقط (منتجات غير مرتبطة بعميل)</summary>
    Task<List<PriceListRowDto>> GetRowsAsync();

    /// <summary>خريطة المتاح لكل منتج (إجمالي عبر مخازن الفرع — أو كل المخازن)</summary>
    Task<Dictionary<int, int>> GetStockMapAsync(int? branchId);

    /// <summary>الفروع لفلتر التوفر</summary>
    Task<List<PriceListBranchDto>> GetBranchesAsync();

    /// <summary>منتجات عليها طلب تعديل سعر معلق (لعرض الشارة)</summary>
    Task<HashSet<int>> GetPendingRequestProductIdsAsync();

    /// <summary>تعديل مباشر — المدير ومدير الحسابات فقط (يتسجل في PriceHistory + AuditLogs)</summary>
    Task<(bool Success, string Message)> UpdatePriceAsync(int productId, string tier, decimal newPrice, string reason, string currentUser);

    /// <summary>طلب تعديل سعر — يستخدم نفس جدول PriceChangeRequests وورك فلو المراجعة الموجود</summary>
    Task<(bool Success, string Message)> RequestPriceChangeAsync(int productId, string tier, decimal newPrice, string reason, string currentUser);

    /// <summary>تاريخ أسعار منتج</summary>
    Task<List<PriceHistoryRowDto>> GetHistoryAsync(int productId, int take = 15);

    /// <summary>معرفات كل صور المنتج (الرئيسية أولاً) — تُعرض عبر /api/product-image-by-id/{id}</summary>
    Task<List<int>> GetImageIdsAsync(int productId);
}
