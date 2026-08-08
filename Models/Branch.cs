namespace COCOBOLOERPNEW.Models;

public partial class Branch
{
    public int BranchId { get; set; }
    public string BranchCode { get; set; } = null!;
    public string BranchNameAr { get; set; } = null!;
    public string? BranchNameEn { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public int? ManagerEmployeeId { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }

    public virtual Employee? ManagerEmployee { get; set; }
}
