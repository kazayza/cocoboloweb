namespace COCOBOLOERPNEW.DTOs;

public class BranchListDto
{
    public int BranchId { get; set; }
    public string BranchCode { get; set; } = "";
    public string BranchNameAr { get; set; } = "";
    public string? BranchNameEn { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public int? ManagerEmployeeId { get; set; }
    public string? ManagerEmployeeName { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

public class BranchFormDto
{
    public int BranchId { get; set; }
    public string BranchCode { get; set; } = "";
    public string BranchNameAr { get; set; } = "";
    public string? BranchNameEn { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public int? ManagerEmployeeId { get; set; }
    public bool IsActive { get; set; } = true;
}
