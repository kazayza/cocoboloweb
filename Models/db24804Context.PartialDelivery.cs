using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Models;

/// <summary>
/// تسجيل كيانات التسليم الجزئي (أحداث تسليم الفواتير) — partial للـ DbContext.
/// ⚠️ لا يضيف أي تنفيذ لـ OnModelCreatingPartial (موجود مرة واحدة في db24804Context.B2B.cs)؛
/// تكوين الكيانين موجود في OnModelCreating الرئيسي، والجدولان يُنشآن بـ partial-delivery-tables.sql.
/// </summary>
public partial class db24804Context
{
    public virtual DbSet<InvoiceDelivery> InvoiceDeliveries { get; set; }

    public virtual DbSet<InvoiceDeliveryItem> InvoiceDeliveryItems { get; set; }

    /// <summary>حلقة صيانة التسليمات (القسم 11.3) — الجدول يُنشأ بـ delivery-outcomes.sql</summary>
    public virtual DbSet<DeliveryMaintenanceItem> DeliveryMaintenanceItems { get; set; }

    /// <summary>طلبات تعديل مواعيد التسليم — الجدولان يُنشآن بـ delivery-schedule-requests.sql</summary>
    public virtual DbSet<DeliveryScheduleChangeRequest> DeliveryScheduleChangeRequests { get; set; }

    public virtual DbSet<DeliveryScheduleChangeRequestLine> DeliveryScheduleChangeRequestLines { get; set; }
}
