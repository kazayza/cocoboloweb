using System.Security.Claims;

namespace COCOBOLOERPNEW.Services;

public static class B2BPermissions
{
    public const string PortalRole = "B2BPortal";
    public const string FormName = "B2B";
    public const string ViewClaim = FormName + ":View";

    public static bool IsPortalUser(ClaimsPrincipal user)
        => user.Identity?.IsAuthenticated == true && user.IsInRole(PortalRole);

    public static bool CanManage(ClaimsPrincipal user)
        => user.Identity?.IsAuthenticated == true &&
           (user.IsInRole("Admin") || user.IsInRole("GeneralManager") || user.IsInRole("AccountManager") || user.HasClaim("Permission", ViewClaim));

    public static int? GetPortalUserId(ClaimsPrincipal user)
        => int.TryParse(user.FindFirst("PortalUserId")?.Value, out var id) ? id : null;

    public static int? GetPartyId(ClaimsPrincipal user)
        => int.TryParse(user.FindFirst("PartyId")?.Value, out var id) ? id : null;

    public static int? GetResponsibleEmployeeId(ClaimsPrincipal user)
        => int.TryParse(user.FindFirst("ResponsibleEmployeeId")?.Value, out var id) ? id : null;

    public static string GetPortalDisplayName(ClaimsPrincipal user)
        => user.FindFirst("PortalDisplayName")?.Value
           ?? user.Identity?.Name
           ?? "عميل B2B";

    public static string GetPortalRecipientKey(int portalUserId)
        => $"B2BPortalUser:{portalUserId}";
}
