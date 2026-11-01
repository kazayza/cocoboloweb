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
                "شراء", "مشتري", "مورد", "موردين", "مصروف", "مصاريف", "خزين", "خزنه", "خزن", "خزائن",
                "صندوق", "فلوس", "اقفال", "قفل", "اول اليوم", "اخر اليوم", "بداية اليوم", "نهاية اليوم",
                "كاش", "تدفق", "حساب شخصي",
                "حسابات شخصية", "مدفوع", "مدفوعات", "مالي", "مالية", "ايراد", "ايرادات", "رصيد", "مستحق",
                "مصروفات"
            },
            new[]
            {
                "get_purchase_invoices_summary", "get_open_purchase_invoices", "get_purchase_invoice_details",
                "get_payments_summary", "get_supplier_balances", "get_expenses_summary", "get_cashflow_summary",
                "get_personal_accounts", "get_cashbox_summary", "get_cashbox_transactions",
                "get_cashbox_day_closing"
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

        new("deliveries", new[]
            {
                "تسليم", "تسليمات", "اتسلم", "اتسلمت", "اتسلّم", "لسه ماتسلم", "مندوب", "مناديب",
                "مسؤول التسليم", "مسئول التسليم", "موعد التسليم", "مواعيد التسليم", "كشف التسليم",
                "اذن التسليم", "إذن التسليم", "حالة التسليم", "جدول التسليمات", "متاخرة التسليم", "متأخرة التسليم",
                "الالتزام بالمواعيد", "التزام بالمواعيد", "اداء التسليم", "أداء التسليم", "نسبة التسليم",
                "التسليم في الموعد", "في موعدها", "في الموعد"
            },
            new[]
            {
                "get_delivery_status", "get_deliveries_schedule", "get_overdue_deliveries",
                "get_delivery_loads", "get_today_deliveries_summary", "get_delivery_performance"
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
                "get_recent_interactions", "get_leads", "get_employee_tasks",
                "get_my_tasks", "get_my_created_tasks", "get_task_thread", "get_team_overdue_tasks"
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
        var matchedNames = new List<string>();

        foreach (var g in Groups)
        {
            if (g.Keywords.Any(k => q.Contains(Normalize(k))))
                matchedNames.Add(g.Name);
        }

        // سؤال عام أو غامض: أدوات الملخص بس (مش كل الأدوات) عشان التوكنز
        if (matchedNames.Count == 0)
        {
            var summaryTools = new HashSet<string>(AlwaysTools.Concat(
                Groups.Single(g => g.Name == "management").Tools));
            return allowed.Where(d => summaryTools.Contains(d.Name)).ToList();
        }

        return SelectForGroups(allowed, matchedNames);
    }

    /// <summary>
    /// أدوات مجموعات بعينها + الأدوات الدائمة + أي أداة مش مصنفة في أي مجموعة (أمان).
    /// نفس فلترة Select بالظبط، بس المجموعات متحددة من بره (حسم توضيح / إنقاذ تقريبي).
    /// </summary>
    public static IReadOnlyList<AiToolDefinition> SelectForGroups(
        IReadOnlyList<AiToolDefinition> allowed, IReadOnlyCollection<string> groupNames)
    {
        var wanted = new HashSet<string>(AlwaysTools);
        foreach (var g in Groups)
        {
            if (groupNames.Contains(g.Name))
                foreach (var t in g.Tools) wanted.Add(t);
        }

        var classified = new HashSet<string>(Groups.SelectMany(g => g.Tools).Concat(AlwaysTools));

        return allowed
            .Where(d => wanted.Contains(d.Name) || !classified.Contains(d.Name))
            .ToList();
    }

    private static string Normalize(string text) =>
        text.NormalizeArabic().ToLowerInvariant();

    // ════════════════════════════════════════════════════════════
    // تحليل المطابقة + التوضيح عند الغموض (Phase 3)
    // الفكرة: بدل ما السؤال الغامض يتوجّه تخميناً لأدوات غلط أو يسقط في
    // أدوات الملخص بصمت، زكي بيسأل سؤال توضيحي قصير والرد اللي بعده بيحسم.
    // ════════════════════════════════════════════════════════════

    /// <summary>مجموعات «ميتا»: بتتقاطع مع كل المواضيع (تقارير/مؤشرات، أخبار/عملات) فمابتشاركش في كشف الغموض</summary>
    private static readonly string[] MetaGroups = { "management", "general" };

    /// <summary>
    /// عنقود كل مجموعة: الغموض الحقيقي بيحصل بين عنقودين مختلفين.
    /// جوه نفس العنقود المطابقة المتعددة طبيعية (سؤال مديونيات بيطابق البيع والمديونيات معاً)
    /// والموديل بيحسمها من اتحاد الأدوات عادي.
    /// </summary>
    private static readonly Dictionary<string, string> ClusterOf = new()
    {
        ["sales"] = "money", ["receivables"] = "money", ["finance"] = "money",
        ["returns"] = "money", ["financial_statements"] = "money", ["approvals"] = "money",
        ["production"] = "ops", ["deliveries"] = "ops", ["inventory"] = "ops",
        ["hr"] = "people", ["crm"] = "people", ["care"] = "people"
    };

    /// <summary>اسم الموضوع بالعربي زي ما بيظهر للمستخدم في سؤال التوضيح</summary>
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["sales"] = "فواتير البيع والمبيعات",
        ["receivables"] = "مديونيات العملاء والموردين",
        ["finance"] = "المشتريات والخزينة والمصروفات",
        ["returns"] = "المرتجعات والسلف والدفعات المقدمة",
        ["financial_statements"] = "القوائم المالية والأرباح",
        ["approvals"] = "الطلبات والاعتمادات المعلقة",
        ["production"] = "أوامر التشغيل",
        ["deliveries"] = "حالة التسليمات",
        ["inventory"] = "المخزون",
        ["hr"] = "الموظفين والحضور والرواتب",
        ["crm"] = "فرص البيع ومهام الـ CRM",
        ["care"] = "شكاوى وخدمة العملاء"
    };

    private static string LabelOf(string groupName) => Labels.GetValueOrDefault(groupName, groupName);

    /// <summary>نتيجة مطابقة مجموعة واحدة مع السؤال</summary>
    /// <param name="TotalHits">عدد كلمات المجموعة المطابقة في السؤال (أو درجة التشابه في المطابقة التقريبية)</param>
    /// <param name="UniqueHits">عدد الكلمات المطابقة «المميزة» — مش موجودة في قايمة أي مجموعة تانية</param>
    public sealed record GroupHit(string Name, string Label, int TotalHits, int UniqueHits, bool IsMeta);

    /// <summary>
    /// بيحسب لكل مجموعة (عندها أداة مسموحة واحدة على الأقل للمستخدم) مطابقتها مع السؤال.
    /// الكلمات المشتركة بين المجموعات (زي «تسليم» في البيع والتسليمات) بتتحسب في الإجمالي
    /// بس مش في «المميز» — لأنها مش دليل غموض، الكلمة بتنتمي للموضوعين فعلاً.
    /// </summary>
    public static IReadOnlyList<GroupHit> Analyze(IReadOnlyList<AiToolDefinition> allowed, string question)
    {
        var allowedNames = new HashSet<string>(allowed.Select(d => d.Name));
        var q = Normalize(question ?? "");

        // المطابقة والملكية على الصورة المُطبّعة: صيغ الكلمة الواحدة (شكاوى/شكاوي)
        // بتتحسب مرة واحدة، وإلا الأعداد هتتضخم وتكسر قاعدة التعادل
        var keywordOwners = new Dictionary<string, int>();
        foreach (var g in Groups)
            foreach (var k in g.Keywords.Select(Normalize).Distinct())
                keywordOwners[k] = keywordOwners.GetValueOrDefault(k) + 1;

        var hits = new List<GroupHit>();
        foreach (var g in Groups)
        {
            if (!g.Tools.Any(allowedNames.Contains)) continue;

            var matched = g.Keywords.Select(Normalize).Distinct().Where(k => q.Contains(k)).ToList();
            if (matched.Count == 0) continue;

            var unique = matched.Count(k => keywordOwners[k] == 1);
            hits.Add(new GroupHit(g.Name, LabelOf(g.Name), matched.Count, unique, MetaGroups.Contains(g.Name)));
        }
        return hits;
    }

    /// <summary>
    /// قاعدة الغموض — بترجع 2–3 مواضيع مرشحة للتوضيح، أو null لو السؤال واضح أو عام:
    /// 1) تعادل بين عنقودين: مجموعتان+ من عنقودين مختلفين، لكل واحدة كلمة مميزة مطابقة
    ///    واحدة على الأقل وإجمالي مطابقة ≥2، وعدد المميز متعادل بينهم ومش أكتر من 2
    ///    (فمفيش مجموعة غالبة). التعادل جوه نفس العنقود مش غموض.
    /// 2) صفر مطابقة مباشرة لأي موضوع شغل: بنبص على تشابه الحروف (غلطات كتابة) —
    ///    لو لقينا مرشحين اتنين+ والسؤال فيه إشارة شغل (كام/اعرض/عندنا...) بنوضح.
    ///    أسئلة الأخبار/العملات الواضحة والمعرفة العامة عمرها ما بتوصل لتوضيح.
    /// </summary>
    public static IReadOnlyList<GroupHit>? ClarificationCandidates(
        IReadOnlyList<AiToolDefinition> allowed, string question)
    {
        var hits = Analyze(allowed, question);
        var biz = hits.Where(h => !h.IsMeta).ToList();

        if (biz.Count > 0)
        {
            var strong = biz.Where(h => h.UniqueHits >= 1 && h.TotalHits >= 2).ToList();
            if (strong.Count >= 2)
            {
                var top = strong.Max(h => h.UniqueHits);
                if (top <= 2)
                {
                    var tied = strong.Where(h => h.UniqueHits == top).ToList();
                    if (tied.Select(h => ClusterOf.GetValueOrDefault(h.Name, h.Name)).Distinct().Count() >= 2)
                        return tied.OrderByDescending(h => h.TotalHits).Take(3).ToList();
                }
            }
            return null;
        }

        // مفيش أي مطابقة مباشرة لموضوع شغل
        if (HasStrongGeneralHit(question))
            return null; // سؤال أخبار/عملات واضح — سيبه يمشي عادي

        if (!HasBusinessSignal(question))
            return null; // غالباً معرفة عامة — سيبه للموديل زي ما هو (GEN-01)

        var fuzzy = FuzzyMatches(allowed, question);
        return fuzzy.Count >= 2 ? fuzzy.Take(3).ToList() : null;
    }

    /// <summary>
    /// مطابقة تقريبية بالحروف (لسان/غلطات كتابة): كلمة السؤال بتطابق كلمة مفتاحية لو
    /// التشابه (Levenshtein) ≥ 0.6 أو اشتركوا في بادئة ≥5 حروف (بعد شيل «ال»).
    /// الكلمة المفتاحية المستقلة وزنها 2، وكلمة جوه عبارة وزنها 1.
    /// </summary>
    public static IReadOnlyList<GroupHit> FuzzyMatches(IReadOnlyList<AiToolDefinition> allowed, string question)
    {
        var allowedNames = new HashSet<string>(allowed.Select(d => d.Name));
        var qTokens = Tokens(Normalize(question ?? ""))
            .Where(t => StripAl(t).Length >= 4)
            .Distinct()
            .ToList();

        var results = new List<GroupHit>();
        foreach (var g in Groups)
        {
            if (MetaGroups.Contains(g.Name)) continue;
            if (!g.Tools.Any(allowedNames.Contains)) continue;

            var standalone = g.Keywords.Where(k => !k.Contains(' '))
                .SelectMany(k => Tokens(Normalize(k))).Distinct().ToList();
            var phraseTokens = g.Keywords.Where(k => k.Contains(' '))
                .SelectMany(k => Tokens(Normalize(k))).Distinct().ToList();

            var score = 0;
            foreach (var qt in qTokens)
            {
                if (standalone.Any(kt => Similar(qt, kt))) score += 2;
                else if (phraseTokens.Any(kt => Similar(qt, kt))) score += 1;
            }

            if (score > 0)
                results.Add(new GroupHit(g.Name, LabelOf(g.Name), score, score, false));
        }

        // OrderByDescending ثابت الترتيب: التعادل بيتحسم بترتيب تعريف المجموعات
        return results.OrderByDescending(h => h.TotalHits).ToList();
    }

    /// <summary>بادئة سؤال التوضيح — علامة ثابتة بتتعرف عليها الجولة اللي بعدها واختبارات الـ Golden</summary>
    public const string ClarificationPrefix = "قصدك ";

    /// <summary>نص سؤال التوضيح: سطر واحد قصير بالعامية، فيه المواضيع المرشحة بالترتيب</summary>
    public static string BuildClarificationText(IReadOnlyList<GroupHit> candidates) =>
        ClarificationPrefix + string.Join("، ولا ", candidates.Select(c => c.Label)) + "؟";

    /// <summary>
    /// بيحاول يحسم سؤال توضيح سابق من رد المستخدم القصير:
    /// - اسم الموضوع نفسه، أو ترتيبه (الأولى/التانية/التالتة أو 1/2/3)، أو كلمة مفتاحية
    ///   بتحسم لموضوع واحد من المرشحين بس.
    /// - «الاتنين/كلهم» بترجع كل المرشحين (الموديل هيجاوب على الموضوعين باتحاد أدواتهم).
    /// - أي رد تاني بيرجع null: السؤال بيتمشي كسؤال جديد عادي.
    /// </summary>
    public static IReadOnlyList<string>? ResolveClarification(
        IReadOnlyList<AiToolDefinition> allowed, string? lastAssistantText, string reply)
    {
        if (string.IsNullOrWhiteSpace(lastAssistantText) || string.IsNullOrWhiteSpace(reply))
            return null;

        var text = lastAssistantText.Trim();
        if (!text.StartsWith(ClarificationPrefix, StringComparison.Ordinal))
            return null;

        var body = text[ClarificationPrefix.Length..].Trim().TrimEnd('؟', '?', ' ', '.');
        var labels = body.Split("، ولا ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (labels.Length < 2)
            return null;

        var candidates = new List<(string Name, string Label)>();
        foreach (var label in labels)
        {
            var name = Labels.FirstOrDefault(kv => kv.Value == label).Key;
            if (name == null) return null;
            candidates.Add((name, label));
        }

        var r = Normalize(reply.Trim());
        var replyTokens = Tokens(r);

        // «الاتنين / كلهم» = كمّل بالمواضيع كلها مع بعض
        var allWords = new[] { "الاتنين", "كلهم", "كله", "كلها" }.Select(Normalize).ToHashSet();
        if (replyTokens.Any(t => allWords.Contains(t)))
            return candidates.Select(c => c.Name).ToList();

        // ترتيب: الأولى/التانية/التالتة أو أرقام عربي/إفرنجي
        var ordinalSets = new[]
        {
            new[] { "الأولى", "الاولى", "الأول", "الاول", "١", "1" },
            new[] { "التانية", "الثانية", "التاني", "الثاني", "٢", "2" },
            new[] { "التالتة", "الثالثة", "التالت", "الثالث", "٣", "3" }
        };
        for (var i = 0; i < ordinalSets.Length && i < candidates.Count; i++)
        {
            var set = ordinalSets[i].Select(Normalize).ToHashSet();
            if (replyTokens.Any(t => set.Contains(t)))
                return new[] { candidates[i].Name };
        }

        // اسم الموضوع نفسه مكتوب في الرد
        foreach (var c in candidates)
            if (r.Contains(Normalize(c.Label)))
                return new[] { c.Name };

        // كلمات مفتاحية بتحسم لموضوع واحد من المرشحين بس
        var hits = Analyze(allowed, reply)
            .Where(h => candidates.Any(c => c.Name == h.Name))
            .ToList();
        if (hits.Count == 1)
            return new[] { hits[0].Name };

        return null;
    }

    // ── مساعدات الغموض ──

    /// <summary>كلمات بتدل إن السؤال عن شغل/بيانات مش معرفة عامة (مقارنة على مستوى الكلمة الكاملة)</summary>
    private static readonly string[] BusinessSignalWords =
    {
        "كام", "كم", "اعرض", "اعرضلي", "هات", "هاتلي", "وريني", "اجمالي", "مجموع", "عدد",
        "حالة", "ملخص", "تقرير", "بيانات", "عندنا", "باقي", "مستحق", "اعرف", "عايز", "عاوز",
        "لسه", "وصلت", "وصل"
    };

    private static bool HasBusinessSignal(string question)
    {
        var signals = BusinessSignalWords.Select(Normalize).ToHashSet();
        return Tokens(Normalize(question ?? "")).Any(t => signals.Contains(t));
    }

    /// <summary>كلمات مجموعة الأخبار الضعيفة: لوحدها ماتكفيش نعتبر السؤال أخبار واضح</summary>
    private static readonly string[] WeakGeneralKeywords = { "خبر", "اخبار", "أخبار" };

    private static bool HasStrongGeneralHit(string question)
    {
        var q = Normalize(question ?? "");
        var weak = WeakGeneralKeywords.Select(Normalize).ToHashSet();
        return Groups.Single(g => g.Name == "general").Keywords
            .Where(k => !weak.Contains(Normalize(k)))
            .Any(k => q.Contains(Normalize(k)));
    }

    private static readonly char[] TokenSeparators =
    {
        ' ', '\t', '\r', '\n', '؟', '?', '،', ',', '.', ':', '؛', ';', '(', ')', '[', ']',
        '«', '»', '"', '\'', '-', '/', '\\', '!', '…'
    };

    private static List<string> Tokens(string normalized) =>
        normalized.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .ToList();

    private static string StripAl(string token) =>
        token.StartsWith("ال", StringComparison.Ordinal) && token.Length > 3 ? token[2..] : token;

    private static bool Similar(string a, string b)
    {
        var x = StripAl(a);
        var y = StripAl(b);
        if (x.Length < 4 || y.Length < 4) return false;
        if (CommonPrefix(x, y) >= 5) return true;

        var sim = 1.0 - (double)Levenshtein(x, y) / Math.Max(x.Length, y.Length);
        return sim >= 0.6;
    }

    private static int CommonPrefix(string a, string b)
    {
        var n = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < n && a[i] == b[i]) i++;
        return i;
    }

    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }
}
