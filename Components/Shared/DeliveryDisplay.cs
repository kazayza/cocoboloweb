using System.Globalization;
using MudBlazor;

namespace COCOBOLOERPNEW.Components.Shared;

/// <summary>
/// ⭐ عرض مواعيد التسليم — تنسيقات وحالات بصرية واحدة لكل شاشات التسليم
/// (قائمة المتابعة، تفاصيل التسليم، حوار تسجيل تسليمة، نموذج الفاتورة).
/// كل التواريخ المارة من هنا «مواعيد معروضة» أصلًا: قارئها طبّق قاعدة العرض
/// عبر DeliveryScheduleResolver.DisplayDate (الإخفاء للمخفيين)، وهذه الدوال
/// تلوّن وتسمّي فقط — لا تعيد حساب أي موعد حقيقي.
/// </summary>
public static class DeliveryDisplay
{
    private static readonly CultureInfo ArEg = new("ar-EG");

    /// <summary>«الأحد 30-11» — اسم اليوم بالعربية والأرقام لاتينية كسائر الشاشات.</summary>
    public static string FormatWeekdayDate(DateTime date)
        => $"{date.ToString("dddd", ArEg)} {date.ToString("dd-MM", CultureInfo.InvariantCulture)}";

    /// <summary>«30-11» / «2-11» — يوم/شهر مختصر لأشرطة الملخص.</summary>
    public static string FormatDayMonth(DateTime date)
        => date.ToString("d-MM", CultureInfo.InvariantCulture);

    /// <summary>
    /// لون وأيقونة شيب موعد بند واحد: مكتمل التسليم = أخضر، متأخر = أحمر،
    /// مستحق اليوم = كهرماني، قادم = رمادي محايد.
    /// </summary>
    public static (Color Color, string Icon) LineChipStyle(
        DateTime? displayDate, bool isOverdue, bool fullyDelivered)
    {
        if (fullyDelivered)
            return (Color.Success, Icons.Material.Filled.CheckCircle);
        if (isOverdue)
            return (Color.Error, Icons.Material.Filled.Warning);
        if (displayDate.HasValue && displayDate.Value.Date == DateTime.Today)
            return (Color.Warning, Icons.Material.Filled.EventAvailable);
        return (Color.Default, Icons.Material.Filled.Event);
    }

    /// <summary>
    /// حالة «موعد» (مجموعة بنود فاتورة بموعد معروض واحد) كما تظهر في قائمة
    /// المتابعة: اكتمل / متأخر / مستحق اليوم / قادم (وبلا موعد للحافة اليتيمة).
    /// </summary>
    public static (string Text, Color Color, string Icon) AppointmentState(
        DateTime? displayDate, int pendingLineCount, bool hasOverdue)
    {
        if (pendingLineCount <= 0)
            return ("اكتمل", Color.Success, Icons.Material.Filled.CheckCircle);
        if (hasOverdue)
            return ("متأخر", Color.Error, Icons.Material.Filled.Warning);
        if (displayDate.HasValue && displayDate.Value.Date == DateTime.Today)
            return ("مستحق اليوم", Color.Warning, Icons.Material.Filled.EventAvailable);
        if (!displayDate.HasValue)
            return ("بلا موعد", Color.Default, Icons.Material.Filled.EventBusy);
        return ("قادم", Color.Default, Icons.Material.Filled.Event);
    }
}
