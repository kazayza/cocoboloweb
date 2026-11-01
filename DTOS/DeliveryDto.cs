using System;
using System.Collections.Generic;
using System.Linq;

namespace COCOBOLOERPNEW.DTOs;

#region 📋 ملخص التسليمات (Delivery Summary DTO)

public class DeliverySummaryDto
{
    public int TotalCount { get; set; }
    public int PendingCount { get; set; }          // جاري
    public int PartialCount { get; set; }          // جزئي (تسليم على دفعات — لم يكتمل بعد)
    public int DeliveredCount { get; set; }        // تم التسليم
    public int OverdueCount { get; set; }          // متأخر
    public int ReturnedCount { get; set; }         // مرتجع
    
    public decimal TotalGrandTotal { get; set; }
    public decimal TotalPaidAmount { get; set; }
    public decimal TotalRemaining => TotalGrandTotal - TotalPaidAmount;
}

#endregion

#region 📦 تفاصيل التسليم الكاملة (Delivery Detail DTO)

public class DeliveryDetailDto
{
    // معلومات الفاتورة
    public int TransactionId { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime TransactionDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string TransactionType { get; set; } = "";
    
    // معلومات العميل
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? PartyPhone { get; set; }
    public string? PartyAddress { get; set; }
    
    // معلومات موظف البيع (المسئول)
    public int? SalesEmployeeId { get; set; }
    public string? SalesEmployeeName { get; set; }
    
    // معلومات مندوب التسليم
    public int? DeliveryEmployeeId { get; set; }
    public string? DeliveryEmployeeName { get; set; }
    
    // معلومات التسليم
    public string DeliveryStatus { get; set; } = "";  // جاري | متأخر | تم التسليم | مرتجع
    public DateTime? DeliveredAt { get; set; }
    public string? DeliveredNotes { get; set; }
    
    // المعلومات المالية
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount => GrandTotal - PaidAmount;
    
    // الأيام المتبقية
    public int? DaysRemaining { get; set; }
    
    // المنتجات
    public List<DeliveryProductDto> Products { get; set; } = new();
}

public class DeliveryProductDto
{
    public int TransactionDetailId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal Quantity { get; set; }
    public string? UnitName { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }

    // ⭐ التسليم الجزئي: المقبول من هذا البند عبر كل الأحداث غير الملغاة
    //    (DeliveredQty صار يعني «المقبول نهائيًا» منذ نتائج التسليم — القسم 11.2)
    public decimal DeliveredQty { get; set; }

    // ⭐ نتائج التسليم (11.2): المفتوح = المفوتر − المقبول − المُسوّى، ويتفرع
    //    إلى «في الصيانة» و«بانتظار التسليم» — RemainingQty هو المفتوح نفسه
    public decimal InLoopQty { get; set; }
    public decimal SettledQty { get; set; }
    public decimal RemainingQty => Quantity - DeliveredQty - SettledQty;
    public decimal AwaitingQty => RemainingQty - InLoopQty;

    // ⭐ جدولة تسليم البند: الموعد الفعّال (تاريخه الخاص إن فُصل، وإلا تاريخ
    // استحقاق الفاتورة) — بصيغة العرض لهذا المستخدم: المواعيد المتفق عليها تخضع
    // لنفس إخفاء تاريخ الاستحقاق (−الهامش لغير المستثنين)
    public DateTime? ScheduledDate { get; set; }
    public bool ScheduledDateIsCustom { get; set; }

    /// <summary>الموعد المعروض فات وما زال البند غير مكتمل التسليم</summary>
    public bool IsScheduleOverdue { get; set; }

    // ⭐ مخطط × فعلي: تاريخ آخر حدث تسليم سلّم من هذا البند (حقيقة منجزة — بلا إخفاء)
    public DateTime? LastDeliveryDate { get; set; }

    /// <summary>اكتمل تسليم البند بعد موعده المعروض (يقارن بالموعد الذي يراه المستخدم)</summary>
    public bool DeliveredLate { get; set; }
}

#endregion

#region 🔄 تحديث حالة التسليم (Update DTO)

public class DeliveryUpdateDto
{
    public int TransactionId { get; set; }
    
    public string Status { get; set; } = "";  // تم التسليم | متأخر | مرتجع
    public int? DeliveryEmployeeId { get; set; }
    
    public string? DeliveryEmployeeName { get; set; }
    
