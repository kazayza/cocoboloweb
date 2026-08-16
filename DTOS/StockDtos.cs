using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.DTOs;

public class StockEntryFormDto
{
    public int? BranchId { get; set; }
    public int? WarehouseId { get; set; }
    public int? ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public string EntryReason { get; set; } = "OpeningBalance";
    public string? Notes { get; set; }
}

public class StockProductLookupDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string? ProductDescription { get; set; }
    public string? CustomerName { get; set; }
    public bool IsShowroomProduct => !HasCustomer;
    public bool HasCustomer => !string.IsNullOrWhiteSpace(CustomerName);
}

public class StockEntryResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int? StockTransactionId { get; set; }
    public int? NewQuantity { get; set; }
}

public class StockTransferFormDto
{
    public int? FromBranchId { get; set; }
    public int? FromWarehouseId { get; set; }
    public int? ToBranchId { get; set; }
    public int? ToWarehouseId { get; set; }
    public int? ProductId { get; set; }
    public int Quantity { get; set; }
    public string? Notes { get; set; }
}

public class StockTransferResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? TransferReference { get; set; }
    public int? SourceNewQuantity { get; set; }
    public int? DestinationNewQuantity { get; set; }
}

public class StockTransactionFilterDto
{
    public int? BranchId { get; set; }
    public int? WarehouseId { get; set; }
    public int? ProductId { get; set; }
    public string? TransactionType { get; set; }
    public string? ReferenceType { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? SearchText { get; set; }
}

public class StockTransactionListDto
{
    public int TransactionId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public int? BranchId { get; set; }
    public string? BranchNameAr { get; set; }
    public string? TransactionType { get; set; }
    public int Quantity { get; set; }
    public DateTime? TransactionDate { get; set; }
    public int? ReferenceId { get; set; }
    public string? ReferenceType { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public class StockCountFilterDto
{
    public int? BranchId { get; set; }
    public int? WarehouseId { get; set; }
}

public class StockCountLineDto
{
    public int? StockCountLineId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public int SystemQty { get; set; }
    public int ActualQty { get; set; }
    public int DifferenceQty { get; set; }
    public string? Notes { get; set; }
}

public class StockCountWorkspaceDto
{
    public int? StockCountId { get; set; }
    public int BranchId { get; set; }
    public string BranchNameAr { get; set; } = "";
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public DateTime CountDate { get; set; }
    public string Status { get; set; } = "Draft";
    public string? Notes { get; set; }
    public List<StockCountLineDto> Lines { get; set; } = new();
}

public class StockCountHeaderListDto
{
    public int StockCountId { get; set; }
    public int BranchId { get; set; }
    public string BranchNameAr { get; set; } = "";
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public DateTime CountDate { get; set; }
    public string Status { get; set; } = "Draft";
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? FinalizedBy { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public int LinesCount { get; set; }
    public int DifferenceItemsCount { get; set; }
}
