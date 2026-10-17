namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// أسماء عربي للأدوات وأكواد الأخطاء.
/// بتتستخدم في الشات (الأدوات اللي اتعملت) وفي صفحة الاستخدام، بدل أسماء الأدوات الإنجليزي.
/// </summary>
public static class ZakiLabels
{
    private static readonly Dictionary<string, string> ToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── المبيعات والفواتير ──
        ["get_sales_invoices_summary"] = "ملخص فواتير البيع",
        ["get_open_sales_invoices"] = "فواتير البيع المفتوحة",
        ["get_undelivered_sales_invoices"] = "فواتير لم تُسلَّم",
        ["get_largest_sales_invoices"] = "أكبر فواتير البيع",
        ["get_sales_by_employee"] = "المبيعات حسب الموظف",
        ["get_top_customers"] = "أعلى العملاء",
        ["get_top_products"] = "أعلى المنتجات",
        ["get_sales_breakdown"] = "تفصيل المبيعات",
        ["get_sales_invoice_details"] = "تفاصيل فاتورة بيع",
        ["get_customer_invoices"] = "فواتير عميل",
        ["get_open_quotations"] = "عروض الأسعار المفتوحة",

        // ── المشتريات والمدفوعات ──
        ["get_purchase_invoices_summary"] = "ملخص فواتير الشراء",
        ["get_open_purchase_invoices"] = "فواتير الشراء المفتوحة",
        ["get_purchase_invoice_details"] = "تفاصيل فاتورة شراء",
        ["get_payments_summary"] = "ملخص المدفوعات والمقبوضات",

        // ── الموافقات والتعديلات ──
        ["get_invoice_edit_history"] = "سجل تعديلات الفاتورة",
        ["get_pending_invoice_edit_requests"] = "طلبات تعديل الفواتير المعلقة",
        ["get_price_change_requests"] = "طلبات تغيير الأسعار",
        ["get_pending_delivery_permissions"] = "إذونات التسليم المعلقة",

        // ── المرتجعات والسلف والأرصدة ──
        ["get_returns_summary"] = "ملخص المرتجعات",
        ["get_customer_advances"] = "الدفعات المقدمة للعملاء",
        ["get_customer_balances"] = "أرصدة العملاء",
        ["get_supplier_balances"] = "أرصدة الموردين",

        // ── الخزينة والمالية ──
        ["get_income_statement"] = "قائمة الدخل",
        ["get_balance_sheet"] = "المركز المالي",
        ["get_branches_comparison"] = "مقارنة الفروع",
        ["get_cashflow_summary"] = "التدفق النقدي",
        ["get_cashbox_summary"] = "ملخص الخزائن",
        ["get_cashbox_transactions"] = "حركات الخزينة",
        ["get_expenses_summary"] = "ملخص المصروفات",
        ["get_personal_accounts"] = "الحسابات الشخصية",

        // ── أوامر التشغيل والمخزون ──
        ["get_job_orders"] = "أوامر التشغيل",
        ["get_product_stock"] = "رصيد المنتج",
        ["get_stock_by_warehouse"] = "المخزون حسب المخزن",
        ["get_stock_movements"] = "حركات المخزون",
        ["get_stock_value"] = "قيمة المخزون",
        ["get_low_stock"] = "المخزون المنخفض",

        // ── الموارد البشرية ──
        ["get_attendance_today"] = "حضور اليوم",
        ["get_employee_attendance"] = "حضور موظف",
        ["get_employees_overview"] = "نظرة عامة على الموظفين",
        ["get_exemptions"] = "الاستثناءات اليومية",
        ["get_my_schedule"] = "شيفتاتي",
        ["get_employee_loans"] = "سلف الموظفين",
        ["get_payroll_summary"] = "ملخص المرتبات",

