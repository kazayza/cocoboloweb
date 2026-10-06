using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.DTOs;

// ═══════════════════════════════════════════════════════════
// موديول خدمة العملاء — أنواع و DTOs
// ═══════════════════════════════════════════════════════════

/// <summary>أنواع الفيدباك (مصدر المكالمة)</summary>
public static class CustomerCareKind
{
    public const string Visit              = "Visit";               // بعد زيارة المعرض
    public const string Delivery           = "Delivery";            // بعد تسليم الفاتورة
    public const string SalesInvoice       = "SalesInvoice";         // فاتورة مبيعات لم تُسلَّم بعد (طابور رابع — 2026-09-17)
    public const string ComplaintFollowUp  = "ComplaintFollowUp";    // عند وجود شكوى / بعد حلها
}

/// <summary>حالات سجل الفيدباك</summary>
public static class CustomerCareStatus
{
    public const string Open    = "Open";    // في انتظار مكالمة
    public const string Done    = "Done";    // تم التواصل
    public const string Skipped = "Skipped"; // أُسقط (مؤقتاً)
}

/// <summary>نتيجة المكالمة</summary>
public static class CustomerCareOutcome
{
    public const string Positive      = "Positive";      // رضا إيجابي
    public const string Neutral       = "Neutral";       // محايد — رد بدون رضا أو استياء
    public const string Negative      = "Negative";      // شكوى/استياء
    public const string NoAnswer      = "NoAnswer";      // لم يرد
    public const string NotInterested = "NotInterested"; // غير مهتم
    public const string CallbackLater = "CallbackLater"; // يتصل لاحقاً

    public static string ToText(string? o) => o switch
    {
        null or ""    => "لم يُحدد",
        Positive      => "إيجابي",
        Neutral       => "محايد",
        Negative      => "سلبي / استياء",
        NoAnswer      => "لم يرد",
        NotInterested => "غير مهتم",
        CallbackLater => "يتصل لاحقاً",
        _ => o ?? "—",
    };
}

public static class CustomerCareKindText
{
    public static string ToText(string? k) => k switch
    {
        CustomerCareKind.Visit             => "بعد زيارة المعرض",
        CustomerCareKind.Delivery          => "بعد تسليم الفاتورة",
        CustomerCareKind.SalesInvoice      => "فاتورة مبيعات بانتظار التسليم",
        CustomerCareKind.ComplaintFollowUp => "متابعة شكوى",
        _ => k ?? "—",
    };
}

/// <summary>بطاقة في طابور المكالمات (من أي مصدر)</summary>
public class CustomerCareQueueItemDto
{
    public string Kind { get; set; } = "";                 // Visit | Delivery | ComplaintFollowUp
    public int PartyId { get; set; }
    public int? OpportunityId { get; set; }
    public int? TransactionId { get; set; }
    public int? ComplaintId { get; set; }
    public int? VisitInteractionId { get; set; }
    public string ClientName { get; set; } = "";
    public string? Phone { get; set; }
    public DateTime? SourceDate { get; set; }
    public string Title { get; set; } = "";                // عنوان مرجع (فاتورة/شكوى/زيارة)
    public int? ExistingFeedbackId { get; set; }
    public string? FeedbackStatus { get; set; }            // null = لم يُتصل بعد
    public string? FeedbackOutcome { get; set; }
    public DateTime? NextCallDate { get; set; }
    public byte? Satisfaction { get; set; }                // آخر تقييم (1..5) إن وُجد
    public string? Channel { get; set; }                   // قناة آخر تواصل
    public bool IsOpen => FeedbackStatus is null or CustomerCareStatus.Open;
}

/// <summary>إحصاءات لوحة خدمة العملاء</summary>
public class CustomerCareKpiDto
{
    public int OpenVisits { get; set; }
    public int OpenDeliveries { get; set; }
    public int OpenSalesInvoices { get; set; }
    public int OpenComplaints { get; set; }
    public int DoneToday { get; set; }
    public int DoneTotal { get; set; }
    public double? AvgSatisfaction { get; set; }
    public int PositiveCount { get; set; }
}

public class CustomerCareDashboardDto
{
    public CustomerCareKpiDto Kpis { get; set; } = new();
    public List<CustomerCareQueueItemDto> Visits { get; set; } = new();
    public List<CustomerCareQueueItemDto> Deliveries { get; set; } = new();
    public List<CustomerCareQueueItemDto> SalesInvoices { get; set; } = new(); // 🧾 فواتير مبيعات لم تُسلَّم بعد
    public List<CustomerCareQueueItemDto> Complaints { get; set; } = new();
    public List<CustomerCareQueueItemDto> Callbacks { get; set; } = new(); // «يتصل لاحقاً»
}

public class CustomerCareDashboardFilterDto
{
    public int DaysBack { get; set; } = 30;   // نافذة افتراضية عند عدم اختيار تاريخ
    public DateTime? DateFrom { get; set; }    // فلترة المدة (من)
    public DateTime? DateTo { get; set; }      // فلترة المدة (إلى)
    public bool IncludeContacted { get; set; } // إظهار من تم التواصل معه
}

/// <summary>بيانات تسجيل/تحديث نتيجة مكالمة</summary>
public class CustomerCareLogDto
{
    public int? FeedbackId { get; set; }        // موجود = تحديث
    public string Kind { get; set; } = "";
    public int PartyId { get; set; }
    public string? ClientName { get; set; }
    public string? Phone { get; set; }
    public int? OpportunityId { get; set; }
    public int? TransactionId { get; set; }
    public int? ComplaintId { get; set; }
    public int? VisitInteractionId { get; set; }
    public DateTime? SourceDate { get; set; }
    public string Channel { get; set; } = "اتصال";
    public string Outcome { get; set; } = "";   // فاضي افتراضيًا — بلا اختيار مسبق
    public byte? Satisfaction { get; set; }     // 1..5
    public string? Notes { get; set; }
    public string? EmployeeNotes { get; set; } // انطباع الموظف (داخلي)
    public DateTime? NextCallDate { get; set; }
    public bool MarkSkipped { get; set; }
}

/// <summary>سطر في سجل الفيدباك</summary>
public class CustomerCareHistoryItemDto
{
    public int FeedbackId { get; set; }
    public string Kind { get; set; } = "";
    public string KindText => CustomerCareKindText.ToText(Kind);
    public string ClientName { get; set; } = "";
    public string? Phone { get; set; }
    public string Status { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string OutcomeText => CustomerCareOutcome.ToText(Outcome);
    public byte? Satisfaction { get; set; }
    public string? Notes { get; set; }
    public string? EmployeeNotes { get; set; }
    public DateTime? SourceDate { get; set; }
    public DateTime? NextCallDate { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? CompletedBy { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class CustomerCareHistoryFilterDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? Kind { get; set; }
    public string? Status { get; set; }
    public string? Search { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class CustomerCareOutcomeStatDto
{
    public string Outcome { get; set; } = "";
    public int Count { get; set; }
}

public class CustomerCareHistoryDto
{
    public List<CustomerCareHistoryItemDto> Items { get; set; } = new();
    public List<CustomerCareOutcomeStatDto> OutcomeStats { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageIndex { get; set; }
    public int PageSize { get; set; }
    public bool HasMore => PageIndex * PageSize < TotalCount;
}

/// <summary>فلاتر تقرير/تصدير</summary>
public class CustomerCareReportFilterDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? Kind { get; set; }
}
