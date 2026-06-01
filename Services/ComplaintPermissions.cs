using System.Security.Claims;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// مرجع موحّد لصلاحيات نظام الشكاوى (Single Source of Truth).
/// </summary>
public static class ComplaintPermissions
{
    // ─── الأدوار ─────────────────────────────────────
    private const string RoleAdmin           = "Admin";
    private const string RoleAccountManager  = "AccountManager";
    private const string RoleSalesManager    = "SalesManager";   // ⭐ مدير المبيعات
    private const string RoleSales           = "Sales";
    private const string RoleAccount         = "Account";
    private const string RoleUser            = "User";

    // ─── أسماء الـ Claims ────────────────────────────
    public const string PermView      = "frm_Complaints_Main:View";
    public const string PermAdd       = "frm_Complaints_Main:Add";
    public const string PermEdit      = "frm_Complaints_Main:Edit";
    public const string PermDelete    = "frm_Complaints_Main:Delete";
    public const string PermAssign    = "frm_Complaints_Main:Assign";
    public const string PermClose     = "frm_Complaints_Main:Close";
    public const string PermEscalate  = "frm_Complaints_Main:Escalate";

    // ═══════════════════════════════════════════════
    //                    السماحيات
    // ═══════════════════════════════════════════════

    /// <summary>هل يقدر يفتح موديول الشكاوى؟ (كل المستخدمين الموثقين)</summary>
    public static bool CanView(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true;

    /// <summary>هل يقدر يسجّل شكوى جديدة؟</summary>
    public static bool CanCreate(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.IsInRole(RoleSalesManager)
        || user.IsInRole(RoleSales)
        || user.HasClaim("Permission", PermAdd);

    /// <summary>هل يقدر يعدّل بيانات الشكوى الأساسية؟</summary>
    public static bool CanEdit(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.IsInRole(RoleSalesManager)
        || user.HasClaim("Permission", PermEdit);

    /// <summary>هل يقدر يحذف شكوى؟</summary>
    public static bool CanDelete(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.HasClaim("Permission", PermDelete);

    /// <summary>هل يقدر يسند الشكوى لموظف؟</summary>
    public static bool CanAssign(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.IsInRole(RoleSalesManager)
        || user.HasClaim("Permission", PermAssign);

    /// <summary>هل يقدر يغير الحالة (يحل/يرفض/يقفل)؟</summary>
    public static bool CanChangeStatus(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.IsInRole(RoleSalesManager)
        || user.HasClaim("Permission", PermClose);

    /// <summary>هل يقدر يصعّد شكوى؟</summary>
    public static bool CanEscalate(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.IsInRole(RoleSalesManager)
        || user.HasClaim("Permission", PermEscalate);

    /// <summary>هل يقدر يضيف متابعة (Follow-up)؟</summary>
    public static bool CanAddFollowUp(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.IsInRole(RoleSalesManager)
        || user.IsInRole(RoleSales)
        || user.IsInRole(RoleAccount);

    /// <summary>هل يقدر يدير الأنواع (التصنيفات)؟</summary>
    public static bool CanManageTypes(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleSalesManager);

    /// <summary>هل يقدر يصدّر Excel؟</summary>
    public static bool CanExport(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.IsInRole(RoleSalesManager);

    /// <summary>هل يشوف كل الشكاوى ولا اللي عاملها بس؟</summary>
    public static bool CanViewAll(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.IsInRole(RoleSalesManager)
        || user.IsInRole(RoleAccount);
}
