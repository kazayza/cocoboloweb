using COCOBOLOERPNEW.DTOs;
using Microsoft.AspNetCore.Components.Forms;

namespace COCOBOLOERPNEW.Services;

public interface IB2BRequestService
{
    Task<List<B2BRequestListDto>> GetRequestsAsync(int? partyId = null, string? status = null, int? responsibleEmployeeId = null);
    Task<B2BRequestDetailDto?> GetByIdAsync(int id);
    Task<List<B2BProductLookupDto>> SearchProductsAsync(string? searchText, int take = 50);
    Task<List<B2BQuotationLookupDto>> SearchQuotationsAsync(int partyId, string? searchText, int take = 20);
    Task<List<B2BInvoiceLookupDto>> SearchInvoicesAsync(int partyId, string? searchText, int take = 20);
    Task<B2BQuotationLookupDto?> GetQuotationLookupByIdAsync(int quotationId);
    Task<B2BInvoiceLookupDto?> GetInvoiceLookupByIdAsync(int invoiceId);
    Task<(bool Success, string Message, int? Id)> CreateAsync(B2BCreateRequestDto dto, int portalUserId, int partyId, int? responsibleEmployeeId, string currentUserName);
    Task<(bool Success, string Message, int? Id)> CreateInternalAsync(B2BInternalCreateRequestDto dto, string currentUserName);
    Task<(bool Success, string Message)> UploadAttachmentsAsync(int requestId, IReadOnlyList<IBrowserFile> files, string currentUserName);
    Task<(bool Success, string Message)> UpdateStatusAsync(int requestId, string newStatus, string? handledBy, string? internalNotes = null, string? customerResponse = null, int? quotationId = null, int? invoiceId = null);
}