    public DateTime? DeliveredAt { get; set; }
    
    public string? Notes { get; set; }
    
    public string UserName { get; set; } = "";  // اللي عمل التحديث
}

#endregion

#region 🔍 فلتر التسليمات (Filter DTO)

public class DeliveryFilterDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string DateFilterType { get; set; } = DeliveryDateFilterTypes.DueDate;
    public string? PartyName { get; set; }
    public string? DeliveryStatus { get; set; }
    public int? SalesEmployeeId { get; set; }
    public int? PageNumber { get; set; } = 1;
    public int? PageSize { get; set; } = 20;
}

public static class DeliveryDateFilterTypes
{
    public const string InvoiceDate = "invoice";
    public const string DueDate = "due";
    public const string DeliveredDate = "delivered";
}

#endregion

/// <summary>حالة إذن التسليم لفاتورة عليها مبالغ متبقية (طلب/منح)</summary>
public class DeliveryPermissionStateDto
{
    public int TransactionId { get; set; }
    public bool HasPendingRequest { get; set; }
    public string? RequestedBy { get; set; }
    public DateTime? RequestedAt { get; set; }
    public string? GrantedBy { get; set; }
    public DateTime? GrantedAt { get; set; }
}

/// <summary>صف في كشف التسليمات غير المسلَّمة (للطباعة — بدون مبالغ)</summary>
public class DailyDeliverySheetRowDto
{
    public int TransactionId { get; set; }
    public string PartyName { get; set; } = null!;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public DateTime? DueDate { get; set; }
    public int? DaysRemaining { get; set; }
    public string? DeliveryStatus { get; set; }
    public string? DeliveryEmployeeName { get; set; }

    // ⭐ التسليم الجزئي: الفاتورة بدأ تسليمها ولم تكتمل — تُعلَّم في الكشف بالمتبقي
    public bool IsPartial { get; set; }
    public decimal TotalInvoicedQty { get; set; }
    public decimal TotalRemainingQty { get; set; }

    // ⭐ جدولة البنود: الكشف صار مدفوعًا بمواعيد البنود — كل سطر يمثل مجموعة بنود
    // مستحقة في موعد واحد لفاتورة واحدة (DueDate هنا = موعد المجموعة المعروض)
    public string? DueLinesSummary { get; set; }
    public int DueLineCount { get; set; }
}

#region 🚚 التسليم الجزئي (أحداث التسليم)

/// <summary>الحالات الفعّالة لعرض التسليم — تُشتق في طبقة الخدمة من أحداث التسليم لا من نص الـView وحده</summary>
public static class DeliveryEffectiveStatuses
{
    public const string Pending   = "جارى";
    public const string Partial   = "جزئي";
    public const string Delivered = "تم التسليم";
    public const string Returned  = "مرتجع";
    public const string Overdue   = "متأخر";
}

/// <summary>بند واحد في طلب تسجيل تسليمة</summary>
public class RecordDeliveryItemDto
{
    public int TransactionDetailId { get; set; }

    /// <summary>الكمية المحاوَلة في هذه الدفعة (تُقسَّم على الحصص أدناه)</summary>
    public decimal Quantity { get; set; }

    // ─── نتائج التسليم (القسم 11.1): تقسيم الكمية المحاوَلة على الحصص ───
    // AcceptedQty = null وكل الراجعة صفر ⇒ المسار القديم: الكل مقبول.
    // غير ذلك يجب أن يساوي مجموع الحصص الأربع الكميةَ المحاوَلة، والوصف
    // إلزامي عند أي حصة راجعة.
    /// <summary>الحصة المقبولة عند العميل (null = تُشتق: الكل مقبول إن لم تُدخل حصص)</summary>
    public decimal? AcceptedQty { get; set; }
    public decimal ReturnedManufacturingQty { get; set; }
    public decimal ReturnedHandlingQty { get; set; }
    public decimal ReturnedModificationQty { get; set; }

    /// <summary>وصف العيب/التلف/التعديل المطلوب — إلزامي عند أي حصة راجعة</summary>
    public string? DispositionNotes { get; set; }

    /// <summary>سعر تعديل متفق عليه لحظة التسليم (اختياري) — يُستخدم مبلغًا أوليًا لرسم التعديل المعلّق</summary>
    public decimal? AgreedModificationPrice { get; set; }

