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
        string? partyName,
        string? deliveryStatus);
    
    Task<VwSalesDeliveryStatus?> GetByTransactionIdAsync(int transactionId);
    
    // ─── الملخص والإحصائيات ────────────────────────
    
    Task<DeliverySummaryDto> GetSummaryAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        string? partyName,
        string? deliveryStatus);
    
    // ─── تفاصيل التسليم ────────────────────────────
    
    Task<DeliveryDetailDto?> GetDeliveryDetailsAsync(int transactionId);
    
    // ─── تحديث حالة التسليم ────────────────────────
    
    Task<(bool Success, string Message)> UpdateDeliveryStatusAsync(
        DeliveryUpdateDto dto);
    
    // ─── قائمة الموظفين (المندوبين) ────────────────
    
    Task<List<EmployeeLookupDto>> GetDeliveryEmployeesAsync();
    
    // ─── PDF ────────────────────────────────────────
    
    Task<byte[]> GenerateDeliveryPdfAsync(int transactionId);
}

