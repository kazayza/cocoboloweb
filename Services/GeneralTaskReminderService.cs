using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// تذكيرات وتصعيد التكليفات العامة — يُفعّل حقلي ReminderEnabled/ReminderMinutes
/// اللذين كانا ميتين (يُكتبان عند الإنشاء ولا يستهلكهما أي كود):
///  أ) تذكير قبل الموعد للمكلَّف: قبل DueDateTime بـ ReminderMinutes
///     (افتراضي 30 دقيقة عند عدم الضبط) — مرة واحدة لكل (تكليف × موعد).
///  ب) تنبيه تأخر صباح اليوم التالي للموعد: للمكلَّف والمنشئ معًا —
///     مرة واحدة لكل (تكليف × يوم).
///  ج) تصعيد: تكليف متأخر ≥ 3 أيام → إشعار لدور المدير العام —
///     مرة واحدة لكل (تكليف × موعد).
///
/// عدم التكرار: علامات في سجل التدقيق (AuditLogs) بنفس نمط
/// DeliveryReminderService — تُفحص قبل الإرسال وتُكتب بعده.
/// النطاق: التكليفات العامة المفتوحة فقط (TaskScope = General، نشطة،
/// Pending/In Progress) — بما فيها المرتبطة بمستندات (نفس عائلة النطاق).
/// أي فشل (مثل عدم تشغيل سكربت الأعمدة بعد) يُسجَّل تحذيرًا ويُتخطى التشغيل
/// بهدوء — الخدمة لا تُسقط التطبيق أبدًا.
/// </summary>
public class GeneralTaskReminderService : BackgroundService
{
    private const string PreDueAction    = "GeneralTaskPreDueReminder";
    private const string OverdueAction   = "GeneralTaskOverdueReminder";
    private const string EscalationAction = "GeneralTaskEscalation";
    private const string SystemUser      = "system";
    private const string TasksTable      = "CRM_Tasks";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GeneralTaskReminderService> _logger;
    private DateTime _lastDailyRunDate = DateTime.MinValue;

