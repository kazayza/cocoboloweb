namespace COCOBOLOERPNEW.Models;

public partial class StockCountHeader
{
    public int StockCountId { get; set; }
    public int BranchId { get; set; }
    public int WarehouseId { get; set; }
    public DateTime CountDate { get; set; }
    public string Status { get; set; } = "Draft";
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public string? FinalizedBy { get; set; }

    public virtual Branch Branch { get; set; } = null!;
    public virtual Warehouse Warehouse { get; set; } = null!;
    public virtual ICollection<StockCountLine> Lines { get; set; } = new List<StockCountLine>();
}
