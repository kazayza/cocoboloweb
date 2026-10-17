using System.Security.Claims;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// خريطة شاشات المشروع: مولّدة من Components/Shared/SidebarNav.razor.
/// لو اتغيرت القائمة الجانبية، أعيد توليد الملف (أو عدّل السطر المناسب).
/// الصلاحيات والأدوار هنا بتتفحص بنفس منطق القائمة: أي واحد منهم بيكفي.
/// </summary>
public static class ZakiScreenMap
{
    public sealed record Screen(string Section, string Title, string Route, string[] Permissions, string[] Roles);

    public static readonly Screen[] All =
    {
        new("عام", "الرئيسية", "/", new string[] { "frmDashboard:View" }, new string[] {  }),
        new("عام", "طلباتي (إذن / مأمورية)", "/my-exemptions", new string[] {  }, new string[] {  }),
        new("البيانات الأساسية", "بيانات الشركة", "/company-info", new string[] { "frm_CompanyInfo:View" }, new string[] {  }),
        new("المصنع", "تكاليف عروض الأسعار", "/quotations", new string[] {  }, new string[] { "FactoryManager", "ProductionManager", "factory" }),
        new("المبيعات", "المنتجات", "/products", new string[] { "frm_ProductList:View" }, new string[] {  }),
        new("المبيعات", "قائمة الأسعار", "/products/price-list", new string[] { "frm_PriceList:View" }, new string[] {  }),
        new("المبيعات", "عروض الاسعار", "/quotations", new string[] { "frmQuotationsList:View" }, new string[] {  }),
        new("المبيعات", "العملاء", "/customers", new string[] { "frm_PartiesList:View" }, new string[] {  }),
        new("المبيعات", "الفواتير", "/sales/invoices", new string[] { "frm_PartiesInvoices:View" }, new string[] {  }),
        new("المبيعات", "فواتير الشراء", "/factory/purchases", new string[] { "frm_SupplierInvoices:View" }, new string[] {  }),
        new("المبيعات", "أوامر التشغيل", "/production/job-orders", new string[] { "frm_JobOrders:View", "frm_PartiesInvoices:View" }, new string[] { "FactoryManager", "ProductionManager", "factory" }),
        new("المبيعات", "المدفوعات", "/payments", new string[] { "frm_Payments:View" }, new string[] {  }),
        new("المبيعات", "رسوم المعاينة", "/additional-charges", new string[] { "frmInspectionCharge:View" }, new string[] {  }),
        new("المبيعات", "متابعة التسليم", "/sales-delivery-status", new string[] { "frmInvoiceDeliveryStatus:View" }, new string[] {  }),
        new("المبيعات", "إدارة الشكاوى", "/complaints/list", new string[] { "frm_Complaints_Main:View" }, new string[] {  }),
        new("المبيعات", "طلبات تعديل الأسعار", "/sales/price-requests", new string[] { "frm_PriceRequests:View" }, new string[] {  }),
        new("المخزون", "المخازن", "/admin/warehouses", new string[] { "frm_Warehouses:View" }, new string[] { "Admin" }),
        new("المخزون", "إضافة رصيد للمخزن", "/inventory/stock-entry", new string[] { "frm_StockEntry:View", "frm_StockTransactions:View", "frm_StockTransfer:View" }, new string[] { "AccountManager", "Admin", "GeneralManager", "ProductionManager", "factory" }),
        new("المخزون", "تحويل مخزني", "/inventory/transfer", new string[] { "frm_StockEntry:View", "frm_StockTransactions:View", "frm_StockTransfer:View" }, new string[] { "AccountManager", "Admin", "GeneralManager", "ProductionManager", "factory" }),
        new("المخزون", "حركة المخزون", "/inventory/transactions", new string[] { "frm_StockEntry:View", "frm_StockTransactions:View", "frm_StockTransfer:View" }, new string[] { "AccountManager", "Admin", "GeneralManager", "ProductionManager", "factory" }),
        new("المخزون", "الجرد المخزني", "/inventory/count", new string[] { "frm_StockCount:View" }, new string[] { "Admin" }),
        new("المخزون", "قيمة المخزون", "/inventory/stock-value", new string[] {  }, new string[] { "AccountManager", "Admin" }),
        new("الخزينة والمالية", "لوحة الخزينة", "/cashbox/dashboard", new string[] { "frmCashBox:View", "frmCashBoxDashboard:View" }, new string[] {  }),
        new("الخزينة والمالية", "المصروفات", "/expenses", new string[] { "frm_ExpensesList:View" }, new string[] {  }),
        new("الخزينة والمالية", "مجموعات المصروفات", "/expense-groups", new string[] { "frm_ExpensesGroup:View" }, new string[] {  }),
        new("الخزينة والمالية", "إدارة الخزن", "/cashbox/manage", new string[] { "frmCashBox:View" }, new string[] {  }),
        new("الخزينة والمالية", "حركات الخزينة", "/cashbox/transactions", new string[] { "frmCashBoxTransactions:View", "frm_CashBoxTransaction:View" }, new string[] {  }),
        new("الخزينة والمالية", "حركة يدوية / سندات", "/cashbox/manual", new string[] { "frm_CashBoxManual:View" }, new string[] {  }),
        new("الخزينة والمالية", "الذمم (حسابات شخصية)", "/cashbox/personal-accounts", new string[] { "frmPersonalAccounts:View" }, new string[] {  }),
        new("الخزينة والمالية", "ملخص الخزن (تقرير)", "/cashbox/summary", new string[] { "frmCashBoxSummary:View", "frm_TotalInOutNetCashBox:View" }, new string[] {  }),
        new("الخزينة والمالية", "قائمة التدفقات النقدية", "/reports/cash-flow", new string[] { "frm_CashFlowNew:View" }, new string[] {  }),
        new("الموارد البشرية", "لوحة الموارد البشرية", "/hr/dashboard", new string[] { "frm_HR_Dashboard:View" }, new string[] {  }),
        new("الموارد البشرية", "كافة الموظفين", "/employees", new string[] { "frm_Employeeslist:View" }, new string[] {  }),
        new("الموارد البشرية", "تسجيل راتب", "/hr/payroll/processor", new string[] { "frm_Payroll:View" }, new string[] {  }),
        new("الموارد البشرية", "دفعات خارج الراتب", "/hr/payroll/bonuses", new string[] { "frm_PayrollPayment:View" }, new string[] {  }),
        new("الموارد البشرية", "سجل المرتبات", "/hr/payroll", new string[] { "frmPayrollProcessor:View" }, new string[] {  }),
        new("الموارد البشرية", "الحضور اليدوي", "/hr/payroll/attendance", new string[] { "frmPayrollReport:View" }, new string[] {  }),
        new("الموارد البشرية", "متابعة الشيفتات", "/shifts", new string[] { "frm_AllShifts:View" }, new string[] {  }),
        new("الموارد البشرية", "شيفتاتي", "/my-schedule", new string[] { "frm_MySchedule:View" }, new string[] {  }),
        new("الموارد البشرية", "بصمة الموظف", "/attendance", new string[] { "frm_empAttendance:View" }, new string[] {  }),
        new("الموارد البشرية", "الاستثناءات", "/hr/exemptions", new string[] { "frmDailyExemptions:View" }, new string[] {  }),
        new("الموارد البشرية", "سلف الموظفين", "/hr/loans", new string[] { "frmEmployeeLoans:View" }, new string[] {  }),
        new("CRM", "Leads", "/crm/leads", new string[] { "frm_LeadsCRM:View" }, new string[] {  }),
        new("CRM", "My Leads", "/crm/leads/my", new string[] { "frm_LeadsCRM:View" }, new string[] {  }),
        new("CRM", "مراحل البيع", "/crm/opportunities", new string[] { "frmSalesPipeline:View" }, new string[] {  }),
        new("CRM", "تواصل جديد", "/crm/opportunities/new", new string[] { "frmNewInteraction:View" }, new string[] {  }),
        new("CRM", "مهام المتابعة", "/crm/tasks", new string[] { "frmDailyTasks:View" }, new string[] {  }),
        new("CRM", "استرداد الفرص الخاسرة", "/crm/lost-recovery", new string[] { "frm_LostRecovery:View" }, new string[] { "Admin", "CustomerService", "GeneralManager", "SalesManager" }),
        new("CRM", "تقرير الاسترداد", "/crm/lost-recovery/report", new string[] { "frm_LostRecovery:View" }, new string[] { "Admin", "CustomerService", "GeneralManager", "SalesManager" }),
        new("CRM", "التكليفات العامة", "/crm/general-tasks", new string[] {  }, new string[] { "AccountManager", "Admin", "GeneralManager" }),
        new("CRM", "بطاقة العميل", "/crm/customers", new string[] { "frmClientCard:View" }, new string[] {  }),
        new("CRM", "شركات B2B", "/crm/b2b-companies", new string[] { "frm_B2BCrm:View" }, new string[] { "AccountManager", "Admin", "GeneralManager" }),
        new("CRM", "سجل التواصلات", "/crm/interactions", new string[] { "frmInteractionsLog:View" }, new string[] {  }),
        new("خدمة العملاء", "فيدباك العملاء", "/crm/customer-care", new string[] { "frm_CustomerCare:View" }, new string[] { "Account", "AccountManager", "Admin", "CustomerService", "GeneralManager", "SalesManager" }),
        new("خدمة العملاء", "السجل والتقرير", "/crm/customer-care/history", new string[] { "frm_CustomerCare:View" }, new string[] { "Account", "AccountManager", "Admin", "CustomerService", "GeneralManager", "SalesManager" }),
        new("B2B", "لوحة B2B", "/admin/b2b/dashboard", new string[] { "frm_CustomerCare:View" }, new string[] { "Account", "AccountManager", "Admin", "CustomerService", "GeneralManager", "SalesManager" }),
        new("B2B", "مستخدمي البوابة", "/admin/b2b/portal-users", new string[] { "frm_CustomerCare:View" }, new string[] { "Account", "AccountManager", "Admin", "CustomerService", "GeneralManager", "SalesManager" }),
        new("B2B", "طلبات B2B", "/admin/b2b/requests", new string[] { "frm_CustomerCare:View" }, new string[] { "Account", "AccountManager", "Admin", "CustomerService", "GeneralManager", "SalesManager" }),
        new("التقارير", "لوحة القيادة التنفيذية", "/executive-cockpit", new string[] { "frm_ExecutiveCockpit:View" }, new string[] { "Admin" }),
        new("التقارير", "CRM Reports", "/crm/reports", new string[] { "frmCRM_Dashboard:View" }, new string[] {  }),
        new("التقارير", "Leads Dashboard", "/crm/leads/dashboard", new string[] { "frmleadsdashboard:View" }, new string[] {  }),
        new("التقارير", "Marketing Dashboard", "/reports/marketing", new string[] { "frmleadsdashboard:View" }, new string[] {  }),
        new("التقارير", "تحليلات المبيعات", "/reports/sales-analytics", new string[] { "rpt_SalesAnalytics:View" }, new string[] {  }),
        new("التقارير", "مؤشرات الخزينة", "/cashbox/dashboard", new string[] { "frm_CashBoxDashboard:View" }, new string[] {  }),
        new("التقارير", "مؤشر المصروفات", "/expenses/dashboard", new string[] { "frmExpensesDashboard:View" }, new string[] {  }),
        new("التقارير", "قائمة الدخل", "/reports/income-statement", new string[] { "rpt_IncomeStatement:View" }, new string[] {  }),
        new("التقارير", "قائمة المركز المالي", "/reports/balance-sheet", new string[] { "rpt_IncomeStatement:View" }, new string[] {  }),
        new("التقارير", "تقرير المصروفات", "/expenses/report", new string[] { "rptExpensesReport:View" }, new string[] {  }),
        new("التقارير", "بصمة الحضور", "/attendance/logs", new string[] { "rpt_empAttendance:View" }, new string[] {  }),
        new("التقارير", "تقرير الحضور الشهري", "/hr/reports/attendance", new string[] { "rpt_AttendanceReport:View" }, new string[] {  }),
        new("التقارير", "كشف الرواتب", "/hr/reports/payroll", new string[] { "frmPayrollReport:View" }, new string[] {  }),
        new("إدارة النظام", "المستخدمين", "/admin/users", new string[] { "frm_AllUsers:View" }, new string[] {  }),
        new("إدارة النظام", "الصلاحيات", "/admin/permissions", new string[] { "frm_AddPermission:View" }, new string[] {  }),
        new("إدارة النظام", "استخدام زكي", "/assistant/usage", new string[] {  }, new string[] { "Admin" }),
        new("إدارة النظام", "اختبار زكي", "/assistant/golden-test", new string[] {  }, new string[] { "Admin" }),
        new("إدارة النظام", "ربط الصلاحيات", "/admin/permissions/manage", new string[] { "frmManagePermissions:View" }, new string[] {  }),
        new("إدارة النظام", "الإشعارات", "/notifications", new string[] { "frm_NotificationsAll:View" }, new string[] {  }),
        new("إدارة النظام", "تغيير كلمة السر", "/account/change-password", new string[] { "frm_ChangePassword:View" }, new string[] {  }),
        new("إدارة النظام", "متابعة النظام", "/admin/audit", new string[] { "frm_AuditViewer:View" }, new string[] {  }),
        new("إدارة النظام", "إدارة تسعير المنتجات", "/admin/pricing-margins", new string[] { "frm_PricingMargins:View" }, new string[] {  }),
    };

    /// <summary>هل المستخدم يشوف الشاشة؟ (Admin يشوف الكل، والشاشة بدون قيود مفتوحة للجميع)</summary>
    public static bool CanSee(Screen screen, ClaimsPrincipal user)
    {
        if (user.IsInRole("Admin")) return true;
        if (screen.Permissions.Length == 0 && screen.Roles.Length == 0) return true;

        foreach (var p in screen.Permissions)
        {
            if (user.HasClaim("Permission", p)) return true;
        }
        foreach (var r in screen.Roles)
        {
            if (user.IsInRole(r)) return true;
        }
        return false;
    }
}
