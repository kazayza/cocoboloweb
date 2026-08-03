using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IB2BRequestService
{
    Task<List<B2BRequestListDto>> GetRequestsAsync(int? partyId = null, string? status = null, int? responsibleEmployeeId = null);
    Task<B2BRequestDetailDto?> GetByIdAsync(int id);
    Task<List<B2BProductLookupDto>> SearchProductsAsync(string? searchText, int take = 20);
    Task<(bool Success, string Message, int? Id)> CreateAsync(B2BCreateRequestDto dto, int portalUserId, int partyId, int? responsibleEmployeeId, string currentUserName);
    Task<(bool Success, string Message)> UpdateStatusAsync(int requestId, string newStatus, string? handledBy, string? notes = null, int? quotationId = null, int? invoiceId = null);
}
