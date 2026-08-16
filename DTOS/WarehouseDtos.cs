namespace COCOBOLOERPNEW.DTOs;

public class WarehouseListDto
{
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public int? BranchId { get; set; }
    public string? BranchNameAr { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

public class WarehouseFormDto
{
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public int? BranchId { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}
