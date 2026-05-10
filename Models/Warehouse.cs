using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class Warehouse
{
    public int WarehouseId { get; set; }

    public string WarehouseName { get; set; } = null!;

    public string? Location { get; set; }

    public bool? IsActive { get; set; }

    public string? Notes { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<StockLevel> StockLevels { get; set; } = new List<StockLevel>();

    public virtual ICollection<StockTransaction> StockTransactions { get; set; } = new List<StockTransaction>();
}
