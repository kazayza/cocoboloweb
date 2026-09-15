using System.Security.Claims;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// مرجع موحّد لصلاحيات موديول B2B CRM (شاشات /crm/b2b/* المنفصلة).
/// ⭐ قرار المستخدم: المختص بملفات B2B هو مجموعة/دور جديد مخصوص (وليس CustomerService).
/// - موظف B2B المختص (الدور الجديد) → يرى/يدير «شغله فقط».
/// - المدراء: Admin / GeneralManager / AccountManager → يرون/يديرون كل شركات B2B.
/// - دور CustomerService (فيدباك خدمة العملاء) لا يدخل هذا الموديول إطلاقاً.
/// - باقي الأدوار (Sales وغيرهم) لا يدخلون أيضاً.
/// </summary>
public static class B2bCrmPermissions
{
    // ─── الأدوار ─────────────────────────────────────────────
    public const string RoleAdmin            = "Admin";
    public const string RoleAccountManager   = "AccountManager";
    public const string RoleGeneralManager   = "GeneralManager";

    /// <summary>
    /// ⚙️ اسم/أسماء دور «موظف B2B المختص» كما هي مسجّلة في جدول المستخدمين (User.Role).
    /// ✅ الاسم الفعلي عندك (حسب تأكيدك): "B2B".
    /// لو أضفت لاحقاً اسماً آخر في إدارة المستخدمين أضفه هنا أيضاً.
    /// </summary>
    public static readonly string[] SpecialistRoles = { "B2B" };

    /// <summary>هل المستخدم من مجموعة «موظف B2B المختص» (نطاقه: شغله فقط)؟</summary>
    public static bool IsB2bSpecialist(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
        && SpecialistRoles.Any(user.IsInRole);

    // ─── Claims ──────────────────────────────────────────────
    /// <summary>دخول الموديول (لمن يُمنح الصلاحية خارج الأدوار أعلاه).</summary>
    public const string PermView = "frm_B2BCrm:View";

    /// <summary>هل يقدر يفتح موديول B2B CRM أصلاً؟</summary>
    public static bool CanView(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
        && (user.IsInRole(RoleAdmin)
            || user.IsInRole(RoleAccountManager)
            || user.IsInRole(RoleGeneralManager)
            || SpecialistRoles.Any(user.IsInRole)
            || user.HasClaim(c => c.Type == "Permission" && c.Value == PermView));

    /// <summary>
    /// هل يرى كل شركات B2B (وليس شغله فقط)؟
    /// ⭐ حسب قرار العميل: Admin + GeneralManager + AccountManager فقط.
    /// </summary>
    public static bool CanViewAll(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
        && (user.IsInRole(RoleAdmin)
            || user.IsInRole(RoleAccountManager)
            || user.IsInRole(RoleGeneralManager));

    /// <summary>
    /// هل يقدر يدخل بيانات (إنشاء/تعديل/تحريك مراحل/إغلاق)؟
    /// نعم للمختص (الدور الجديد) وللمدراء — أما نطاق البيانات فيُحكم في طبقة الخدمة:
    /// المختص على شغله فقط (EmployeeId == الموظف الحالي)، والمدراء على الكل.
    /// ⚠️ حامل صلاحية frm_B2BCrm:View فقط (بدون دور) = مشاهدة بلا إدخال.
    /// </summary>
    public static bool CanManage(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
        && (SpecialistRoles.Any(user.IsInRole)
            || user.IsInRole(RoleAdmin)
            || user.IsInRole(RoleAccountManager)
            || user.IsInRole(RoleGeneralManager));
}
