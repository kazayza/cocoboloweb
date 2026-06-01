using System.Security.Claims;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// مرجع موحّد لصلاحيات شاشات التسليم — Single Source of Truth.
/// أي تعديل على الصلاحيات يحصل هنا فقط.
/// </summary>
public static class DeliveryPermissions
{
    // ─── أسماء الأدوار (متناسقة مع باقي المشروع) ─────────────
    private const string RoleAdmin           = "Admin";
    private const string RoleAccountManager  = "AccountManager";
    private const string RoleAccount         = "Account";
    private const string RoleSales           = "Sales";
    private const string RoleUser            = "User";   // المندوب

    // ─── الصلاحيات (Claims) ─────────────────────────────────
    public const string PermView     = "frmInvoiceDeliveryStatus:View";
    public const string PermEdit     = "frmInvoiceDeliveryStatus:Edit";
    public const string PermDeliver  = "frmInvoiceDeliveryStatus:Deliver";

    // ════════════════════════════════════════════════════════
    //                      السماحيات
    // ════════════════════════════════════════════════════════

    /// <summary>هل المستخدم يقدر يفتح الشاشة أصلاً؟</summary>
    public static bool CanView(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true &&
        (user.IsInRole(RoleAdmin)
         || user.IsInRole(RoleAccountManager)
         || user.IsInRole(RoleAccount)
         || user.IsInRole(RoleSales)
         || user.IsInRole(RoleUser)
         || user.HasClaim("Permission", PermView));

    /// <summary>هل يقدر يشوف المبالغ المالية (إجمالي / مدفوع / متبقي / أسعار)؟</summary>
    public static bool CanSeeAmounts(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.IsInRole(RoleAccount)
        || user.IsInRole(RoleSales);

    /// <summary>هل يقدر يعدّل المندوب والملاحظات والحالة الكاملة؟</summary>
    public static bool CanEditFull(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.IsInRole(RoleSales)
        || user.HasClaim("Permission", PermEdit);

    /// <summary>هل يقدر يأكد التسليم (زرار "تم التسليم" السريع)؟</summary>
    public static bool CanConfirmDelivery(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleSales)
        || user.IsInRole(RoleUser)
        || user.HasClaim("Permission", PermDeliver);

    /// <summary>هل يقدر يطبع إذن التسليم؟</summary>
    public static bool CanPrint(ClaimsPrincipal user) =>
        CanView(user);
}