    public GeneralTaskReminderService(
        IServiceScopeFactory scopeFactory,
        ILogger<GeneralTaskReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // استنى شوية بعد قيام التطبيق (نفس نمط خدمة تذكير التسليمات)
        try { await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPreDueAsync(CairoNow(), stoppingToken);

                var now = CairoNow();
                if (now.Hour >= 8 && _lastDailyRunDate != now.Date)
                {
                    await RunDailyAsync(now.Date, stoppingToken);
                    _lastDailyRunDate = now.Date;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "General task reminders: run failed (will retry next tick)");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken); }
            catch (TaskCanceledException) { return; }
        }
    }

    // ─── أ) تذكير قبل الموعد ────────────────────────────────────────────────────
    private async Task RunPreDueAsync(DateTime now, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var notify = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<db24804Context>>();

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // نافذة فحص واسعة (حتى 24 ساعة) والتصفية الدقيقة بالدقائق بالداخل —
        // العلامة تمنع التكرار فلا خطر من اتساع النافذة
        var horizon = now.AddHours(24);
        var tasks = await db.CrmTasks.AsNoTracking()
            .Where(t => t.IsActive
                        && t.TaskScope == TaskScopes.General
                        && (t.Status == "Pending" || t.Status == "In Progress")
                        && t.ReminderEnabled
                        && t.DueDate >= now.Date.AddDays(-1)
                        && t.DueDate <= horizon.Date)
            .Select(t => new
            {
                t.TaskId, t.AssignedTo, t.TaskDescription, t.DueDate, t.DueTime, t.ReminderMinutes
            })
            .ToListAsync(ct);

        foreach (var t in tasks)
        {
            ct.ThrowIfCancellationRequested();

            var dueMoment = t.DueDate.Date + (t.DueTime?.ToTimeSpan() ?? TimeSpan.FromHours(10));
            var lead = TimeSpan.FromMinutes(t.ReminderMinutes ?? 30);
            if (dueMoment <= now || dueMoment - now > lead) continue;

            var marker = $"TaskPreDue:{t.TaskId}:{dueMoment:yyyyMMddHHmm}";
            if (await MarkerExistsAsync(db, PreDueAction, marker, ct)) continue;

            var sent = await NotifyEmployeeAsync(db, notify, t.AssignedTo,
                "⏰ تذكير بتكليف قريب",
                $"تكليف مُسند إليك يقترب موعده.\nالمهمة: {t.TaskDescription}\nالمطلوب قبل: {dueMoment:yyyy/MM/dd} الساعة {dueMoment:HH:mm}",
                t.TaskId, ct);

            if (sent)
            {
                await audit.LogAsync<object>(TasksTable, PreDueAction, marker, null,
                    new { t.TaskId, t.AssignedTo, DueMoment = dueMoment, LeadMinutes = (int)lead.TotalMinutes },
                    SystemUser);
            }
        }
    }

    // ─── ب+ج) التأخر اليومي والتصعيد ────────────────────────────────────────────
    private async Task RunDailyAsync(DateTime today, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var notify = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<db24804Context>>();

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var overdue = await db.CrmTasks.AsNoTracking()
            .Include(t => t.AssignedToNavigation)
            .Where(t => t.IsActive
                        && t.TaskScope == TaskScopes.General
                        && (t.Status == "Pending" || t.Status == "In Progress")
                        && t.DueDate < today)
            .Select(t => new
            {
                t.TaskId,
                t.AssignedTo,
                AssigneeName = t.AssignedToNavigation.FullName,
                t.TaskDescription,
                t.DueDate,
                t.CreatedBy
            })
            .ToListAsync(ct);

        foreach (var t in overdue)
        {
            ct.ThrowIfCancellationRequested();
            var lateDays = (today - t.DueDate.Date).Days;

            // ب) تنبيه التأخر — مرة لكل تكليف في اليوم
            var overdueMarker = $"TaskOverdue:{t.TaskId}:{today:yyyyMMdd}";
            if (!await MarkerExistsAsync(db, OverdueAction, overdueMarker, ct))
            {
                await NotifyEmployeeAsync(db, notify, t.AssignedTo,
                    "🔴 تكليف متأخر",
                    $"تكليف مُسند إليك تجاوز موعده ولم يكتمل بعد (متأخر {lateDays} يوم).\nالمهمة: {t.TaskDescription}\nكان المطلوب قبل: {t.DueDate:yyyy/MM/dd}",
                    t.TaskId, ct);

                if (!string.IsNullOrWhiteSpace(t.CreatedBy))
                {
                    await notify.AddAsync(
                        title: "🔴 تكليف متأخر أنشأته",
                        message: $"تكليف أنشأته تجاوز موعده ولم يكتمل بعد (متأخر {lateDays} يوم).\nالمكلَّف: {t.AssigneeName}\nالمهمة: {t.TaskDescription}\nكان المطلوب قبل: {t.DueDate:yyyy/MM/dd}",
                        recipientUser: t.CreatedBy,
                        createdBy: SystemUser,
                        formName: "crm/general-tasks",
                        relatedTable: TasksTable,
                        relatedId: t.TaskId);
                }

                await audit.LogAsync<object>(TasksTable, OverdueAction, overdueMarker, null,
                    new { t.TaskId, t.AssignedTo, t.CreatedBy, LateDays = lateDays }, SystemUser);
            }

            // ج) التصعيد للمدير العام عند تأخر ≥ 3 أيام — مرة لكل (تكليف × موعد)
            if (lateDays >= 3)
            {
                var escMarker = $"TaskEscalation:{t.TaskId}:{t.DueDate:yyyyMMdd}";
                if (!await MarkerExistsAsync(db, EscalationAction, escMarker, ct))
                {
                    await notify.NotifyRoleAsync(
                        "🚨 تصعيد: تكليف متأخر ٣ أيام",
                        $"تكليف عام متأخر {lateDays} يوم دون إتمام.\nالمكلَّف: {t.AssigneeName}\nأنشأه: {t.CreatedBy}\nالمهمة: {t.TaskDescription}\nكان المطلوب قبل: {t.DueDate:yyyy/MM/dd}",
                        SystemRoles.GeneralManager, SystemUser,
                        formName: "crm/general-tasks",
                        relatedTable: TasksTable,
                        relatedId: t.TaskId);

                    await audit.LogAsync<object>(TasksTable, EscalationAction, escMarker, null,
                        new { t.TaskId, t.AssignedTo, t.CreatedBy, LateDays = lateDays }, SystemUser);
                }
            }
        }
    }

    private static async Task<bool> MarkerExistsAsync(db24804Context db, string actionType, string marker, CancellationToken ct)
        => await db.AuditLogs.AsNoTracking().AnyAsync(a =>
            a.TableName == TasksTable &&
            a.ActionType == actionType &&
            a.PrimaryKeyValue == marker, ct);

    /// <summary>إشعار موظف عبر حسابه المرتبط — false عند غياب حساب نشط</summary>
    private static async Task<bool> NotifyEmployeeAsync(
        db24804Context db, NotificationService notify, int employeeId,
        string title, string message, int taskId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.EmployeeId == employeeId && u.IsActive == true)
            .Select(u => u.Username)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(user)) return false;

        await notify.AddAsync(
            title: title,
            message: message,
            recipientUser: user,
            createdBy: SystemUser,
            formName: "crm/general-tasks",
            relatedTable: TasksTable,
            relatedId: taskId);
        return true;
    }

    private static DateTime CairoNow()
    {
        var utc = DateTime.UtcNow;
        foreach (var id in new[] { "Egypt Standard Time", "Africa/Cairo" })
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(id);
                return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return utc.AddHours(2);
    }
}
