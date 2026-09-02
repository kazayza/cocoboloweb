namespace COCOBOLOERPNEW.DTOs;

// ════════════════════════════════════════════════════════════
// قيمة المخزون (تكلفة + بيع) — صلاحية: Admin / AccountManager فقط
// ════════════════════════════════════════════════════════════
public class StockValueRowDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public int? BranchId { get; set; }
    public string? BranchNameAr { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal CostValue => Quantity * UnitCost;
    public decimal SaleValue => Quantity * UnitSalePrice;
    public decimal ExpectedProfit => SaleValue - CostValue;
}

public class StockValueSummaryDto
{
    public decimal TotalCostValue { get; set; }
    public decimal TotalSaleValue { get; set; }
    public decimal TotalExpectedProfit => TotalSaleValue - TotalCostValue;
    public int TotalProducts { get; set; }
    public long TotalQuantity { get; set; }
    public int WarehousesCount { get; set; }
}