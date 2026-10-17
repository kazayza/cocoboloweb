using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>نتيجة تنفيذ أداة: JSON للموديل + جدول للعرض (اختياري) + هل اتمنعت</summary>
public record ZakiToolOutcome(string Json, ZakiTable? Table, bool IsDenied = false);

/// <summary>
/// الأدوات اللي زكي يقدر ينفذها + تنفيذها.
/// القواعد:
/// - كل أداة بتتحقق من الصلاحية بنفسها (مش بنعتمد على الموديل).
/// - البائع (Sales) يشوف بياناته هو بس (بالموظف المربوط باليوزر).
/// - باقي الأدوار تشوف نفس اللي الشاشات بتعرضه.
/// - أسماء الحقول والجداول عربي من الأساس.
/// - مفيش أي أداة بتكتب في قاعدة البيانات.
/// - مفيش أرقام تليفونات في الردود.
/// </summary>
public class ZakiToolExecutor
{
    public const string GetOpenOpportunities = "get_open_opportunities";
    public const string GetOpenTasks = "get_open_tasks";
    public const string GetOverdueTasks = "get_overdue_tasks";
    public const string GetCrmSummary = "get_crm_summary";
    public const string GetRecentInteractions = "get_recent_interactions";
    public const string GetLeads = "get_leads";
    public const string GetMarketingSummary = "get_marketing_summary";
    public const string GetExecutiveSummary = "get_executive_summary";

    private static readonly string[] ClosedStageNames =
    {
        "تم البيع", "خسارة", "غير مهتم", "Closed Deal", "Lost", "Not Interested"
    };

    private static readonly string[] MarketingRoles =
    {
        "Admin", "GeneralManager", "AccountManager", "SocialManager", "SocialView"
    };

    // المؤشرات التنفيذية = بيانات مالية. الأدوار: Admin و AccountManager بس.
    private static readonly string[] ExecutiveRoles =
    {
        "Admin", "AccountManager"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // عشان العربي يفضل مقروء وميتحولش لـ \uXXXX (ده بيزود التوكنز جداً)
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private readonly IOpportunityService _opportunities;
    private readonly ITaskService _tasks;
    private readonly ICrmDashboardService _dashboard;
    private readonly IInteractionService _interactions;
    private readonly ILeadsCrmService _leads;
    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly IMarketingDashboardService _marketing;
    private readonly IExecutiveCockpitService _cockpit;
    private readonly ILogger<ZakiToolExecutor> _logger;

    public ZakiToolExecutor(
        IOpportunityService opportunities,
        ITaskService tasks,
        ICrmDashboardService dashboard,
        IInteractionService interactions,
        ILeadsCrmService leads,
        IDbContextFactory<db24804Context> dbFactory,
        IMarketingDashboardService marketing,
        IExecutiveCockpitService cockpit,
        ILogger<ZakiToolExecutor> logger)
    {
        _opportunities = opportunities;
        _tasks = tasks;
        _dashboard = dashboard;
        _interactions = interactions;
        _leads = leads;
        _dbFactory = dbFactory;
        _marketing = marketing;
        _cockpit = cockpit;
        _logger = logger;
    }

    /// <summary>البائع: يشوف بياناته هو بس</summary>
    public static bool IsSalesOnly(ClaimsPrincipal user) => user.IsInRole("Sales");

    // ════════════════════════════════════════════════════════════
    // تعريف الأدوات (الموديل بيشوف بس الأدوات المسموحة للمستخدم)
    // ════════════════════════════════════════════════════════════
    public IReadOnlyList<AiToolDefinition> GetDefinitionsFor(ClaimsPrincipal user)
    {
        var list = new List<AiToolDefinition>();

        if (CanViewOpportunities(user)) list.Add(OpenOpportunitiesDefinition());
        if (CanViewTasks(user))
        {
            list.Add(OpenTasksDefinition());
            list.Add(OverdueTasksDefinition());
        }
        if (CanViewDashboard(user)) list.Add(SummaryDefinition());
        if (CanViewInteractions(user)) list.Add(InteractionsDefinition());
        if (CanViewLeads(user)) list.Add(LeadsDefinition());
        if (CanViewMarketing(user)) list.Add(MarketingDefinition());
        if (CanViewExecutive(user)) list.Add(ExecutiveDefinition());

        return list;
    }

    /// <summary>
    /// تنفيذ أداة. بيرجع JSON للموديل + جدول للعرض.
    /// ownEmployeeId = الموظف المربوط باليوزر (مهم للبائع).
    /// </summary>
    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string name, string argumentsJson, int? ownEmployeeId, CancellationToken ct = default)
    {
        // 1) الأداة لازم تكون من اللي المستخدم مسموح بيها
        if (GetDefinitionsFor(user).All(d => d.Name != name))
        {
            return new ZakiToolOutcome(
                ToJson(new Dictionary<string, object?> { ["خطأ"] = "ليس لديك صلاحية لهذه البيانات." }),
                null,
                IsDenied: true);
        }

        // 2) البائع لازم يكون مربوط بموظف، وإلا مفيش بيانات له
        var sales = IsSalesOnly(user);
        var own = sales ? ownEmployeeId : null;
        var isFinanceOrMarketing = name is GetMarketingSummary or GetExecutiveSummary;

        if (sales && own is null && !isFinanceOrMarketing)
        {
            return NoEmployeeLinked();
        }

        // 3) تنفيذ
        var args = ParseArgs(argumentsJson);

        return name switch
        {
            GetOpenOpportunities => await OpenOpportunitiesAsync(args, own),
            GetOpenTasks => await TasksAsync(args, overdueOnly: false, own),
            GetOverdueTasks => await TasksAsync(args, overdueOnly: true, own),
            GetCrmSummary => await SummaryAsync(user),
            GetRecentInteractions => await InteractionsAsync(args, own),
            GetLeads => await LeadsAsync(args, own, ct),
            GetMarketingSummary => await MarketingAsync(args),
            GetExecutiveSummary => await ExecutiveAsync(args),
            _ => new ZakiToolOutcome(
                ToJson(new Dictionary<string, object?> { ["خطأ"] = "أداة غير معروفة." }),
                null)
        };
    }

