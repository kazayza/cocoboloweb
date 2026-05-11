namespace COCOBOLOERPNEW.DTOs;

// ============================
// DTO لقائمة الفواتير
// ============================
public class InvoiceListDto
{
    public int TransactionId { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime TransactionDate { get; set; }
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? PartyPhone { get; set; }
    public int WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public int? EmpId { get; set; }
    public string? EmpName { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal? NetTotalAmount { get; set; }
    public decimal TotalChargesAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal Remaining => GrandTotal - PaidAmount;
    public decimal PaidPercentage => GrandTotal == 0 ? 0 : Math.Round((PaidAmount / GrandTotal) * 100, 1);
    public string? PaymentMethod { get; set; }
    public string? InvoiceStatus { get; set; }
    public bool? IsDelivered { get; set; }
    public DateTime? DueDate { get; set; }
    public int ItemsCount { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

// ============================
// DTO للفورم (إضافة / تعديل)
// ============================
public class InvoiceFormDto
{
    public int TransactionId { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime TransactionDate { get; set; } = DateTime.Now;
    public int? PartyId { get; set; }
    public string? PartyName { get; set; }
    public int? WarehouseId { get; set; }
    public int? EmpId { get; set; }
    public string? EmpName { get; set; }
    public DateTime? DueDate { get; set; }
    public string TransactionType { get; set; } = "Sale";

    public decimal TotalAmount { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal? NetTotalAmount { get; set; }
    public decimal TotalChargesAmount { get; set; }
    public decimal GrandTotal { get; set; }

    public decimal PaidAmount { get; set; }
    public decimal AppliedAdvanceAmount { get; set; }
    public string? PaymentMethod { get; set; } = "Cash";
    public int? CashBoxId { get; set; }

    public string? Notes { get; set; }
    public string? InvoiceStatus { get; set; } = "Open";
    public bool? IsDelivered { get; set; } = false;
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<InvoiceItemDto> Items { get; set; } = new();
    public List<InvoiceChargeDto> Charges { get; set; } = new();
    public List<int> SelectedAdvanceChargeIds { get; set; } = new();

    public int? MirrorPurchaseTransactionId { get; set; }
    public string? MirrorPurchaseReferenceNumber { get; set; }
}

// ============================
// صنف داخل الفاتورة
// ============================
public class InvoiceItemDto
{
    public int DetailId { get; set; }
    public int ProductId { get; set; }
    public string? ProductName { get; set; }
    public string? ProductDescription { get; set; }
    public string? ProductImagePath { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount => Math.Round(Quantity * UnitPrice, 2);
    public string? Notes { get; set; }
    public int? AvailableStock { get; set; }

    // Pricing Tier
    public string PricingTier { get; set; } = PricingTiers.Premium;

    // الأسعار المتاحة من المنتج
    public decimal? SalePricePremium { get; set; }
    public decimal? SalePriceElite { get; set; }
    public decimal? PurchasePricePremium { get; set; }
    public decimal? PurchasePriceElite { get; set; }
    public int? Period { get; set; }
}

// ============================
// رسوم إضافية داخل الفاتورة
// ============================
public class InvoiceChargeDto
{
    public int ChargeId { get; set; }
    public string? ChargeDescription { get; set; }
    public decimal ChargeAmount { get; set; }
    public string? Notes { get; set; }
}

// ============================
// دفعة مقدمة لعميل
// ============================
public class CustomerAdvanceDto
{
    public int ChargeId { get; set; }
    public int PartyId { get; set; }
    public string? ChargeDescription { get; set; }
    public decimal ChargeAmount { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
    public bool IsApplied { get; set; }
    public int? AppliedToTransactionId { get; set; }
    public string? AppliedToReferenceNumber { get; set; }
}

// ============================
// تفاصيل الفاتورة للعرض
// ============================
public class InvoiceDetailsDto
{
    public InvoiceFormDto Invoice { get; set; } = new();
    public List<PaymentHistoryDto> Payments { get; set; } = new();
}

public class PaymentHistoryDto
{
    public int PaymentId { get; set; }
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public string? PaymentMethod { get; set; }
    public string? CashBoxName { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public decimal Percentage { get; set; }  // النسبة من الإجمالي
}

// ============================
// فلتر الفواتير
// ============================
public class InvoiceFilterDto
{
    public string? SearchText { get; set; }
    public int? PartyId { get; set; }
    public int? WarehouseId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? InvoiceStatus { get; set; }
    public string? PaymentMethod { get; set; }
    public bool? IsDelivered { get; set; }
    public bool? HasRemaining { get; set; }
    public string TransactionType { get; set; } = "Sale";
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string SortBy { get; set; } = "TransactionDate";
    public bool SortDescending { get; set; } = true;
}

// ============================
// إحصائيات الفواتير
// ============================
public class InvoiceStatsDto
{
    public int TotalCount { get; set; }
    public decimal TotalSales { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalRemaining { get; set; }
    public int TodayCount { get; set; }
    public decimal TodaySales { get; set; }
    public int OpenCount { get; set; }
    public int OverdueCount { get; set; }
}

// ============================
// لـ Autocomplete العميل
// ============================
public class PartyLookupDto
{
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? Phone { get; set; }
    public string? Phone2 { get; set; }
    public string? City { get; set; }
    public decimal AdvanceBalance { get; set; }
}

// ============================
// لـ Autocomplete المنتج
// ============================
public class ProductLookupDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string? ProductDescription { get; set; }
    public string? ImagePath { get; set; }
    public decimal? SuggestedSalePrice { get; set; }
    public decimal? SuggestedSalePriceElite { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? PurchasePriceElite { get; set; }
    public int AvailableStock { get; set; }
    public string? PricingType { get; set; }
    public int? Period { get; set; }
}

// ============================
// طباعة الفاتورة
// ============================
public class InvoicePrintDto
{
    public InvoiceFormDto Invoice { get; set; } = new();
    public List<PaymentHistoryDto> Payments { get; set; } = new();

    // بيانات الشركة
    public string? CompanyName { get; set; }
    public string? CompanyPhone { get; set; }
    public string? CompanyAddress { get; set; }
    public string? CompanyTaxNumber { get; set; }
    public string? CompanyLogo { get; set; }

    // بيانات العميل الكاملة
    public string? CustomerAddress { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerCity { get; set; }
}

// ============================
// ثوابت
// ============================
public static class InvoiceStatuses
{
    public const string Open = "Open";
    public const string PartiallyPaid = "PartiallyPaid";
    public const string Paid = "Paid";
    public const string Cancelled = "Cancelled";

    public static readonly Dictionary<string, string> All = new()
    {
        { Open, "مفتوحة" },
        { PartiallyPaid, "مدفوعة جزئياً" },
        { Paid, "مدفوعة بالكامل" },
        { Cancelled, "ملغية" }
    };
}

public static class TransactionTypes
{
    public const string Sale = "Sale";
    public const string Purchase = "Purchase";
    public const string SaleReturn = "SaleReturn";
    public const string PurchaseReturn = "PurchaseReturn";
}

public static class PaymentMethods
{
    public const string Cash = "Cash";
    public const string Bank = "Bank";
    public const string Credit = "Credit";
    public const string InstaPay = "InstaPay";
    public const string Other = "Other";

    public static readonly Dictionary<string, string> All = new()
    {
        { Cash, "نقدي" },
        { Bank, "تحويل بنكي" },
        { Credit, "آجل" },
        { InstaPay, "InstaPay" },
        { Other, "أخرى" }
    };
}

public static class AdvanceChargeTypes
{
    public const string Inspection = "رسوم معاينة";
    public const string Deposit = "عربون";
    public const string Other = "دفعة مقدمة";
}

public static class PricingTiers
{
    public const string Premium = "Premium";
    public const string Elite = "Elite";

    public static readonly Dictionary<string, string> All = new()
    {
        { Premium, "بريميوم" },
        { Elite, "إيليت" }
    };
}

public static class SystemConstants
{
    public const int DefaultSupplierId = 11;

    // النسب الجاهزة للدفع
    public static readonly int[] QuickPayPercentages = { 70, 20, 10 };
}

// ============================
// أدوار النظام (لإرسال الإشعارات)
// ============================
public static class SystemRoles
{
    public const string Admin = "Admin";
    public const string SalesManager = "SalesManager";
    public const string Sales = "Sales";
    public const string AccountManager = "AccountManager";
    public const string Account = "Account";
}