    /// <summary>ربط إعادة التسليم: سجل الصيانة «الجاهز» الذي يُغلقه قبول هذا البند</summary>
    public int? MaintenanceId { get; set; }

    /// <summary>إجمالي الحصص الراجعة في هذا البند</summary>
    public decimal TotalReturnedQty =>
        ReturnedManufacturingQty + ReturnedHandlingQty + ReturnedModificationQty;
}

/// <summary>طلب تسجيل تسليمة (دفعة) على فاتورة بيع</summary>
public class RecordDeliveryDto
{
    public int TransactionId { get; set; }
    public DateTime DeliveryDate { get; set; }
    public int? DeliveryEmployeeId { get; set; }
    public string? DeliveryEmployeeName { get; set; }
    public string? Notes { get; set; }

    /// <summary>مفتاح منع التكرار — يولّده الحوار مرة واحدة عند فتحه فلا يتكرر الحدث بإعادة الإرسال</summary>
    public Guid EventGuid { get; set; }

    public string UserName { get; set; } = "";
    public List<RecordDeliveryItemDto> Items { get; set; } = new();
}

/// <summary>بند داخل حدث تسليم مسجّل (للعرض في سجل التسليمات)</summary>
public class DeliveryEventItemDto
{
    public int TransactionDetailId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal Quantity { get; set; }

    // ⭐ نتائج التسليم (11.1): حصص البند كما سُجّلت
    public decimal AcceptedQty { get; set; }
    public decimal ReturnedManufacturingQty { get; set; }
    public decimal ReturnedHandlingQty { get; set; }
    public decimal ReturnedModificationQty { get; set; }
    public decimal TotalReturnedQty =>
        ReturnedManufacturingQty + ReturnedHandlingQty + ReturnedModificationQty;
    public string? DispositionNotes { get; set; }

    /// <summary>سجل الصيانة الذي أغلقه هذا البند (بند إعادة تسليم) إن وُجد</summary>
    public int? MaintenanceId { get; set; }
}

/// <summary>حدث تسليم واحد في سجل تسليمات الفاتورة</summary>
public class DeliveryEventDto
{
    public int DeliveryId { get; set; }
    public DateTime DeliveryDate { get; set; }
    public int? DeliveryEmployeeId { get; set; }
    public string? DeliveryEmployeeName { get; set; }
    public string? Notes { get; set; }
    public string RecordedBy { get; set; } = "";
    public DateTime RecordedAt { get; set; }
    public bool IsVoided { get; set; }
    public string? VoidedBy { get; set; }
    public DateTime? VoidedAt { get; set; }
    public string? VoidReason { get; set; }
    public bool CompletedInvoice { get; set; }
    public bool IsLegacy { get; set; }
    public List<DeliveryEventItemDto> Items { get; set; } = new();
    public decimal TotalQuantity => Items.Sum(i => i.Quantity);

    // ⭐ نتائج التسليم (11.1): نتيجة الحدث مشتقة من حصص بنوده — «بملاحظات»
    //    إن وُجدت أي حصة راجعة في أي بند (الأحداث الصناعية للترحيل كلها مقبولة)
    public decimal TotalAcceptedQty => Items.Sum(i => i.AcceptedQty);
    public decimal TotalReturnedQty => Items.Sum(i => i.TotalReturnedQty);
    public decimal TotalReturnedManufacturingQty => Items.Sum(i => i.ReturnedManufacturingQty);
    public decimal TotalReturnedHandlingQty => Items.Sum(i => i.ReturnedHandlingQty);
    public decimal TotalReturnedModificationQty => Items.Sum(i => i.ReturnedModificationQty);
    public bool HasRemarks => Items.Any(i => i.TotalReturnedQty > 0.0005m);
    public string OutcomeText => HasRemarks ? "بملاحظات" : "بدون ملاحظات";

    /// <summary>سجلات الصيانة المولودة من هذا الحدث (حصصه الراجعة) بحالاتها الحالية</summary>
    public List<DeliveryMaintenanceItemDto> MaintenanceRecords { get; set; } = new();

    /// <summary>رسوم التعديل المولودة من هذا الحدث إن وُجدت (شيب الحالة في سجل التسليمات)</summary>
    public List<DeliveryEventChargeDto> ModificationCharges { get; set; } = new();
}

