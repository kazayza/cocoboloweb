using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class StockLevel
{
    public int StockLevelId { get; set; }

    public int ProductId { get; set; }

    public int WarehouseId { get; set; }

    public int Quantity { get; set; }

    public DateTime? LastUpdatedAt { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Product Product { get; set; } = null!;

    public virtual Warehouse Warehouse { get; set; } = null!;
}
