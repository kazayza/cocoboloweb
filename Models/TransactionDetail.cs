using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class TransactionDetail
{
    public int DetailId { get; set; }

    public int TransactionId { get; set; }

    public int ProductId { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal? TotalAmount { get; set; }

    public string? Notes { get; set; }

    public virtual Product Product { get; set; } = null!;

    public virtual Transaction Transaction { get; set; } = null!;
}