/// <summary>رسم تعديل مرتبط بحدث تسليم — شيب مختصر في سجل التسليمات</summary>
public class DeliveryEventChargeDto
{
    public int ChargeId { get; set; }
    public string Status { get; set; } = "";
    public string StatusName =>
        ChargeStatuses.All.TryGetValue(Status, out var n) ? n : Status;
    public decimal? ChargeAmount { get; set; }
}

/// <summary>تقدم بند فاتورة واحد: المفوتر / المقبول / في الصيانة / المُسوّى / المفتوح</summary>
public class DeliveryProgressLineDto
{
    public int TransactionDetailId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal InvoicedQty { get; set; }

    /// <summary>المقبول نهائيًا عند العميل (مجموع حصص القبول في الأحداث غير الملغاة)</summary>
    public decimal DeliveredQty { get; set; }
    public decimal AcceptedQty => DeliveredQty;

    // ⭐ نتائج التسليم (11.2): المفتوح = المفوتر − المقبول − المُسوّى بردّ نهائي،
    //    وهو ما يحل محل «المتبقي» في كل اشتقاقات الحالة والمتأخرات والكشف
    /// <summary>كمية البند الموجودة الآن داخل حلقة الصيانة (سجلات غير منتهية)</summary>
    public decimal InLoopQty { get; set; }

    /// <summary>كمية أُغلقت بتسوية نهائية (ردّ نهائي/رصيد) — لن تُسلَّم لكنها محسومة</summary>
    public decimal SettledQty { get; set; }

    public decimal OpenQty => InvoicedQty - DeliveredQty - SettledQty;
    public decimal RemainingQty => OpenQty;

    /// <summary>المتاح للتحميل الآن = المفتوح − في الصيانة</summary>
    public decimal AwaitingQty => OpenQty - InLoopQty;

    // ⭐ جدولة البند + مخطط × فعلي (نفس تعريفات DeliveryProductDto)
    public DateTime? ScheduledDate { get; set; }
    public bool ScheduledDateIsCustom { get; set; }
    public bool IsScheduleOverdue { get; set; }
    public DateTime? LastDeliveredDate { get; set; }
    public bool DeliveredLate { get; set; }
}

/// <summary>مجموعة بنود فاتورة لها موعد تسليم معروض واحد (لملخص جدولة القائمة)</summary>
public class DeliveryScheduleGroupDto
{
    /// <summary>الموعد بصيغة العرض للمستخدم الحالي (بعد الإخفاء إن انطبق)</summary>
    public DateTime? DisplayDate { get; set; }
    public int LineCount { get; set; }
    public int PendingLineCount { get; set; }

    /// <summary>إجمالي الكمية غير المسلَّمة بعد في بنود هذا الموعد (لعرض «موعد» القائمة)</summary>
    public decimal PendingQty { get; set; }

    /// <summary>في المجموعة بند لم يكتمل وموعدها المعروض فات</summary>
    public bool HasOverdue { get; set; }
}

/// <summary>ملخص جدولة فاتورة لقائمة التسليمات: مواعيدها المميزة وبنود كل موعد</summary>
public class InvoiceScheduleSummaryDto
{
    public int TransactionId { get; set; }

    /// <summary>للفاتورة بند واحد على الأقل مفصول يدويًا بموعد خاص</summary>
    public bool HasCustomDates { get; set; }

    public List<DeliveryScheduleGroupDto> Groups { get; set; } = new();
}

/// <summary>تقدم تسليم فاتورة كاملًا: بنود + أحداث + حالة فعّالة</summary>
public class InvoiceDeliveryProgressDto
{
    public int TransactionId { get; set; }

    /// <summary>الحالة الفعّالة الأساسية (جارى/جزئي/تم التسليم/مرتجع) — «متأخر» اشتقاق عرض يُطبق في الشاشات على التاريخ المعروض</summary>
    public string EffectiveStatus { get; set; } = DeliveryEffectiveStatuses.Pending;

    public bool HasEvents { get; set; }
    public decimal TotalInvoicedQty { get; set; }

    /// <summary>إجمالي المقبول نهائيًا (TotalDeliveredQty = المقبول منذ القسم 11)</summary>
    public decimal TotalDeliveredQty { get; set; }

    // ⭐ نتائج التسليم (11.2): اكتمال الفاتورة ⟺ المفتوح = 0 (مقبول + مُسوّى)
    public decimal TotalInLoopQty { get; set; }
    public decimal TotalSettledQty { get; set; }
    public decimal TotalOpenQty => TotalInvoicedQty - TotalDeliveredQty - TotalSettledQty;
    public decimal TotalRemainingQty => TotalOpenQty;

