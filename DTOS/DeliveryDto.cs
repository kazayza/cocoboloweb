using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.DTOs;

#region 📋 ملخص التسليمات (Delivery Summary DTO)

public class DeliverySummaryDto
{
    public int TotalCount { get; set; }
    public int PendingCount { get; set; }          // جاري
    public int DeliveredCount { get; set; }        // تم التسليم
    public int OverdueCount { get; set; }          // متأخر
    public int ReturnedCount { get; set; }         // مرتجع
    
    public decimal TotalGrandTotal { get; set; }
    public decimal TotalPaidAmount { get; set; }
    public decimal TotalRemaining => TotalGrandTotal - TotalPaidAmount;
}

#endregion

#region 📦 تفاصيل التسليم الكاملة (Delivery Detail DTO)

public class DeliveryDetailDto
{
    // معلومات الفاتورة
    public int TransactionId { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime TransactionDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string TransactionType { get; set; } = "";
    
    // معلومات العميل
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? PartyPhone { get; set; }
    public string? PartyAddress { get; set; }
    
    // معلومات موظف البيع (المسئول)
    public int? SalesEmployeeId { get; set; }
    public string? SalesEmployeeName { get; set; }
    
    // معلومات مندوب التسليم
    public int? DeliveryEmployeeId { get; set; }
    public string? DeliveryEmployeeName { get; set; }
    
    // معلومات التسليم
    public string DeliveryStatus { get; set; } = "";  // جاري | متأخر | تم التسليم | مرتجع
    public DateTime? DeliveredAt { get; set; }
    public string? DeliveredNotes { get; set; }
    
    // المعلومات المالية
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount => GrandTotal - PaidAmount;
    
    // الأيام المتبقية
    public int? DaysRemaining { get; set; }
    
    // المنتجات
    public List<DeliveryProductDto> Products { get; set; } = new();
}

public class DeliveryProductDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal Quantity { get; set; }
    public string? UnitName { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }
}

#endregion

#region 🔄 تحديث حالة التسليم (Update DTO)

public class DeliveryUpdateDto
{
    public int TransactionId { get; set; }
    
    public string Status { get; set; } = "";  // تم التسليم | متأخر | مرتجع
    
    public string? DeliveryEmployeeName { get; set; }
    
    public DateTime? DeliveredAt { get; set; }
    
    public string? Notes { get; set; }
    
    public string UserName { get; set; } = "";  // اللي عمل التحديث
}

#endregion

#region 🔍 فلتر التسليمات (Filter DTO)

public class DeliveryFilterDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? PartyName { get; set; }
    public string? DeliveryStatus { get; set; }
    public int? SalesEmployeeId { get; set; }
    public int? PageNumber { get; set; } = 1;
    public int? PageSize { get; set; } = 20;
}

#endregion