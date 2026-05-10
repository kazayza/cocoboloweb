using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class StockTransaction
{
    public int TransactionId { get; set; }

    public int ProductId { get; set; }

    public int WarehouseId { get; set; }

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

    public virtual Product Product { get; set; } = null!;

    public virtual Warehouse Warehouse { get; set; } = null!;
}