    /// <summary>اكتملت الفاتورة وبها كمية مُسوّاة بردّ نهائي — وسم عرضي «اكتملت بتسوية»</summary>
    public bool CompletedBySettlement =>
        EffectiveStatus == DeliveryEffectiveStatuses.Delivered && TotalSettledQty > 0.0005m;

    public DateTime? LastEventDate { get; set; }

    public List<DeliveryProgressLineDto> Lines { get; set; } = new();
    public List<DeliveryEventDto> Events { get; set; } = new();

    /// <summary>سجلات الصيانة الجاهزة لإعادة التسليم لهذه الفاتورة (لحوار التسجيل — القسم 11.3)</summary>
    public List<DeliveryMaintenanceItemDto> ReadyMaintenance { get; set; } = new();
}

/// <summary>بند داخل إذن تسليم دفعة واحدة (للطباعة من سجل التسليمات)</summary>
public class DeliveryEventPermitItemDto
{
    public string ProductName { get; set; } = "";
    public decimal Quantity { get; set; }
    public string? UnitName { get; set; }

    /// <summary>موعد تسليم البند بصيغة العرض للمستخدم الحالي (نفس إخفاء الاستحقاق)</summary>
    public DateTime? ScheduledDate { get; set; }
    public bool ScheduledDateIsCustom { get; set; }
}

/// <summary>
/// إذن تسليم حدث واحد: بنود هذه الدفعة وكمياتها فقط + ملخص تراكمي
/// (إجمالي الفاتورة / المسلَّم حتى هذه الدفعة / المتبقي بعدها).
/// تاريخ الحدث حقيقة منجزة بلا إخفاء، ومواعيد البنود بصيغة العرض.
/// </summary>
public class DeliveryEventPermitDto
{
    public int DeliveryId { get; set; }
    public int TransactionId { get; set; }
    public DateTime TransactionDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string PartyName { get; set; } = "";
    public string? PartyPhone { get; set; }
    public string? PartyAddress { get; set; }
    public string? SalesEmployeeName { get; set; }

    public DateTime DeliveryDate { get; set; }
    public string? DeliveryEmployeeName { get; set; }
    public string? Notes { get; set; }
    public string RecordedBy { get; set; } = "";
    public bool CompletedInvoice { get; set; }
    public bool IsLegacy { get; set; }

    public List<DeliveryEventPermitItemDto> Items { get; set; } = new();
    public decimal EventTotalQty => Items.Sum(i => i.Quantity);

    public decimal TotalInvoicedQty { get; set; }
    public decimal DeliveredThroughEventQty { get; set; }
    public decimal RemainingAfterEventQty => TotalInvoicedQty - DeliveredThroughEventQty;
}

/// <summary>
/// تغطية دفعة تسليم ماليًا (قاعدة المالك): لا تخرج دفعة إلا إذا كان المدفوع من
/// الفاتورة يغطي قيمة ما قُبل عند العميل سابقًا + قيمة المقبول في هذه الدفعة،
/// وإلا فالمسار هو طلب إذن التسليم الموجود. قيمة البند = إجمالي سطر الفاتورة
/// الصافي ÷ كميته المفوترة × الكمية المقبولة.
/// </summary>
public class DeliveryBatchCoverageDto
{
    public int TransactionId { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal RemainingAmount => GrandTotal - PaidAmount;

    /// <summary>قيمة ما قُبل عند العميل في أحداث سابقة غير ملغاة</summary>
    public decimal PreviouslyDeliveredValue { get; set; }

    /// <summary>قيمة المقبول في الدفعة المقترحة الحالية</summary>
    public decimal ThisBatchValue { get; set; }

    /// <summary>المطلوب تغطيته = المسلَّم سابقًا + هذه الدفعة</summary>
    public decimal RequiredValue => PreviouslyDeliveredValue + ThisBatchValue;

    /// <summary>المدفوع يغطي المطلوب (أو لا يوجد متبقٍّ على الفاتورة أصلًا)</summary>
    public bool Covered { get; set; }

    /// <summary>يوجد إذن تسليم ممنوح موثّق في سجل التدقيق</summary>
    public bool HasGrant { get; set; }

    /// <summary>يوجد طلب إذن معلّق بانتظار الموافقة</summary>
    public bool HasPendingRequest { get; set; }

    /// <summary>المستخدم الحالي نفسه من أصحاب الموافقة (مدير/مدير حسابات)</summary>
    public bool CurrentUserCanOverride { get; set; }

    /// <summary>محصلة البوابة: التغطية أو إذن أو صلاحية موافقة — نفس حكم خادم التسجيل</summary>
    public bool GatePasses { get; set; }
}

/// <summary>
/// موعد تسليم مفتوح واحد (فاتورة × موعد) — لقسم «التسليمات القادمة» في الرئيسية
/// ولتنبيهات ما قبل الموعد بأسبوع. التواريخ بصيغة عرض الفئة/المشاهد المستدعي.
/// </summary>
public class UpcomingDeliveryAppointmentDto
{
    public int TransactionId { get; set; }

