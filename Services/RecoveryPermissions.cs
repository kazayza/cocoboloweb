using System.Security.Claims;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// مرجع موحّد لصلاحيات استرداد الفرص الخاسرة (Single Source of Truth).
/// الأدوار المصرّح بها أصلاً: Admin / GeneralManager / SalesManager.
/// أي موظف (خصوصًا خدمة العملاء) يحصل على الصلاحية عبر شاشة الصلاحيات.
/// </summary>
public static class RecoveryPermissions
{
    // ─── الأدوار ─────────────────────────────────────
    private const string RoleAdmin          = "Admin";
    private const string RoleGeneralManager = "GeneralManager";
    private const string RoleSalesManager   = "SalesManager";

    // ─── أسماء الـ Claims ────────────────────────────
    public const string PermView        = "frm_LostRecovery:View";
    public const string PermRevive      = "frm_LostRecovery:Revive";
    public const string PermLogContact  = "frm_LostRecovery:LogContact";

    // ═══════════════════════════════════════════════
    //                    السماحيات
    // ═══════════════════════════════════════════════

    /// <summary>هل يقدر يفتح شاشة استرداد الفرص الخاسرة؟</summary>
    public static bool CanView(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleGeneralManager)
        || user.IsInRole(RoleSalesManager)
        || user.HasClaim(c => c.Type == "Permission" && c.Value == PermView);

    /// <summary>
    /// هل يقدر ينفّذ "العميل راجع" (إعادة فتح/إنشاء فرصة)؟
    /// ⭐ صلاحية Revive مستقلة عن View: تُمنح لمن يُراد له فعلًا تنفيذ الاسترداد
    /// (لا يكفي امتلاك frm_LostRecovery:View). الإجراء مسؤولية موثقة بالـ Audit.
    /// </summary>
    public static bool CanRevive(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleGeneralManager)
        || user.IsInRole(RoleSalesManager)
        || user.HasClaim(c => c.Type == "Permission" && c.Value == PermRevive);

    /// <summary>
    /// هل يقدر يسجّل محاولات تواصل مع العميل؟
    /// ⭐ صلاحية LogContact مستقلة عن Revive: تُمنح لخدمة العملاء لتسجيل التواصلات
    /// (لا يكفي امتلاك frm_LostRecovery:View). الإجراء موثق بالـ Audit.
    /// </summary>
    public static bool CanLogContact(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleGeneralManager)
        || user.IsInRole(RoleSalesManager)
        || user.IsInRole("CustomerService")
        || user.HasClaim(c => c.Type == "Permission" && c.Value == PermLogContact);
}
