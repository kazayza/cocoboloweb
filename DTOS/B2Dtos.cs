namespace COCOBOLOERPNEW.DTOs;

public static class B2BRequestTypes
{
    public const string Quotation = "Quotation";
    public const string Reorder = "Reorder";
    public const string PaymentProof = "PaymentProof";
    public const string Support = "Support";

    public static readonly string[] All =
    [
        Quotation,
        Reorder,
        PaymentProof,
        Support
    ];
}

public static class B2BRequestStatuses
{
    public const string New = "New";
    public const string UnderReview = "UnderReview";
    public const string Converted = "Converted";
    public const string Closed = "Closed";
    public const string Rejected = "Rejected";

    public static readonly string[] All =
    [
        New,
        UnderReview,
        Converted,
        Closed,
        Rejected
    ];
}

public class B2BPortalUserListDto
{
    public int PortalUserId { get; set; }
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public int? ResponsibleEmployeeId { get; set; }
    public string? ResponsibleEmployeeName { get; set; }
    public string FullName { get; set; } = "";
    public string UserName { get; set; } = "";
    public string? Email { get; set; }
    public string? Mobile { get; set; }
    public bool IsActive { get; set; }
    public bool CanViewPrices { get; set; }
    public bool CanViewFinancials { get; set; }
    public bool CanRequestQuotation { get; set; }
    public bool CanUploadPaymentProof { get; set; }
    public DateTime? LastLogin { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class B2BPortalUserFormDto
{
    public int PortalUserId { get; set; }
    public int? PartyId { get; set; }
    public int? ResponsibleEmployeeId { get; set; }
    public string FullName { get; set; } = "";
    public string UserName { get; set; } = "";
    public string? Password { get; set; }
    public string? Email { get; set; }
    public string? Mobile { get; set; }
    public bool IsActive { get; set; } = true;
    public bool CanViewPrices { get; set; } = true;
    public bool CanViewFinancials { get; set; } = true;
    public bool CanRequestQuotation { get; set; } = true;
    public bool CanUploadPaymentProof { get; set; } = true;
}

public class B2BLookupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class B2BProductLookupDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string? ProductDescription { get; set; }
    public string? ImageUrl { get; set; }
    public string DisplayName => string.IsNullOrWhiteSpace(ProductDescription)
        ? ProductName
        : $"{ProductName} — {ProductDescription}";
}

public class B2BQuotationLookupDto
{
    public int QuotationId { get; set; }
    public string ReferenceNumber { get; set; } = "";
    public DateTime QuotationDate { get; set; }
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? Status { get; set; }
    public decimal GrandTotal { get; set; }
    public int? InvoiceId { get; set; }
    public string? InvoiceReferenceNumber { get; set; }
    public string DisplayName => $"{ReferenceNumber} — {PartyName}";
}

public class B2BInvoiceLookupDto
{
    public int TransactionId { get; set; }
    public string ReferenceNumber { get; set; } = "";
    public DateTime TransactionDate { get; set; }
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? Status { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal Remaining => GrandTotal - PaidAmount;
    public int? QuotationId { get; set; }
    public string? QuotationReferenceNumber { get; set; }
    public string DisplayName => $"{ReferenceNumber} — {PartyName}";
}

public class B2BRequestListDto
{
    public int RequestId { get; set; }
    public string RequestType { get; set; } = B2BRequestTypes.Quotation;
    public string Status { get; set; } = B2BRequestStatuses.New;
    public string RequestSource { get; set; } = "Portal";
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public int? PortalUserId { get; set; }
    public string PortalUserName { get; set; } = "";
    public string? RequestedContactName { get; set; }
    public string? RequestedContactPhone { get; set; }
    public int? ResponsibleEmployeeId { get; set; }
    public string? ResponsibleEmployeeName { get; set; }
    public int? RelatedQuotationId { get; set; }
    public int? RelatedInvoiceId { get; set; }
    public int ItemsCount { get; set; }
    public string? Notes { get; set; }
    public string? InternalNotes { get; set; }
    public string? CustomerResponse { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? HandledAt { get; set; }
    public string? HandledBy { get; set; }
}

public class B2BRequestDetailDto : B2BRequestListDto
{
    public List<B2BRequestItemDto> Items { get; set; } = new();
    public List<B2BRequestAttachmentDto> Attachments { get; set; } = new();
}

public class B2BRequestItemDto
{
    public int RequestItemId { get; set; }
    public int? ProductId { get; set; }
    public string? ProductName { get; set; }
    public string? ProductImageUrl { get; set; }
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
}

public class B2BCreateRequestDto
{
    public string RequestType { get; set; } = B2BRequestTypes.Quotation;
    public string? Notes { get; set; }
    public List<B2BRequestItemInputDto> Items { get; set; } = new();
}

public class B2BInternalCreateRequestDto
{
    public int? PartyId { get; set; }
    public int? PortalUserId { get; set; }
    public int? ResponsibleEmployeeId { get; set; }
    public string RequestType { get; set; } = B2BRequestTypes.Quotation;
    public string RequestSource { get; set; } = "Internal";
    public string? RequestedContactName { get; set; }
    public string? RequestedContactPhone { get; set; }
    public string? Notes { get; set; }
    public List<B2BRequestItemInputDto> Items { get; set; } = new();
}

public class B2BRequestItemInputDto
{
    public int? ProductId { get; set; }
    public B2BProductLookupDto? SelectedProduct { get; set; }
    public decimal Quantity { get; set; } = 1;
    public string? Notes { get; set; }
}

public class B2BRequestAttachmentDto
{
    public int AttachmentId { get; set; }
    public string FileName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string? ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
    public string UploadedBy { get; set; } = "";
}

public class B2BPortalDashboardDto
{
    public string PartyName { get; set; } = "";
    public string PortalUserName { get; set; } = "";
    public int OpenQuotationsCount { get; set; }
    public int OpenInvoicesCount { get; set; }
    public int PendingDeliveriesCount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public List<B2BRequestListDto> RecentRequests { get; set; } = new();
}

public class B2BLoginResultDto
{
    public int PortalUserId { get; set; }
    public int PartyId { get; set; }
    public int? ResponsibleEmployeeId { get; set; }
    public string UserName { get; set; } = "";
    public string FullName { get; set; } = "";
}
