using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

/// <summary>
/// رأس طلب تعديل مواعيد تسليم فاتورة بيع (تعديل التاريخ الكلي و/أو مواعيد
/// بنود — تقسيم الفاتورة على دفعات بمواعيد مختلفة).
/// مكتوب يدويًا بنفس أسلوب الـScaffold؛ الجدول يُنشأ بـ delivery-schedule-requests.sql.
/// دورة الحياة: Pending ← قرار (Approved/ Rejected) أو سحب صاحب الطلب (Withdrawn) —
/// الاعتماد يطبّق القيم النهائية على الفاتورة وبنودها في نفس معاملة البتّ.
/// </summary>
public partial class DeliveryScheduleChangeRequest
{
    public int Id { get; set; }

    public int TransactionId { get; set; }

    /// <summary>اسم مستخدم مقدّم الطلب (مسئول التسليمات أو مدير الإنتاج)</summary>
    public string RequesterUserName { get; set; } = null!;

    /// <summary>لقطة الاسم المعروض لمقدّم الطلب وقت التقديم</summary>
    public string? RequesterName { get; set; }

    /// <summary>سبب طلب التعديل (إلزامي)</summary>
    public string Reason { get; set; } = null!;

    /// <summary>التاريخ الكلي المقترح للفاتورة — NULL = لا تغيير مقترحًا على التاريخ الكلي</summary>
    public DateTime? ProposedDueDate { get; set; }

    /// <summary>Pending | Approved | Rejected | Withdrawn (DeliveryScheduleRequestStatuses)</summary>
    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    /// <summary>اسم مستخدم من بتّ في الطلب (المدير أو مدير الحسابات)</summary>
    public string? DecidedBy { get; set; }

    public DateTime? DecidedAt { get; set; }

    /// <summary>ملاحظة القرار — إلزامية عند الرفض</summary>
    public string? DecisionNote { get; set; }

    public virtual Transaction Transaction { get; set; } = null!;

    public virtual ICollection<DeliveryScheduleChangeRequestLine> Lines { get; set; } = new List<DeliveryScheduleChangeRequestLine>();
}
