using System.Security.Claims;

namespace COCOBOLOERPNEW.Services;

public static class AuditPermissions
{
    private const string RoleAdmin           = "Admin";
    private const string RoleAccountManager  = "AccountManager";

    public const string PermView   = "frm_AuditViewer:View";
    public const string PermExport = "frm_AuditViewer:Export";
    public const string PermPurge  = "frm_AuditViewer:Purge";

    public static bool CanView(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.HasClaim("Permission", PermView);

    public static bool CanExport(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.HasClaim("Permission", PermExport);

    public static bool CanPurge(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.HasClaim("Permission", PermPurge);
}