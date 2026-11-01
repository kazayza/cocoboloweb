using System;

namespace COCOBOLOERPNEW.Models;

/// <summary>
/// سجل صيانة لحصة راجعة من تسليمة (خطة التسليم الجزئي — القسم 11.3):
/// كل حصة راجعة (عيب تصنيع/تلف مناولة/تعديل عميل) في بند حدث تسليم تُنشئ
/// سجلًا واحدًا داخل نفس معاملة تسجيل الحدث، ويتحرك عبر آلة الحالات في
/// DeliveryMaintenanceStatuses حتى يُغلق (إعادة تسليم/استبدال/ردّ نهائي/إلغاء).
/// مكتوب يدويًا بنفس أسلوب الـScaffold؛ الجدول يُنشأ بـ delivery-outcomes.sql.
/// الإلغاء منطقي فقط (IsVoided) — لا حذف فيزيائي أبدًا.
/// </summary>
public partial class DeliveryMaintenanceItem
{
    public int MaintenanceId { get; set; }

    /// <summary>بند حدث التسليم الذي وُلدت منه الحصة الراجعة (الأصل)</summary>
    public int DeliveryItemId { get; set; }

    /// <summary>حدث التسليم المصدر (مُكرَّر تيسيرًا للاستعلام والعرض)</summary>
    public int DeliveryId { get; set; }

    public int TransactionId { get; set; }

    public int TransactionDetailId { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>تصنيف السبب — مفردات DeliveryDispositions؛ لا يتغير إلا بإعادة تصنيف موثقة</summary>
    public string ReasonClass { get; set; } = null!;

    public string IssueDescription { get; set; } = null!;

    /// <summary>الحالة — مفردات DeliveryMaintenanceStatuses</summary>
    public string Status { get; set; } = null!;

    /// <summary>1 حصرًا لسجلات تعديل العميل (لها رسم إضافي مرتبط)</summary>
    public bool Billable { get; set; }

    /// <summary>رسم التعديل المرتبط (لسجلات التعديل فقط)</summary>
    public int? AdditionalChargeId { get; set; }

    /// <summary>التكلفة الداخلية الفعلية للإصلاح/التعديل — تسجيل معلوماتي فقط، لا قيد محاسبي</summary>
    public decimal? RepairCost { get; set; }

    public string? RepairCostNotes { get; set; }

    public int? HandledByEmployeeId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? ReadyAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public string? StatusBy { get; set; }

    public string? ResolutionNotes { get; set; }

    public bool IsVoided { get; set; }

    public string? VoidedBy { get; set; }

    public DateTime? VoidedAt { get; set; }

    public string? VoidReason { get; set; }

    public virtual InvoiceDeliveryItem DeliveryItem { get; set; } = null!;

    public virtual Transaction Transaction { get; set; } = null!;

    public virtual TransactionDetail TransactionDetail { get; set; } = null!;

    public virtual AdditionalCharge? AdditionalCharge { get; set; }

    public virtual Employee? HandledByEmployee { get; set; }
}
