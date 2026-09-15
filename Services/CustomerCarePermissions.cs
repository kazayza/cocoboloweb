using System.Security.Claims;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// مرجع موحّد لصلاحيات موديول خدمة العملاء (فيدباك ما بعد البيع).
/// الدور الرئيسي: CustomerService — الموظف المختص بالاتصال بأخذ الفيدباك.
/// </summary>
public static class CustomerCarePermissions
{
    // ─── الأدوار ─────────────────────────────────────────────
    public const string RoleCustomerService  = "CustomerService";   // ⭐ الموظف المختص
    public const string RoleAdmin            = "Admin";
    public const string RoleAccountManager   = "AccountManager";
    public const string RoleSalesManager     = "SalesManager";
    public const string RoleGeneralManager   = "GeneralManager";
    public const string RoleAccount          = "Account";

    // ─── Claims ──────────────────────────────────────────────
    public const string PermView   = "frm_CustomerCare:View";
    public const string PermExport = "frm_CustomerCare:Export";

    /// <summary>هل يقدر يفتح موديول خدمة العملاء؟</summary>
    public static bool CanView(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
        && (user.IsInRole(RoleCustomerService)
            || user.IsInRole(RoleAdmin)
            || user.IsInRole(RoleAccountManager)
            || user.IsInRole(RoleSalesManager)
            || user.IsInRole(RoleGeneralManager)
            || user.IsInRole(RoleAccount)
            || user.HasClaim("Permission", PermView));

    /// <summary>هل يقدر يسجّل/يحدّث نتيجة مكالمة؟</summary>
    public static bool CanLog(ClaimsPrincipal user) => CanView(user);

    /// <summary>هل يشوف كل السجل/الطابور ولا شغله بس؟
    /// (نمط الشكاوى: المدراء + الحسابات يرون الكل)</summary>
    public static bool CanViewAll(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.IsInRole(RoleSalesManager)
        || user.IsInRole(RoleGeneralManager)
        || user.IsInRole(RoleAccount);

    /// <summary>هل يقدر يصدّر Excel؟</summary>
    public static bool CanExport(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.IsInRole(RoleSalesManager)
        || user.IsInRole(RoleGeneralManager)
        || user.HasClaim("Permission", PermExport);
}
