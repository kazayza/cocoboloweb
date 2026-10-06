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
    private const string RoleGeneralManager  = "GeneralManager"; // ⭐ مدير  العام
    private const string RoleSales           = "Sales";
    private const string RoleAccount         = "Account";
    private const string RoleUser            = "User";

    // ─── أسماء الـ Claims ────────────────────────────
    // ⭐ ملاحظة: النظام بيدعم 4 أفعال بس لكل شاشة (View/Add/Edit/Delete) — جدول UserPermission مفيهوش أعمدة غيرها.
    // فالإسناد والحل بيتحكم فيهم per-user عبر صلاحية «التعديل» (Edit) الموجودة فعلًا في شاشة الصلاحيات.
    public const string PermView      = "frm_Complaints_Main:View";
    public const string PermAdd       = "frm_Complaints_Main:Add";
    public const string PermEdit      = "frm_Complaints_Main:Edit";
    public const string PermDelete    = "frm_Complaints_Main:Delete";

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
        || user.IsInRole(RoleGeneralManager)
        || user.IsInRole(RoleSales)
        || user.HasClaim("Permission", PermAdd);

    /// <summary>هل يقدر يعدّل بيانات الشكوى الأساسية؟</summary>
    public static bool CanEdit(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.IsInRole(RoleSalesManager)
        || user.IsInRole(RoleGeneralManager)
        || user.HasClaim("Permission", PermEdit);

    /// <summary>هل يقدر يحذف شكوى؟</summary>
    public static bool CanDelete(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.HasClaim("Permission", PermDelete);

    /// <summary>⭐ هل يقدر يسند الشكوى لموظف؟ — الدايرة الإدارية + أي مستخدم معاه صلاحية «تعديل» الشكاوى (طلب المستخدم)</summary>
    public static bool CanAssign(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.IsInRole(RoleSalesManager)
        || user.IsInRole(RoleGeneralManager)
        || user.HasClaim("Permission", PermEdit);

    /// <summary>⭐ هل يقدر يغير الحالة (يحل/يرفض/يقفل)؟ — اللي بيتابع من حقه يحل (12-ح4/ب) + أصحاب صلاحية «تعديل» الشكاوى (طلب المستخدم)</summary>
    public static bool CanChangeStatus(ClaimsPrincipal user) =>
        CanAddFollowUp(user)   // Admin · AM · SM · GM · Sales · Account · أو إذن Add
        || CanEdit(user);      // + دايرة التعديل (بما فيها إذن frm_Complaints_Main:Edit per-user)

    /// <summary>⭐ هل يقدر يصعّد شكوى؟ — المدير العام + الأدمن + مدير الحسابات فقط (طلب المستخدم)</summary>
    public static bool CanEscalate(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.IsInRole(RoleGeneralManager);

    /// <summary>هل يقدر يضيف متابعة (Follow-up)؟</summary>
    public static bool CanAddFollowUp(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.IsInRole(RoleSalesManager)
        || user.IsInRole(RoleGeneralManager)
        || user.IsInRole(RoleSales)
        || user.IsInRole(RoleAccount)
        || user.HasClaim("Permission", PermAdd);

    /// <summary>هل يقدر يدير الأنواع (التصنيفات)؟</summary>
    public static bool CanManageTypes(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleSalesManager)
        || user.IsInRole(RoleGeneralManager);

    /// <summary>هل يقدر يصدّر Excel؟</summary>
    public static bool CanExport(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.IsInRole(RoleSalesManager)
        || user.IsInRole(RoleGeneralManager);

    /// <summary>هل يشوف كل الشكاوى ولا اللي عاملها بس؟</summary>
    public static bool CanViewAll(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.IsInRole(RoleSalesManager)
        || user.IsInRole(RoleGeneralManager)
        || user.IsInRole(RoleAccount);
}
