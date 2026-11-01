namespace COCOBOLOERPNEW.Models;

/// <summary>
/// بند واحد داخل طلب تعديل مواعيد: موعد مقترح لبند فاتورة بعينه.
/// لا يوجد صف إلا للبنود التي غيّرها مقدّم الطلب فعلًا.
/// ProposedIsCustom = false تعني «إرجاع البند لاتباع تاريخ الفاتورة»
/// (عند التطبيق: DeliveryDate = NULL وDeliveryDateIsCustom = false على البند)،
/// وProposedIsCustom = true مع ProposedDate تعني موعدًا خاصًا مفصولًا.
/// مكتوب يدويًا بنفس أسلوب الـScaffold؛ الجدول يُنشأ بـ delivery-schedule-requests.sql.
/// </summary>
public partial class DeliveryScheduleChangeRequestLine
{
    public int Id { get; set; }

    public int RequestId { get; set; }

    public int TransactionDetailId { get; set; }

    public DateTime? ProposedDate { get; set; }

    public bool ProposedIsCustom { get; set; }

    public virtual DeliveryScheduleChangeRequest Request { get; set; } = null!;

    public virtual TransactionDetail TransactionDetail { get; set; } = null!;
}
