using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class Temp_InvoicePrintDetail
{
    public int TempID { get; set; }

    public int TransactionID { get; set; }

    public int Productid { get; set; }

    public string ProductName { get; set; } = null!;

    public string ItemType { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal? LineTotal { get; set; }

    public string? Notes { get; set; }
}
