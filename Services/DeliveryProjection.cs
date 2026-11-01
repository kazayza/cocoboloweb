using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// إعادة بناء إسقاط التسليم على صف الفاتورة — دالة مشتركة واحدة (خطة التسليم
/// الجزئي، القسم 11.2) يستدعيها كل من يغيّر معادلة الاكتمال: تسجيل حدث تسليم،
/// إلغاء حدث، وإغلاق سجل صيانة بردّ نهائي (التسوية قد تُكمل الفاتورة دون حدث
/// جديد). القواعد:
///  • المفتوح لكل بند = المفوتر − المقبول (حصص القبول في الأحداث غير الملغاة)
///    − المُسوّى (كميات سجلات الصيانة المُغلقة بردّ نهائي/رصيد).
///  • IsDelivered = اكتملت فقط (المفتوح = 0 مع وجود أحداث) — الجزئي ليس
///    «مسلَّمة» لأي بوابة قائمة.
///  • DeliveredAt = تاريخ حدث الإكمال؛ والاستثناء الوحيد: اكتمال بالتسوية
///    وحدها ⇒ تاريخ قرار التسوية المُكمل (آخر إغلاق بردّ نهائي).
///  • المندوب = لقطة آخر حدث غير ملغى.
/// تُستدعى داخل معاملة الكتابة نفسها، والفاتورة محمّلة ومتعقَّبة في نفس الـ DbContext.
/// </summary>
public static class DeliveryProjection
{
    private const decimal QtyEpsilon = 0.0005m;

    public static async Task RecomputeAsync(db24804Context db, Transaction transaction)
    {
        var txId = transaction.TransactionId;

        var totalInvoiced = await db.TransactionDetails.AsNoTracking()
            .Where(d => d.TransactionId == txId)
            .GroupBy(d => d.TransactionId)
            .Select(g => g.Sum(d => d.Quantity))
            .FirstOrDefaultAsync();

        // المقبول: حصص القبول فقط (لا الكمية المحاوَلة — الراجعة ما زالت مفتوحة)
        var acceptedRows = await (from i in db.InvoiceDeliveryItems.AsNoTracking()
                                  join d in db.InvoiceDeliveries.AsNoTracking()
                                      on i.DeliveryId equals d.DeliveryId
                                  where d.TransactionId == txId && !d.IsVoided
                                  group i by d.TransactionId into g
                                  select new { Total = g.Sum(x => x.AcceptedQty) })
            .ToListAsync();
        var totalAccepted = acceptedRows.Count > 0 ? acceptedRows[0].Total : 0m;

        // المُسوّى: سجلات مُغلقة بردّ نهائي/رصيد (غير ملغاة)
        var settledRows = await db.DeliveryMaintenanceItems.AsNoTracking()
            .Where(m => m.TransactionId == txId && !m.IsVoided
                     && m.Status == DTOs.DeliveryMaintenanceStatuses.ClosedByCredit)
            .GroupBy(m => m.TransactionId)
            .Select(g => g.Sum(m => m.Quantity))
            .ToListAsync();
        var totalSettled = settledRows.Count > 0 ? settledRows[0] : 0m;

        var activeEvents = await db.InvoiceDeliveries.AsNoTracking()
            .Where(d => d.TransactionId == txId && !d.IsVoided)
            .OrderBy(d => d.DeliveryDate).ThenBy(d => d.DeliveryId)
            .ToListAsync();

        var open = totalInvoiced - totalAccepted - totalSettled;
        var completed = activeEvents.Count > 0 && open <= QtyEpsilon;

        transaction.IsDelivered = completed;
        if (completed)
        {
            var completing = activeEvents.LastOrDefault(e => e.CompletedInvoice);
            if (completing != null)
            {
                transaction.DeliveredAt = completing.DeliveryDate;
            }
            else
            {
                // اكتمال بالتسوية دون حدث مُكمل: DeliveredAt = تاريخ قرار التسوية
                var settleDate = await db.DeliveryMaintenanceItems.AsNoTracking()
                    .Where(m => m.TransactionId == txId && !m.IsVoided
                             && m.Status == DTOs.DeliveryMaintenanceStatuses.ClosedByCredit
                             && m.ClosedAt != null)
                    .Select(m => (DateTime?)m.ClosedAt)
                    .MaxAsync();
                transaction.DeliveredAt = settleDate
                    ?? activeEvents[activeEvents.Count - 1].DeliveryDate;
            }
        }
        else
        {
            transaction.DeliveredAt = null;
        }

        var last = activeEvents.LastOrDefault();
        if (last != null)
        {
            transaction.DeliveryEmployeeId   = last.DeliveryEmployeeId;
            transaction.DeliveryEmployeeName = last.DeliveryEmployeeName;
        }
    }
}
