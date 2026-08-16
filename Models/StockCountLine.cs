namespace COCOBOLOERPNEW.Models;

public partial class StockCountLine
{
    public int StockCountLineId { get; set; }
    public int StockCountId { get; set; }
    public int ProductId { get; set; }
    public int SystemQty { get; set; }
    public int ActualQty { get; set; }
    public int DifferenceQty { get; set; }
    public string? Notes { get; set; }

    public virtual StockCountHeader StockCount { get; set; } = null!;
    public virtual Product Product { get; set; } = null!;
}
