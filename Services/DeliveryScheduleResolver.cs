namespace COCOBOLOERPNEW.Services;

/// <summary>
/// ⭐ جدولة تسليم البنود — المحلّل الوحيد لمواعيد تسليم بنود الفاتورة.
/// كل قارئ (خدمة التسليمات، شاشات الفواتير، الكشوف) يمر من هنا حتى لا تنحرف
/// التعريفات بين الأسطح:
///
///   • التاريخ الفعّال للبند = تاريخه الخاص إن كان مفصولًا يدويًا
///     (DeliveryDateIsCustom)، وإلا تاريخ استحقاق الفاتورة (DueDate).
///     البنود الوارثة لا تخزّن شيئًا، فتتحرك تلقائيًا مع تاريخ الفاتورة.
///   • مواعيد البنود «مواعيد متفق عليها» كتاريخ الاستحقاق تمامًا: تخضع لنفس
///     إخفاء العرض (هامش الأيام لغير المستثنين — DeliveryPermissions.CanSeeRealDeliveryDate)
///     ونفس قاعدة «المعروض لا يسبق تاريخ الفاتورة» في MaskValues بخدمة التسليمات.
///     أما تواريخ أحداث التسليم الفعلية فحقائق منجزة لا تُخفى أبدًا.
/// </summary>
public static class DeliveryScheduleResolver
{
    // نفس مسامحة خدمة التسليمات عند اختبار «المتبقي = صفر» (الكميات decimal(18,3))
    public const decimal QtyEpsilon = 0.0005m;

    /// <summary>التاريخ الفعّال الحقيقي لبند فاتورة (قبل أي إخفاء عرض).</summary>
    public static DateTime? EffectiveDate(DateTime? lineDate, bool isCustom, DateTime? invoiceDueDate)
        => isCustom && lineDate.HasValue ? lineDate.Value.Date : invoiceDueDate?.Date;

    /// <summary>
    /// تاريخ العرض لمستخدم بعينه: المستثنون يرون الحقيقي، ومن عداهم يرونه منقوصًا
    /// هامش الأيام ولا يسبق تاريخ الفاتورة — نفس دلالات MaskValues حرفيًا.
    /// </summary>
    public static DateTime? DisplayDate(
        DateTime? effectiveDate, DateTime transactionDate, bool seesRealDate, int bufferDays)
    {
        if (effectiveDate == null) return null;
        if (seesRealDate) return effectiveDate.Value.Date;

        var shown = effectiveDate.Value.Date.AddDays(-bufferDays);
        var floor = transactionDate.Date;
        if (shown < floor) shown = floor;   // المعروض لا يسبق تاريخ الفاتورة
        return shown;
    }

    /// <summary>بند متأخر: موعده المعروض فات وما زال فيه متبقٍّ لم يُسلَّم (من أحداث التسليم).</summary>
    public static bool IsLineOverdue(DateTime? displayDate, decimal remainingQty)
        => remainingQty > QtyEpsilon
           && displayDate.HasValue
           && displayDate.Value.Date < DateTime.Today;

    /// <summary>
    /// تطبيع زوج (تاريخ/مفصول؟) قبل الكتابة: لا يُخزَّن تاريخ إلا مع الفصل اليدوي،
    /// والفصل بلا تاريخ يعود «وارثًا» — فلا توجد حالة مخزّنة متناقضة أبدًا.
    /// </summary>
    public static (DateTime? Date, bool IsCustom) Normalize(DateTime? lineDate, bool isCustom)
        => isCustom && lineDate.HasValue
            ? (lineDate.Value.Date, true)
            : (null, false);
}
