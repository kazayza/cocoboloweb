namespace COCOBOLOERPNEW.Models;

public partial class B2BPortalUser
{
    public int PortalUserId { get; set; }
    public int PartyId { get; set; }
    public int? ResponsibleEmployeeId { get; set; }
    public string FullName { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public string HashedPassword { get; set; } = null!;
    public string? Email { get; set; }
    public string? Mobile { get; set; }
    public bool IsActive { get; set; }
    public bool CanViewPrices { get; set; }
    public bool CanViewFinancials { get; set; }
    public bool CanRequestQuotation { get; set; }
    public bool CanUploadPaymentProof { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
    public DateTime? LastLogin { get; set; }

    public virtual Party Party { get; set; } = null!;
    public virtual Employee? ResponsibleEmployee { get; set; }
    public virtual ICollection<B2BRequest> B2BRequests { get; set; } = new List<B2BRequest>();
}
