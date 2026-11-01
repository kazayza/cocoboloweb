using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

/// <summary>
/// رأس حدث تسليم (تسليمة واحدة على فاتورة بيع) — التسليم الجزئي.
/// مكتوب يدويًا بنفس أسلوب الـScaffold؛ الجدول يُنشأ بـ partial-delivery-tables.sql.
/// الإلغاء منطقي فقط (IsVoided) — لا حذف فيزيائي أبدًا.
/// </summary>
public partial class InvoiceDelivery
{
    public int DeliveryId { get; set; }

    public int TransactionId { get; set; }

    public DateTime DeliveryDate { get; set; }

    public int? DeliveryEmployeeId { get; set; }

    public string? DeliveryEmployeeName { get; set; }

    public string? Notes { get; set; }

    public string RecordedBy { get; set; } = null!;

    public DateTime RecordedAt { get; set; }

    public bool IsVoided { get; set; }

    public string? VoidedBy { get; set; }

    public DateTime? VoidedAt { get; set; }

    public string? VoidReason { get; set; }

    public bool CompletedInvoice { get; set; }

    public bool IsLegacy { get; set; }

    public Guid EventGuid { get; set; }

    public virtual Transaction Transaction { get; set; } = null!;

    public virtual Employee? DeliveryEmployee { get; set; }

    public virtual ICollection<InvoiceDeliveryItem> InvoiceDeliveryItems { get; set; } = new List<InvoiceDeliveryItem>();
}
