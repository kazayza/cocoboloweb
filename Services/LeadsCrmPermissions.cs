using System.Security.Claims;

namespace COCOBOLOERPNEW.Services;

public static class LeadsCrmPermissions
{
    private const string FormName = "frm_LeadsCRM";

    public const string PermView = "frm_LeadsCRM:View";
    public const string PermAdd = "frm_LeadsCRM:Add";
    public const string PermEdit = "frm_LeadsCRM:Edit";
    public const string PermDelete = "frm_LeadsCRM:Delete";

    public static bool CanView(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("Permission", PermView);

    public static bool CanEdit(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("Permission", PermEdit);

    public static bool CanDelete(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("Permission", PermDelete);

    public static bool CanConvert(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("Permission", PermEdit);
    public static bool CanViewDashboard(ClaimsPrincipal user) =>
    user.IsInRole("Admin")
    || user.IsInRole("SalesManager")
    || user.IsInRole("SocialView")
    || user.IsInRole("SocialManager");
}