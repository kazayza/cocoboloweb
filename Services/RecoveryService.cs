using System.Security.Claims;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// خدمة استرداد الفرص الخاسرة:
///  1) التقط الفرص اللي وصلت مرحلة خسارة/غير مهتم (4 / 5) تلقائيًا.
///  2) اسندها تلقائيًا لأقل موظف خدمة عملاء انشغالًا + مهمة + إشعار له.
///  3) سجّل محاولات التواصل بموظف خدمة العملاء الحالي وبنتیجه موحدة.
///  4) نفّذ "العميل راجع" (نفس الفرصة أو فرصة جديدة مرتبطة بالقديمة).
///  5) "رفض نهائي" يُخرج الفرصة من الطابور نهائيًا (بلا إزعاج).
/// تستخدم DbContextFactory (مثيل لكل عملية) — آمنة مع الـ Blazor InteractiveServer.
/// </summary>
public class RecoveryService
{
    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly NotificationService _notifications;
    private readonly IAuditService _audit;
    private readonly ILogger<RecoveryService> _logger;

    // مراحل الخروج من مصدر واحد للحقيقة (CrmStages) — نفس منطق OpportunityService
    private const int LostStageId = CrmStages.LostStageId;
    private const int NotInterestedStageId = CrmStages.NotInterestedStageId;
    private const string CsDepartment = "خدمة العملاء";
    private const string ActiveEmployeeStatus = "نشط"; // أو "Active" حسب لغة الإدخال
    private const string TaskPending = "Pending";

    public RecoveryService(
        IDbContextFactory<db24804Context> dbFactory,
        NotificationService notifications,
        IAuditService audit,
        ILogger<RecoveryService> logger)
    {
        _dbFactory = dbFactory;
        _notifications = notifications;
        _audit = audit;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════
    //  لوحة الإحصائيات
    // ═══════════════════════════════════════════════════════════
    public async Task<RecoveryStatsDto> GetStatsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var now = DateTime.Now;
        var monthStart = new DateTime(now.Year, now.Month, 1);

        var lost = await db.SalesOpportunities.AsNoTracking()
            .Where(o => (o.StageId == LostStageId || o.StageId == NotInterestedStageId)
                && o.IsActive
                && (o.IsRecoveryRejected == null || o.IsRecoveryRejected == false)) // NULL = ليس مرفوضًا
            .Select(o => new { o.OpportunityId, o.ExpectedValue })
            .ToListAsync();

        var lostIds = lost.Select(o => o.OpportunityId).ToList();

        var assignedIds = new HashSet<int>();
        if (lostIds.Count > 0)
        {
            assignedIds = (await db.CrmTasks.AsNoTracking()
                .Where(t => t.OpportunityId.HasValue
                    && lostIds.Contains(t.OpportunityId.Value)
                    && t.TaskScope == "Recovery"   // ⭐ مهام الاسترداد فقط (متسق مع الفهرس الفريد)
                    && t.Status == TaskPending
                    && t.IsActive)
                .Select(t => t.OpportunityId!.Value)
                .Distinct()
                .ToListAsync()).ToHashSet();
        }

        var revived = await db.CustomerInteractions.AsNoTracking()
            .Where(i => i.InteractionDate >= monthStart
                && i.StageBeforeId.HasValue
                && (i.StageBeforeId == LostStageId || i.StageBeforeId == NotInterestedStageId)
                && i.StageAfterId.HasValue
                && i.StageAfterId != LostStageId
                && i.StageAfterId != NotInterestedStageId)
            .Select(i => i.OpportunityId)   // ⭐ فرص مُستردة فعلًا (Distinct) — وليس عدد سجلات العودة
            .Distinct()
            .CountAsync();

        // عدد الفرص المُسندة التي لم يُسجَّل عليها أي تواصل (والفرصة ما زالت في مرحلة الخسارة)
        var uncontacted = 0;
        if (lostIds.Count > 0)
        {
            var contacted = await db.CustomerInteractions.AsNoTracking()
                .Where(i => lostIds.Contains(i.OpportunityId)
                    && i.StageBeforeId.HasValue
                    && (i.StageBeforeId == LostStageId || i.StageBeforeId == NotInterestedStageId)
                    && i.StageAfterId.HasValue
                    && (i.StageAfterId == LostStageId || i.StageAfterId == NotInterestedStageId)
                    && !i.Summary.StartsWith("نقل تلقائي"))
                .Select(i => i.OpportunityId)
                .Distinct()
                .ToListAsync();
            var contactedSet = contacted.ToHashSet();
            uncontacted = lost.Count(o => assignedIds.Contains(o.OpportunityId)
                && !contactedSet.Contains(o.OpportunityId));
        }

        return new RecoveryStatsDto
        {
            LostCount = lost.Count,
            LostValue = lost.Sum(o => o.ExpectedValue ?? 0),
            UnassignedCount = lost.Count(o => !assignedIds.Contains(o.OpportunityId)),
            RevivedThisMonth = revived,
            UncontactedCount = uncontacted
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  طابور الفرص الخاسرة (مع مزامنة تلقائية للمستجد)
    // ═══════════════════════════════════════════════════════════
    // ═══════════════════════════════════════════════════════════
    //  طابور الفرص الخاسرة — تحميل تدريجي (صفحة صفحة)
    //  يُعاد الكشف عن المستجد أولًا، ثم تُحسب الصفحة المطلوبة فقط
    // ═══════════════════════════════════════════════════════════
    public async Task<LostRecoveryPageDto> GetQueueAsync(
        LostRecoveryFilterDto filter, int pageIndex, int pageSize, string? username = null)
    {
        if (pageIndex < 1) pageIndex = 1;
        if (pageSize < 1) pageSize = 12;

        await using var db = await _dbFactory.CreateDbContextAsync();

        // نبدأ من جدول الفرص نفسه حتى تتوفر أعمدة الاسترداد + الاستبعادات مباشرة
        var q = db.SalesOpportunities.AsNoTracking()
            .Where(o => (o.StageId == LostStageId || o.StageId == NotInterestedStageId)
                && o.IsActive
                && (o.IsRecoveryRejected == null || o.IsRecoveryRejected == false)); // NULL = ليس مرفوضًا

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();

            // نجيب معرّفات العملاء المطابقة أولًا (استعلام واحد على العملاء بدل شرط متداخل لكل فرصة)
            var matched = await db.Parties.AsNoTracking()
                .Where(p => (p.PartyName != null && p.PartyName.Contains(s))
                    || (p.Phone != null && p.Phone.Contains(s))
                    || (p.Phone2 != null && p.Phone2.Contains(s)))
                .Select(p => p.PartyId)
                .ToListAsync();

            if (matched.Count == 0)
            {
                return new LostRecoveryPageDto
                {
                    Items = new List<LostRecoveryItemDto>(),
                    TotalCount = 0,
                    PageIndex = pageIndex,
                    PageSize = pageSize,
                    HasMore = false
                };
            }

            q = q.Where(o => matched.Contains(o.PartyId));
        }

        if (filter.Kind == "lost")
            q = q.Where(o => o.StageId == LostStageId);
        else if (filter.Kind == "notinterested")
            q = q.Where(o => o.StageId == NotInterestedStageId);

        if (filter.MinValue.HasValue)
            q = q.Where(o => o.ExpectedValue.HasValue && o.ExpectedValue >= filter.MinValue.Value);

        if (filter.CandidateOnly == true)
            q = q.Where(o => o.IsRecoveryCandidate == true);

        int mineEmpId = 0;
        if (filter.MineOnly)
        {
            mineEmpId = await ResolveEmployeeByUsernameAsync(username);
            if (mineEmpId == 0)
                return new LostRecoveryPageDto
                {
                    Items = new List<LostRecoveryItemDto>(),
                    TotalCount = 0,
                    PageIndex = 1,
                    PageSize = pageSize,
                    HasMore = false
                };

            q = q.Where(o => db.CrmTasks.Any(t => t.OpportunityId == o.OpportunityId
                && t.TaskScope == "Recovery"   // ⭐ مهام الاسترداد فقط — ليست متابعات المبيعات العادية
                && t.Status == TaskPending && t.IsActive && t.AssignedTo == mineEmpId));
        }

        // فلتر "متأخرة فقط": فات موعد المتابعة المحدد
        if (filter.LateOnly == true)
            q = q.Where(o => o.NextFollowUpDate.HasValue
                && o.NextFollowUpDate.Value.Date < DateTime.Today);

        var total = await q.CountAsync();

        // الترتيب حسب اختيار المستخدم
        switch (filter.SortBy)
        {
            case "recent": // الأحدث إغلاقًا
                q = q.OrderByDescending(o => o.ClosedAt)
                    .ThenByDescending(o => o.OpportunityId);
                break;
            case "days": // الأقدم إغلاقًا = الأكثر تأخرًا
                q = q.OrderBy(o => o.ClosedAt)
                    .ThenByDescending(o => o.OpportunityId);
                break;
            case "followup": // الأقرب موعد متابعة (اللي مفيهاش موعد في الآخر)
                q = q.OrderBy(o => o.NextFollowUpDate == null)
                    .ThenBy(o => o.NextFollowUpDate)
                    .ThenByDescending(o => o.OpportunityId);
                break;
            default: // value — الأعلى قيمة
                q = q.OrderByDescending(o => o.ExpectedValue)
                    .ThenByDescending(o => o.OpportunityId);
                break;
        }

        var rows = await q
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.OpportunityId,
                o.PartyId,
                o.StageId,
                o.ExpectedValue,
                o.InterestedProduct,
                o.ClosedAt,
                o.LostReasonId,
                o.LostNotes,
                o.IsRecoveryCandidate,
                o.RecoveryNotes,
                o.LastContactDate,
                o.NextFollowUpDate
            })
            .ToListAsync();

