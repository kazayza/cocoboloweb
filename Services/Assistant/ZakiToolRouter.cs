using COCOBOLOERPNEW.Services;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// موجّه الأدوات: بيبعت للموديل الأدوات المرتبطة بموضوع السؤال بس، بدل كل الأدوات في كل طلب.
/// ده بيقلل التوكنز بشكل كبير (الحد المجاني لـ Groq 8000 توكن/دقيقة).
/// - الأدوات بتفضل محكومة بالصلاحيات: الموجّه بيشتغل على قايمة الأدوات المسموحة للمستخدم أصلاً.
/// - لو السؤال ماطابقش أي موضوع، بنبعت كل الأدوات المسموحة (سؤال عام أو غامض).
/// - أي أداة مش متصنفة هنا بتفضل متاحة دايماً (أمان).
/// </summary>
public static class ZakiToolRouter
{
    private sealed record Group(string Name, string[] Keywords, string[] Tools);

    /// <summary>أدوات دايماً متاحة (بتتسأل في أي وقت)</summary>
    private static readonly string[] AlwaysTools =
    {
        ZakiKnowledgeService.FindScreen,
        ZakiNotificationService.GetUnreadNotifications
    };

    private static readonly Group[] Groups =
    {
        new("sales", new[]
            {
                "فاتور", "مبيع", "بيع", "تحصيل", "متبقي", "متاخر", "تسليم", "مرتجع", "عميل", "عملاء",
                "منتج", "صنف", "اصناف", "بائع", "عرض سعر", "عروض", "اسعار", "سعر"
            },
            new[]
            {
                "get_sales_invoices_summary", "get_open_sales_invoices", "get_undelivered_sales_invoices",
                "get_largest_sales_invoices", "get_sales_by_employee", "get_top_customers", "get_top_products",
                "get_sales_breakdown", "get_sales_invoice_details", "get_customer_invoices",
                "get_open_quotations"
            }),

        new("financial_statements", new[]
            {
                "قائمه الدخل", "قائمة الدخل", "دخل", "ربح", "ارباح", "أرباح", "خساره", "خسارة", "خسائر",
                "مركز مالي", "مركز", "ميزانيه", "ميزانية", "اصول", "أصول", "خصوم", "التزامات", "فرع", "فروع",
                "الفرع", "هامش", "صافي", "مجمل", "مصروفات", "مصاريف"
            },
            new[]
            {
                "get_income_statement", "get_balance_sheet", "get_branches_comparison"
            }),

        new("approvals", new[]
            {
                "موافق", "اعتماد", "معلق", "معلقه", "طلب تعديل", "طلبات", "تعديل فاتورة", "سجل التعديلات",
                "تعديلات", "تعديل سعر", "اسعار", "خصم", "إذن تسليم", "اذن تسليم", "طلب"
            },
            new[]
            {
                "get_invoice_edit_history", "get_pending_invoice_edit_requests", "get_price_change_requests",
                "get_pending_delivery_permissions"
            }),

        new("receivables", new[]
            {
                "مديون", "مديونيه", "مديونية", "رصيد", "مستحق", "متبقي", "متاخر", "سداد", "تحصيل", "عليه فلوس"
            },
            new[]
            {
                "get_customer_balances", "get_supplier_balances"
            }),

        new("returns", new[]
            {
                "مرتجع", "مرتجعات", "سلف", "سلفه", "سلفة", "دفعه مقدمه", "دفعة مقدمة", "مقدم"
            },
            new[]
            {
                "get_returns_summary", "get_customer_advances"
            }),

        new("finance", new[]
            {
                "شراء", "مشتري", "مورد", "موردين", "مصروف", "مصاريف", "خزين", "كاش", "تدفق", "حساب شخصي",
                "حسابات شخصية", "مدفوع", "مدفوعات", "مالي", "مالية", "ايراد", "ايرادات", "رصيد", "مستحق",
                "مصروفات"
            },
            new[]
            {
                "get_purchase_invoices_summary", "get_open_purchase_invoices", "get_purchase_invoice_details",
                "get_payments_summary", "get_supplier_balances", "get_expenses_summary", "get_cashflow_summary",
                "get_personal_accounts", "get_cashbox_summary", "get_cashbox_transactions"
            }),

        new("production", new[]
            {
                "تشغيل", "تصنيع", "انتاج", "مصنع", "ورشه", "ورشة", "اوامر", "أوامر", "امر شغل", "أمر شغل",
                "اوردر", "أوردر", "مواعيد التسليم", "موعد التسليم"
            },
            new[]
            {
                "get_job_orders"
            }),

        new("general", new[]
            {
                "خبر", "اخبار", "أخبار", "عالم", "العالم", "عالمي", "دولار", "يورو", "استرليني", "إسترليني",
                "ريال", "درهم", "سعر الصرف", "عملة", "عملات", "سعر الدولار", "الدولار", "الدولار الامريكي", "الجنيه مقابل", "كام الدولار", "سعر اليورو", "الاسترليني"
            },
            new[]
            {
                "get_world_news", "get_exchange_rate"
            }),

        new("crm", new[]
            {
                "فرص", "فرصه", "فرصة", "مهام", "مهمه", "مهمة", "متابعه", "متابعة", "تواصل", "ليد", "ليدز",
                "lead", "crm", "عميل محتمل", "مراحل", "تكليف", "تكليفات", "مصدر", "مصادر", "فورم", "فورمات", "حمله", "حملة", "حملات"
            },
            new[]
            {
                "get_open_opportunities", "get_open_tasks", "get_overdue_tasks", "get_crm_summary",
                "get_recent_interactions", "get_leads", "get_employee_tasks"
            }),

        new("inventory", new[]
            {
                "مخزن", "مخازن", "مخزون", "جرد", "نافد", "خلص", "خلاص", "حركه", "حركات", "كميه", "stock",
                "مستودع", "صنف"
            },
            new[]
            {
                "get_product_stock", "get_stock_movements", "get_stock_value", "get_low_stock",
                "get_stock_by_warehouse"
            }),

        new("hr", new[]
            {
                "موظف", "موظفين", "حضور", "غياب", "غايب", "تأخير", "تاخير", "اجازه", "اجازة", "استثناء",
                "اذن", "إذن", "مأمورية", "شيفت", "شيفتات", "مرتب", "مرتبات", "راتب", "بصمه", "بصمة",
                "انصراف", "الموارد البشرية", "hr"
            },
            new[]
            {
                "get_attendance_today", "get_employee_attendance", "get_employees_overview", "get_exemptions",
                "get_my_schedule", "get_employee_loans", "get_payroll_summary"
            }),

        new("care", new[]
            {
                "شكوى", "شكوي", "شكاوي", "شكاوى", "خدمه العملاء", "خدمة العملاء", "فيدباك", "تقييم", "رضا",
                "زياره", "زيارة"
            },
            new[]
            {
                "get_open_complaints", "get_customer_care_summary", "get_customer_feedback"
            }),

        new("management", new[]
            {
                "تسويق", "حمله", "حملة", "حملات", "تنفيذي", "مؤشر", "مؤشرات", "تقرير", "تقارير", "اداء",
                "أداء", "نشاط", "لوحه", "لوحة", "dashboard", "kpi", "ملخص"
            },
            new[]
            {
                "get_marketing_summary", "get_executive_summary", "get_employee_activity"
            }),
    };

