using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.DTOs;

/// <summary>
/// مفردات تصنيف كميات بند التسليمة (خطة التسليم الجزئي — القسم 11.1):
/// كل كمية محاوَلة في حدث تسليم تُقسَّم لأربع حصص مجموعها = الكمية المحاوَلة،
/// والمقبولة وحدها تُحسب «مُسلَّمة نهائيًا» — والراجعة تفتح سجل صيانة.
/// </summary>
public static class DeliveryDispositions
{
    public const string Accepted             = "Accepted";
    public const string ManufacturingDefect  = "ManufacturingDefect";
    public const string HandlingDamage       = "HandlingDamage";
    public const string CustomerModification = "CustomerModification";

    public static readonly Dictionary<string, string> All = new()
    {
        { Accepted, "مقبول" },
        { ManufacturingDefect, "عيب تصنيع" },
        { HandlingDamage, "تلف مناولة (عمالة التسليم)" },
        { CustomerModification, "تعديل بطلب العميل" }
    };

    /// <summary>اسم عربي آمن لأي تصنيف (يسقط للخام إن كان غير معروف)</summary>
    public static string NameOf(string? cls) =>
        cls != null && All.TryGetValue(cls, out var n) ? n : cls ?? "";
}

/// <summary>
/// آلة حالات سجل صيانة التسليمات (القسم 11.3). الحالات المنتهية تُخرج الكمية
/// من «في الصيانة» (InLoop): إعادة التسليم تُضيفها للمقبول، والردّ النهائي
/// ينقلها للمُسوّى، والاستبدال يعيدها لـ«بانتظار التسليم»، والإلغاء لبنود التعديل.
/// </summary>
public static class DeliveryMaintenanceStatuses
{
    public const string Pending             = "Pending";
    public const string InProgress          = "InProgress";
    public const string ReadyForRedelivery  = "ReadyForRedelivery";
    public const string Redelivered         = "Redelivered";
    public const string Unrepairable        = "Unrepairable";
    public const string ClosedByReplacement = "ClosedByReplacement";
    public const string ClosedByCredit      = "ClosedByCredit";
    public const string Cancelled           = "Cancelled";

    public static readonly Dictionary<string, string> All = new()
    {
        { Pending, "بانتظار الصيانة" },
        { InProgress, "قيد الصيانة" },
        { ReadyForRedelivery, "جاهز لإعادة التسليم" },
        { Redelivered, "أعيد تسليمه" },
        { Unrepairable, "تعذّر إصلاحه" },
        { ClosedByReplacement, "أُغلق باستبدال" },
        { ClosedByCredit, "أُغلق بردّ نهائي/رصيد" },
        { Cancelled, "أُلغي الطلب" }
    };

    /// <summary>الحالات التي تبقى فيها الكمية «في الصيانة» (داخل الحلقة)</summary>
    public static readonly string[] InLoop =
        { Pending, InProgress, ReadyForRedelivery, Unrepairable };

    /// <summary>الحالات المنتهية — لا انتقالات بعدها</summary>
    public static readonly string[] Terminal =
        { Redelivered, ClosedByReplacement, ClosedByCredit, Cancelled };

    public static bool IsInLoop(string? status) =>
        status != null && Array.IndexOf(InLoop, status) >= 0;

    public static bool IsTerminal(string? status) =>
        status != null && Array.IndexOf(Terminal, status) >= 0;

    public static string NameOf(string? status) =>
        status != null && All.TryGetValue(status, out var n) ? n : status ?? "";
}

/// <summary>سجل صيانة واحد كما يُعرض في طابور الصيانة وسجل التسليمات وملخصات الفاتورة</summary>
public class DeliveryMaintenanceItemDto
{
    public int MaintenanceId { get; set; }
    public int DeliveryItemId { get; set; }
    public int DeliveryId { get; set; }
    public int TransactionId { get; set; }
    public string? TransactionRef { get; set; }
    public string? PartyName { get; set; }
    public int TransactionDetailId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal Quantity { get; set; }

    public string ReasonClass { get; set; } = "";
    public string ReasonClassName => DeliveryDispositions.NameOf(ReasonClass);
    public string IssueDescription { get; set; } = "";

    public string Status { get; set; } = "";
    public string StatusName => DeliveryMaintenanceStatuses.NameOf(Status);
    public bool IsInLoop => DeliveryMaintenanceStatuses.IsInLoop(Status);
    public bool IsTerminal => DeliveryMaintenanceStatuses.IsTerminal(Status);

    public bool Billable { get; set; }
    public int? AdditionalChargeId { get; set; }
    public string? ChargeStatus { get; set; }
    public string? ChargeStatusName =>
        ChargeStatus != null && ChargeStatuses.All.TryGetValue(ChargeStatus, out var n) ? n : ChargeStatus;
    public decimal? ChargeAmount { get; set; }

    /// <summary>التكلفة الداخلية — تُملأ فقط لمن يملك رؤيتها (DeliveryPermissions.CanSeeRepairCost)</summary>
    public decimal? RepairCost { get; set; }
    public string? RepairCostNotes { get; set; }

    public int? HandledByEmployeeId { get; set; }
    public string? HandledByEmployeeName { get; set; }

    public DateTime CreatedAt { get; set; }
    public int AgeDays => Math.Max(0, (DateTime.Today - CreatedAt.Date).Days);
    public DateTime? StartedAt { get; set; }
    public DateTime? ReadyAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? StatusBy { get; set; }
    public string? ResolutionNotes { get; set; }
    public bool IsVoided { get; set; }
}

/// <summary>ملخص نتائج تسليم فاتورة واحدة — لشريط تفاصيل الفاتورة والبطاقات الخفيفة (القسم 11.5)</summary>
public class InvoiceOutcomesSummaryDto
{
    public int TransactionId { get; set; }

    /// <summary>عدد أحداث التسليم غير الملغاة التي فيها أي حصة راجعة</summary>
    public int EventsWithRemarks { get; set; }

    /// <summary>إجمالي الكمية الموجودة الآن داخل حلقة الصيانة (سجلات غير منتهية)</summary>
    public decimal InMaintenanceQty { get; set; }

    /// <summary>عدد سجلات الصيانة غير المنتهية</summary>
    public int OpenMaintenanceRecords { get; set; }

    /// <summary>عدد رسوم التعديل المرتبطة بتسليمات الفاتورة وما زالت بانتظار الاعتماد</summary>
    public int PendingModificationCharges { get; set; }

    public bool HasOutcomes =>
        EventsWithRemarks > 0 || OpenMaintenanceRecords > 0 || PendingModificationCharges > 0;
}
