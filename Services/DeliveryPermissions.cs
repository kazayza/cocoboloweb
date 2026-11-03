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
    private const string RoleGeneralManager  = "GeneralManager";
    private const string RoleDeliveryOfficer = "DeliveryOfficer";   // مسئول التسليمات
    private const string RoleProductionManager = "ProductionManager";
    private const string RoleFactoryManager    = "FactoryManager";

    // ─── الصلاحيات (Claims) ─────────────────────────────────
    public const string PermView     = "frmInvoiceDeliveryStatus:View";
    public const string PermEdit     = "frmInvoiceDeliveryStatus:Edit";
    public const string PermDeliver  = "frmInvoiceDeliveryStatus:Deliver";

    // ─── صيانة التسليمات (القسم 11.5/11.6) ──────────────────
    public const string PermMaintenanceView = "frmDeliveryMaintenance:View";
    public const string PermMaintenanceEdit = "frmDeliveryMaintenance:Edit";

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
         || user.IsInRole(RoleDeliveryOfficer)
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
        || user.IsInRole(RoleDeliveryOfficer)
        || user.HasClaim("Permission", PermDeliver);

    /// <summary>
    /// هل يرى تاريخ الاستحقاق الحقيقي المتفق عليه مع العميل؟
    /// من عداه يرى التاريخ منقوصًا هامش الأيام (Delivery:DateBufferDays) كضغط تنفيذي —
    /// الإخفاء في طبقة خدمة التسليمات عند العرض فقط، ولا يُكتب أي تاريخ مخفي في قاعدة البيانات.
    /// </summary>
    public static bool CanSeeRealDeliveryDate(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.IsInRole(RoleSales)
        || user.IsInRole(RoleGeneralManager)
        || user.IsInRole(RoleDeliveryOfficer);

    /// <summary>هل يقدر يطبع إذن التسليم؟</summary>
    public static bool CanPrint(ClaimsPrincipal user) =>
        CanView(user);

    /// <summary>
    /// الموافقة على تسليم فاتورة عليها مبالغ متبقية (أو منح إذن التسليم للمندوب) — المدير ومدير الحسابات فقط.
    /// </summary>
    public static bool CanOverrideRemaining(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin) || user.IsInRole(RoleAccountManager);

    // ════════════════════════════════════════════════════════
    //        طلبات تعديل مواعيد التسليم (تعديل/تقسيم)
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// طلب تعديل مواعيد تسليم فاتورة (تأجيل/تقسيم على دفعات): مسئول التسليمات
    /// ومدير الإنتاج — من يعرفان الحقيقة التشغيلية للمواعيد. التعديل نفسه لا يتم
    /// إلا باعتماد (CanDecideScheduleChange) الذي يطبّق القيم النهائية.
    /// </summary>
    public static bool CanRequestScheduleChange(ClaimsPrincipal user) =>
        user.IsInRole(RoleDeliveryOfficer) || user.IsInRole(RoleProductionManager);

    /// <summary>
    /// البتّ في طلبات تعديل المواعيد (اعتماد يطبّق المواعيد النهائية / رفض بسبب) —
    /// نفس ثنائي CanOverrideRemaining: المدير ومدير الحسابات فقط.
    /// </summary>
    public static bool CanDecideScheduleChange(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin) || user.IsInRole(RoleGeneralManager) || user.IsInRole(RoleAccountManager);

    // ════════════════════════════════════════════════════════
    //        صيانة التسليمات — حلقة المرتجعات (القسم 11.6)
    // ════════════════════════════════════════════════════════

    /// <summary>عرض شاشة صيانة التسليمات: إدارة/حسابات/إنتاج/مصنع/مسئول تسليمات أو Claim العرض.</summary>
    public static bool CanViewMaintenance(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true &&
        (user.IsInRole(RoleAdmin)
         || user.IsInRole(RoleAccountManager)
         || user.IsInRole(RoleProductionManager)
         || user.IsInRole(RoleFactoryManager)
         || user.IsInRole(RoleDeliveryOfficer)
         || user.HasClaim("Permission", PermMaintenanceView));

    /// <summary>انتقالات حالات الصيانة (بدء/جاهزية/تعذّر): مصنع/إنتاج/مدير أو Claim التعديل.</summary>
    public static bool CanTransitionMaintenance(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleProductionManager)
        || user.IsInRole(RoleFactoryManager)
        || user.HasClaim("Permission", PermMaintenanceEdit);

    /// <summary>إغلاق باستبدال: أدوار الانتقالات + مدير الحسابات.</summary>
    public static bool CanCloseMaintenanceByReplacement(ClaimsPrincipal user) =>
        CanTransitionMaintenance(user) || user.IsInRole(RoleAccountManager);

    /// <summary>إغلاق بردّ نهائي/رصيد — قرار مالي الأثر: المدير ومدير الحسابات فقط (قرار المالك D11).</summary>
    public static bool CanCloseMaintenanceByCredit(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin) || user.IsInRole(RoleAccountManager);

    /// <summary>إعادة تصنيف سبب الرجوع (قرار المالك D12): مدير/مدير حسابات/مدير إنتاج — بسبب إلزامي وبتدقيق.</summary>
    public static bool CanReclassifyMaintenance(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.IsInRole(RoleProductionManager);

    /// <summary>
    /// رؤية التكلفة الداخلية للإصلاح: إدارة/حسابات/إنتاج/مصنع فقط —
    /// لا تُعرض للمناديب ولا للمبيعات أبدًا (تكلفة داخلية — القسم 11.6).
    /// </summary>
    public static bool CanSeeRepairCost(ClaimsPrincipal user) =>
        user.IsInRole(RoleAdmin)
        || user.IsInRole(RoleAccountManager)
        || user.IsInRole(RoleProductionManager)
        || user.IsInRole(RoleFactoryManager);
}
