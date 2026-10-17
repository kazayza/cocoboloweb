using System.Security.Claims;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using COCOBOLOERPNEW.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static COCOBOLOERPNEW.Services.Assistant.ZakiInvoiceQueries;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// الملخص الصباحي من زكي: كل يوم بعد الساعة المحددة (بتوقيت القاهرة، الافتراضي 8 الصبح)،
/// كل مستخدم نشط بيتبعتله إشعار واحد في جرس الإشعارات فيه اللي مستعجل ومسموح له يشوفه.
/// - مفيش موديل ذكاء اصطناعي هنا: النص بيتكوّن بقوالب من أرقام قواعد البيانات (تكلفة توكنز = 0).
/// - الصلاحيات بتتحسب من جدول UserPermissions زي تسجيل الدخول بالظبط (BuildPrincipalAsync).
///   لو اتغير منطق تسجيل الدخول في Program.cs لازم يتغير هنا كمان.
/// - مرة واحدة في اليوم لكل مستخدم (بنفحص إشعار اتبعت النهارده قبل ما نعمل واحد جديد).
/// - المستخدم اللي مفيهوش أي بند مستعجل مابياخدش إشعار.
/// </summary>
public class ZakiDailyDigestService : BackgroundService
{
    private const string DigestTitle = "ملخص صباحي من زكي";
    private const string DigestCreator = "زكي";
    private const int UndeliveredAfterDays = 7;

    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly AssistantOptions _opt;
    private readonly ILogger<ZakiDailyDigestService> _logger;
    private readonly ZakiDigestPreferences _prefs;
    private DateTime _lastRunDate = DateTime.MinValue;

    public ZakiDailyDigestService(
        IDbContextFactory<db24804Context> dbFactory,
        IOptions<AssistantOptions> options,
        ZakiDigestPreferences prefs,
        ILogger<ZakiDailyDigestService> logger)
    {
        _dbFactory = dbFactory;
        _opt = options.Value;
        _prefs = prefs;
        _logger = logger;
    }

    private sealed record UserRow(int UserId, string Username, string? Role, DateTime? CrmAccessFromDate);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // استنى شوية بعد قيام التطبيق
        try { await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = CairoNow();
                if (_opt.DigestEnabled && now.Hour >= _opt.DigestHour && _lastRunDate != now.Date)
                {
                    await RunOnceAsync(now.Date, stoppingToken);
                    _lastRunDate = now.Date;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Zaki digest: run failed");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken); }
            catch (TaskCanceledException) { return; }
        }
    }

    /// <summary>يبعت الملخص لكل المستخدمين النشطين. أي خطأ في مستخدم مابيوقفش الباقيين.</summary>
    public async Task RunOnceAsync(DateTime day, CancellationToken ct)
    {
        List<UserRow> users;
        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
        {
            users = await db.Users.AsNoTracking()
                .Where(u => u.IsActive == true)
                .Select(u => new UserRow(u.UserId, u.Username, u.Role, u.CrmAccessFromDate))
                .ToListAsync(ct);
        }

        // المستخدمين اللي ملغيين الاشتراك مابياخدوش الملخص
        var optedOut = await _prefs.GetOptedOutUsersAsync(ct);

        var sent = 0;
        foreach (var u in users)
        {
            if (optedOut.Contains(u.Username))
                continue;

            try
            {
                if (await SendForUserAsync(u, day, ct))
                    sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Zaki digest: failed for user {User}", u.Username);
            }
        }

        _logger.LogInformation("Zaki digest: sent {Sent} of {Total} users for {Day:yyyy-MM-dd}",
            sent, users.Count, day);
    }