        var result = new LostRecoveryPageDto
        {
            Items = new List<LostRecoveryItemDto>(),
            TotalCount = total,
            PageIndex = pageIndex,
            PageSize = pageSize,
            HasMore = pageIndex * pageSize < total
        };

        if (rows.Count == 0) return result;

        var ids = rows.Select(r => r.OpportunityId).ToList();

        // بيانات العرض (اسم العميل / الهاتف / المرحلة / المندوب) من الـ View — للصفحة فقط
        var views = await db.VwSalesOpportunities.AsNoTracking()
            .Where(v => ids.Contains(v.OpportunityId))
            .ToListAsync();
        var viewMap = views.ToDictionary(v => v.OpportunityId);

        // أسباب الخسارة (عربية) للصفحة
        var reasonIds = rows.Where(r => r.LostReasonId.HasValue)
            .Select(r => r.LostReasonId!.Value).Distinct().ToList();
        var reasonNames = new Dictionary<int, string>();
        if (reasonIds.Count > 0)
        {
            reasonNames = await db.LostReasons.AsNoTracking()
                .Where(r => reasonIds.Contains(r.LostReasonId))
                .ToDictionaryAsync(r => r.LostReasonId, r => r.ReasonNameAr ?? r.ReasonName);
        }

        // مهام الاسترداد المفتوحة على هذه الصفحة فقط
        var tasks = await db.CrmTasks.AsNoTracking()
            .Where(t => t.OpportunityId.HasValue && ids.Contains(t.OpportunityId.Value)
                && t.Status == TaskPending && t.IsActive)
            .Select(t => new { t.OpportunityId, t.AssignedTo })
            .ToListAsync();
        var taskByOpp = tasks.GroupBy(t => t.OpportunityId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var empIds = tasks.Select(t => t.AssignedTo).Distinct().ToList();
        var empNames = new Dictionary<int, string>();
        if (empIds.Count > 0)
        {
            empNames = await db.Employees.AsNoTracking()
                .Where(e => empIds.Contains(e.EmployeeId))
                .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName);
        }

        // آخر تواصل لخدمة العملاء (تواصل تم والفرصة ما زالت في مرحلة الخسارة)
        var lastCs = await GetLastCsContactsAsync(db, ids);

        foreach (var r in rows)
        {
            if (!viewMap.TryGetValue(r.OpportunityId, out var v)) continue;
            var task = taskByOpp.TryGetValue(r.OpportunityId, out var tk) ? tk : null;
            (DateTime Date, string? ByName, string? Summary)? lc = null;
            if (lastCs.TryGetValue(r.OpportunityId, out var lcVal)) lc = lcVal;

            result.Items.Add(new LostRecoveryItemDto
            {
                OpportunityId = r.OpportunityId,
                PartyId = r.PartyId,
                ClientName = v.ClientName ?? "",
                Phone = v.Phone1,
                StageId = r.StageId,
                StageNameAr = v.StageNameAr ?? v.StageName ?? "خسارة",
                StageColor = v.StageColor ?? "#94a3b8",
                IsNotInterested = r.StageId == NotInterestedStageId,
                ExpectedValue = r.ExpectedValue,
                InterestedProduct = r.InterestedProduct,
                ClosedAt = r.ClosedAt,
                DaysSinceClosed = r.ClosedAt.HasValue ? (DateTime.Today - r.ClosedAt.Value.Date).Days : 0,
                LostReasonNameAr = r.LostReasonId.HasValue && reasonNames.TryGetValue(r.LostReasonId.Value, out var rn) ? rn : null,
                LostNotes = r.LostNotes,
                IsRecoveryCandidate = r.IsRecoveryCandidate,
                RecoveryNotes = r.RecoveryNotes,
                PreviousEmployeeName = v.EmployeeName,
                RecoveryEmployeeId = task?.AssignedTo,
                RecoveryEmployeeName = task != null && empNames.TryGetValue(task.AssignedTo, out var en) ? en : null,
                LastContactDate = r.LastContactDate,
                NextFollowUpDate = r.NextFollowUpDate,
                LastCsDate = lc?.Date,
                LastCsBy = lc?.ByName,
                LastCsSummary = lc?.Summary,
                IsFollowUpOverdue = task != null && r.NextFollowUpDate.HasValue
                    && r.NextFollowUpDate.Value.Date < DateTime.Today
            });
        }

