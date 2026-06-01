namespace COCOBOLOERPNEW.Services;

/// <summary>
/// مترجم أسماء الجداول من الإنجليزية للعربية
/// </summary>
public static class TableNameTranslator
{
    private static readonly Dictionary<string, string> _translations = new(StringComparer.OrdinalIgnoreCase)
    {
        // ─── المستخدمين والصلاحيات ───
        ["Users"]                = "المستخدمين",
        ["UserPermissions"]      = "صلاحيات المستخدمين",
        ["Permissions"]          = "الصلاحيات",
        ["Roles"]                = "الأدوار",

        // ─── العملاء والمبيعات ───
        ["Parties"]              = "العملاء/الموردين",
        ["Products"]             = "المنتجات",
        ["ProductGroups"]        = "مجموعات المنتجات",
        ["ProductImages"]        = "صور المنتجات",
        ["Transactions"]         = "الفواتير",
        ["TransactionDetails"]   = "تفاصيل الفواتير",
        ["Payments"]             = "المدفوعات",
        ["Quotations"]           = "عروض الأسعار",
        ["QuotationDetails"]     = "تفاصيل عروض الأسعار",
        ["AdditionalCharges"]    = "الرسوم الإضافية",

        // ─── الموظفين والـ HR ───
        ["Employees"]            = "الموظفين",
        ["EmployeeShifts"]       = "ورديات الموظفين",
        ["Attendances"]          = "الحضور والانصراف",
        ["Payrolls"]             = "الرواتب",
        ["PayrollItems"]         = "تفاصيل الرواتب",
        ["EmployeeLoans"]        = "سُلف الموظفين",
        ["LoanInstallments"]     = "أقساط السُلف",
        ["ShortPermissions"]     = "أذونات الانصراف",
        ["BiometricLogs"]        = "سجلات البصمة",

        // ─── CRM ───
        ["CRM_Tasks"]            = "مهام المتابعة",
        ["CustomerInteractions"] = "تواصلات العملاء",
        ["SalesOpportunities"]   = "فرص البيع",
        ["ContactTypes"]         = "أنواع التواصل",
        ["SalesStages"]          = "مراحل البيع",
        ["CustomerSources"]      = "مصادر العملاء",
        ["Interests"]            = "فئات الاهتمام",
        ["Commissions"]          = "العمولات",

        // ─── المالية والخزينة ───
        ["Expenses"]             = "المصروفات",
        ["ExpenseGroups"]        = "مجموعات المصروفات",
        ["CashBoxes"]            = "الخزن",
        ["CashboxTransactions"]  = "حركات الخزينة",
        ["PersonalAccounts"]     = "الحسابات الشخصية",
        ["PersonalAccountTransactions"] = "حركات الحسابات الشخصية",

        // ─── التسليمات والشكاوى ───
        ["Complaints"]           = "الشكاوى",
        ["ComplaintFollowUps"]   = "متابعات الشكاوى",
        ["ComplaintAttachments"] = "مرفقات الشكاوى",
        ["ComplaintTypes"]       = "أنواع الشكاوى",

        // ─── أخرى ───
        ["Warehouses"]           = "المخازن",
        ["Notifications"]        = "الإشعارات",
        ["Companies"]            = "بيانات الشركة",
        ["Audit_Log"]            = "سجل التدقيق",
    };

    /// <summary>إرجاع الاسم العربي إن وُجد، وإلا الاسم الأصلي</summary>
    public static string Translate(string? tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName)) return "—";
        return _translations.TryGetValue(tableName, out var ar) ? ar : tableName;
    }

    /// <summary>هل في ترجمة عربية للجدول ده؟</summary>
    public static bool HasTranslation(string? tableName) =>
        !string.IsNullOrWhiteSpace(tableName) && _translations.ContainsKey(tableName);
}