    /// <summary>مرجع الفاتورة إن وجد، وإلا #رقمها</summary>
    public string Reference { get; set; } = "";
    public string PartyName { get; set; } = "";

    /// <summary>الموعد الحقيقي المتفق عليه (قبل أي إخفاء)</summary>
    public DateTime RealDate { get; set; }

    /// <summary>الموعد كما تراه فئة المستدعي (حقيقي أو منقوص الهامش بأرضية تاريخ الفاتورة)</summary>
    public DateTime DisplayDate { get; set; }

    /// <summary>عدد البنود المستحقة (المفتوحة) في هذا الموعد</summary>
    public int DueLineCount { get; set; }

    /// <summary>الكمية المفتوحة المتبقية في بنود هذا الموعد</summary>
    public decimal RemainingQty { get; set; }
}

/// <summary>تقدم مختصر لفاتورة (لقائمة التسليمات — يُجلب دفعة واحدة بلا N+1)</summary>
public class DeliveryProgressSummaryDto
{
    public int TransactionId { get; set; }

    /// <summary>الحالة الأساسية من الأحداث (جارى/جزئي/تم التسليم) — المرتجع والمتأخر يقررهما صف الـView عند الدمج</summary>
    public string EffectiveStatus { get; set; } = DeliveryEffectiveStatuses.Pending;

    public bool HasEvents { get; set; }
    public int EventCount { get; set; }
    public decimal TotalInvoicedQty { get; set; }

    /// <summary>المقبول نهائيًا (TotalDeliveredQty = المقبول منذ نتائج التسليم)</summary>
    public decimal TotalDeliveredQty { get; set; }

    // ⭐ نتائج التسليم (11.2): المتبقي صار «المفتوح» = مفوتر − مقبول − مُسوّى
    public decimal TotalInLoopQty { get; set; }
    public decimal TotalSettledQty { get; set; }
    public decimal TotalRemainingQty => TotalInvoicedQty - TotalDeliveredQty - TotalSettledQty;
    public DateTime? LastEventDate { get; set; }

    public double PercentDelivered => TotalInvoicedQty > 0
        ? (double)(TotalDeliveredQty / TotalInvoicedQty * 100m)
        : 0;
}

#endregion

#region 🗓️ طلبات تعديل مواعيد التسليم (تعديل / تقسيم على دفعات)

/// <summary>حالات طلب تعديل المواعيد — قيم التخزين الإنجليزية وأسماؤها العربية</summary>
public static class DeliveryScheduleRequestStatuses
{
    public const string Pending   = "Pending";
    public const string Approved  = "Approved";
    public const string Rejected  = "Rejected";
    public const string Withdrawn = "Withdrawn";

    public static readonly Dictionary<string, string> All = new()
    {
        { Pending,   "قيد الانتظار" },
        { Approved,  "معتمد" },
        { Rejected,  "مرفوض" },
        { Withdrawn, "مسحوب" },
    };

    public static string NameOf(string? status)
        => status != null && All.TryGetValue(status, out var n) ? n : (status ?? "");
}

/// <summary>بند واحد في طلب إنشاء تعديل مواعيد — لا يُرسل إلا للبنود التي غيّرها الطالب فعلًا</summary>
public class ScheduleChangeLineInputDto
{
    public int TransactionDetailId { get; set; }

    /// <summary>الموعد الخاص المقترح — يُستخدم فقط عندما يكون ProposedIsCustom = true</summary>
    public DateTime? ProposedDate { get; set; }

