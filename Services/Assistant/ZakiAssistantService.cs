using System.Diagnostics;
using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>رابط فتح لصف في جدول (مثلاً إشعار). لما يتفتح بيتعلّم NotificationId كمقروء</summary>
public sealed record ZakiRowLink(string Url, int NotificationId);

/// <summary>جدول جاهز للعرض والتصدير (عناوين عربية، قيم منسقة). RowLinks اختيارية: زرار فتح لكل صف</summary>
public record ZakiTable(
    string Title,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<ZakiRowLink?>? RowLinks = null);

/// <summary>نتيجة سؤال زكي للواجهة</summary>
public record ZakiAnswer(
    bool Success,
    string Text,
    int TotalTokens,
    IReadOnlyList<string> ToolsUsed,
    IReadOnlyList<ZakiTable> Tables,
    long? ConversationId = null,
    long? MessageId = null);

/// <summary>
/// المنسّق: بيبعت السؤال للموديل، ولو طلب أدوات بينفذها ويرجّع النتايج للموديل،
/// لحد ما يرجع رد نهائي. الجداول بتتجمع من الأدوات مباشرة (مش من نص الموديل).
/// - البائع: بياناته هو بس (ZakiSalesDataService).
/// - المخزون: ZakiInventoryService.
/// - باقي الأدوار: نفس اللي الشاشات بتعرضه (ZakiToolExecutor).
/// - تقرير نشاط الموظف: Admin بس.
/// وكل سؤال بيتسجل في جداول المراجعة (بدون ما يوقف الرد لو التسجيل فشل).
/// </summary>
public class ZakiAssistantService
{
    private const int MaxToolRounds = 3;
    private const int MaxHistoryMessages = 6;
    private const int MaxHistoryChars = 1200;
    /// <summary>توكنز محجوزة لسؤال كامل (عشان الرد ما يتقطعش في نص الطريق)</summary>

    private sealed record ToolLog(string Name, string Args, int Rows, bool Denied, int DurationMs);

    private readonly IAiProvider _ai;
    private readonly ZakiToolExecutor _tools;
    private readonly ZakiSalesDataService _sales;
    private readonly ZakiInventoryService _inventory;
    private readonly ZakiCustomerCareService _care;
    private readonly ZakiEmployeeActivityService _activity;
    private readonly ZakiNotificationService _notifications;
    private readonly ZakiSalesDocumentsService _salesDocs;
    private readonly ZakiFinanceInvoicesService _finance;
    private readonly ZakiEmployeeTasksService _employeeTasks;
    private readonly ZakiApprovalsService _approvals;
    private readonly ZakiHrService _hr;
    private readonly ZakiKnowledgeService _knowledge;
    private readonly ZakiFinancialStatementsService _financialStatements;
    private readonly ZakiBalancesService _balances;
    private readonly ZakiCashboxService _cashbox;
    private readonly ZakiStockAlertsService _stockAlerts;
    private readonly ZakiReturnsAdvancesService _returnsAdvances;
    private readonly ZakiFeedbackService _feedback;
    private readonly ZakiFinanceExtraService _financeExtra;
    private readonly ZakiJobOrdersService _jobOrders;
    private readonly ZakiWebInfoService _webInfo;
    private readonly ZakiHrExtraService _hrExtra;
    private readonly ZakiUserContext _userContext;
    private readonly ZakiLogService _log;
    private readonly AssistantOptions _opt;
    private readonly ILogger<ZakiAssistantService> _logger;

    public ZakiAssistantService(
        IAiProvider ai,
        ZakiToolExecutor tools,
        ZakiSalesDataService sales,
        ZakiInventoryService inventory,
        ZakiCustomerCareService care,
        ZakiEmployeeActivityService activity,
        ZakiNotificationService notifications,
        ZakiSalesDocumentsService salesDocs,
        ZakiFinanceInvoicesService finance,
        ZakiEmployeeTasksService employeeTasks,
        ZakiApprovalsService approvals,
        ZakiHrService hr,
        ZakiKnowledgeService knowledge,
        ZakiFinancialStatementsService financialStatements,
        ZakiBalancesService balances,
        ZakiCashboxService cashbox,
        ZakiStockAlertsService stockAlerts,
        ZakiReturnsAdvancesService returnsAdvances,
        ZakiFeedbackService feedback,
        ZakiFinanceExtraService financeExtra,
        ZakiJobOrdersService jobOrders,
        ZakiWebInfoService webInfo,
        ZakiHrExtraService hrExtra,
        ZakiUserContext userContext,
        ZakiLogService log,
        IOptions<AssistantOptions> options,
        ILogger<ZakiAssistantService> logger)
    {
        _ai = ai;
        _tools = tools;
        _sales = sales;
        _inventory = inventory;
        _care = care;
        _activity = activity;
        _notifications = notifications;
        _salesDocs = salesDocs;
        _finance = finance;
        _employeeTasks = employeeTasks;
        _approvals = approvals;
        _hr = hr;
        _knowledge = knowledge;
        _financialStatements = financialStatements;
        _balances = balances;
        _cashbox = cashbox;
        _stockAlerts = stockAlerts;
        _returnsAdvances = returnsAdvances;
        _feedback = feedback;
        _financeExtra = financeExtra;
        _jobOrders = jobOrders;
        _webInfo = webInfo;
        _hrExtra = hrExtra;
        _userContext = userContext;
        _log = log;
        _opt = options.Value;
        _logger = logger;
    }

