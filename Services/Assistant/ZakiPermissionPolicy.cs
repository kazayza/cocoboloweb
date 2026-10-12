using System.Security.Claims;
using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// السياسة المركزية لصلاحيات زكي (P2).
///
/// المبدأ:
/// 1) القرارات الحساسة (مين يشوف مبالغ/مديونيات/خزينة/تنفيذي) كلها هنا في مكان واحد —
///    أي أداة جديدة لازم تسأل السياسة دي بدل ما تخترع فحص خاص.
/// 2) الأدوات القديمة فحصاتها متسقة مع شاشات ERP (frm_*:View) ومتغطية باختبارات
///    الأدوار في الـ golden set — مش هنعيد كتابتها (شغالة ومُختبرة).
/// 3) التدقيق «حي»: قائمة أدوات كل دور بتتجمع من DefinitionsFor الفعلية
///    (ZakiAssistantService.ToolsFor) — مستحيل تبان قديمة.
/// </summary>
public static class ZakiPermissionPolicy
{
    /// <summary>الأدوار القياسية في النظام (SystemRoles) — للتدقيق واختبارات الأدوار</summary>
    public static readonly string[] StandardRoles =
    {
        SystemRoles.Admin,
        SystemRoles.GeneralManager,
        SystemRoles.AccountManager,
        SystemRoles.SalesManager,
        SystemRoles.Sales,
        SystemRoles.HrManager,
        SystemRoles.Account,
        SystemRoles.SocialManager,
        SystemRoles.ProductionManager,
        SystemRoles.FactoryManager
    };

    /// <summary>
    /// Principal اصطناعي لاختبارات الأدوار والتدقيق — دور واحد من غير صلاحيات شاشات.
    /// (الاسم golden-test مش مستخدم حقيقي، والاختبارات بتتعلم في اللوج.)
    /// </summary>
    public static ClaimsPrincipal MakePrincipal(string role, params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "golden-test"),
            new(ClaimTypes.Role, role)
        };
        claims.AddRange(permissions.Select(p => new Claim("Permission", p)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "GoldenTest"));
    }

    // ── القرارات المركزية (نفس دلالات الفحوصات القائمة — مصدر واحد للمستقبل) ──

    /// <summary>مبالغ وأرقام المبيعات</summary>
    public static bool CanViewSalesAmounts(ClaimsPrincipal user) =>
        ZakiInvoiceQueries.CanViewSalesInvoices(user);

    /// <summary>تكلفة المشتريات وأسعار الموردين</summary>
    public static bool CanViewPurchaseCosts(ClaimsPrincipal user) =>
        ZakiInvoiceQueries.CanViewPurchaseInvoices(user);

    /// <summary>مديونيات العملاء</summary>
    public static bool CanViewCustomerDebts(ClaimsPrincipal user) =>
        ZakiInvoiceQueries.CanViewReceipts(user);

    /// <summary>أرصدة الموردين</summary>
    public static bool CanViewSupplierBalances(ClaimsPrincipal user) =>
        ZakiInvoiceQueries.CanViewPurchasePayments(user);

    /// <summary>الخزينة (ملخص)</summary>
    public static bool CanViewCashbox(ClaimsPrincipal user) =>
        ZakiCashboxService.CanViewSummary(user);

    /// <summary>المخزون وأرصدته</summary>
    public static bool CanViewStock(ClaimsPrincipal user) =>
        ZakiStockAlertsService.CanView(user);

    /// <summary>المؤشرات التنفيذية (نفس ExecutiveRoles في ZakiToolExecutor)</summary>
    public static bool CanViewExecutive(ClaimsPrincipal user) =>
        user.IsInRole(SystemRoles.Admin) || user.IsInRole(SystemRoles.AccountManager);
}
