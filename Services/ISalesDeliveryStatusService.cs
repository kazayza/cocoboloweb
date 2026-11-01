using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;

namespace COCOBOLOERPNEW.Services;

public interface ISalesDeliveryStatusService
{
    // ─── جلب البيانات ──────────────────────────────
    
    Task<List<VwSalesDeliveryStatus>> GetAllAsync();
    
    Task<List<VwSalesDeliveryStatus>> GetFilteredAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        string dateFilterType,
        string? partyName,
        string? deliveryStatus);
    
    Task<VwSalesDeliveryStatus?> GetByTransactionIdAsync(int transactionId);
    
    // ─── الملخص والإحصائيات ────────────────────────
    
    Task<DeliverySummaryDto> GetSummaryAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        string dateFilterType,
        string? partyName,
        string? deliveryStatus);
    
    // ─── تفاصيل التسليم ────────────────────────────
    
    Task<DeliveryDetailDto?> GetDeliveryDetailsAsync(int transactionId);
    
    // ─── تحديث حالة التسليم ────────────────────────
    
    // ⭐ جلب الموظف (كود + اسم) من اسم المستخدم — لزرار التسليم السريع
    Task<(int? EmployeeId, string? FullName)> GetEmployeeByUserNameAsync(string userName);

    Task<(bool Success, string Message)> UpdateDeliveryStatusAsync(
        DeliveryUpdateDto dto);
    
    // ─── قائمة الموظفين (المندوبين) ────────────────
    
    Task<List<EmployeeLookupDto>> GetDeliveryEmployeesAsync();
    
    // ─── كشف التسليمات غير المسلَّمة (طباعة) ────────
    
    Task<List<DailyDeliverySheetRowDto>> GetDailyDeliverySheetAsync(DateTime? from, DateTime? to);
    
    // ─── إذن التسليم لفواتير عليها مبالغ متبقية ────
    
    /// <summary>المندوب يطلب إذن تسليم فاتورة عليها مبالغ متبقية (إشعار للأدارة ومدير الحسابات)</summary>
    Task<(bool Success, string Message)> RequestDeliveryPermissionAsync(int transactionId, string requestedBy);
    
    /// <summary>المدير/مدير الحسابات يمنح إذن التسليم (إشعار لطالب الإذن)</summary>
    Task<(bool Success, string Message)> GrantDeliveryPermissionAsync(int transactionId, string grantedBy);
    
    /// <summary>حالة إذن التسليم لدفعة فواتير (للعرض في القوائم)</summary>
    Task<Dictionary<int, DeliveryPermissionStateDto>> GetDeliveryPermissionStatesAsync(List<int> transactionIds);
    
    // ─── PDF ────────────────────────────────────────

    Task<byte[]> GenerateDeliveryPdfAsync(int transactionId);

    // ─── التسليم الجزئي (أحداث التسليم) ─────────────

    /// <summary>تقدم تسليم فاتورة واحدة: بنود (مفوتر/مسلَّم/متبقي) + سجل الأحداث + الحالة الفعّالة</summary>
    Task<InvoiceDeliveryProgressDto?> GetDeliveryProgressAsync(int transactionId);

    /// <summary>تقدم مختصر لمجموعة فواتير دفعة واحدة (لقائمة التسليمات — بلا N+1)</summary>
    Task<Dictionary<int, DeliveryProgressSummaryDto>> GetDeliveryProgressBatchAsync(List<int> transactionIds);

    /// <summary>ملخصات جدولة تسليم البنود لمجموعة فواتير: المواعيد المميزة المعروضة للمستخدم الحالي وبنود كل موعد</summary>
    Task<Dictionary<int, InvoiceScheduleSummaryDto>> GetLineScheduleSummariesBatchAsync(List<int> transactionIds);

    /// <summary>إذن تسليم حدث واحد: بنود الدفعة وكمياتها + ملخص تراكمي (لطباعة ورقة الدفعة من سجل التسليمات)</summary>
    Task<DeliveryEventPermitDto?> GetDeliveryEventPermitAsync(int deliveryId);

    /// <summary>ملخص نتائج تسليم فاتورة (أحداث بملاحظات/في الصيانة/رسوم تعديل معلّقة) لشريط تفاصيل الفاتورة — القسم 11.5</summary>
    Task<InvoiceOutcomesSummaryDto?> GetInvoiceOutcomesSummaryAsync(int transactionId);

    /// <summary>تغطية دفعة مقترحة ماليًا (المدفوع مقابل المسلَّم سابقًا + هذه الدفعة) — لعرض حوار التسجيل قبل الحفظ</summary>
    Task<DeliveryBatchCoverageDto> GetBatchCoverageAsync(int transactionId, List<RecordDeliveryItemDto> items);

    /// <summary>مواعيد التسليم القادمة خلال نافذة أيام على التواريخ المعروضة للمستخدم الحالي (لقسم الرئيسية)</summary>
    Task<List<UpcomingDeliveryAppointmentDto>> GetUpcomingAppointmentsAsync(int withinDays);

    /// <summary>المواعيد المفتوحة التي يطابق تاريخها المعروض لفئة (حقيقي/مخفي) يومًا مستهدفًا (لتنبيهات ما قبل الموعد)</summary>
    Task<List<UpcomingDeliveryAppointmentDto>> GetAppointmentsDueOnAsync(DateTime targetDisplayedDate, bool seesRealDate);

    // ─── طلبات تعديل مواعيد التسليم (تعديل / تقسيم) ────

    /// <summary>مسئول التسليمات/مدير الإنتاج يطلب تعديل مواعيد فاتورة (تاريخ كلي و/أو مواعيد بنود) — إشعار للمدير ومدير الحسابات</summary>
    Task<(bool Success, string Message)> CreateScheduleChangeRequestAsync(CreateScheduleChangeRequestDto dto);

    /// <summary>سجل طلبات تعديل المواعيد لفاتورة (الأحدث أولًا) — لصفحة تفاصيل التسليم</summary>
    Task<List<ScheduleChangeRequestDto>> GetScheduleChangeRequestsAsync(int transactionId);

    /// <summary>حالات الطلبات المعلّقة لمجموعة فواتير دفعة واحدة (لشريحة قائمة التسليمات)</summary>
    Task<Dictionary<int, ScheduleChangePendingStateDto>> GetPendingScheduleRequestStatesAsync(List<int> transactionIds);

    /// <summary>بيانات شاشة الاعتماد: الطلب + الجدولة الحقيقية الحالية — للمُعتمِدين فقط (المدير/مدير الحسابات)</summary>
    Task<ScheduleChangeApprovalDto?> GetScheduleChangeApprovalDataAsync(int requestId);

    /// <summary>البتّ في طلب تعديل مواعيد: اعتماد يطبّق القيم النهائية على الفاتورة وبنودها في معاملة واحدة / رفض بسبب إلزامي — إشعار لصاحب الطلب</summary>
    Task<(bool Success, string Message)> DecideScheduleChangeRequestAsync(
        int requestId, bool approve, List<ScheduleChangeFinalLineDto> finalLines,
        DateTime? finalDueDate, string? note, string deciderUser);

    /// <summary>سحب طلب تعديل مواعيد — لصاحبه فقط وما دام معلّقًا</summary>
    Task<(bool Success, string Message)> WithdrawScheduleChangeRequestAsync(int requestId, string userName);

    /// <summary>تسجيل تسليمة (دفعة) بكميات لكل بند — المصدر الوحيد لكتابة حالة التسليم بعد النشر</summary>
    Task<(bool Success, string Message)> RecordDeliveryAsync(RecordDeliveryDto dto);

    /// <summary>إلغاء حدث تسليم (منطقي، بسبب إلزامي) — للمدير ومدير الحسابات فقط</summary>
    Task<(bool Success, string Message)> VoidDeliveryEventAsync(int deliveryId, string reason, string userName);
}

