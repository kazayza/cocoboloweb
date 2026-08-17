namespace COCOBOLOERPNEW.DTOs;

public class PurchaseReceiptListDto
{
    public int TransactionId { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime TransactionDate { get; set; }
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public DateTime? DueDate { get; set; }
    public bool IsDelivered { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string? DeliveredNotes { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public int ItemsCount { get; set; }
    public int? DaysRemaining { get; set; }
    public string ReceiptStatus { get; set; } = PurchaseReceiptStatusNames.Pending;
    public decimal RemainingAmount => GrandTotal - PaidAmount;
}

public class PurchaseReceiptSummaryDto
{
    public int TotalCount { get; set; }
    public int PendingCount { get; set; }
    public int ReceivedCount { get; set; }
    public int OverdueCount { get; set; }
    public decimal TotalGrandTotal { get; set; }
    public decimal TotalPaidAmount { get; set; }
    public decimal TotalRemaining => TotalGrandTotal - TotalPaidAmount;
}

public static class PurchaseReceiptStatusNames
{
    public const string Pending = "بانتظار الاستلام";
    public const string Received = "تم الاستلام";
    public const string Overdue = "متأخر";
}

public static class PurchaseReceiptDateFilterTypes
{
    public const string DueDate = "DueDate";
    public const string InvoiceDate = "InvoiceDate";
    public const string ReceivedDate = "ReceivedDate";
}