    /// <summary>بيختار الأدوات المناسبة للسؤال من قايمة الأدوات المسموحة للمستخدم</summary>
    public static IReadOnlyList<AiToolDefinition> Select(IReadOnlyList<AiToolDefinition> allowed, string question)
    {
        if (allowed.Count == 0) return allowed;

        var q = Normalize(question);
        var wanted = new HashSet<string>(AlwaysTools);
        var matchedAny = false;

        foreach (var g in Groups)
        {
            if (g.Keywords.Any(k => q.Contains(Normalize(k))))
            {
                matchedAny = true;
                foreach (var t in g.Tools) wanted.Add(t);
            }
        }

        // سؤال عام أو غامض: أدوات الملخص بس (مش كل الأدوات) عشان التوكنز
        if (!matchedAny)
        {
            var summaryTools = new HashSet<string>(AlwaysTools.Concat(
                Groups.Single(g => g.Name == "management").Tools));
            return allowed.Where(d => summaryTools.Contains(d.Name)).ToList();
        }

        var classified = new HashSet<string>(Groups.SelectMany(g => g.Tools).Concat(AlwaysTools));

        return allowed
            .Where(d => wanted.Contains(d.Name) || !classified.Contains(d.Name))
            .ToList();
    }

    private static string Normalize(string text) =>
        text.NormalizeArabic().ToLowerInvariant();
}
