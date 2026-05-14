namespace COCOBOLOERPNEW.DTOs;

// ============================
// قائمة المدفوعات
// ============================
public class PaymentListDto
{
    public int PaymentId { get; set; }
    public string ReceiptNumber { get; set; } = "";  // RCV-2025-00001 / PMT-2025-00001
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public decimal Percentage { get; set; }   // النسبة من إجمالي الفاتورة

    // Invoice Info
    public int TransactionId { get; set; }
    public string? InvoiceReferenceNumber { get; set; }
    public string TransactionType { get; set; } = "";   // Sale / Purchase
    public decimal InvoiceGrandTotal { get; set; }
    public decimal InvoicePaidAmount { get; set; }
    public decimal InvoiceRemaining => InvoiceGrandTotal - InvoicePaidAmount;

    // Party Info
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? PartyPhone { get; set; }

    // Employee (للمشتريات: العميل الأصلي عند الفاتورة المرآة)
    public int? EmpId { get; set; }
    public string? EmpName { get; set; }

    // Payment Details
    public string? PaymentMethod { get; set; }
    public int? CashBoxId { get; set; }
    public string? CashBoxName { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }

    // Status
    public bool IsCancelled { get; set; }
    public string? CancelReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }
}

// ============================
// فلتر المدفوعات
// ============================
public class PaymentFilterDto
{
    public string? SearchText { get; set; }       // عميل / تليفون / رقم الفاتورة
    public string TransactionType { get; set; } = TransactionTypes.Sale; // Sale / Purchase
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int? CashBoxId { get; set; }
    public string? PaymentMethod { get; set; }
    public int? EmpId { get; set; }
    public int? PartyId { get; set; }
    public bool? IncludeCancelled { get; set; }
    public bool? IncludeMirrorPurchases { get; set; }  // الفواتير المرآة (PartyId=11)
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string SortBy { get; set; } = "PaymentDate";
    public bool SortDescending { get; set; } = true;
}

// ============================
// إحصائيات
// ============================
public class PaymentStatsDto
{
    public int TotalCount { get; set; }
    public decimal TotalAmount { get; set; }
    public int TodayCount { get; set; }
    public decimal TodayAmount { get; set; }
    public int MonthCount { get; set; }
    public decimal MonthAmount { get; set; }
    public int CashBoxesCount { get; set; }
    public List<CashBoxSummaryDto> CashBoxBreakdown { get; set; } = new();
    public List<PaymentMethodSummaryDto> MethodBreakdown { get; set; } = new();
}

public class CashBoxSummaryDto
{
    public int CashBoxId { get; set; }
    public string CashBoxName { get; set; } = "";
    public int Count { get; set; }
    public decimal Total { get; set; }
}

public class PaymentMethodSummaryDto
{
    public string PaymentMethod { get; set; } = "";
    public string PaymentMethodAr { get; set; } = "";
    public int Count { get; set; }
    public decimal Total { get; set; }
}

// ============================
// فورم تسجيل دفعة جديدة
// ============================
public class PaymentFormDto
{
    public int? TransactionId { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.Now;
    public string? InvoiceReferenceNumber { get; set; }
    public string TransactionType { get; set; } = TransactionTypes.Sale;

    public int? PartyId { get; set; }
    public string? PartyName { get; set; }
    public string? PartyPhone { get; set; }

    // معلومات الفاتورة
    public decimal InvoiceGrandTotal { get; set; }
    public decimal InvoicePaidBefore { get; set; }
    public decimal InvoiceRemaining { get; set; }

    // الدفعة الجديدة
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "Cash";
    public int? CashBoxId { get; set; }
    public string? Notes { get; set; }

    // Validation
    public bool IsValid { get; set; }
    public string? LastModifiedBy { get; set; }
    public DateTime? LastModifiedAt { get; set; }
}


// ============================
// تحليل النسب لفاتورة (المرجع للمستخدم)
// ============================
public class PaymentAnalysisDto
{
    public int TransactionId { get; set; }
    public string? ReferenceNumber { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidBefore { get; set; }
    public decimal Remaining { get; set; }
    public decimal PaidPercentage { get; set; }

    // جدول النسب المرجعي (70/20/10)
    public List<PaymentTierDto> Tiers { get; set; } = new();

    // تحليل الدفعة المدخلة (لو فيه)
    public PaymentTierAnalysisDto? CurrentPaymentAnalysis { get; set; }
}

public class PaymentTierDto
{
    public int TierNumber { get; set; }       // 1, 2, 3
    public string TierName { get; set; } = "";  // "الدفعة الأولى"
    public decimal Percentage { get; set; }   // 70, 20, 10
    public decimal Amount { get; set; }       // المبلغ المحسوب
    public decimal AmountPaid { get; set; }   // المدفوع منها فعلاً
    public decimal AmountRemaining => Math.Max(0, Amount - AmountPaid);
    public decimal CompletionPercentage => Amount == 0 ? 0 : Math.Round((AmountPaid / Amount) * 100, 1);
    public string Status { get; set; } = "Pending";  // Paid / Partial / Pending
}

public class PaymentTierAnalysisDto
{
    public decimal EnteredAmount { get; set; }
    public decimal PercentageOfInvoice { get; set; }   // النسبة من إجمالي الفاتورة
    public string Description { get; set; } = "";       // وصف نصي للتحليل
    public List<TierAllocation> Allocations { get; set; } = new();
}

public class TierAllocation
{
    public int TierNumber { get; set; }
    public string TierName { get; set; } = "";
    public decimal AmountAllocated { get; set; }
    public decimal RemainingInTier { get; set; }
    public string Note { get; set; } = "";
}

// ============================
// سند القبض/الصرف للطباعة
// ============================
public class PaymentReceiptDto
{
    public PaymentListDto Payment { get; set; } = new();

    // بيانات الشركة
    public string? CompanyName { get; set; }
    public string? CompanyPhone { get; set; }
    public string? CompanyAddress { get; set; }
    public string? CompanyTaxNumber { get; set; }

    // بيانات العميل التفصيلية
    public string? CustomerAddress { get; set; }
    public string? CustomerCity { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerNationalId { get; set; }

    // المبلغ بالحروف (عربي)
    public string AmountInWords { get; set; } = "";

    // نوع السند
    public string ReceiptType { get; set; } = "Receive"; // Receive / Pay
    public string ReceiptTypeAr => ReceiptType == "Receive" ? "سند قبض" : "سند صرف";
}

// ============================
// ثوابت الـ Tiers الافتراضية
// ============================
public static class PaymentTiers
{
    public static readonly int[] DefaultPercentages = { 70, 20, 10 };

    public static readonly Dictionary<int, string> TierNames = new()
    {
        { 1, "الدفعة الأولى (70%)" },
        { 2, "الدفعة الثانية (20%)" },
        { 3, "الدفعة الثالثة (10%)" }
    };
}

// ============================
// Lookup للفاتورة في PaymentForm
// ============================
public class InvoiceLookupDto
{
    public int TransactionId { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime TransactionDate { get; set; }
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? PartyPhone { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal Remaining => GrandTotal - PaidAmount;
    public string TransactionType { get; set; } = "";
    public string? Status { get; set; }
    public string? EmpName { get; set; }
}
public class PartyBalanceDto
{
    public int TotalInvoices { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal Remaining { get; set; }
    public int PaidInvoices { get; set; }
    public int PartialInvoices { get; set; }
    public int OpenInvoices { get; set; }
}