using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IB2BPortalUserService
{
    Task<List<B2BPortalUserListDto>> GetUsersAsync();
    Task<B2BPortalUserFormDto?> GetForEditAsync(int id);
    Task<List<B2BLookupDto>> SearchPartyLookupsAsync(string? searchText, int take = 20);
    Task<B2BLookupDto?> GetPartyLookupByIdAsync(int id);
    Task<List<B2BLookupDto>> GetEmployeeLookupsAsync();
    Task<(bool Success, string Message, int? Id)> SaveAsync(B2BPortalUserFormDto dto, string currentUserName);
    Task<(bool Success, string Message)> SetActiveAsync(int id, bool isActive, string currentUserName);
}