    private async Task<bool> SendForUserAsync(UserRow u, DateTime day, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // ما نبعتش تاني لو الملخص اتبعت النهارده
        var alreadySent = await db.Notifications.AsNoTracking().AnyAsync(n =>
            n.RecipientUser == u.Username && n.CreatedBy == DigestCreator
            && n.Title == DigestTitle && n.CreatedAt >= day, ct);
        if (alreadySent)
            return false;

        var principal = await BuildPrincipalAsync(db, u, ct);
        var lines = await BuildLinesAsync(db, principal, day, ct);
        if (lines.Count == 0)
            return false;

        db.Notifications.Add(new Notification
        {
            Title = DigestTitle,
            Message = string.Join("\n", lines) + "\n\nالتفاصيل في شاشات الفواتير والتسليم والموافقات.",
            RecipientUser = u.Username,
            CreatedBy = DigestCreator,
            CreatedAt = DateTime.Now,
            IsRead = false,
            ReminderEnabled = false
        });

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>البنود المستعجلة للمستخدم، كل بند بنفس شروط الشاشة/الأداة المقابلة</summary>
    private static async Task<List<string>> BuildLinesAsync(
        db24804Context db, ClaimsPrincipal principal, DateTime day, CancellationToken ct)
    {
        var lines = new List<string>();
        var scope = await BuildScopeAsync(db, principal, null);

        if (CanViewSalesInvoices(principal))
        {
            var overdue = Base(db, scope, TransactionTypes.Sale)
                .Where(t => t.GrandTotal > t.PaidAmount && t.DueDate.HasValue && t.DueDate.Value < day);

            var count = await overdue.CountAsync(ct);
            if (count > 0)
            {
                var remaining = await overdue.SumAsync(t => (decimal?)(t.GrandTotal - t.PaidAmount), ct) ?? 0m;
                lines.Add($"• فواتير بيع متأخرة السداد: {count} فاتورة، المتبقي {Money(remaining)}");
            }
        }

        if (CanViewPurchaseInvoices(principal))
        {
            // فواتير الشراء المرآة مستبعدة زي شاشة المشتريات
            var overdue = Base(db, scope, TransactionTypes.Purchase)
                .Where(t => (t.ReferenceType == null || !t.ReferenceType.StartsWith(MirrorPrefix))
                            && t.GrandTotal > t.PaidAmount && t.DueDate.HasValue && t.DueDate.Value < day);

            var count = await overdue.CountAsync(ct);
            if (count > 0)
            {
                var remaining = await overdue.SumAsync(t => (decimal?)(t.GrandTotal - t.PaidAmount), ct) ?? 0m;
                lines.Add($"• فواتير شراء متأخرة السداد: {count} فاتورة، المتبقي علينا {Money(remaining)}");
            }
        }

        if (CanViewDeliveryStatus(principal))
        {
            var cutoff = day.AddDays(-UndeliveredAfterDays);
            var count = await Base(db, scope, TransactionTypes.Sale)
                .Where(t => t.IsDelivered != true && t.TransactionDate < cutoff)
                .CountAsync(ct);

            if (count > 0)
                lines.Add($"• فواتير لم تُسلَّم من أكتر من {UndeliveredAfterDays} أيام: {count} فاتورة");
        }

        if (ZakiApprovalsService.CanViewPriceRequests(principal))
        {
            var pending = await db.PriceChangeRequests.AsNoTracking()
                .CountAsync(r => r.Status == "Pending", ct);

            if (pending > 0)
                lines.Add($"• طلبات تغيير أسعار معلقة محتاجة مراجعة: {pending}");
        }

        if (ZakiStockAlertsService.CanView(principal))
        {
            // الصنف النافد: مجموع رصيده في كل المخازن صفر أو أقل (نفس تعريف أداة المخزون المنخفض)
            var outOfStock = await db.StockLevels.AsNoTracking()
                .GroupBy(s => s.ProductId)
                .Select(g => new { Total = g.Sum(x => x.Quantity) })
                .CountAsync(x => x.Total <= 0, ct);

            if (outOfStock > 0)
                lines.Add($"• أصناف رصيدها صفر في المخزن: {outOfStock}");
        }

        if (ZakiSalesDocumentsService.CanViewInvoiceHistory(principal))
        {
            var count = await db.Transactions.AsNoTracking()
                .CountAsync(t => t.EditStatus == InvoiceEditStatuses.Pending
                                 && (t.InvoiceStatus == null || t.InvoiceStatus != CancelledStatus), ct);

            if (count > 0)
                lines.Add($"• طلبات تعديل فواتير معلقة محتاجة اعتماد: {count}");
        }

        return lines;
    }

    /// <summary>
    /// بيبني ClaimsPrincipal للمستخدم من قاعدة البيانات بنفس شكل تسجيل الدخول:
    /// الاسم، الدور، UserId، تاريخ الاطلاع، وصلاحيات الـ View.
    /// </summary>
    private static async Task<ClaimsPrincipal> BuildPrincipalAsync(
        db24804Context db, UserRow u, CancellationToken ct)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, u.Username),
            new(ClaimTypes.Role, string.IsNullOrWhiteSpace(u.Role) ? "User" : u.Role),
            new("UserId", u.UserId.ToString())
        };

        if (u.CrmAccessFromDate.HasValue)
            claims.Add(new Claim("CrmAccessFrom", u.CrmAccessFromDate.Value.ToString("yyyy-MM-dd")));

        var perms = await (
            from up in db.UserPermissions.AsNoTracking()
            join p in db.Permissions.AsNoTracking() on up.PermissionId equals p.PermissionId
            where up.UserId == u.UserId && up.CanView
            select p.FormName
        ).ToListAsync(ct);

        foreach (var form in perms.Where(f => !string.IsNullOrWhiteSpace(f)))
            claims.Add(new Claim("Permission", $"{form}:View"));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "ZakiDigest"));
    }

    private static DateTime CairoNow()
    {
        var utc = DateTime.UtcNow;
        foreach (var id in new[] { "Egypt Standard Time", "Africa/Cairo" })
        {
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(id));
            }
            catch (TimeZoneNotFoundException)
            {
                // الاسم التاني (ويندوز / لينكس)
            }
        }
        return utc.AddHours(2);
    }
}
