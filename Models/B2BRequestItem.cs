namespace COCOBOLOERPNEW.Models;

public partial class B2BRequestItem
{
    public int RequestItemId { get; set; }
    public int RequestId { get; set; }
    public int? ProductId { get; set; }
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }

    public virtual B2BRequest Request { get; set; } = null!;
    public virtual Product? Product { get; set; }
}
