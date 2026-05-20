namespace COCOBOLOERPNEW.DTOs;

// ============================
// قائمة عروض الأسعار
// ============================
public class QuotationListDto
{
    public int QuotationId { get; set; }
    public string ReferenceNumber { get; set; } = "";
    public DateTime QuotationDate { get; set; }
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? PartyPhone { get; set; }
    public int? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public string? PricingType { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public int ItemsCount { get; set; }
    public string Status { get; set; } = QuotationStatuses.Draft;
    public int? InvoiceId { get; set; }
    public string? InvoiceReference { get; set; }
    public DateTime? ValidUntil { get; set; }
    public bool IsExpired => ValidUntil.HasValue && ValidUntil.Value.Date < DateTime.Today && InvoiceId == null;
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

// ============================
// فورم عرض السعر
// ============================
public class QuotationFormDto
{
    public int QuotationId { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime QuotationDate { get; set; } = DateTime.Now;
    public DateTime? ValidUntil { get; set; } = DateTime.Today.AddDays(15);

    public int? PartyId { get; set; }
    public string? PartyName { get; set; }
    public int? WarehouseId { get; set; }
    public int? EmpId { get; set; }
    public string? EmpName { get; set; }

    public string PricingType { get; set; } = PricingTiers.Premium; // Premium / Elite

    public decimal TotalAmount { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal? NetTotalAmount { get; set; }
    public decimal GrandTotal { get; set; }

    public string? Notes { get; set; }
    public string? Terms { get; set; }   // الشروط والأحكام
    public string Status { get; set; } = QuotationStatuses.Draft;

    public int? InvoiceId { get; set; }
    public string? InvoiceReference { get; set; }

    public List<QuotationItemDto> Items { get; set; } = new();
}

public class QuotationItemDto
{
    public int QuotationDetailId { get; set; }
    public int ProductId { get; set; }
    public string? ProductName { get; set; }
    public string? ProductDescription { get; set; }
    public string? ProductImagePath { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount => Math.Round(Quantity * UnitPrice, 2);
    public string? Notes { get; set; }

    public string PricingTier { get; set; } = PricingTiers.Premium;
    public decimal? SalePricePremium { get; set; }
    public decimal? SalePriceElite { get; set; }
}

// ============================
// فلتر عروض الأسعار
// ============================
public class QuotationFilterDto
{
    public string? SearchText { get; set; }
    public int? PartyId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? Status { get; set; }
    public bool? IsConverted { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string SortBy { get; set; } = "QuotationDate";
    public bool SortDescending { get; set; } = true;
}

// ============================
// إحصائيات
// ============================
public class QuotationStatsDto
{
    public int TotalCount { get; set; }
    public decimal TotalValue { get; set; }
    public int PendingCount { get; set; }
    public int AcceptedCount { get; set; }
    public int ConvertedCount { get; set; }
    public int ExpiredCount { get; set; }
    public int RejectedCount { get; set; }
    public decimal ConvertedValue { get; set; }
    public decimal ConversionRate { get; set; } // %
}

// ============================
// طباعة عرض السعر
// ============================
public class QuotationPrintDto
{
    public QuotationFormDto Quotation { get; set; } = new();

    // بيانات الشركة
    public string? CompanyName { get; set; }
    public string? CompanyPhone { get; set; }
    public string? CompanyAddress { get; set; }
    public string? CompanyTaxNumber { get; set; }

    // بيانات العميل الكاملة
    public string? CustomerAddress { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerCity { get; set; }
}

// ============================
// ثوابت
// ============================
public static class QuotationStatuses
{
    public const string Draft = "Draft";           // مسودة
    public const string Sent = "Sent";             // أُرسلت للعميل
    public const string Accepted = "Accepted";     // قبلها العميل
    public const string Rejected = "Rejected";     // رفضها
    public const string Converted = "Converted";   // تحوّلت لفاتورة
    public const string Expired = "Expired";       // انتهت صلاحيتها

    public static readonly Dictionary<string, string> All = new()
    {
        { Draft, "مسودة" },
        { Sent, "تم الإرسال" },
        { Accepted, "مقبول" },
        { Rejected, "مرفوض" },
        { Converted, "تحوّل لفاتورة" },
        { Expired, "منتهي الصلاحية" }
    };
}
