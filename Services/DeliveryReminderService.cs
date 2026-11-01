using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// تنبيه «تسليم بعد أسبوع»: مرة واحدة يوميًا (بعد 8 صباحًا بتوقيت القاهرة) يبحث عن
/// مواعيد التسليم (فاتورة × موعد، لم تكتمل بعد) التي يقع تاريخها بعد 7 أيام بالضبط
/// **كما تراه فئة المستلم**:
/// - فئة التاريخ الحقيقي (DeliveryOfficer — يرى التاريخ الحقيقي): التنبيه عند realDate − 7.
/// - فئة التاريخ المقنّع (ProductionManager — يرى التاريخ منقوص الهامش): التنبيه عند displayedDate − 7.
/// فيصل كل مستلم التنبيه قبل أسبوع تمامًا من التاريخ الظاهر له.
///
/// عدم التكرار: علامة في سجل التدقيق (AuditLogs) بمفتاح
/// «رقم الفاتورة:تاريخ الموعد الحقيقي:الفئة» — تُفحص قبل الإرسال وتُكتب بعده،
/// فلا يتكرر التنبيه لنفس الموعد ونفس الفئة أبدًا، حتى لو أُعيد تشغيل التطبيق
/// أو تغيّر يوم التشغيل.
/// </summary>
public class DeliveryReminderService : BackgroundService
{
    private const string ReminderActionType = "DeliveryAdvanceReminder";
    private const string ReminderTitle      = "⏰ تسليم بعد أسبوع";
    private const string ReminderCreator    = "system";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeliveryReminderService> _logger;
    private DateTime _lastRunDate = DateTime.MinValue;

    public DeliveryReminderService(
        IServiceScopeFactory scopeFactory,
        ILogger<DeliveryReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // استنى شوية بعد قيام التطبيق (نفس نمط خدمة ملخص زكي)
        try { await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = CairoNow();
                if (now.Hour >= 8 && _lastRunDate != now.Date)
                {
                    await RunOnceAsync(now.Date, stoppingToken);
                    _lastRunDate = now.Date;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Delivery reminder: run failed");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken); }
            catch (TaskCanceledException) { return; }
        }
    }

    private async Task RunOnceAsync(DateTime today, CancellationToken ct)
    {
        // التاريخ المستهدف: بعد 7 أيام من اليوم — بصيغة عرض كل فئة
        var target = today.AddDays(7);

        using var scope = _scopeFactory.CreateScope();
        var delivery = scope.ServiceProvider.GetRequiredService<ISalesDeliveryStatusService>();
        var notify   = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var audit    = scope.ServiceProvider.GetRequiredService<IAuditService>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<db24804Context>>();

        // فئة التاريخ الحقيقي → مسئول التسليمات
        await RemindClassAsync(delivery, notify, audit, dbFactory,
            target, seesRealDate: true, role: "DeliveryOfficer", ct);

        // فئة التاريخ المقنّع → مدير الإنتاج
        await RemindClassAsync(delivery, notify, audit, dbFactory,
            target, seesRealDate: false, role: "ProductionManager", ct);
    }

    private async Task RemindClassAsync(
        ISalesDeliveryStatusService delivery,
        NotificationService notify,
        IAuditService audit,
        IDbContextFactory<db24804Context> dbFactory,
        DateTime targetDisplayedDate,
        bool seesRealDate,
        string role,
        CancellationToken ct)
    {
        var appointments = await delivery.GetAppointmentsDueOnAsync(targetDisplayedDate, seesRealDate);
        if (appointments.Count == 0) return;

        var classKey = seesRealDate ? "Real" : "Masked";

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        foreach (var appt in appointments)
        {
            ct.ThrowIfCancellationRequested();

            // مفتاح عدم التكرار: الفاتورة + تاريخ الموعد الحقيقي + فئة العرض
            var marker = $"{appt.TransactionId}:{appt.RealDate:yyyyMMdd}:{classKey}";
            var alreadySent = await db.AuditLogs.AsNoTracking().AnyAsync(a =>
                a.TableName == "Transactions" &&
                a.ActionType == ReminderActionType &&
                a.PrimaryKeyValue == marker, ct);
            if (alreadySent) continue;

            var message =
                $"فاتورة {appt.Reference} — العميل: {appt.PartyName}. " +
                $"موعد التسليم كما يظهر لك: {appt.DisplayDate:dddd dd/MM/yyyy} (بعد 7 أيام). " +
                $"بنود مستحقة في هذا الموعد: {appt.DueLineCount} — كمية متبقية: {appt.RemainingQty:0.###}.";

            await notify.NotifyRoleAsync(
                ReminderTitle, message, role, ReminderCreator,
                formName: "sales-delivery-status",
                relatedTable: "Transactions",
                relatedId: appt.TransactionId);

            // العلامة بعد نجاح الإرسال — تمنع أي تكرار لاحق لنفس الموعد والفئة
            await audit.LogAsync<object>(
                "Transactions", ReminderActionType, marker,
                null,
                new
                {
                    appt.TransactionId,
                    appt.Reference,
                    appt.PartyName,
                    RealDate = appt.RealDate,
                    DisplayDate = appt.DisplayDate,
                    Class = classKey,
                    Role = role,
                    appt.DueLineCount,
                    appt.RemainingQty
                },
                ReminderCreator);
        }
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
