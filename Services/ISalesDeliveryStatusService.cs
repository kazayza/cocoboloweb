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
}

