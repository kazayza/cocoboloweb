namespace COCOBOLOERPNEW.DTOs;

/// <summary>صف في قائمة الأسعار (الكتالوج العام — منتجات غير مرتبطة بعميل)</summary>
public class PriceListRowDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    public string GroupName { get; set; } = null!;
    public decimal? StdPrice { get; set; }
    public decimal? PremiumPrice { get; set; }
    public decimal? ElitePrice { get; set; }
    public int PricingStatusId { get; set; }

    public bool HasAnyPrice => (StdPrice ?? 0) > 0 || (PremiumPrice ?? 0) > 0 || (ElitePrice ?? 0) > 0;
}

/// <summary>فرع لفلتر التوفر</summary>
public class PriceListBranchDto
{
    public int BranchId { get; set; }
    public string BranchName { get; set; } = null!;
}

/// <summary>سجل في تاريخ أسعار منتج</summary>
public class PriceHistoryRowDto
{
    public string PriceType { get; set; } = null!;
    public decimal? OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public string ChangedBy { get; set; } = null!;
    public DateTime ChangedAt { get; set; }
    public string? ChangeReason { get; set; }
}
