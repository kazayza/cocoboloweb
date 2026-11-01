using System.Security.Claims;
using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// F-02: مصدر واحد لقواعد التفويض المستخدمة في سياسات الـ endpoints
/// (Program.cs) وفي حرّاس طبقة الخدمات معًا — حتى لا تنحرف القاعدتان عن بعضهما.
/// القواعد تعكس بوابات الواجهة الحالية:
///  - مستخدم داخلي: مُصادَق وليس مستخدم بوابة B2B (نفس كوكي المصادقة، دور B2BPortal).
///  - كتابات الشركة: داخلي + (Admin أو صلاحية frm_CompanyInfo حسب نوع العملية:
///    Add للإضافة، Edit للتعديل/الشعار، Delete للحذف) — مطابقة لبوابات واجهة صفحة
///    بيانات الشركة نفسها؛ ورابط الصفحة في القائمة يظهر بـ frm_CompanyInfo:View.
///  - تصدير عروض الأسعار: داخلي + (Admin أو frmQuotationsList:View) وليس مشاهد
///    تكلفة مقيّدًا (ProductionManager / factory) — مطابق لشرط إظهار أزرار التصدير
///    في قائمة العروض (! _isCostOnlyViewer)؛ الـ Admin مستثنى من قيد التكلفة.
/// </summary>
public static class AppAuthorization
{
    public const string InternalUserPolicy = "InternalUser";
    public const string PortalUserPolicy = "PortalUser";
    public const string CompanyAddPolicy = "CompanyAdd";
    public const string CompanyEditPolicy = "CompanyEdit";
    public const string CompanyDeletePolicy = "CompanyDelete";
    public const string QuotationExportPolicy = "QuotationExport";

    public static bool IsInternalUser(ClaimsPrincipal? user)
        => user?.Identity?.IsAuthenticated == true && !B2BPermissions.IsPortalUser(user);

    /// <summary>مستخدم بوابة B2B: مُصادَق ويحمل دور البوابة (نفس قاعدة B2BPermissions).</summary>
    public static bool IsPortalUser(ClaimsPrincipal? user)
        => user?.Identity?.IsAuthenticated == true && B2BPermissions.IsPortalUser(user!);

    /// <summary>كتابة بيانات الشركة/الفروع حسب نوع العملية (Add/Edit/Delete).</summary>
    public static bool CanWriteCompany(ClaimsPrincipal? user, string action)
        => IsInternalUser(user)
           && (user!.IsInRole(SystemRoles.Admin)
               || user.HasClaim("Permission", $"frm_CompanyInfo:{action}"));

    public static bool IsRestrictedCostViewer(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) return false;
        if (user.IsInRole(SystemRoles.Admin)) return false;
        return user.IsInRole(SystemRoles.ProductionManager) || user.IsInRole("factory");
    }

    public static bool CanExportQuotations(ClaimsPrincipal? user)
        => IsInternalUser(user)
           && (user!.IsInRole(SystemRoles.Admin)
               || user.HasClaim("Permission", "frmQuotationsList:View"))
           && !IsRestrictedCostViewer(user);

    // ── F-09 (Part B/2): حرّاس طبقة الخدمات على عمليات الكتابة ──────────────
    // قاعدة موحّدة لكل الخدمات: مستخدم داخلي (مُصادَق وليس بوابة B2B)،
    // والـ Admin يتجاوز دائمًا، ثم أيٌّ من صلاحيات Permission أو الأدوار
    // التي تفتح بها صفحات الوحدة نفسها إجراءَ الكتابة المطلوب.

    /// <summary>داخلي + (Admin أو أي صلاحية من القائمة أو أي دور من القائمة).</summary>
    public static bool HasClaimOrRole(ClaimsPrincipal? user, string[] claims, string[] roles)
    {
        if (!IsInternalUser(user)) return false;
        if (user!.IsInRole(SystemRoles.Admin)) return true;
        foreach (var claim in claims)
            if (user.HasClaim("Permission", claim)) return true;
        foreach (var role in roles)
            if (user.IsInRole(role)) return true;
        return false;
    }

    /// <summary>داخلي + (Admin أو صلاحية Permission واحدة محددة).</summary>
    public static bool HasClaim(ClaimsPrincipal? user, string claim)
        => HasClaimOrRole(user, new[] { claim }, Array.Empty<string>());
}
