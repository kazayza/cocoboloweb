using System.Security.Claims;
using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// واجهة موديول خدمة العملاء (فيدباك بعد زيارة المعرض / بعد التسليم / متابعة الشكاوى).
/// </summary>
public interface ICustomerCareService
{
    /// <summary>لوحة المكالمات: يجمع الزيارات/التسليمات/الشكاوى المؤهلة + الإحصاءات</summary>
    Task<CustomerCareDashboardDto> GetDashboardAsync(CustomerCareDashboardFilterDto filter, ClaimsPrincipal? user);

    /// <summary>سجل الفيدباك (مع فلترة + تصفح)</summary>
    Task<CustomerCareHistoryDto> GetHistoryAsync(CustomerCareHistoryFilterDto filter, ClaimsPrincipal? user);

    /// <summary>تسجيل/تحديث نتيجة مكالمة فيدباك</summary>
    Task<(bool Success, string Message, int? FeedbackId)> LogOutcomeAsync(CustomerCareLogDto dto, ClaimsPrincipal? user);

    /// <summary>حذف/إخفاء سجل</summary>
    Task<(bool Success, string Message)> HideAsync(int feedbackId, ClaimsPrincipal? user);

    /// <summary>تقرير/تصدير Excel</summary>
    Task<(byte[] Content, string Mime, string FileName)> ExportAsync(CustomerCareReportFilterDto filter, ClaimsPrincipal? user);
}
