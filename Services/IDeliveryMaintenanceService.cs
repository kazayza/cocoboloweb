using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// حلقة صيانة مرتجعات التسليم (خطة التسليم الجزئي — القسم 11.3):
/// طابور عمل المصنع الخفيف وانتقالات حالات السجلات وإغلاقاتها وإعادة تصنيفها.
/// الصلاحيات تُفحص داخل الخدمة نفسها (DeliveryPermissions) — الخدمة مصدر الحقيقة.
/// </summary>
public interface IDeliveryMaintenanceService
{
    /// <summary>طابور الصيانة: سجلات غير ملغاة بفلاتر (حالة/تصنيف/بحث) — الأقدم أولًا داخل الحلقة</summary>
    Task<List<DeliveryMaintenanceItemDto>> GetQueueAsync(
        string? status, string? reasonClass, string? search, bool includeTerminal);

    /// <summary>بدء الصيانة (بانتظار ← قيد الصيانة) مع إسناد اختياري لموظف مصنع</summary>
    Task<(bool Success, string Message)> StartAsync(int maintenanceId, int? handledByEmployeeId, string userName);

    /// <summary>جاهز لإعادة التسليم (مع تكلفة إصلاح داخلية اختيارية) — يُشعر البائع ومسؤول التسليمات</summary>
    Task<(bool Success, string Message)> MarkReadyAsync(
        int maintenanceId, decimal? repairCost, string? repairCostNotes, int? handledByEmployeeId, string userName);

    /// <summary>تعذّر إصلاحه — تنتظر قرار استبدال/ردّ نهائي من الإدارة أو الحسابات (D11)</summary>
    Task<(bool Success, string Message)> MarkUnrepairableAsync(int maintenanceId, string? notes, string userName);

    /// <summary>إغلاق باستبدال: الكمية تعود لـ«بانتظار التسليم» وتُسلَّم وحدتها البديلة بحدث عادي</summary>
    Task<(bool Success, string Message)> CloseByReplacementAsync(int maintenanceId, string? notes, string userName);

    /// <summary>إغلاق بردّ نهائي/رصيد: الكمية تُسوّى (قد يُكمل الفاتورة — يُعاد بناء إسقاطها) — المدير ومدير الحسابات فقط</summary>
    Task<(bool Success, string Message)> CloseByCreditAsync(int maintenanceId, string? notes, string userName);

    /// <summary>إلغاء طلب (لسجلات التعديل عند تراجع العميل): المدير ومدير الحسابات، بسبب إلزامي</summary>
    Task<(bool Success, string Message)> CancelAsync(int maintenanceId, string reason, string userName);

    /// <summary>إعادة تصنيف سبب الرجوع (D12): مدير/مدير حسابات/مدير إنتاج + سبب إلزامي + تدقيق قبل/بعد</summary>
    Task<(bool Success, string Message)> ReclassifyAsync(int maintenanceId, string newClass, string reason, string userName);
}
