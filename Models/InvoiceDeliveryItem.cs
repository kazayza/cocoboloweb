using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

/// <summary>
/// بند حدث تسليم: كمية مُسلَّمة من بند فاتورة أصلي (TransactionDetail) في دفعة واحدة.
/// الربط بالبند لا بالمنتج — الفاتورة قد تحمل نفس المنتج في بندين منفصلين.
/// مكتوب يدويًا بنفس أسلوب الـScaffold؛ الجدول يُنشأ بـ partial-delivery-tables.sql.
/// </summary>
public partial class InvoiceDeliveryItem
{
    public int DeliveryItemId { get; set; }

    public int DeliveryId { get; set; }

    public int TransactionDetailId { get; set; }

    public decimal Quantity { get; set; }

    // ─── نتائج التسليم (القسم 11.1): كمية البند المحاوَلة تُقسَّم لأربع حصص
    //     مجموعها = Quantity. قبل الميزة كانت كل الكمية مقبولة؛ الترحيل في
    //     delivery-outcomes.sql ملأ AcceptedQty للصفوف القائمة.
    /// <summary>قُبلت وبقيت عند العميل — الكمية الوحيدة التي تُحسب «مُسلَّمة نهائيًا»</summary>
    public decimal AcceptedQty { get; set; }

    /// <summary>رُدّت لعيب تصنيع اكتُشف عند التسليم</summary>
    public decimal ReturnedManufacturingQty { get; set; }

    /// <summary>رُدّت لتلف مناولة أثناء النقل/التسليم</summary>
    public decimal ReturnedHandlingQty { get; set; }

    /// <summary>رُدّت لأن العميل طلب تعديلًا عليها</summary>
    public decimal ReturnedModificationQty { get; set; }

    /// <summary>وصف العيب/التلف/التعديل المطلوب — إلزامي عند أي حصة راجعة</summary>
    public string? DispositionNotes { get; set; }

    /// <summary>عند إعادة التسليم: سجل الصيانة الذي يُغلقه هذا البند بقبوله</summary>
    public int? MaintenanceId { get; set; }

    public virtual InvoiceDelivery Delivery { get; set; } = null!;

    public virtual DeliveryMaintenanceItem? Maintenance { get; set; }

    public virtual TransactionDetail TransactionDetail { get; set; } = null!;
}