    /// <summary>كل الأدوات المسموحة للمستخدم</summary>
    private IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user)
    {
        var list = _tools.GetDefinitionsFor(user).ToList();
        list.AddRange(_inventory.DefinitionsFor(user));
        list.AddRange(_care.DefinitionsFor(user));
        list.AddRange(_notifications.DefinitionsFor(user));
        list.AddRange(_salesDocs.DefinitionsFor(user));
        list.AddRange(_finance.DefinitionsFor(user));
        list.AddRange(_employeeTasks.DefinitionsFor(user));
        list.AddRange(_approvals.DefinitionsFor(user));
        list.AddRange(_hr.DefinitionsFor(user));
        list.AddRange(_knowledge.DefinitionsFor(user));
        list.AddRange(_financialStatements.DefinitionsFor(user));
        list.AddRange(_balances.DefinitionsFor(user));
        list.AddRange(_cashbox.DefinitionsFor(user));
        list.AddRange(_stockAlerts.DefinitionsFor(user));
        list.AddRange(_returnsAdvances.DefinitionsFor(user));
        list.AddRange(_feedback.DefinitionsFor(user));
        list.AddRange(_financeExtra.DefinitionsFor(user));
        list.AddRange(_jobOrders.DefinitionsFor(user));
        list.AddRange(_webInfo.DefinitionsFor(user));
        list.AddRange(_hrExtra.DefinitionsFor(user));
        if (ZakiEmployeeActivityService.CanView(user))
            list.Add(ZakiEmployeeActivityService.Definition());
        return list;
    }

    /// <summary>هل المستخدم عنده أي أداة من أدوات زكي؟ (يتحكم في ظهور الصفحة)</summary>
    public bool CanUse(ClaimsPrincipal user) => DefinitionsFor(user).Count > 0;

    /// <summary>قائمة أدوات المستخدم — للتدقيق الحي في صفحة اختبار زكي (P2)</summary>
    public IReadOnlyList<AiToolDefinition> ToolsFor(ClaimsPrincipal user) => DefinitionsFor(user);

    /// <param name="conversationId">رقم المحادثة الحالية. لو null بنفتح محادثة جديدة.</param>
    /// <param name="history">الرسائل السابقة في نفس المحادثة (سؤال/رد بس) عشان الأسئلة المتابعة.</param>
    public async Task<ZakiAnswer> AskAsync(
        ClaimsPrincipal user,
        string question,
        long? conversationId = null,
        IReadOnlyList<AiMessage>? history = null,
        CancellationToken ct = default,
        bool isTest = false)
    {
        long? messageId = null;
        var sw = Stopwatch.StartNew();
        var userName = user.Identity?.Name ?? "";
        var allowedTools = DefinitionsFor(user);
        // ترتيب ثابت للأدوات: عشان الـ prefix يفضل متطابق والـ prompt caching يشتغل
        var tools = ZakiToolRouter.Select(allowedTools, question)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
        var toolsUsed = new List<string>();
        var tables = new List<ZakiTable>();
        var toolLogs = new List<ToolLog>();
        var totalTokens = 0;
        var promptTokens = 0;
        var completionTokens = 0;

        var info = await _userContext.ResolveAsync(user, ct);
        var isSales = ZakiToolExecutor.IsSalesOnly(user);

        // أسئلة الاختبار (golden set) بتتعلم في اللوج وما بتحسبش في الحد اليومي
        var loggedQuestion = isTest ? $"[اختبار] {question}" : question;

        conversationId ??= await _log.StartConversationAsync(userName, info.EmployeeId, loggedQuestion, ct);

        // ── الحد اليومي (الـ Admin معفي) ──
        if (!isTest && !user.IsInRole("Admin") && _opt.DailyQuestionLimitPerUser > 0)
        {
            var askedToday = await _log.GetTodayQuestionCountAsync(userName, ct);
            if (askedToday >= _opt.DailyQuestionLimitPerUser)
            {
                var limitText = $"وصلت للحد اليومي ({_opt.DailyQuestionLimitPerUser} سؤال). تقدر تكمل بكرة.";
                messageId = await LogAsync(conversationId, loggedQuestion, limitText, false, "DAILY_LIMIT", 0, 0, 0, sw, toolLogs, userName, ct, isTest);
                return new ZakiAnswer(false, limitText, 0, toolsUsed, tables, conversationId, messageId);
            }
        }

        var messages = new List<AiMessage>
        {
            new AiMessage("system", BuildSystemPrompt(info.DisplayName, info.EmployeeId.HasValue, tools.Count > 0))
        };

        // ── ذاكرة المحادثة: آخر رسائل بس ──
        if (history is { Count: > 0 })
        {
            foreach (var h in history.TakeLast(MaxHistoryMessages))
                messages.Add(h with { Content = Truncate(h.Content, MaxHistoryChars) });
        }

        messages.Add(new AiMessage("user", question));

        // حجز التوكنز للسؤال: متوسط الأسئلة الفعلية (آخر 7 أيام) بدل رقم ثابت كبير
        var reserve = await _log.GetReserveTokensPerQuestionAsync(ct);

        // ميزانية المستخدم اليومية (الـ Admin معفي، والاختبار ما بيتحسبش)
        if (!isTest && !user.IsInRole("Admin") && _opt.PerUserDailyTokenBudget > 0)
        {
            var usedByUser = await _log.GetTodayTotalTokensForUserAsync(userName, ct);
            if (usedByUser + reserve >= _opt.PerUserDailyTokenBudget)
            {
                const string userBudgetText = "وصلت للحد اليومي لاستخدامك لزكي، تقدر تكمل بكرة.";
                messageId = await LogAsync(conversationId, loggedQuestion, userBudgetText, false, "USER_TOKEN_BUDGET",
                    0, 0, 0, sw, toolLogs, userName, ct, isTest);
                return new ZakiAnswer(false, userBudgetText, 0, toolsUsed, tables, conversationId, messageId);
            }
        }

        // ميزانية اليوم للحساب كله (Groq المجاني = 200,000 في اليوم): لو خلصت نقولها من الأول
        if (_opt.DailyTokenBudget > 0
            && await _log.GetTodayTotalTokensAsync(ct) + reserve >= _opt.DailyTokenBudget)
        {
            const string budgetText = "وصلنا للحد اليومي لاستهلاك الذكاء الاصطناعي، هيتجدد بكرة. جرّب تاني بعدين، أو كلّم الإدارة.";
            messageId = await LogAsync(conversationId, loggedQuestion, budgetText, false, "DAILY_TOKEN_BUDGET",
                0, 0, 0, sw, toolLogs, userName, ct, isTest);
            return new ZakiAnswer(false, budgetText, 0, toolsUsed, tables, conversationId, messageId);
        }

        for (var round = 0; round <= MaxToolRounds; round++)
        {
            var result = await _ai.CompleteAsync(messages, tools.Count > 0 ? tools : null, ct);

            // حد الدقيقة: نستنى شوية ونعيد مرة واحدة بدل ما نفشل فوراً
            if (!result.Success && result.ErrorCode == "RATE_LIMITED")
            {
                await Task.Delay(TimeSpan.FromSeconds(20), ct);
                result = await _ai.CompleteAsync(messages, tools.Count > 0 ? tools : null, ct);
            }

            if (!result.Success)
            {
                messageId = await LogAsync(conversationId, loggedQuestion, result.ErrorMessage, false, result.ErrorCode, 0, 0, 0, sw, toolLogs, userName, ct, isTest);
                var failText = tables.Count > 0
                    ? (result.ErrorMessage ?? "حصل خطأ.") + " الجدول اللي فوق فيه البيانات اللي قدرت أجيبها قبل ما الرد يتقطع."
                    : result.ErrorMessage ?? "حصل خطأ.";
                return new ZakiAnswer(false, failText, totalTokens, toolsUsed, tables, conversationId, messageId);
            }

            _logger.LogInformation(
                "Zaki tokens: round={Round} prompt={Prompt} cached={Cached} completion={Completion}",
                round, result.PromptTokens, result.CachedPromptTokens, result.CompletionTokens);

            totalTokens += result.TotalTokens;
            promptTokens += result.PromptTokens;
            completionTokens += result.CompletionTokens;

            // رد نهائي
            if (result.ToolCalls.Count == 0)
            {
                messageId = await LogAsync(conversationId, loggedQuestion, result.Content, true, null, promptTokens, completionTokens, totalTokens, sw, toolLogs, userName, ct, isTest);
                return new ZakiAnswer(true, result.Content ?? "", totalTokens, toolsUsed, tables, conversationId, messageId);
            }

            // الموديل طلب أدوات: نسجّل طلبه، ننفّذ كل أداة، ونرجّع النتايج
            messages.Add(new AiMessage("assistant", result.Content, ToolCalls: result.ToolCalls));

            foreach (var call in result.ToolCalls)
            {
                toolsUsed.Add(call.Name);

                var toolSw = Stopwatch.StartNew();
                ZakiToolOutcome outcome;
                try
                {
                    outcome = await RunToolAsync(user, call.Name, call.ArgumentsJson, info, isSales, ct);
                }
                catch (InvalidOperationException ex)
                    when (ex.Message.Contains("second operation", StringComparison.OrdinalIgnoreCase))
                {
                    // استعلام تاني في نفس اللحظة (بولينج إشعارات مثلًا) لمس نفس الـ DbContext —
                    // ندي اللحظة تعدي ونعيد الأداة مرة واحدة
                    _logger.LogWarning("Zaki tool {Tool} hit DbContext concurrency — retrying once", call.Name);
                    await Task.Delay(500, ct);
                    outcome = await RunToolAsync(user, call.Name, call.ArgumentsJson, info, isSales, ct);
                }
                toolSw.Stop();

                var rows = outcome.Table?.Rows.Count ?? 0;
                if (outcome.Table != null)
                    tables.Add(outcome.Table);

                toolLogs.Add(new ToolLog(call.Name, call.ArgumentsJson, rows, outcome.IsDenied, (int)toolSw.ElapsedMilliseconds));

                messages.Add(new AiMessage("tool", outcome.Json, ToolCallId: call.Id, Name: call.Name));
            }
        }

        _logger.LogWarning("Zaki reached max tool rounds");
        const string maxText = "ما قدرتش أكمل الإجابة، جرّب سؤال أبسط.";
        messageId = await LogAsync(conversationId, loggedQuestion, maxText, false, "MAX_ROUNDS", promptTokens, completionTokens, totalTokens, sw, toolLogs, userName, ct, isTest);
        return new ZakiAnswer(false, maxText, totalTokens, toolsUsed, tables, conversationId, messageId);
    }

    /// <summary>
    /// توجيه الأداة للخدمة الصح:
    /// - تقرير النشاط: للـ Admin بس.
    /// - المخزون: ZakiInventoryService.
    /// - البائع: أدوات الفرص والمهام والتواصلات من خدمته (بياناته هو بس).
    /// - الباقي: من ZakiToolExecutor.
    /// </summary>
    private async Task<ZakiToolOutcome> RunToolAsync(
        ClaimsPrincipal user, string toolName, string argumentsJson, ZakiUserInfo info, bool isSales, CancellationToken ct)
    {
        if (toolName == ZakiEmployeeActivityService.ToolName)
        {
            if (!ZakiEmployeeActivityService.CanView(user))
            {
                return new ZakiToolOutcome(
                    "{\"خطأ\":\"ليس لديك صلاحية لهذه البيانات.\"}",
                    null,
                    IsDenied: true);
            }
            return await _activity.ExecuteFromArgsAsync(argumentsJson, ct);
        }

        if (ZakiFinancialStatementsService.Handles(toolName))
            return await _financialStatements.ExecuteAsync(user, toolName, argumentsJson, ct);

        if (ZakiKnowledgeService.Handles(toolName))
            return await _knowledge.ExecuteAsync(user, argumentsJson, ct);

        if (ZakiHrService.Handles(toolName))
            return await _hr.ExecuteAsync(user, toolName, argumentsJson, ct);

        if (ZakiBalancesService.Handles(toolName))
            return await _balances.ExecuteAsync(user, toolName, argumentsJson, ct);

        if (ZakiCashboxService.Handles(toolName))
            return await _cashbox.ExecuteAsync(user, toolName, argumentsJson, ct);

        if (ZakiStockAlertsService.Handles(toolName))
            return await _stockAlerts.ExecuteAsync(user, toolName, argumentsJson, ct);

        if (ZakiReturnsAdvancesService.Handles(toolName))
            return await _returnsAdvances.ExecuteAsync(user, toolName, argumentsJson, ct);

        if (ZakiFeedbackService.Handles(toolName))
            return await _feedback.ExecuteAsync(user, toolName, argumentsJson, ct);

        if (ZakiFinanceExtraService.Handles(toolName))
            return await _financeExtra.ExecuteAsync(user, toolName, argumentsJson, ct);

        if (ZakiJobOrdersService.Handles(toolName))
            return await _jobOrders.ExecuteAsync(user, toolName, argumentsJson, ct);

        if (ZakiWebInfoService.Handles(toolName))
            return await _webInfo.ExecuteAsync(user, toolName, argumentsJson, ct);

        if (ZakiHrExtraService.Handles(toolName))
            return await _hrExtra.ExecuteAsync(user, toolName, argumentsJson, ct);

        if (ZakiApprovalsService.Handles(toolName))
            return await _approvals.ExecuteAsync(user, toolName, argumentsJson, ct);

        if (ZakiEmployeeTasksService.Handles(toolName))
            return await _employeeTasks.ExecuteAsync(user, argumentsJson, info.EmployeeId, ct);

        if (ZakiFinanceInvoicesService.Handles(toolName))
            return await _finance.ExecuteAsync(user, toolName, argumentsJson, ct);

        if (ZakiSalesDocumentsService.Handles(toolName))
            return await _salesDocs.ExecuteAsync(user, toolName, argumentsJson, info, ct);

        if (ZakiNotificationService.Handles(toolName))
            return await _notifications.ExecuteAsync(user, toolName, argumentsJson, ct);

        if (ZakiCustomerCareService.Handles(toolName))
            return await _care.ExecuteAsync(user, toolName, argumentsJson, ct);

        if (ZakiInventoryService.Handles(toolName))
            return await _inventory.ExecuteAsync(user, toolName, argumentsJson, ct);

        if (isSales && ZakiSalesDataService.Handles(toolName))
            return await _sales.ExecuteAsync(user, toolName, argumentsJson, info, ct);

        return await _tools.ExecuteAsync(user, toolName, argumentsJson, info.EmployeeId, ct);
    }

    /// <summary>يسجل السؤال والرد وكل الأدوات، وبيزود عداد الاستهلاك. أي خطأ مابيوقفش الرد.</summary>
    private async Task<long?> LogAsync(
        long? conversationId,
        string question,
        string? answer,
        bool ok,
        string? errorCode,
        int promptTokens,
        int completionTokens,
        int totalTokens,
        Stopwatch sw,
        List<ToolLog> toolLogs,
        string userName,
        CancellationToken ct,
        bool isTest = false)
    {
        long? savedId = null;
        if (conversationId.HasValue)
        {
            var messageId = await _log.LogMessageAsync(
                conversationId.Value, question, answer, ok, errorCode,
                promptTokens, completionTokens, totalTokens, (int)sw.ElapsedMilliseconds, ct);

            if (messageId.HasValue)
            {
                savedId = messageId;
                foreach (var t in toolLogs)
                    await _log.LogToolCallAsync(messageId.Value, t.Name, t.Args, t.Rows, t.Denied, t.DurationMs, ct);
            }
        }

        if (!isTest)
            await _log.AddUsageAsync(userName, totalTokens, ct);

        return savedId;
    }

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= max ? text : text[..max];
    }

    private static string BuildSystemPrompt(string displayName, bool isEmployee, bool hasTools)
    {
        var today = TodayInCairo().ToString("yyyy-MM-dd");
        var who = isEmployee ? $"{displayName} (موظف في الشركة)" : displayName;

        var toolNote = hasTools
            ? "استخدم الأدوات المتاحة لجلب البيانات قبل أي إجابة عن بيانات النظام. ماتخمنش أي رقم أو اسم أو تاريخ."
            : "مفيش أدوات متاحة لك دلوقتي. لو السؤال عن بيانات النظام، قول إنه خارج صلاحياتك.";

        return $"""
أنت زكي، المساعد الذكي لنظام COCOBOLO ERP (شركة أثاث: مبيعات، مشتريات، مخزون، خزينة، CRM، موارد بشرية، خدمة عملاء).
## طريقة الرد
- ابدأ بالإجابة مباشرة (الرقم أو النتيجة)، وبعدين التفاصيل المهمة في نقطتين أو تلاتة. ماتبدأش بالاسم ولا بكلمات ترحيب.
- اكتب عربي مصري واضح ومحترف، مختصر، من غير حشو ولا تكرار.
- الأرقام بالجنيه بالشكل «1,250 ج.م». التواريخ بالشكل yyyy-MM-dd. واذكر الفترة اللي الرد عليها (الشهر ده، اليوم، كل الفترات...).
- الجداول بتتعرض تحت ردك تلقائياً، فماتكتبش جداول Markdown ولا تعيد سرد كل الصفوف.
- ماتذكرش أسماء الأدوات ولا الحقول الإنجليزية ولا أي تفاصيل تقنية أو JSON.
- لو الرد مش كامل بسبب الصلاحيات، قول ده في جملة واحدة من غير ما تشرح الصلاحيات الداخلية.
- لو الأداة رجعت قائمة فاضية، قول إنه مفيش بيانات مطابقة في نطاق صلاحياتك. ماتقولش إنها مش موجودة في النظام.
- لو السؤال عن فرع معين (مثلاً «فرع المعادي»)، مرّر اسم الفرع في حقل الفرع للأداة، واذكر اسم الفرع في ردك. لو مذكورش فرع، الأرقام لكل الفروع. «فرعي» = فرع المستخدم.
- لو السؤال متابعة لرد قبله، استخدم سياق المحادثة السابقة.
- أخبار العالم أو الأخبار العامة: استخدم أداة الأخبار. وسعر الدولار أو أي عملة أو الصرف: استخدم أداة سعر الصرف.
- قاعدة صارمة: ماتذكرش أي سعر صرف أو خبر من ذاكرتك أبداً. لو الأداة فشلت أو رجعت فاضية، قول إنك مش قادر تجيب المعلومة دلوقتي، ومتخمّنش رقم.
- أي سعر صرف أو خبر لازم يتذكر معاه المصدر ووقت التحديث اللي رجعوا من الأداة.
- أسئلة المعرفة العامة (مش عن النظام): جاوب من معرفتك باختصار. ولو المعلومة بتتغير بسرعة، قول إنها ممكن تكون قديمة.
- لو فيه قرار أو خطوة لازم المستخدم يعملها، اقترح الخطوة التالية في جملة واحدة (مثلاً: «تقدر تراجعها من شاشة الفواتير»).
- لو مش متأكد من حاجة، قول ده بوضوح. ماتخترعش.
- أنت للقراءة فقط: ماتنفذش ولا تعد بتنفيذ أي تعديل أو حذف أو اعتماد.

## السؤال عن الشاشات والإجراءات
- لو السؤال عن مكان شاشة أو إزاي تعمل حاجة في النظام، استخدم أداة البحث عن الشاشات، واذكر اسم الشاشة والرابط.
- ماتقترحش شاشة المستخدم مش مسموحله بيها.

## قاموس النظام (علشان تفهم الأسئلة صح)
- الفاتورة: بيع أو شراء. حالاتها: مفتوحة، مدفوعة جزئياً، مسددة، ملغاة. المتبقي = الإجمالي − المدفوع. المتأخرة = فيها متبقي وتاريخ استحقاقها فات.
- في فاتورة البيع: البائع هو الموظف اللي عملها. في فاتورة الشراء: العميل هو العميل المرتبط بيها.
- فاتورة الشراء المرآة: فاتورة داخلية بتتعمل تلقائياً مقابل بيع في المعرض.
- عرض السعر: مسودة ← تم الإرسال ← مقبول أو مرفوض، أو يتحول لفاتورة.
- إذن التسليم: لو فيه مبالغ متبقية، التسليم بيحتاج إذن من الإدارة.
- طلب تعديل الفاتورة: بيتعمل ويتعتمد أو يترفض، وكل التعديلات بتتسجل في سجل التعديلات.
- طلب تعديل السعر: بيراجعه مدير المبيعات أو الحسابات.
- الحضور بيتحسب من البصمة، والإذن أو الاستثناء بيغطي الغياب أو التأخير.
- المهمة المتأخرة: موعدها فات ومش مكتملة.
- النافد من المخزون: رصيده صفر.

## السياق الحالي
تاريخ اليوم: {today}. المستخدم الحالي: {who}.
{toolNote}
""";
    }

    private static DateTime TodayInCairo()
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }
}