    /// <summary>true = موعد خاص مفصول؛ false = إرجاع البند لاتباع تاريخ الفاتورة الكلي</summary>
    public bool ProposedIsCustom { get; set; }
}

/// <summary>طلب إنشاء تعديل مواعيد تسليم (من مسئول التسليمات أو مدير الإنتاج)</summary>
public class CreateScheduleChangeRequestDto
{
    public int TransactionId { get; set; }

    /// <summary>التاريخ الكلي الجديد المقترح للفاتورة — null = لا تغيير على التاريخ الكلي</summary>
    public DateTime? ProposedDueDate { get; set; }

    /// <summary>سبب التعديل (إلزامي)</summary>
    public string Reason { get; set; } = "";

    /// <summary>اسم مستخدم مقدّم الطلب (تفرضه الشاشة من جلسة المستخدم)</summary>
    public string RequesterUserName { get; set; } = "";

    public List<ScheduleChangeLineInputDto> Lines { get; set; } = new();
}

/// <summary>بند مقترح داخل طلب تعديل مواعيد (عرض)</summary>
public class ScheduleChangeRequestLineDto
{
    public int TransactionDetailId { get; set; }
    public string ProductName { get; set; } = "";
    public DateTime? ProposedDate { get; set; }
    public bool ProposedIsCustom { get; set; }
}

/// <summary>طلب تعديل مواعيد كامل — لسجل الطلبات في صفحة تفاصيل التسليم</summary>
public class ScheduleChangeRequestDto
{
    public int Id { get; set; }
    public int TransactionId { get; set; }
    public string RequesterUserName { get; set; } = "";
    public string? RequesterName { get; set; }
    public string Reason { get; set; } = "";
    public DateTime? ProposedDueDate { get; set; }
    public string Status { get; set; } = DeliveryScheduleRequestStatuses.Pending;
    public string StatusName => DeliveryScheduleRequestStatuses.NameOf(Status);
    public DateTime CreatedAt { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }

    public List<ScheduleChangeRequestLineDto> Lines { get; set; } = new();
}

/// <summary>قيمة نهائية لبند واحد عند اعتماد الطلب — ما يُكتب فعليًا على البند</summary>
public class ScheduleChangeFinalLineDto
{
    public int TransactionDetailId { get; set; }

    /// <summary>التاريخ النهائي — يُستخدم فقط عندما يكون FinalIsCustom = true</summary>
    public DateTime? FinalDate { get; set; }

    /// <summary>true = موعد خاص مفصول؛ false = اتباع تاريخ الفاتورة (يُخزَّن NULL)</summary>
    public bool FinalIsCustom { get; set; }
}

/// <summary>حالة طلب تعديل المواعيد المعلّق لفاتورة (لقائمة التسليمات — دفعة واحدة)</summary>
public class ScheduleChangePendingStateDto
{
    public int TransactionId { get; set; }
    public bool HasPendingRequest { get; set; }
    public int? RequestId { get; set; }
    public string? RequestedBy { get; set; }
    public DateTime? RequestedAt { get; set; }
}

/// <summary>بند بموعدِه الحقيقي الحالي — لشاشة اعتماد الطلب (المُعتمِد يرى التواريخ الحقيقية دائمًا)</summary>
public class ScheduleChangeCurrentLineDto
{
    public int TransactionDetailId { get; set; }
    public string ProductName { get; set; } = "";

    /// <summary>الموعد الفعّال الحقيقي الحالي (خاص البند إن فُصل، وإلا تاريخ الفاتورة الحقيقي)</summary>
    public DateTime? CurrentDate { get; set; }

    /// <summary>البند مفصول حاليًا بموعد خاص</summary>
    public bool CurrentIsCustom { get; set; }

    /// <summary>الكمية المفتوحة (مفوتر − مقبول − مُسوّى) — كميات فقط، لا مبالغ</summary>
    public decimal RemainingQty { get; set; }
}

/// <summary>بيانات شاشة اعتماد طلب تعديل مواعيد: الطلب + الجدولة الحقيقية الحالية للمقارنة</summary>
public class ScheduleChangeApprovalDto
{
    public ScheduleChangeRequestDto Request { get; set; } = new();
    public string PartyName { get; set; } = "";
    public string? ReferenceNumber { get; set; }

    /// <summary>تاريخ الفاتورة الكلي الحقيقي الحالي</summary>
    public DateTime? CurrentDueDate { get; set; }

    public List<ScheduleChangeCurrentLineDto> CurrentLines { get; set; } = new();
}

#endregion