    // ════════════════════════════════════════════════════════════
    // 1) الفرص المفتوحة
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> OpenOpportunitiesAsync(JsonElement args, int? own)
    {
        var limit = ReadInt(args, "limit", 20, 1, 20);

        // الخدمة بتطبق سكوب تاريخ الاطلاع ونطاق المستخدم. البائع بيتفلتر بموظفه.
        var page = await _opportunities.GetOpportunitiesAsync(new OpportunityFilterDto
        {
            PageNumber = 1,
            PageSize = 100,
            IsActive = true,
            EmployeeId = own,
            SortBy = "CreatedAt",
            SortDescending = true
        });

        var open = page.Items
            .Where(o => !IsClosed(o.StageNameAr, o.StageName))
            .Take(limit)
            .ToList();

        var headers = new[]
        {
            "رقم الفرصة", "العميل", "المرحلة", "القيمة المتوقعة",
            "المسؤول", "تاريخ الإنشاء", "آخر تواصل", "المتابعة القادمة"
        };

        var rows = open.Select(o => (IReadOnlyList<string>)new[]
        {
            o.OpportunityId.ToString(Culture),
            o.ClientName,
            o.StageNameAr ?? o.StageName,
            Money(o.ExpectedValue),
            o.EmployeeName ?? "—",
            Date(o.CreatedAt),
            Date(o.LastContactDate),
            Date(o.NextFollowUpDate)
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["عدد الفرص المعروضة"] = rows.Count,
            ["إجمالي الفرص في نطاقك"] = page.TotalCount,
            ["الفرص"] = ToObjects(headers, rows)
        });

        _logger.LogInformation("Zaki tool {Tool} returned {Count} rows", GetOpenOpportunities, rows.Count);

        return new ZakiToolOutcome(json, rows.Count == 0 ? null : new ZakiTable("الفرص المفتوحة", headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 2) المهام: المفتوحة أو المتأخرة (أداتين منفصلتين)
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> TasksAsync(JsonElement args, bool overdueOnly, int? own)
    {
        var limit = ReadInt(args, "limit", 20, 1, 20);

        // الخدمة بتستبعد المهام المكتملة والملغاة، وبتطبق سكوب المستخدم
        var page = await _tasks.GetTasksAsync(new TaskFilterDto
        {
            PageNumber = 1,
            PageSize = 100,
            IsOverdue = overdueOnly ? true : null,
            AssignedTo = own
        });

        var list = page.Items.Take(limit).ToList();
        var today = DateTime.Today;

        var headers = new[]
        {
            "رقم المهمة", "العميل", "نوع المهمة", "موعد التنفيذ",
            "الحالة", "الأولوية", "المسؤول"
        };

        var rows = list.Select(t => (IReadOnlyList<string>)new[]
        {
            t.TaskId.ToString(Culture),
            t.ClientName ?? "—",
            t.TaskTypeNameAr ?? t.TaskTypeName ?? "—",
            Date(t.DueDate),
            TaskStatusAr(t.Status),
            PriorityAr(t.Priority),
            t.AssignedToName ?? "—"
        }).ToList();

        var overdueInList = list.Count(t => t.DueDate.Date < today);
        var title = overdueOnly ? "المهام المتأخرة" : "المهام المفتوحة";

        var json = ToJson(new Dictionary<string, object?>
        {
            ["نوع القائمة"] = title,
            ["عدد المهام المعروضة"] = rows.Count,
            ["من المعروضة متأخرة"] = overdueInList,
            ["إجمالي المهام في نطاقك"] = page.TotalCount,
            ["المهام"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, rows.Count == 0 ? null : new ZakiTable(title, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 3) ملخص CRM (الأرقام) — للإدارة بس، البائع ملوش
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> SummaryAsync(ClaimsPrincipal user)
    {
        // نفس الطريقة اللي لوحة CRM بتستخدمها
        var role = user.FindFirst(ClaimTypes.Role)?.Value;
        var d = await _dashboard.GetDashboardAsync(user.Identity?.Name ?? "", role, new CrmDashboardFilterDto());

        var headers = new[] { "المؤشر", "القيمة" };

        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "الفرص المفتوحة", d.OpenOpportunities.ToString(Culture) },
            new[] { "إجمالي الفرص", d.TotalOpportunities.ToString(Culture) },
            new[] { "الفرص المكسوبة", d.WonOpportunities.ToString(Culture) },
            new[] { "الفرص الخاسرة", d.LostOpportunities.ToString(Culture) },
            new[] { "قيمة الفرص المفتوحة", Money(d.PipelineValue) },
            new[] { "قيمة الفرص المكسوبة", Money(d.WonValue) },
            new[] { "نسبة التحويل", Pct(d.ConversionRate, signed: false) },
            new[] { "المهام المتأخرة", d.OverdueTasks.ToString(Culture) },
            new[] { "مهام اليوم", d.TodayTasks.ToString(Culture) },
            new[] { "متابعات اليوم", d.FollowUpsToday.ToString(Culture) },
            new[] { "متابعات متأخرة", d.FollowUpsOverdue.ToString(Culture) }
        };

        var json = ToJson(new Dictionary<string, object?>
        {
            ["ملخص CRM"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, new ZakiTable("ملخص CRM", headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 4) آخر التواصلات (مع العميل أو عامة)
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> InteractionsAsync(JsonElement args, int? own)
    {
        var limit = ReadInt(args, "limit", 10, 1, 20);
        var opportunityId = ReadInt(args, "opportunityId", 0, 0, int.MaxValue);

        // الخدمة بتطبق سكوب المستخدم، وبترتيب الأحدث الأول
        var page = await _interactions.GetInteractionsAsync(new InteractionFilterDto
        {
            PageNumber = 1,
            PageSize = limit,
            OpportunityId = opportunityId > 0 ? opportunityId : null,
            EmployeeId = own,
            SortDescending = true
        });

        var headers = new[] { "التاريخ", "العميل", "الموظف", "المرحلة بعد التواصل", "الملخص" };

        var rows = page.Items.Select(i => (IReadOnlyList<string>)new[]
        {
            Date(i.InteractionDate),
            i.ClientName,
            i.EmployeeName ?? "—",
            i.StageAfterNameAr ?? i.StageAfterName ?? "—",
            Truncate(i.Summary, 120)
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["عدد التواصلات المعروضة"] = rows.Count,
            ["التواصلات"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, rows.Count == 0 ? null : new ZakiTable("آخر التواصلات", headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 5) العملاء المحتملين (ملخص، أو المتابعات المتأخرة)
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> LeadsAsync(JsonElement args, int? own, CancellationToken ct)
    {
        var view = ReadString(args, "view") ?? "summary";

        if (view == "upcoming_followups")
            return await UpcomingFollowUpsAsync(args, own, ct);

        if (view is "by_source" or "by_campaign" or "by_form")
            return await LeadBreakdownAsync(view, own, ct);

        if (view == "late_followups")
        {
            var limit = ReadInt(args, "limit", 20, 1, 20);

            var page = await _leads.GetLeadsAsync(new LeadsCrmFilterDto
            {
                LateFollowUpOnly = true,
                AssignedEmployeeId = own,
                PageNumber = 1,
                PageSize = limit
            });

            var headers = new[] { "الاسم", "الحملة", "حالة العميل", "الموظف المسؤول", "مرحلة المشروع" };

            var rows = page.Items.Select(l => (IReadOnlyList<string>)new[]
            {
                l.FullName,
                l.CampaignName ?? "—",
                l.LeadStatus ?? "—",
                l.AssignedEmployeeName ?? "—",
                l.ProjectStage ?? "—"
            }).ToList();

            var title = "متابعات العملاء المحتملين المتأخرة";
            var json = ToJson(new Dictionary<string, object?>
            {
                ["عدد المتابعات المتأخرة"] = page.TotalCount,
                ["العملاء"] = ToObjects(headers, rows)
            });

            return new ZakiToolOutcome(json, rows.Count == 0 ? null : new ZakiTable(title, headers, rows));
        }

        // ملخص الأرقام
        var s = await _leads.GetStatsAsync(new LeadsCrmFilterDto { AssignedEmployeeId = own });

        var summaryHeaders = new[] { "المؤشر", "القيمة" };
        var summaryRows = new List<IReadOnlyList<string>>
        {
            new[] { "إجمالي العملاء المحتملين", s.TotalLeads.ToString(Culture) },
            new[] { "جدد", s.NewLeads.ToString(Culture) },
            new[] { "تم التواصل معهم", s.ContactedLeads.ToString(Culture) },
            new[] { "مؤهلين", s.QualifiedLeads.ToString(Culture) },
            new[] { "تم تحويلهم لعملاء", s.ConvertedLeads.ToString(Culture) },
            new[] { "مرفوضين", s.RejectedLeads.ToString(Culture) },
            new[] { "متابعات متأخرة", s.LateFollowUpLeads.ToString(Culture) },
            new[] { "مكررين", s.DuplicateLeads.ToString(Culture) },
            new[] { "وصلوا اليوم", s.TodayLeads.ToString(Culture) },
            new[] { "وصلوا هذا الأسبوع", s.ThisWeekLeads.ToString(Culture) },
            new[] { "وصلوا هذا الشهر", s.ThisMonthLeads.ToString(Culture) }
        };

        var summaryJson = ToJson(new Dictionary<string, object?>
        {
            ["ملخص العملاء المحتملين"] = ToObjects(summaryHeaders, summaryRows)
        });

        return new ZakiToolOutcome(summaryJson, new ZakiTable("ملخص العملاء المحتملين", summaryHeaders, summaryRows));
    }

    // ── المتابعات القادمة للعملاء المحتملين (من جدول تفاعلات الـ Leads) ──
    private sealed record UpcomingRow(
        string FullName, string? LeadStatus, string? Platform, string? CampaignName,
        DateTime? NextFollowUpDate, string? Summary, int? AssignedEmployeeId);

    private async Task<ZakiToolOutcome> UpcomingFollowUpsAsync(JsonElement args, int? own, CancellationToken ct)
    {
        var days = ReadInt(args, "days", 7, 1, 60);
        var limit = ReadInt(args, "limit", 20, 1, 50);
        var now = DateTime.Now;
        var until = now.AddDays(days);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // نفس شروط الشاشة: متابعة مفتوحة، والليد مش محوّل ولا مرفوض
        var open = from i in db.LeadInteractions.AsNoTracking()
                   join l in db.LeadsCRMs.AsNoTracking() on i.LeadId equals l.LeadId
                   where i.NextFollowUpDate != null && !i.IsCompleted
                         && !l.IsConverted && l.LeadStatus != "محول" && l.LeadStatus != "مرفوض"
                   select new { i, l };

        if (own.HasValue)
            open = open.Where(x => x.l.AssignedEmployeeId == own.Value);

        var overdue = await open.CountAsync(x => x.i.NextFollowUpDate < now, ct);

        var upcoming = open.Where(x => x.i.NextFollowUpDate >= now && x.i.NextFollowUpDate < until);
        var upcomingCount = await upcoming.CountAsync(ct);

        var items = await upcoming
            .OrderBy(x => x.i.NextFollowUpDate)
            .Take(limit)
            .Select(x => new UpcomingRow(
                x.l.FullName, x.l.LeadStatus, x.l.Platform, x.l.CampaignName,
                x.i.NextFollowUpDate, x.i.Summary, x.l.AssignedEmployeeId))
            .ToListAsync(ct);

        var empNames = await ZakiInvoiceQueries.EmployeeNamesAsync(db,
            items.Where(i => i.AssignedEmployeeId.HasValue).Select(i => i.AssignedEmployeeId!.Value), ct);

        var headers = new[] { "العميل", "الحالة", "المصدر", "الحملة", "موعد المتابعة", "الموظف", "آخر ملاحظة" };
        var rows = items.Select(i => (IReadOnlyList<string>)new[]
        {
            i.FullName,
            i.LeadStatus ?? "—",
            i.Platform ?? "—",
            i.CampaignName ?? "—",
            i.NextFollowUpDate.HasValue ? i.NextFollowUpDate.Value.ToString("yyyy-MM-dd HH:mm", Culture) : "—",
            i.AssignedEmployeeId.HasValue ? ZakiInvoiceQueries.NameOf(i.AssignedEmployeeId.Value, empNames) : "—",
            string.IsNullOrWhiteSpace(i.Summary) ? "—" : (i.Summary.Length > 80 ? i.Summary[..80] + "…" : i.Summary)
        }).ToList();

        var title = $"المتابعات القادمة خلال {days} يوم";
        var json = ToJson(new Dictionary<string, object?>
        {
            ["متابعات متأخرة (فاتت ميعادها ولسه مفتوحة)"] = overdue,
            [$"متابعات قادمة خلال {days} يوم"] = upcomingCount,
            ["المتابعات القادمة"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, rows.Count == 0 ? null : new ZakiTable(title, headers, rows));
    }

    // ── تحليل المصادر / الحملات / الفورم: عدد العملاء ونسبة التحويل ──
    private sealed record LeadGroupRow(string? Key, int Total, int Converted, int Rejected);

    private async Task<ZakiToolOutcome> LeadBreakdownAsync(string view, int? own, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // العملاء المكررين مابيتحسبوش (زي الشاشة)
        var leads = db.LeadsCRMs.AsNoTracking().Where(l => !l.IsDuplicate);
        if (own.HasValue)
            leads = leads.Where(l => l.AssignedEmployeeId == own.Value);

        List<LeadGroupRow> groups = view switch
        {
            "by_source" => await leads.GroupBy(l => l.Platform)
                .Select(g => new LeadGroupRow(g.Key, g.Count(), g.Count(x => x.IsConverted), g.Count(x => x.LeadStatus == "مرفوض")))
                .ToListAsync(ct),
            "by_campaign" => await leads.GroupBy(l => l.CampaignName)
                .Select(g => new LeadGroupRow(g.Key, g.Count(), g.Count(x => x.IsConverted), g.Count(x => x.LeadStatus == "مرفوض")))
                .ToListAsync(ct),
            _ => await leads.GroupBy(l => l.FormLanguage)
                .Select(g => new LeadGroupRow(g.Key, g.Count(), g.Count(x => x.IsConverted), g.Count(x => x.LeadStatus == "مرفوض")))
                .ToListAsync(ct)
        };

        if (groups.Count == 0)
            return new ZakiToolOutcome(ToJson(new Dictionary<string, object?> { ["ملاحظة"] = "مفيش عملاء محتملين في نطاقك." }), null);

        var label = view switch
        {
            "by_source" => "المصدر",
            "by_campaign" => "الحملة",
            _ => "الفورم"
        };

        var ordered = groups
            .OrderByDescending(g => g.Converted)
            .ThenByDescending(g => g.Total == 0 ? 0 : (double)g.Converted / g.Total)
            .Take(15)
            .ToList();

        var headers = new[] { label, "عدد العملاء", "تم تحويلهم", "نسبة التحويل", "مرفوضين" };
        var rows = ordered.Select(g => (IReadOnlyList<string>)new[]
        {
            string.IsNullOrWhiteSpace(g.Key) ? "غير محدد" : g.Key!,
            g.Total.ToString(Culture),
            g.Converted.ToString(Culture),
            g.Total == 0 ? "—" : Math.Round(100.0 * g.Converted / g.Total, 1).ToString("0.#", Culture) + "%",
            g.Rejected.ToString(Culture)
        }).ToList();

        var title = view switch
        {
            "by_source" => "أداء مصادر العملاء المحتملين",
            "by_campaign" => "أداء الحملات",
            _ => "أداء الفورمات"
        };

        var json = ToJson(new Dictionary<string, object?>
        {
            ["ملاحظة"] = "المرتب بعدد اللي اتحولوا. النسبة على عينات صغيرة مش دقيقة، فركّز على اللي عنده 10 عملاء فأكتر.",
            ["التفصيل"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, new ZakiTable(title, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 6) مؤشرات التسويق
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> MarketingAsync(JsonElement args)
    {
        var period = ReadString(args, "period") ?? "this_month";
        var today = TodayInCairo().Date;

        var (dateFrom, dateTo) = period switch
        {
            "last_month" => (new DateTime(today.Year, today.Month, 1).AddMonths(-1),
                             new DateTime(today.Year, today.Month, 1).AddDays(-1)),
            "this_year" => (new DateTime(today.Year, 1, 1), today),
            _ => (new DateTime(today.Year, today.Month, 1), today)
        };

        var dash = await _marketing.GetDashboardAsync(dateFrom, dateTo, "all");

        var headers = new[] { "المؤشر", "القيمة", "ملاحظة" };
        var rows = dash.Kpis.Select(k => (IReadOnlyList<string>)new[]
        {
            k.Label,
            k.Value,
            string.IsNullOrWhiteSpace(k.Sub) ? "—" : k.Sub
        }).ToList();

        var periodText = $"{dateFrom:yyyy-MM-dd} إلى {dateTo:yyyy-MM-dd}";
        var title = "مؤشرات التسويق";

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفترة"] = periodText,
            ["المؤشرات"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, rows.Count == 0 ? null : new ZakiTable(title + " — " + periodText, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 7) المؤشرات التنفيذية (Cockpit) — مالية
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> ExecutiveAsync(JsonElement args)
    {
        var period = ReadString(args, "period") ?? "month";
        if (period is not ("today" or "week" or "month" or "quarter" or "year"))
            period = "month";

        var d = await _cockpit.GetCockpitAsync(period);

        var headers = new[] { "المؤشر", "القيمة", "التغيير عن الفترة السابقة" };

        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "المبيعات", Money(d.Sales.Value), Pct(d.Sales.PrevPct, signed: true) },
            new[] { "المحصّل", Money(d.Collected.Value), Pct(d.Collected.PrevPct, signed: true) },
            new[] { "المشتريات", Money(d.Purchases.Value), Pct(d.Purchases.PrevPct, signed: true) },
            new[] { "المصروفات", Money(d.Expenses.Value), Pct(d.Expenses.PrevPct, signed: true) },
            new[] { "صافي النتيجة", Money(d.Net.Value), Pct(d.Net.PrevPct, signed: true) },
            new[] { "رصيد الخزينة", Money(d.CashBalance), "—" },
            new[] { "الفرص المفتوحة", d.PipelineOpen.ToString(Culture), Money(d.PipelineValue) },
            new[] { "فرص راكدة", d.PipelineStalled.ToString(Culture), "—" },
            new[] { "المبيعات المتوقعة", Money(d.ForecastValue), "—" },
            new[] { "قرارات معلقة", d.DecisionsPending.ToString(Culture), "—" }
        };

        var title = string.IsNullOrWhiteSpace(d.PeriodLabel) ? "المؤشرات التنفيذية" : "المؤشرات التنفيذية — " + d.PeriodLabel;

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفترة"] = d.PeriodLabel,
            ["ملخص الفترة"] = Truncate(d.Story, 300),
            ["المؤشرات"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, new ZakiTable(title, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // تعريفات الأدوات (الوصف بيقول للموديل إمتى يستخدمها)
    // ════════════════════════════════════════════════════════════
    private static AiToolDefinition OpenOpportunitiesDefinition() => new(
        GetOpenOpportunities,
        "ترجع الفرص المفتوحة (مش مكسوبة ومش خاسرة) للمستخدم الحالي، بنفس بيانات شاشة الفرص. استخدمها لأي سؤال عن الفرص المفتوحة أو قيمتها أو مراحلها.",
        new
        {
            type = "object",
            properties = new
            {
                limit = new { type = "integer", description = "عدد الفرص المطلوبة من 1 إلى 20", minimum = 1, maximum = 20 }
            }
        });

    private static AiToolDefinition OpenTasksDefinition() => new(
        GetOpenTasks,
        "ترجع المهام المفتوحة (غير المكتملة) للمستخدم الحالي، سواء متأخرة أو لسه في موعدها. استخدمها لأي سؤال عن المهام المفتوحة أو مهام الأسبوع أو مهام معينة. لو السؤال عن المتأخرة بس استخدم get_overdue_tasks.",
        new
        {
            type = "object",
            properties = new
            {
                limit = new { type = "integer", description = "عدد المهام المطلوبة من 1 إلى 20", minimum = 1, maximum = 20 }
            }
        });

    private static AiToolDefinition OverdueTasksDefinition() => new(
        GetOverdueTasks,
        "ترجع المهام المتأخرة بس للمستخدم الحالي (موعدها فات ولسه مش مكتملة). استخدمها دايماً لأي سؤال عن المهام المتأخرة أو المتأخرات أو اللي فاتت مواعيدها.",
        new
        {
            type = "object",
            properties = new
            {
                limit = new { type = "integer", description = "عدد المهام المطلوبة من 1 إلى 20", minimum = 1, maximum = 20 }
            }
        });

    private static AiToolDefinition SummaryDefinition() => new(
        GetCrmSummary,
        "ترجع ملخص أرقام CRM للشركة (للإدارة): عدد الفرص وقيمتها ونسبة التحويل والمهام والمتابعات. استخدمها لأي سؤال عن الأرقام الإجمالية للشركة.",
        new
        {
            type = "object",
            properties = new { }
        });

    private static AiToolDefinition InteractionsDefinition() => new(
        GetRecentInteractions,
        "ترجع آخر التواصلات مع العملاء للمستخدم الحالي، ويمكن تحديد فرصة معينة. استخدمها لأي سؤال عن آخر تواصل أو ماذا اتفقنا مع عميل.",
        new
        {
            type = "object",
            properties = new
            {
                limit = new { type = "integer", description = "عدد التواصلات من 1 إلى 20", minimum = 1, maximum = 20 },
                opportunityId = new { type = "integer", description = "اختياري: رقم الفرصة لو عايز تواصلاتها بس" }
            }
        });

    private static AiToolDefinition LeadsDefinition() => new(
        GetLeads,
        "ملخص العملاء المحتملين (Leads)، والمتابعات المتأخرة والقادمة، وأداء المصادر والحملات والفورمات (عدد العملاء ونسبة التحويل). summary للأرقام، late_followups للمتأخرة، upcoming_followups للقادمة، by_source / by_campaign / by_form للتحليل.",
        new
        {
            type = "object",
            properties = new
            {
                view = new { type = "string", @enum = new[] { "summary", "late_followups", "upcoming_followups", "by_source", "by_campaign", "by_form" }, description = "summary / late_followups / upcoming_followups / by_source / by_campaign / by_form" },
                days = new { type = "integer", description = "عدد الأيام القادمة للمتابعات (1 إلى 60، الافتراضي 7)", minimum = 1, maximum = 60 },
                limit = new { type = "integer", description = "عدد الصفوف (1 إلى 20، والمتابعات حتى 50)", minimum = 1, maximum = 50 }
            }
        });

    private static AiToolDefinition MarketingDefinition() => new(
        GetMarketingSummary,
        "ترجع مؤشرات التسويق (الحملات والقمع والتكلفة والنتائج) لفترة. استخدمها لأي سؤال عن التسويق أو الحملات أو أداء الإعلانات.",
        new
        {
            type = "object",
            properties = new
            {
                period = new { type = "string", @enum = new[] { "this_month", "last_month", "this_year" }, description = "الفترة: this_month أو last_month أو this_year" }
            }
        });

    private static AiToolDefinition ExecutiveDefinition() => new(
        GetExecutiveSummary,
        "ترجع المؤشرات التنفيذية المالية للشركة (المبيعات والتحصيل والمشتريات والمصروفات وصافي النتيجة ورصيد الخزينة والفرص). استخدمها لأي سؤال عن الصورة المالية أو الإدارية العامة.",
        new
        {
            type = "object",
            properties = new
            {
                period = new { type = "string", @enum = new[] { "today", "week", "month", "quarter", "year" }, description = "الفترة: today أو week أو month أو quarter أو year" }
            }
        });

    // ════════════════════════════════════════════════════════════
    // الصلاحيات
    // ════════════════════════════════════════════════════════════
    private static bool CanViewOpportunities(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("Permission", "frmSalesPipeline:View") || user.HasClaim("Permission", "frmOpportunities:View");

    private static bool CanViewTasks(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("Permission", "frmDailyTasks:View") || user.HasClaim("Permission", "frmCrmTasks:View");

    // ملخص الشركة للإدارة بس (البائع ملوش)
    private static bool CanViewDashboard(ClaimsPrincipal user) =>
        !IsSalesOnly(user)
        && (user.IsInRole("Admin") || user.HasClaim("Permission", "frmCrmDashboard:View"));

    private static bool CanViewInteractions(ClaimsPrincipal user) =>
        user.IsInRole("Admin")
        || user.HasClaim("Permission", "frmNewInteraction:View")
        || user.HasClaim("Permission", "frmInteractionsLog:View")
        || user.HasClaim("Permission", "frmInteractions:View");

    private static bool CanViewLeads(ClaimsPrincipal user) =>
        LeadsCrmPermissions.CanView(user);

    private static bool CanViewMarketing(ClaimsPrincipal user) =>
        MarketingRoles.Any(r => user.IsInRole(r));

    // الأدوار: Admin و AccountManager. وكمان أي يوزر ليه صلاحية الشاشة صراحة (frm_ExecutiveCockpit:View).
    private static bool CanViewExecutive(ClaimsPrincipal user) =>
        ExecutiveRoles.Any(r => user.IsInRole(r))
        || user.HasClaim("Permission", "frm_ExecutiveCockpit:View");

    // ════════════════════════════════════════════════════════════
    // مساعدات التنسيق والقراءة
    // ════════════════════════════════════════════════════════════
    private static ZakiToolOutcome NoEmployeeLinked() =>
        new(ToJson(new Dictionary<string, object?>
        {
            ["ملاحظة"] = "حسابك مش مربوط بموظف في النظام، فمش هقدر أعرض بياناتك. كلم الإدارة عشان يتربط حسابك."
        }), null);

    private static bool IsClosed(string? nameAr, string name) =>
        ClosedStageNames.Contains(nameAr ?? "") || ClosedStageNames.Contains(name ?? "");

    private static string TaskStatusAr(string? status) => status switch
    {
        "Pending" => "قيد الانتظار",
        "InProgress" => "جاري التنفيذ",
        "Started" => "جاري التنفيذ",
        "Active" => "نشطة",
        "Completed" => "مكتملة",
        "Cancelled" => "ملغاة",
        _ => status ?? "—"
    };

    private static string PriorityAr(string? priority) => priority switch
    {
        "High" => "عالية",
        "Medium" => "متوسطة",
        "Normal" => "متوسطة",
        "Low" => "منخفضة",
        _ => priority ?? "—"
    };

    private static string Money(decimal? value) =>
        value.HasValue ? value.Value.ToString("#,0", Culture) + " ج.م" : "—";

    private static string Pct(decimal value, bool signed) =>
        (signed && value > 0 ? "+" : "") + value.ToString("0.#", Culture) + "%";

    private static string Date(DateTime? value) =>
        value.HasValue ? value.Value.ToString("yyyy-MM-dd", Culture) : "—";

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return "—";
        return text.Length <= max ? text : text[..max] + "…";
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

    /// <summary>يحوّل الصفوف لقايمة قواميس بالعناوين العربية (عشان الموديل يقرأ أسماء عربية)</summary>
    private static List<Dictionary<string, object?>> ToObjects(string[] headers, IEnumerable<IReadOnlyList<string>> rows) =>
        rows.Select(r =>
        {
            var d = new Dictionary<string, object?>();
            for (var i = 0; i < headers.Length && i < r.Count; i++)
                d[headers[i]] = r[i];
            return d;
        }).ToList();

    private static JsonElement ParseArgs(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static int ReadInt(JsonElement args, string name, int fallback, int min, int max)
    {
        if (args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt32(out var n))
        {
            return Math.Clamp(n, min, max);
        }
        return fallback;
    }

    private static string? ReadString(JsonElement args, string name)
    {
        if (args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String)
        {
            return v.GetString();
        }
        return null;
    }

    private static string ToJson(object value) => JsonSerializer.Serialize(value, JsonOptions);
}
