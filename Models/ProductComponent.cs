using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class ProductComponent
{
    public int ComponentId { get; set; }

    public int ProductId { get; set; }

    public string ComponentName { get; set; } = null!;

    public int Quantity { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Product Product { get; set; } = null!;
}