        return result;
    }

    // آخر تواصل مسجل لخدمة العملاء لكل فرصة (بينما هي في مرحلة الخسارة)
    private async Task<Dictionary<int, (DateTime Date, string? ByName, string? Summary)>>
        GetLastCsContactsAsync(db24804Context db, List<int> opportunityIds)
    {
        var result = new Dictionary<int, (DateTime, string?, string?)>();
        var closedStages = new[] { LostStageId, NotInterestedStageId };

        var ints = await db.CustomerInteractions.AsNoTracking()
            .Where(i => opportunityIds.Contains(i.OpportunityId)
                && i.StageBeforeId.HasValue && closedStages.Contains(i.StageBeforeId.Value)
                && i.StageAfterId.HasValue && closedStages.Contains(i.StageAfterId.Value)
                && !i.Summary.StartsWith("نقل تلقائي"))
            .OrderBy(i => i.InteractionDate)
            .Select(i => new { i.OpportunityId, i.InteractionDate, i.Summary, i.CreatedBy })
            .ToListAsync();
        if (ints.Count == 0) return result;

        var lastPerOpp = ints.GroupBy(i => i.OpportunityId)
            .ToDictionary(g => g.Key, g => g.Last());

        var creators = lastPerOpp.Values.Select(x => x.CreatedBy)
            .Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        var userNames = new Dictionary<string, string>();
        if (creators.Count > 0)
        {
            userNames = await db.Users.AsNoTracking()
                .Where(u => creators.Contains(u.Username))
                .ToDictionaryAsync(u => u.Username, u => u.FullName);
        }

        foreach (var kv in lastPerOpp)
        {
            var rec = kv.Value;
            var by = !string.IsNullOrWhiteSpace(rec.CreatedBy)
                     && userNames.TryGetValue(rec.CreatedBy, out var fn)
                ? fn : rec.CreatedBy;
            result[kv.Key] = (rec.InteractionDate, by, StripChannelPrefix(rec.Summary));
        }
        return result;
    }

    // يشيل البادئة [قناة] من الملخص عند العرض
    private static string? StripChannelPrefix(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary)) return summary;
        var t = summary.Trim();
        if (t.StartsWith("[") && t.Contains(']'))
            t = t[(t.IndexOf(']') + 1)..].Trim();
        return string.IsNullOrWhiteSpace(t) ? summary : t;
    }

    // ═══════════════════════════════════════════════════════════
    // موظف اليوزر الحالي — يُمرَّر اسم المستخدم من الشاشة كبارامتر
    private async Task<int> ResolveEmployeeByUsernameAsync(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return 0;
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Users.AsNoTracking()
            .Where(u => u.Username == username && u.EmployeeId.HasValue)
            .Select(u => u.EmployeeId!.Value)
            .FirstOrDefaultAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  حالة طابور الاسترداد — تُحفظ في الـ Scoped service حتى
    //  تثبت الفلاتر ومكان "تحميل المزيد" عند التنقل خارج الصفحة والعودة
    // ═══════════════════════════════════════════════════════════
    public RecoveryQueueState QueueState { get; } = new();

    // صيانة تُنفَّذ مرة واحدة عند الدخول/التحديث الصريح فقط:
    // توزيع الخسائر الجديدة + تذكير المتأخرين (بدون تكرار خلف كل استعلام)
    public async Task RunQueueMaintenanceAsync()
    {
        await SyncNewLossesAsync();
        await SendOverdueRemindersAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  مزامنة المستجد: مهمة + إشعار لأقل موظف خدمة عملاء انشغالًا
    // ═══════════════════════════════════════════════════════════
    public async Task SyncNewLossesAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var lost = await db.SalesOpportunities.AsNoTracking()
            .Where(o => (o.StageId == LostStageId || o.StageId == NotInterestedStageId) && o.IsActive)
            .Select(o => new { o.OpportunityId, o.IsRecoveryRejected })
            .ToListAsync();
        if (lost.Count == 0) return;

        // استبعد المرفوضين نهائيًا فقط — لا إشعارات ولا إزعاج
        var activeLost = lost.Where(o => o.IsRecoveryRejected != true).Select(o => o.OpportunityId).ToList();
        if (activeLost.Count == 0) return;

        var queued = await db.CrmTasks.AsNoTracking()
            .Where(t => t.OpportunityId.HasValue
                && activeLost.Contains(t.OpportunityId.Value)
                && t.TaskScope == "Recovery"   // ⭐ مهام الاسترداد فقط (متسق مع الفهرس الفريد)
                && t.Status == TaskPending
                && t.IsActive)
            .Select(t => t.OpportunityId!.Value)
            .Distinct()
            .ToListAsync();

        var need = activeLost.Except(queued).ToList();
        if (need.Count == 0) return;

        // موظفو خدمة العملاء النشطون ولهم حساب نظام
        var employees = await db.Employees.AsNoTracking()
            .Where(e => e.Department == CsDepartment
                && (e.Status == ActiveEmployeeStatus || e.Status == "Active"))
            .Select(e => new { e.EmployeeId, e.FullName })
            .ToListAsync();
        if (employees.Count == 0) return;

        var empIds = employees.Select(e => e.EmployeeId).ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => u.EmployeeId.HasValue && empIds.Contains(u.EmployeeId.Value) && u.IsActive == true)
            .Select(u => new { u.EmployeeId, u.Username })
            .ToListAsync();
        if (users.Count == 0) return;

        var userByEmp = users.ToDictionary(u => u.EmployeeId!.Value, u => u.Username!);
        var csEmps = employees.Where(e => userByEmp.ContainsKey(e.EmployeeId)).ToList();
        if (csEmps.Count == 0) return;

        // الحمل الحالي (مهام مفتوحة) لكل موظف خدمة عملاء
        var loads = await db.CrmTasks.AsNoTracking()
            .Where(t => t.Status == TaskPending && t.IsActive && empIds.Contains(t.AssignedTo))
            .GroupBy(t => t.AssignedTo)
            .Select(g => new { EmpId = g.Key, Cnt = g.Count() })
            .ToDictionaryAsync(x => x.EmpId, x => x.Cnt);

        var now = DateTime.Now;
        var created = 0;

        // ⭐ قواميس دفعية بدل 4 استعلامات لكل فرصة — إلغاء N+1 كليًا
        var oppRows = await db.SalesOpportunities.AsNoTracking()
            .Where(o => need.Contains(o.OpportunityId))
            .Select(o => new { o.OpportunityId, o.PartyId, o.StageId, o.ExpectedValue, o.LostReasonId })
            .ToListAsync();
        var partyIdList = oppRows.Select(o => o.PartyId).Distinct().ToList();
        var partyMap = partyIdList.Count == 0
            ? new Dictionary<int, string>()
            : await db.Parties.AsNoTracking()
                .Where(p => partyIdList.Contains(p.PartyId))
                .ToDictionaryAsync(p => p.PartyId, p => p.PartyName ?? $"عميل #{p.PartyId}");
        var stageIdList = oppRows.Select(o => o.StageId).Distinct().ToList();
        var stageMap = await db.SalesStages.AsNoTracking()
            .Where(s => stageIdList.Contains(s.StageId))
            .ToDictionaryAsync(s => s.StageId, s => s.StageNameAr ?? s.StageName ?? "خسارة");
        var reasonIdList = oppRows.Where(o => o.LostReasonId.HasValue).Select(o => o.LostReasonId!.Value).Distinct().ToList();
        var reasonMap = reasonIdList.Count == 0
            ? new Dictionary<int, string>()
            : await db.LostReasons.AsNoTracking()
                .Where(r => reasonIdList.Contains(r.LostReasonId))
                .ToDictionaryAsync(r => r.LostReasonId, r => r.ReasonNameAr ?? r.ReasonName ?? "سبب غير محدد");

        foreach (var row in oppRows)
        {
            // الموظف الأقل انشغالًا
            var chosen = csEmps
                .OrderBy(e => loads.TryGetValue(e.EmployeeId, out var cl) ? cl : 0)
                .ThenBy(e => e.EmployeeId)
                .First();

            var partyName = partyMap.TryGetValue(row.PartyId, out var pn) ? pn : $"عميل #{row.PartyId}";
            var stageAr = stageMap.TryGetValue(row.StageId, out var st) ? st : "خسارة";
            var reason = row.LostReasonId.HasValue && reasonMap.TryGetValue(row.LostReasonId.Value, out var rn)
                ? rn : null;

            db.CrmTasks.Add(BuildRecoveryTask(row.OpportunityId, row.PartyId, row.ExpectedValue,
                row.LostReasonId, partyName, reason, chosen.EmployeeId, now));

            try
            {
                // ⭐ حفظ لكل فرصة على حدة: فشل واحد (سباق) لا يُسقط باقي الدفعة
                await db.SaveChangesAsync();
                created++;
                loads[chosen.EmployeeId] = loads.TryGetValue(chosen.EmployeeId, out var c2) ? c2 + 1 : 1;

                await _notifications.AddAsync(
                    "🔁 فرصة خاسرة بانتظار الاسترداد",
                    $"العميل {partyName} — {stageAr} بقيمة {(row.ExpectedValue ?? 0):N0} ج.م. برجاء التواصل خلال 24 ساعة.",
                    userByEmp[chosen.EmployeeId],
                    "RecoverySystem",
                    "crm/lost-recovery",
                    null,
                    null);

                await _audit.LogAsync("CrmTasks", "Recovery/AutoAssign",
                    row.OpportunityId.ToString(),
                    null,
                    new { OpportunityId = row.OpportunityId, PartyId = row.PartyId, AssignedTo = chosen.EmployeeId, AssignedToName = chosen.FullName, StageId = row.StageId, Value = row.ExpectedValue, Source = "SyncNewLosses" },
                    "RecoverySystem");
            }
            catch (DbUpdateException dex) when (IsUniqueViolation(dex))
            {
                // ⭐ سباق محسوم بالفهرس الفريد: جلسة أخرى أسندت قبلنا — استبعد المهمة المضافة وتابع الباقي
                foreach (var entry in db.ChangeTracker.Entries<CrmTask>()
                    .Where(en => en.State == EntityState.Added))
                    entry.State = EntityState.Detached;
                _logger.LogInformation(dex, "SyncNewLosses: opportunity {OpportunityId} assigned concurrently", row.OpportunityId);
            }
        }

        if (created > 0)
            _logger.LogInformation("SyncNewLosses: created {Count} recovery task(s)", created);
    }

    // ═══════════════════════════════════════════════════════════
    //  توزيع فوري لفرصة واحدة لحظة إغلاقها بالخسارة
    //  يُستدعى من مسارات الإغلاق الفعلية (التفاعل السريع / نقل المرحلة / الحفظ)
    //  حتى يصل إشعار موظف خدمة العملاء فورًا — وليس عند فتح الشاشة فقط.
    // ═══════════════════════════════════════════════════════════
    public async Task EnsureRecoveryAssignmentAsync(int opportunityId)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var opp = await db.SalesOpportunities.AsNoTracking()
                .Where(o => o.OpportunityId == opportunityId)
                .Select(o => new
                {
                    o.PartyId,
                    o.StageId,
                    o.ExpectedValue,
                    o.LostReasonId,
                    o.IsRecoveryRejected,
                    o.IsActive
                })
                .FirstOrDefaultAsync();
            if (opp == null) return;
            if (opp.IsActive != true) return;
            if (opp.StageId != LostStageId && opp.StageId != NotInterestedStageId) return;
            if (opp.IsRecoveryRejected == true) return; // رفض نهائي — لا إزعاج إطلاقًا

            // ⭐ معاملة + فحص داخلي: لو وصل إغلاقان متزامنان لا يُنشأ إلا إسناد واحد
            await using var tx = await db.Database.BeginTransactionAsync();

            // مسبقًا عليه مهمة متابعة مفتوحة؟ (مهام الاسترداد فقط — لا تمنعها متابعات المبيعات)
            var hasTask = await db.CrmTasks.AsNoTracking()
                .AnyAsync(t => t.OpportunityId == opportunityId
                    && t.TaskScope == "Recovery"
                    && t.Status == TaskPending && t.IsActive);
            if (hasTask) return; // الخروج هنا يتراجع تلقائيًا (لا تغييرات)

            // موظفو خدمة العملاء النشطون ولهم حساب نظام
            var employees = await db.Employees.AsNoTracking()
                .Where(e => e.Department == CsDepartment
                    && (e.Status == ActiveEmployeeStatus || e.Status == "Active"))
                .Select(e => new { e.EmployeeId, e.FullName })
                .ToListAsync();
            if (employees.Count == 0) return;

            var empIds = employees.Select(e => e.EmployeeId).ToList();
            var users = await db.Users.AsNoTracking()
                .Where(u => u.EmployeeId.HasValue && empIds.Contains(u.EmployeeId.Value) && u.IsActive == true)
                .Select(u => new { u.EmployeeId, u.Username })
                .ToListAsync();
            if (users.Count == 0) return;

            var userByEmp = users.ToDictionary(u => u.EmployeeId!.Value, u => u.Username!);
            var csEmps = employees.Where(e => userByEmp.ContainsKey(e.EmployeeId)).ToList();
            if (csEmps.Count == 0) return;

            // الحمل الحالي لكل موظف خدمة عملاء
            var loads = await db.CrmTasks.AsNoTracking()
                .Where(t => t.Status == TaskPending && t.IsActive && empIds.Contains(t.AssignedTo))
                .GroupBy(t => t.AssignedTo)
                .Select(g => new { EmpId = g.Key, Cnt = g.Count() })
                .ToDictionaryAsync(x => x.EmpId, x => x.Cnt);

            var chosen = csEmps
                .OrderBy(e => loads.TryGetValue(e.EmployeeId, out var c) ? c : 0)
                .ThenBy(e => e.EmployeeId)
                .First();

            var partyName = await db.Parties.AsNoTracking()
                .Where(p => p.PartyId == opp.PartyId)
                .Select(p => p.PartyName)
                .FirstOrDefaultAsync() ?? $"عميل #{opp.PartyId}";

            var stageAr = await db.SalesStages.AsNoTracking()
                .Where(s => s.StageId == opp.StageId)
                .Select(s => s.StageNameAr ?? s.StageName)
                .FirstOrDefaultAsync() ?? "خسارة";

            var reason = opp.LostReasonId.HasValue
                ? await db.LostReasons.AsNoTracking()
                    .Where(r => r.LostReasonId == opp.LostReasonId)
                    .Select(r => r.ReasonNameAr ?? r.ReasonName)
                    .FirstOrDefaultAsync()
                : null;

            var now = DateTime.Now;
            db.CrmTasks.Add(BuildRecoveryTask(opportunityId, opp.PartyId, opp.ExpectedValue,
                opp.LostReasonId, partyName, reason, chosen.EmployeeId, now));

            await db.SaveChangesAsync();
            await tx.CommitAsync();

            await _notifications.AddAsync(
                "🔁 فرصة خاسرة بانتظار الاسترداد",
                $"العميل {partyName} — {stageAr} بقيمة {(opp.ExpectedValue ?? 0):N0} ج.م. برجاء التواصل خلال 24 ساعة.",
                userByEmp[chosen.EmployeeId],
                "RecoverySystem",
                "crm/lost-recovery",
                null,
                null);

            // ⭐ Audit: توثيق الإسناد الفوري (سطر واحد لكل إسناد)
            await _audit.LogAsync("CrmTasks", "Recovery/AutoAssign",
                opportunityId.ToString(),
                null,
                new { OpportunityId = opportunityId, PartyId = opp.PartyId, AssignedTo = chosen.EmployeeId, AssignedToName = chosen.FullName, StageId = opp.StageId, Value = opp.ExpectedValue, Source = "EnsureRecoveryAssignment" },
                "RecoverySystem");
        }
        catch (DbUpdateException dex) when (IsUniqueViolation(dex))
        {
            // ⭐ سباق محسوم بالفهرس الفريد — جلسة أخرى أسندت قبلنا: متوقع وليس خطأ
            _logger.LogInformation(dex, "EnsureRecoveryAssignment: opportunity {OpportunityId} assigned concurrently", opportunityId);
        }
        catch (Exception ex)
        {
            // لا نكسر تدفّق إغلاق الفرصة أبدًا — لكن الفشل لا يمر بصمت بعد الآن
            _logger.LogError(ex, "EnsureRecoveryAssignmentAsync failed for opportunity {OpportunityId}", opportunityId);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  تذكير آلي: متابعات استرداد تجاوزت موعدها
    //  (لا يتكرر — يُرسل مرة واحدة ثم يُغلق التذكير على المهمة)
    // ═══════════════════════════════════════════════════════════
    public async Task SendOverdueRemindersAsync()
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var tasks = await db.CrmTasks
                .Where(t => t.TaskScope == "Recovery"
                    && t.Status == TaskPending
                    && t.IsActive
                    && t.ReminderEnabled
                    && t.DueDate < DateTime.Now)
                .ToListAsync();
            if (tasks.Count == 0) return;

            var empIds = tasks.Select(t => t.AssignedTo).Distinct().ToList();
            var users = await db.Users.AsNoTracking()
                .Where(u => u.EmployeeId.HasValue && empIds.Contains(u.EmployeeId.Value) && u.IsActive == true)
                .Select(u => new { u.EmployeeId, u.Username })
                .ToListAsync();
            var userByEmp = users
                .Where(u => u.EmployeeId.HasValue)
                .ToDictionary(u => u.EmployeeId!.Value, u => u.Username!);
            if (userByEmp.Count == 0) return;

            var partyIds = tasks.Where(t => t.PartyId.HasValue).Select(t => t.PartyId!.Value).Distinct().ToList();
            var partyMap = partyIds.Count == 0
                ? new Dictionary<int, string>()
                : await db.Parties.AsNoTracking()
                    .Where(p => partyIds.Contains(p.PartyId))
                    .ToDictionaryAsync(p => p.PartyId, p => p.PartyName);

            foreach (var t in tasks)
            {
                // ⭐ بلا حساب نظام؟ لا نغلق التذكير (يبقى "متأخرة" ظاهرًا في الطابور) ونُسجّل التحذير
                if (!userByEmp.TryGetValue(t.AssignedTo, out var uname))
                {
                    _logger.LogWarning("Overdue recovery task {TaskId} skipped: no active system account for employee {EmployeeId}", t.TaskId, t.AssignedTo);
                    continue;
                }

                t.ReminderEnabled = false; // نغلق التذكير فقط بعد إرساله فعلًا — لا تكرار لنفس الموعد المتأخر

                var partyName = t.PartyId.HasValue && partyMap.TryGetValue(t.PartyId.Value, out var pn)
                    ? pn
                    : (t.OpportunityId.HasValue ? $"فرصة #{t.OpportunityId}" : "عميل");

                await _notifications.AddAsync(
                    "⏰ متابعة استرداد متأخرة",
                    $"العميل {partyName} — كان موعد المتابعة {t.DueDate:yyyy/MM/dd}. برجاء التواصل اليوم لإتمام الاسترداد.",
                    uname,
                    "RecoverySystem",
                    "crm/lost-recovery",
                    null,
                    null);
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // لا نكسر تدفق العمل أبدًا — لكن الفشل لا يمر بصمت بعد الآن
            _logger.LogError(ex, "SendOverdueRemindersAsync failed");
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  موظف حساب اليوزر الحالي (لشاشة المهام): رقمه + قسمه
    // ═══════════════════════════════════════════════════════════
    public async Task<(int EmployeeId, string Department)?> GetCurrentUserEmployeeAsync(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var empId = await db.Users.AsNoTracking()
            .Where(u => u.Username == username && u.EmployeeId.HasValue)
            .Select(u => u.EmployeeId!.Value)
            .FirstOrDefaultAsync();
        if (empId == 0) return null;
        var dept = await db.Employees.AsNoTracking()
            .Where(e => e.EmployeeId == empId)
            .Select(e => e.Department)
            .FirstOrDefaultAsync();
        return (empId, dept ?? "");
    }

    // ═══════════════════════════════════════════════════════════
    //  موظفو خدمة العملاء (لقائمة فلتر التقرير)
    // ═══════════════════════════════════════════════════════════
    public async Task<List<RecoveryEmployeeOptionDto>> GetCustomerServiceEmployeesLightAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Employees.AsNoTracking()
            .Where(e => e.Department == CsDepartment
                && (e.Status == ActiveEmployeeStatus || e.Status == "Active"))
            .OrderBy(e => e.FullName)
            .Select(e => new RecoveryEmployeeOptionDto { EmployeeId = e.EmployeeId, FullName = e.FullName })
            .ToListAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  تقرير الاسترداد الشامل (P4)
    // ═══════════════════════════════════════════════════════════
    public async Task<RecoveryReportResultDto> GetRecoveryReportAsync(RecoveryReportFilterDto f, int pageIndex = 1, int pageSize = 50)
    {
        var result = new RecoveryReportResultDto { Rows = new List<RecoveryReportRowDto>() };
        await using var db = await _dbFactory.CreateDbContextAsync();

        // 1) الفرص الحالية الخاسرة / غير المهتم (النشطة) = حالات مفتوحة في الاسترداد
        var lostIds = (await db.SalesOpportunities.AsNoTracking()
            .Where(o => (o.StageId == LostStageId || o.StageId == NotInterestedStageId) && o.IsActive)
            .Select(o => o.OpportunityId)
            .ToListAsync()).ToHashSet();

        // 2) سجل "العودة من الخسارة": تفاعلات بدأت في 4/5 وانتهت في مرحلة بيع (مُسترد)
        var revivedRows = await db.CustomerInteractions.AsNoTracking()
            .Where(i => i.StageBeforeId.HasValue
                && (i.StageBeforeId == LostStageId || i.StageBeforeId == NotInterestedStageId)
                && i.StageAfterId.HasValue
                && i.StageAfterId != LostStageId && i.StageAfterId != NotInterestedStageId)
            .Select(i => new { i.OpportunityId, i.InteractionDate, i.CreatedBy })
            .ToListAsync();

        var revivedByOpp = revivedRows
            .GroupBy(r => r.OpportunityId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.InteractionDate));

        var allIds = lostIds.Concat(revivedByOpp.Keys).Distinct().ToList();
        if (allIds.Count == 0) return result;

        var opps = await db.SalesOpportunities.AsNoTracking()
            .Where(o => allIds.Contains(o.OpportunityId))
            .Select(o => new
            {
                o.OpportunityId, o.PartyId, o.StageId, o.IsActive, o.ClosedAt,
                o.LastContactDate, o.CreatedAt, o.ExpectedValue, o.LostReasonId,
                o.IsRecoveryRejected
            })
            .ToListAsync();

        var partyIds = opps.Select(o => o.PartyId).Distinct().ToList();
        var partyMap = partyIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Parties.AsNoTracking()
                .Where(p => partyIds.Contains(p.PartyId))
                .ToDictionaryAsync(p => p.PartyId, p => p.PartyName);

        var phoneMap = partyIds.Count == 0
            ? new Dictionary<int, string?>()
            : await db.Parties.AsNoTracking()
                .Where(p => partyIds.Contains(p.PartyId))
                .ToDictionaryAsync(p => p.PartyId, p => p.Phone);

        var reasonIds = opps.Where(o => o.LostReasonId.HasValue)
            .Select(o => o.LostReasonId!.Value).Distinct().ToList();
        var reasonMap = reasonIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.LostReasons.AsNoTracking()
                .Where(r => reasonIds.Contains(r.LostReasonId))
                .ToDictionaryAsync(r => r.LostReasonId, r => r.ReasonNameAr ?? r.ReasonName);

        var stageIds = opps.Select(o => o.StageId).Distinct().ToList();
        var stageMap = stageIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.SalesStages.AsNoTracking()
                .Where(s => stageIds.Contains(s.StageId))
                .ToDictionaryAsync(s => s.StageId, s => s.StageNameAr ?? s.StageName);

        // 3) تواصلات خدمة العملاء (بدأت وانتهت في مرحلة الخسارة) بعد تاريخ الإغلاق
        var csInts = await db.CustomerInteractions.AsNoTracking()
            .Where(i => allIds.Contains(i.OpportunityId)
                && i.StageBeforeId.HasValue
                && (i.StageBeforeId == LostStageId || i.StageBeforeId == NotInterestedStageId)
                && i.StageAfterId.HasValue
                && (i.StageAfterId == LostStageId || i.StageAfterId == NotInterestedStageId)
                // استثناء تسجيلات النظام لتحركات المراحل (خسارة ↔ غير مهتم) — ليست محاولة تواصل
                && !i.Summary.StartsWith("نقل تلقائي"))
            .Select(i => new { i.OpportunityId, i.InteractionDate, i.Summary, i.CreatedBy })
            .ToListAsync();
        var csByOpp = csInts
            .GroupBy(i => i.OpportunityId)
            .ToDictionary(g => g.Key,
                g => g.Select(x => (Date: x.InteractionDate, Summary: x.Summary, By: x.CreatedBy)).ToList());

        // 4) موظف خدمة العملاء المسؤول حاليا (من مهمة استرداد مفتوحة)
        var tasks = await db.CrmTasks.AsNoTracking()
            .Where(t => t.OpportunityId.HasValue && allIds.Contains(t.OpportunityId.Value)
                && t.Status == TaskPending && t.IsActive && t.TaskScope == "Recovery")
            .Select(t => new { t.OpportunityId, t.AssignedTo })
            .ToListAsync();
        var empByOpp = tasks
            .GroupBy(t => t.OpportunityId!.Value)
            .ToDictionary(g => g.Key, g => g.First().AssignedTo);

        var empIds = tasks.Select(t => t.AssignedTo).Distinct().ToList();
        var empNameMap = empIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Employees.AsNoTracking()
                .Where(e => empIds.Contains(e.EmployeeId))
                .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName);

        // 5) اسم + رقم موظف منفذ التفاعلات (تواصل / عودة) عبر حساب المستخدم
        var allCreators = csInts.Select(x => x.CreatedBy)
            .Concat(revivedRows.Select(x => x.CreatedBy))
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var userMap = new Dictionary<string, (string Name, int EmpId)>();
        if (allCreators.Count > 0)
        {
            var us = await db.Users.AsNoTracking()
                .Where(u => allCreators.Contains(u.Username))
                .Select(u => new { u.Username, u.FullName, u.EmployeeId })
                .ToListAsync();
            foreach (var u in us)
                userMap[u.Username] = (u.FullName ?? u.Username, u.EmployeeId ?? 0);
        }

        // 6) بناء السطور
        foreach (var o in opps)
        {
            // هل الفرصة خاسرة الآن فعلا؟ (في طابور الاسترداد)
            var isCurrentlyLost = o.IsActive
                && (o.StageId == LostStageId || o.StageId == NotInterestedStageId);

            var hasRevive = revivedByOpp.TryGetValue(o.OpportunityId, out var reviveDate);
            var isRejected = o.IsRecoveryRejected == true;

            // خارج نطاق التقرير: ليست خاسرة الآن وليس لها سجل عودة
            if (!isCurrentlyLost && !hasRevive) continue;

            csByOpp.TryGetValue(o.OpportunityId, out var csList);
            csList ??= new List<(DateTime Date, string? Summary, string? By)>();
            // تواصلات بعد آخر خسارة (لو ClosedAt مسجل)
            csList = csList.Where(c => !o.ClosedAt.HasValue || c.Date >= o.ClosedAt.Value).ToList();

            // آخر تواصل مسجل
            (DateTime Date, string? Summary, string? By)? lastCs = null;
            if (csList.Count > 0)
                lastCs = csList.OrderByDescending(c => c.Date).First();

            // حالة السطر — حسب الوضع الحالي الفعلي للفرصة (الأهم: لا "مُسترد" لفرصة خاسرة الآن)
            string statusAr;
            if (isCurrentlyLost)
            {
                if (isRejected) statusAr = "رفض نهائي";
                else if (csList.Count > 0) statusAr = "قيد المتابعة";
                else statusAr = "لم يُتواصل";
            }
            else
            {
                statusAr = "مُسترد";
            }

            // تاريخ الأساس لفلترة الفترة = تاريخ "حدث الحالة" نفسه:
            //  - مُسترد        → تاريخ العودة (سجل العودة)
            //  - قيد المتابعة  → تاريخ آخر تواصل (نشاط التواصل في الفترة)
            //  - رفض نهائي     → تاريخ آخر تواصل (لحظة تسجيل الرفض، أو ClosedAt إن لم يوجد)
            //  - لم يُتواصل    → تاريخ الخسارة ClosedAt (لا يوجد تواصل يسند إليه)
            DateTime? anchorDate;
            string? csName = null;

            if (!isCurrentlyLost)
            {
                anchorDate = reviveDate;
                var lastRev = revivedRows
                    .Where(r => r.OpportunityId == o.OpportunityId && r.InteractionDate == reviveDate)
                    .OrderByDescending(r => r.InteractionDate).FirstOrDefault();
                if (lastRev != null && !string.IsNullOrWhiteSpace(lastRev.CreatedBy)
                    && userMap.TryGetValue(lastRev.CreatedBy, out var u2)) csName = u2.Name;
            }
            else if (statusAr == "لم يُتواصل")
            {
                anchorDate = o.ClosedAt ?? o.LastContactDate ?? o.CreatedAt;
                // الموظف المسؤول: من المهمة المفتوحة فقط (لا يوجد تواصل حتى يُنسب إليه)
                if (empByOpp.TryGetValue(o.OpportunityId, out var eid)
                    && empNameMap.TryGetValue(eid, out var en)) csName = en;
            }
            else // قيد المتابعة أو رفض نهائي — يوجد تواصل مسجل (أو رفض مسجل كتواصل)
            {
                anchorDate = lastCs.HasValue
                    ? lastCs.Value.Date
                    : (o.ClosedAt ?? o.LastContactDate ?? o.CreatedAt);

                // الموظف: من المهمة المفتوحة، وإلا فمنفّذ آخر تواصل
                if (empByOpp.TryGetValue(o.OpportunityId, out var eid2)
                    && empNameMap.TryGetValue(eid2, out var en2)) csName = en2;
                else if (lastCs.HasValue && !string.IsNullOrWhiteSpace(lastCs.Value.By)
                    && userMap.TryGetValue(lastCs.Value.By, out var u1)) csName = u1.Name;
            }

            // فلترة الفترة — حسب تاريخ حدث الحالة أعلاه
            if (f.From.HasValue && (!anchorDate.HasValue || anchorDate.Value.Date < f.From.Value.Date)) continue;
            if (f.To.HasValue && (!anchorDate.HasValue || anchorDate.Value.Date > f.To.Value.Date)) continue;

            var clientName = partyMap.TryGetValue(o.PartyId, out var pn2) ? pn2 : $"عميل #{o.PartyId}";
            if (!string.IsNullOrWhiteSpace(f.SearchText))
            {
                var s = f.SearchText.Trim();
                var phone = phoneMap.TryGetValue(o.PartyId, out var ph) ? (ph ?? "") : "";
                if (!(clientName.Contains(s, StringComparison.OrdinalIgnoreCase)
                    || phone.Contains(s, StringComparison.OrdinalIgnoreCase)))
                    continue;
            }

            // فلترة الموظف المسؤول
            if (f.CsEmployeeId.HasValue)
            {
                int? ownerEmp = null;
                if (empByOpp.TryGetValue(o.OpportunityId, out var oe)) ownerEmp = oe;
                else if (lastCs.HasValue && !string.IsNullOrWhiteSpace(lastCs.Value.By)
                    && userMap.TryGetValue(lastCs.Value.By, out var u3) && u3.EmpId != 0) ownerEmp = u3.EmpId;
                else if (!isCurrentlyLost)
                {
                    var lr = revivedRows
                        .Where(r => r.OpportunityId == o.OpportunityId && r.InteractionDate == reviveDate)
                        .FirstOrDefault();
                    if (lr != null && !string.IsNullOrWhiteSpace(lr.CreatedBy)
                        && userMap.TryGetValue(lr.CreatedBy, out var u4) && u4.EmpId != 0) ownerEmp = u4.EmpId;
                }
                if (ownerEmp != f.CsEmployeeId.Value) continue;
            }

            // فلترة الحالة
            if (f.Status != "all")
            {
                var matches = (f.Status == "rejected" && statusAr == "رفض نهائي")
                    || (f.Status == "revived" && statusAr == "مُسترد")
                    || (f.Status == "contacting" && statusAr == "قيد المتابعة")
                    || (f.Status == "uncontacted" && statusAr == "لم يُتواصل");
                if (!matches) continue;
            }

            result.Rows.Add(new RecoveryReportRowDto
            {
                OpportunityId = o.OpportunityId,
                PartyId = o.PartyId,
                ClientName = clientName,
                Phone = phoneMap.TryGetValue(o.PartyId, out var ph3) ? ph3 : null,
                ExpectedValue = o.ExpectedValue,
                LostReasonName = o.LostReasonId.HasValue && reasonMap.TryGetValue(o.LostReasonId.Value, out var rn) ? rn : null,
                StageNameAr = stageMap.TryGetValue(o.StageId, out var sn) ? sn : null,
                ClosedAt = o.ClosedAt,
                CsEmployeeName = csName,
                ContactCount = csList.Count,
                LastCsDate = lastCs?.Date,
                LastCsSummary = lastCs != null ? StripChannelPrefix(lastCs.Value.Summary) : null,
                RevivedDate = (!isCurrentlyLost && hasRevive) ? reviveDate : null,
                StatusAr = statusAr
            });
        }

        // ترتيب: الأحدث نشاطًا أولًا
        result.Rows = result.Rows
            .OrderByDescending(r => r.RevivedDate ?? r.LastCsDate ?? r.ClosedAt)
            .ToList();

        result.RowCount = result.Rows.Count;
        result.TotalValue = result.Rows.Where(r => r.ExpectedValue.HasValue).Sum(r => r.ExpectedValue!.Value);
        result.UncontactedCount = result.Rows.Count(r => r.StatusAr == "لم يُتواصل");
        result.ContactedCount = result.Rows.Count(r => r.StatusAr == "قيد المتابعة");
        result.RejectedCount = result.Rows.Count(r => r.StatusAr == "رفض نهائي");
        result.RevivedCount = result.Rows.Count(r => r.StatusAr == "مُسترد");

        // ⭐ ترقيم الصفحات في الخادم — يحدّ الصفوف المُرْسَلة عبر WebSocket (يعالج ثقل البحث)
        //    pageSize <= 0 = كل الصفوف (يُستخدم للتصدير Excel الكامل فقط)
        var total = result.Rows.Count;
        result.PageSize = pageSize;
        result.PageIndex = Math.Clamp(pageIndex, 1, Math.Max(1, (int)Math.Ceiling(total / (double)Math.Max(1, pageSize))));
        result.Rows = (pageSize > 0 && total > 0)
            ? result.Rows.Skip((result.PageIndex - 1) * pageSize).Take(pageSize).ToList()
            : result.Rows;
        result.HasMore = pageSize > 0 && result.PageIndex * pageSize < total;

        return result;
    }

    // ═══════════════════════════════════════════════════════════
    //  تسجيل محاولة تواصل بعد الخسارة
    //  ⭐ يُسجَّل بموظف خدمة العملاء الحالي (المرتبط بحساب المستخدم)
    // ═══════════════════════════════════════════════════════════
    public async Task<(bool Success, string Message)> LogContactAsync(RecoveryContactDto dto, string actor, ClaimsPrincipal? user)
    {
        // ⭐ تحصين في طبقة الخدمة: بلا صلاحية View لا يُسجَّل أي تواصل استرداد
        if (user == null || !RecoveryPermissions.CanView(user))
            return (false, "ليس لديك صلاحية تسجيل تواصل الاسترداد.");

        if (string.IsNullOrWhiteSpace(dto.Summary))
            return (false, "اكتب ملخص التواصل أولًا.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        var opp = await db.SalesOpportunities.FindAsync(dto.OpportunityId);
        if (opp == null) return (false, "الفرصة غير موجودة.");

        // ⭐ الموظف الحالي (المرتبط بحساب المستخدم) هو من يسجّل التواصل
        var actorEmpId = await ResolveEmployeeIdAsync(db, actor);

        var isDefinitive = dto.Outcome == "رفض نهائي";
        var now = DateTime.Now;

        db.CustomerInteractions.Add(new CustomerInteraction
        {
            OpportunityId = opp.OpportunityId,
            PartyId = dto.PartyId,
            EmployeeId = actorEmpId != 0 ? actorEmpId : opp.EmployeeId,
            InteractionDate = now,
            Summary = $"[{dto.Channel}] {dto.Summary}",
            StageBeforeId = opp.StageId,
            StageAfterId = opp.StageId,
            NextFollowUpDate = isDefinitive ? null : dto.NextFollowUpDate,
            CreatedBy = actor,
            CreatedAt = now
        });

        opp.LastContactDate = now;
        opp.LastUpdatedBy = actor;
        opp.LastUpdatedAt = now;

        // ⭐ الرفض القاطع: أخرج الفرصة من طابور المتابعة نهائيًا
        if (isDefinitive)
        {
            opp.IsRecoveryRejected = true;
            opp.IsRecoveryCandidate = false;
            opp.RecoveryNotes = $"رفض نهائي ({dto.Channel}): {dto.Summary}";
            opp.NextFollowUpDate = null;

            var open = await db.CrmTasks
                .Where(t => t.OpportunityId == dto.OpportunityId
                    && t.TaskScope == "Recovery"   // ⭐ نلمس مهام الاسترداد فقط
                    && t.Status == TaskPending && t.IsActive)
                .ToListAsync();
            foreach (var t in open)
            {
                t.Status = "Completed";
                t.CompletedDate = now;
                t.CompletedBy = actor;
                t.CompletionNotes = $"رفض نهائي من العميل: {dto.Summary}";
            }
        }
        else if (dto.NextFollowUpDate.HasValue)
        {
            // ⭐ متابعة الاسترداد تظهر لموظف خدمة العملاء المسؤول (وليس لمندوب المبيعات)
            var empId = actorEmpId != 0 ? actorEmpId : opp.EmployeeId ?? 0;
            opp.NextFollowUpDate = dto.NextFollowUpDate.Value;

            var pending = await db.CrmTasks
                .Where(t => t.OpportunityId == dto.OpportunityId
                    && t.TaskScope == "Recovery"   // ⭐ نلمس مهام الاسترداد فقط
                    && t.Status == TaskPending && t.IsActive)
                .FirstOrDefaultAsync();

            if (pending != null)
            {
                pending.DueDate = dto.NextFollowUpDate.Value;
                pending.TaskDescription = $"متابعة استرداد: {dto.Summary}";
            }
            else if (empId > 0)
            {
                db.CrmTasks.Add(new CrmTask
                {
                    OpportunityId = dto.OpportunityId,
                    PartyId = dto.PartyId,
                    AssignedTo = empId,
                    TaskScope = "Recovery",
                    TaskDescription = $"متابعة استرداد: {dto.Summary}",
                    DueDate = dto.NextFollowUpDate.Value,
                    Priority = "Normal",
                    Status = TaskPending,
                    IsActive = true,
                    ReminderEnabled = true,
                    CreatedBy = actor,
                    CreatedAt = now
                });
            }
        }

        await db.SaveChangesAsync();

        // ⭐ Audit: الرفض النهائي يقفل ملف الاسترداد نهائيًا — إجراء موثق
        if (isDefinitive)
        {
            await _audit.LogAsync("SalesOpportunities", "Recovery/RejectFinal",
                dto.OpportunityId.ToString(),
                null,
                new { dto.OpportunityId, dto.PartyId, dto.Channel, dto.Summary, Actor = actor },
                actor);
        }

        return isDefinitive
            ? (true, "تم تسجيل الرفض النهائي — لن تظهر الفرصة في طابور المتابعة مرة أخرى.")
            : (true, "تم تسجيل محاولة التواصل.");
    }

    // موظف حساب اليوزر
    private static async Task<int> ResolveEmployeeIdAsync(db24804Context db, string actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) return 0;
        return await db.Users.AsNoTracking()
            .Where(u => u.Username == actor && u.EmployeeId.HasValue)
            .Select(u => u.EmployeeId!.Value)
            .FirstOrDefaultAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  "العميل راجع" — نفس الفرصة أو فرصة جديدة مرتبطة
    // ═══════════════════════════════════════════════════════════
    public async Task<(bool Success, string Message, int? NewOpportunityId)> ReviveAsync(RecoveryReviveDto dto, string actor, ClaimsPrincipal? user)
    {
        // ⭐ تحصين في طبقة الخدمة: الاسترداد إجراء حساس — بصلاحية Revive أو الأدوار المصرّح بها فقط
        if (user == null || !RecoveryPermissions.CanRevive(user))
            return (false, "ليس لديك صلاحية تنفيذ استرداد الفرص.", null);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var now = DateTime.Now;

        // ⭐ الموظف الحالي (المرتبط بحساب المستخدم) هو من يسجّل إجراء الاسترداد
        var actorEmpId = await ResolveEmployeeIdAsync(db, actor);

        var opp = await db.SalesOpportunities.FindAsync(dto.OpportunityId);
        if (opp == null) return (false, "الفرصة غير موجودة.", null);

        if (opp.StageId != LostStageId && opp.StageId != NotInterestedStageId)
            return (false, "الفرصة ليست في مرحلة خسارة — لا يمكن استردادها.", null);

        if (dto.NewStageId == LostStageId || dto.NewStageId == NotInterestedStageId)
            return (false, "اختر مرحلة بيع فعلية (وليست مرحلة خسارة).", null);

        var stage = await db.SalesStages.AsNoTracking()
            .Where(s => s.StageId == dto.NewStageId && s.IsActive)
            .Select(s => new { s.StageNameAr, s.StageName })
            .FirstOrDefaultAsync();
        if (stage == null) return (false, "المرحلة الجديدة غير صالحة.", null);
        var stageAr = stage.StageNameAr ?? stage.StageName ?? "المرحلة الجديدة";

        var oldStageAr = await db.SalesStages.AsNoTracking()
            .Where(s => s.StageId == opp.StageId)
            .Select(s => s.StageNameAr ?? s.StageName)
            .FirstOrDefaultAsync() ?? "خسارة";

        var partyName = await db.Parties.AsNoTracking()
            .Where(p => p.PartyId == opp.PartyId)
            .Select(p => p.PartyName)
            .FirstOrDefaultAsync() ?? $"عميل #{opp.PartyId}";

        var followUp = dto.NextFollowUpDate ?? DateTime.Today.AddDays(3);

        // إغلاق أي مهام استرداد مفتوحة على الفرصة
        var openTasks = await db.CrmTasks
            .Where(t => t.OpportunityId == dto.OpportunityId
                && t.TaskScope == "Recovery"   // ⭐ نغلق مهام الاسترداد فقط
                && t.Status == TaskPending && t.IsActive)
            .ToListAsync();
        foreach (var t in openTasks)
        {
            t.Status = "Completed";
            t.CompletedDate = now;
            t.CompletedBy = actor;
            t.CompletionNotes = $"تم استرداد العميل — {stageAr}";
        }

        int? newId = null;

        if (dto.SameOpportunity)
        {
            var oldStage = opp.StageId;
            opp.StageId = dto.NewStageId;
            opp.LostReasonId = null;
            opp.LostNotes = null;
            opp.IsRecoveryCandidate = false;
            opp.IsRecoveryRejected = false;
            opp.RecoveryNotes = null;
            opp.ClosedAt = null;
            opp.ClosedBy = null;
            if (dto.ExpectedValue.HasValue) opp.ExpectedValue = dto.ExpectedValue;
            opp.NextFollowUpDate = followUp;
            opp.LastContactDate = now;
            opp.LastUpdatedBy = actor;
            opp.LastUpdatedAt = now;

            db.CustomerInteractions.Add(new CustomerInteraction
            {
                OpportunityId = opp.OpportunityId,
                PartyId = opp.PartyId,
                EmployeeId = actorEmpId != 0 ? actorEmpId : opp.EmployeeId,
                InteractionDate = now,
                Summary = $"🔁 استرداد: عاد العميل بعد {oldStageAr} إلى {stageAr}",
                StageBeforeId = oldStage,
                StageAfterId = dto.NewStageId,
                NextFollowUpDate = followUp,
                Notes = dto.Notes,
                CreatedBy = actor,
                CreatedAt = now
            });
        }
        else
        {
            // فرصة جديدة مرتبطة بالخاسرة
            var newOpp = new SalesOpportunity
            {
                PartyId = opp.PartyId,
                EmployeeId = opp.EmployeeId,
                SourceId = opp.SourceId,
                CategoryId = opp.CategoryId,
                InterestedProduct = dto.NewInterestedProduct ?? opp.InterestedProduct,
                ExpectedValue = dto.ExpectedValue ?? opp.ExpectedValue,
                Location = opp.Location,
                StageId = dto.NewStageId,
                StatusId = opp.StatusId,
                FirstContactDate = now,
                NextFollowUpDate = followUp,
                Notes = $"مستردة من الفرصة الخاسرة #{opp.OpportunityId} ({partyName})" + (string.IsNullOrWhiteSpace(dto.Notes) ? "" : $" — {dto.Notes}"),
                IsActive = true,
                CreatedBy = actor,
                CreatedAt = now
            };
            db.SalesOpportunities.Add(newOpp);
            await db.SaveChangesAsync();
            newId = newOpp.OpportunityId;

            db.CustomerInteractions.Add(new CustomerInteraction
            {
                OpportunityId = newId.Value,
                PartyId = opp.PartyId,
                EmployeeId = actorEmpId != 0 ? actorEmpId : opp.EmployeeId,
                InteractionDate = now,
                Summary = $"🔁 فرصة جديدة بعد الاسترداد من الخاسرة #{opp.OpportunityId} — {stageAr}",
                StageBeforeId = null,
                StageAfterId = dto.NewStageId,
                NextFollowUpDate = followUp,
                CreatedBy = actor,
                CreatedAt = now
            });

            // إغلاق ملف الفرصة القديمة — استُبدلت بفرصة جديدة (تخرج من طابور الاسترداد نهائيًا)
            opp.IsActive = false;
            opp.Notes = (opp.Notes ?? "") + $"\n[استرداد] أُنشئت فرصة جديدة #{newId} بتاريخ {now:yyyy/MM/dd}.";
            opp.LastUpdatedBy = actor;
            opp.LastUpdatedAt = now;

            // تسجيل "عودة" على القديمة حتى تُحتسب في تقرير الاسترداد ضمن المُسترد (بعودة عميل بفرصة بديلة)
            db.CustomerInteractions.Add(new CustomerInteraction
            {
                OpportunityId = opp.OpportunityId,
                PartyId = opp.PartyId,
                EmployeeId = actorEmpId != 0 ? actorEmpId : opp.EmployeeId,
                InteractionDate = now,
                Summary = $"🔁 استرداد: عاد العميل بفرصة جديدة #{newId} بعد {oldStageAr} إلى {stageAr}",
                StageBeforeId = opp.StageId,
                StageAfterId = dto.NewStageId,
                NextFollowUpDate = followUp,
                Notes = dto.Notes,
                CreatedBy = actor,
                CreatedAt = now
            });
        }

        await db.SaveChangesAsync();

        // ⭐ Audit: الاسترداد يعيد فتح الفرصة أو ينشئ فرصة جديدة — إجراء موثق
        await _audit.LogAsync("SalesOpportunities", "Recovery/Revive",
            dto.OpportunityId.ToString(),
            null,
            new { dto.OpportunityId, PartyId = opp.PartyId, Mode = dto.SameOpportunity ? "SameOpportunity" : "NewOpportunity", NewStageId = dto.NewStageId, NewOpportunityId = newId, ExpectedValue = dto.ExpectedValue, Actor = actor },
            actor);

        return (true,
            dto.SameOpportunity
                ? $"تم استرداد العميل {partyName} — عادت الفرصة إلى مرحلة {stageAr}."
                : $"تم إنشاء فرصة جديدة (#{newId}) للعميل {partyName} في مرحلة {stageAr}.",
            newId);
    }

    // ═══════════════════════════════════════════════════════════
    //  موظف خدمة عملاء اليوزر الحالي (لقائمة "قضاياي")
    // ═══════════════════════════════════════════════════════════
    public async Task<int> GetCurrentCsEmployeeIdAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return 0;
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Users.AsNoTracking()
            .Where(u => u.Username == username && u.EmployeeId.HasValue)
            .Select(u => u.EmployeeId!.Value)
            .FirstOrDefaultAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  مراحل البيع الصالحة للاسترداد (نستبعد الخسارة/غير المهتم)
    // ═══════════════════════════════════════════════════════════
    public async Task<List<SalesStage>> GetRecoveryStagesAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.SalesStages.AsNoTracking()
            .Where(s => s.IsActive
                && s.StageId != LostStageId
                && s.StageId != NotInterestedStageId)
            .OrderBy(s => s.StageOrder)
            .ToListAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  سجل التواصل بعد الخسارة (لفرصة واحدة)
    // ═══════════════════════════════════════════════════════════
    public async Task<List<RecoveryHistoryDto>> GetRecoveryHistoryAsync(int opportunityId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var opp = await db.SalesOpportunities.AsNoTracking()
            .Where(o => o.OpportunityId == opportunityId)
            .Select(o => new { o.ClosedAt })
            .FirstOrDefaultAsync();
        if (opp == null) return new List<RecoveryHistoryDto>();

        // بداية النافذة: لحظة الخسارة (أو قبلها احتياطًا لو ClosedAt خالٍ)
        var from = opp.ClosedAt ?? DateTime.Now.AddMonths(-3);

        var rows = await db.CustomerInteractions.AsNoTracking()
            .Where(i => i.OpportunityId == opportunityId && i.InteractionDate >= from)
            .OrderBy(i => i.InteractionDate)
            .Select(i => new { i.InteractionDate, i.Summary, i.CreatedBy, i.EmployeeId })
            .ToListAsync();

        if (rows.Count == 0) return new List<RecoveryHistoryDto>();

        // اسم الموظف من EmployeeId أو من CreatedBy (username)
        var empIds = rows.Where(r => r.EmployeeId.HasValue).Select(r => r.EmployeeId!.Value).Distinct().ToList();
        var empNames = empIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Employees.AsNoTracking()
                .Where(e => empIds.Contains(e.EmployeeId))
                .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName);

        var usernames = rows.Select(r => r.CreatedBy).Distinct().ToList();
        var userNames = usernames.Count == 0
            ? new Dictionary<string, string>()
            : await db.Users.AsNoTracking()
                .Where(u => usernames.Contains(u.Username))
                .ToDictionaryAsync(u => u.Username, u => u.FullName);

        return rows.Select(r =>
        {
            var name = r.EmployeeId.HasValue && empNames.TryGetValue(r.EmployeeId.Value, out var n)
                ? n
                : (userNames.TryGetValue(r.CreatedBy, out var un) ? un : r.CreatedBy);
            var summary = r.Summary ?? "";

            // استخرج القناة من البادئة [قناة]
            string? channel = null;
            if (summary.StartsWith("[") && summary.Contains("]"))
            {
                var close = summary.IndexOf(']');
                channel = summary[1..close];
                summary = summary[(close + 1)..].Trim();
            }

            return new RecoveryHistoryDto
            {
                Date = r.InteractionDate,
                Summary = summary,
                CreatedBy = name,
                EmployeeName = name,
                Channel = channel
            };
        }).ToList();
    }

    // ═══════════════════════════════════════════════════════════
    //  مساعدات مشتركة
    // ═══════════════════════════════════════════════════════════

    // ⭐ بناء مهمة استرداد موحدة — يستخدمها الإسناد الفوري والمزامنة (بلا تكرار كود)
    private static CrmTask BuildRecoveryTask(int opportunityId, int partyId, decimal? expectedValue,
        int? lostReasonId, string partyName, string? reason, int assignedTo, DateTime now)
        => new()
        {
            OpportunityId = opportunityId,
            PartyId = partyId,
            AssignedTo = assignedTo,
            TaskTypeId = null,
            TaskScope = "Recovery",
            TaskDescription = $"استرداد فرصة خاسرة: {partyName} — القيمة {(expectedValue ?? 0):N0} ج.م"
                + (string.IsNullOrWhiteSpace(reason) ? "" : $" (السبب: {reason})"),
            DueDate = DateTime.Today.AddDays(1),
            Priority = (expectedValue ?? 0) >= 50000 ? "High" : "Normal",
            Status = TaskPending,
            IsActive = true,
            ReminderEnabled = true,
            CreatedBy = "RecoverySystem",
            CreatedAt = now
        };

    // ⭐ تعارض الفهرس الفريد (SQL 2601/2627) = جلسة أخرى سبقتنا في الإسناد — متوقع وليس خطأ
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (var e = ex.InnerException; e != null; e = e.InnerException)
            if (e is SqlException sql && (sql.Number == 2601 || sql.Number == 2627))
                return true;
        return false;
    }
}
