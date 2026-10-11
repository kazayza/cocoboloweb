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
    long? ConversationId = null);

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
        if (ZakiEmployeeActivityService.CanView(user))
            list.Add(ZakiEmployeeActivityService.Definition());
        return list;
    }

    /// <summary>هل المستخدم عنده أي أداة من أدوات زكي؟ (يتحكم في ظهور الصفحة)</summary>
    public bool CanUse(ClaimsPrincipal user) => DefinitionsFor(user).Count > 0;

    /// <param name="conversationId">رقم المحادثة الحالية. لو null بنفتح محادثة جديدة.</param>
    /// <param name="history">الرسائل السابقة في نفس المحادثة (سؤال/رد بس) عشان الأسئلة المتابعة.</param>
    public async Task<ZakiAnswer> AskAsync(
        ClaimsPrincipal user,
        string question,
        long? conversationId = null,
        IReadOnlyList<AiMessage>? history = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var userName = user.Identity?.Name ?? "";
        var tools = DefinitionsFor(user);
        var toolsUsed = new List<string>();
        var tables = new List<ZakiTable>();
        var toolLogs = new List<ToolLog>();
        var totalTokens = 0;
        var promptTokens = 0;
        var completionTokens = 0;

        var info = await _userContext.ResolveAsync(user, ct);
        var isSales = ZakiToolExecutor.IsSalesOnly(user);

        conversationId ??= await _log.StartConversationAsync(userName, info.EmployeeId, question, ct);

        // ── الحد اليومي (الـ Admin معفي) ──
        if (!user.IsInRole("Admin") && _opt.DailyQuestionLimitPerUser > 0)
        {
            var askedToday = await _log.GetTodayQuestionCountAsync(userName, ct);
            if (askedToday >= _opt.DailyQuestionLimitPerUser)
            {
                var limitText = $"وصلت للحد اليومي ({_opt.DailyQuestionLimitPerUser} سؤال). تقدر تكمل بكرة.";
                await LogAsync(conversationId, question, limitText, false, "DAILY_LIMIT", 0, 0, 0, sw, toolLogs, userName, ct);
                return new ZakiAnswer(false, limitText, 0, toolsUsed, tables, conversationId);
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

        for (var round = 0; round <= MaxToolRounds; round++)
        {
            var result = await _ai.CompleteAsync(messages, tools.Count > 0 ? tools : null, ct);

            if (!result.Success)
            {
                await LogAsync(conversationId, question, result.ErrorMessage, false, result.ErrorCode, 0, 0, 0, sw, toolLogs, userName, ct);
                return new ZakiAnswer(false, result.ErrorMessage ?? "حصل خطأ.", totalTokens, toolsUsed, tables, conversationId);
            }

            totalTokens += result.TotalTokens;
            promptTokens += result.PromptTokens;
            completionTokens += result.CompletionTokens;

            // رد نهائي
            if (result.ToolCalls.Count == 0)
            {
                await LogAsync(conversationId, question, result.Content, true, null, promptTokens, completionTokens, totalTokens, sw, toolLogs, userName, ct);
                return new ZakiAnswer(true, result.Content ?? "", totalTokens, toolsUsed, tables, conversationId);
            }

            // الموديل طلب أدوات: نسجّل طلبه، ننفّذ كل أداة، ونرجّع النتايج
            messages.Add(new AiMessage("assistant", result.Content, ToolCalls: result.ToolCalls));

            foreach (var call in result.ToolCalls)
            {
                toolsUsed.Add(call.Name);

                var toolSw = Stopwatch.StartNew();
                var outcome = await RunToolAsync(user, call.Name, call.ArgumentsJson, info, isSales, ct);
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
        await LogAsync(conversationId, question, maxText, false, "MAX_ROUNDS", promptTokens, completionTokens, totalTokens, sw, toolLogs, userName, ct);
        return new ZakiAnswer(false, maxText, totalTokens, toolsUsed, tables, conversationId);
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

        if (ZakiHrService.Handles(toolName))
            return await _hr.ExecuteAsync(user, toolName, argumentsJson, ct);

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
    private async Task LogAsync(
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
        CancellationToken ct)
    {
        if (conversationId.HasValue)
        {
            var messageId = await _log.LogMessageAsync(
                conversationId.Value, question, answer, ok, errorCode,
                promptTokens, completionTokens, totalTokens, (int)sw.ElapsedMilliseconds, ct);

            if (messageId.HasValue)
            {
                foreach (var t in toolLogs)
                    await _log.LogToolCallAsync(messageId.Value, t.Name, t.Args, t.Rows, t.Denied, t.DurationMs, ct);
            }
        }

        await _log.AddUsageAsync(userName, totalTokens, ct);
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
            ? "- استخدم الأدوات المتاحة لجلب البيانات قبل الإجابة على أي سؤال عن بيانات النظام."
            : "- مفيش أدوات متاحة لك حالياً. لو السؤال عن بيانات النظام، قول إنه خارج صلاحياتك.";

        return $@"أنت ""زكي""، مساعد داخل نظام ERP لشركة أثاث.
- جاوب بالعربي الواضح والمختصر.
- تاريخ اليوم: {today}. المستخدم الحالي: {who}.
- ماتبدأش الرد باسم المستخدم ولا بكلمة ترحيب. ابدأ بالإجابة مباشرة، وماتكررش الاسم في الرد.
- اكتب عربي واضح وطبيعي، من غير كلام حشو أو كلمات زي «يا» و«وبعدين» في أول الرد.
- أنت للقراءة فقط: لا تنفذ ولا تعد بتنفيذ أي تعديل أو حذف.
- لو السؤال عن بيانات النظام (عملاء، فرص، مهام، مخزون، شكاوى، أرقام الشركة): اعتمد فقط على نتائج الأدوات، ولا تخترع أرقام أو أسماء أو تواريخ.
- لو السؤال عن الإشعارات: قول عددها الكلي وأهم اللي فوق في نقطتين أو تلاتة. الجدول تحت ردك فيه زرار فتح لكل إشعار، فماتكتبش روابط.
- لو السؤال عن مهام موظف بالاسم غير المستخدم الحالي: استخدم get_employee_tasks واكتب اسم الموظف زي ما المستخدم كتبه بالظبط. ماتقولش إنك مش قادر تتحقق قبل ما تجرب الأداة دي.
- لو السؤال عن الفواتير أو المبيعات المفوترة أو عروض الأسعار: اذكر الأرقام الأساسية (العدد، المتبقي، المتأخر) في نقطتين أو تلاتة، والجدول تحت الرد بيعرض التفاصيل.
- لو السؤال عام (مش عن بيانات النظام): جاوب من معرفتك بشكل واضح ومفيد. ولو المعلومة ممكن تكون تغيرت، قول ده بإيجاز.
- الجداول بتتعرض للمستخدم تحت ردك تلقائياً. فلا تكتب جداول Markdown ولا تعيد سرد كل الصفوف. لخّص في نقطتين أو ثلاث.
- استخدم أسماء الحقول العربية كما هي في نتائج الأدوات، وماتستخدمش أي أسماء إنجليزية.
- لو الأداة رجعت قائمة فاضية، قول إنه مفيش بيانات مطابقة في نطاق صلاحياتك. ماتقولش إنها غير موجودة في النظام.
- ماتقولش إن حاجة حصلت (زي إرسال عرض أو اتصال) إلا لو مسجلة في البيانات.
- لو السؤال عن بيانات ومفيش أداة اتنادت لجلبها، ماتقولش ""مفيش بيانات""، قول إنك مش قادر تتحقق منها دلوقتي.
- لو السؤال متابعة لرد قبله، استخدم سياق المحادثة السابقة.
{toolNote}";
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
