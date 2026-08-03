namespace COCOBOLOERPNEW.Models;

public partial class B2BRequest
{
    public int RequestId { get; set; }
    public int PartyId { get; set; }
    public int PortalUserId { get; set; }
    public int? ResponsibleEmployeeId { get; set; }
    public string RequestType { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? Notes { get; set; }
    public int? RelatedQuotationId { get; set; }
    public int? RelatedInvoiceId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
    public DateTime? HandledAt { get; set; }
    public string? HandledBy { get; set; }

    public virtual Party Party { get; set; } = null!;
    public virtual B2BPortalUser PortalUser { get; set; } = null!;
    public virtual Employee? ResponsibleEmployee { get; set; }
    public virtual ICollection<B2BRequestItem> Items { get; set; } = new List<B2BRequestItem>();
}
