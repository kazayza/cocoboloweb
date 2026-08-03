using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IB2BPortalService
{
    Task<B2BPortalDashboardDto> GetDashboardAsync(int partyId, int portalUserId);
}