        // ── المهام وإدارة العملاء (CRM) ──
        ["get_employee_tasks"] = "مهام موظف",
        ["get_open_tasks"] = "المهام المفتوحة",
        ["get_overdue_tasks"] = "المهام المتأخرة",
        ["get_crm_summary"] = "ملخص إدارة العملاء",
        ["get_open_opportunities"] = "الفرص المفتوحة",
        ["get_leads"] = "العملاء المحتملين",
        ["get_recent_interactions"] = "آخر التفاعلات",

        // ── خدمة العملاء ──
        ["get_open_complaints"] = "الشكاوى المفتوحة",
        ["get_customer_care_summary"] = "ملخص خدمة العملاء",
        ["get_customer_feedback"] = "تقييمات العملاء",

        // ── الإدارة والتقارير ──
        ["get_executive_summary"] = "الملخص التنفيذي",
        ["get_marketing_summary"] = "ملخص التسويق",
        ["get_employee_activity"] = "نشاط الموظفين",

        // ── النظام ──
        ["get_unread_notifications"] = "الإشعارات غير المقروءة",
        ["find_screen"] = "البحث عن الشاشات",

        // ── معلومات من خارج النظام ──
        ["get_world_news"] = "أخبار عامة",
        ["get_exchange_rate"] = "سعر الصرف",
    };

    private static readonly Dictionary<string, (string Label, string Hint)> ErrorNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RATE_LIMITED"] = ("حد الاستخدام في الدقيقة", "الموديل وصل للحد المسموح في الدقيقة. السؤال بيتعاد بعد شوية."),
        ["RATE_LIMITED_DAILY"] = ("حد الاستخدام اليومي للموديل", "خلص الحد اليومي للموديل. بيتجدد بعد 24 ساعة، أو لازم ترقية الخطة."),
        ["DAILY_LIMIT"] = ("وصل المستخدم للحد اليومي", "المستخدم سأل أكتر من الحد اليومي المسموح له."),
        ["DAILY_TOKEN_BUDGET"] = ("ميزانية اليوم خلصت", "ميزانية التوكنز اليومية للمساعد خلصت."),
        ["PROVIDER_ERROR"] = ("خطأ من خدمة الذكاء الاصطناعي", "الخدمة رجعت خطأ. لو اتكرر راجع الحد أو الإعدادات."),
        ["REQUEST_TOO_LARGE"] = ("السؤال أو البيانات أكبر من الحد", "الطلب أكبر من الحد المسموح للدقيقة. جرّب سؤال أضيق."),
        ["MAX_ROUNDS"] = ("السؤال احتاج أكتر من المسموح", "زكي طلب بيانات أكتر من عدد المحاولات المسموحة للسؤال الواحد."),
        ["TIMEOUT"] = ("انتهت مهلة الرد", "الرد اتأخر أكتر من المسموح."),
        ["NOT_CONFIGURED"] = ("الخدمة مش متظبطة", "مفتاح الذكاء الاصطناعي مش متضاف في الإعدادات."),
        ["AUTH_FAILED"] = ("مفتاح الذكاء الاصطناعي مرفوض", "الخدمة رفضت المفتاح. راجع الإعدادات."),
        ["UNKNOWN"] = ("غير معروف", "الخطأ مش متسجل بكود واضح."),
    };

    /// <summary>اسم الأداة بالعربي (أو «أداة أخرى» لو مش معروفة)</summary>
    public static string Tool(string? name) =>
        !string.IsNullOrEmpty(name) && ToolNames.TryGetValue(name, out var label) ? label : "أداة أخرى";

    /// <summary>اسم الخطأ بالعربي</summary>
    public static string Error(string? code) =>
        !string.IsNullOrEmpty(code) && ErrorNames.TryGetValue(code, out var e) ? e.Label : ErrorNames["UNKNOWN"].Label;

    /// <summary>شرح قصير للخطأ</summary>
    public static string ErrorHint(string? code) =>
        !string.IsNullOrEmpty(code) && ErrorNames.TryGetValue(code, out var e) ? e.Hint : ErrorNames["UNKNOWN"].Hint;
}
 