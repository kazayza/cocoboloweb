using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// سياسة الاحتفاظ بسجلات زكي: بتحذف الأسئلة والردود والأدوات اللي عدّى عليها أكتر من
/// Assistant:RetentionDays يوم (الافتراضي 90). بتشتغل مرة كل 24 ساعة، والأسئلة بتحتوي
/// على بيانات عملاء وفواتير، فالحذف ده مهم للخصوصية.
/// الترتيب: الأدوات ← الرسائل ← المحادثات الفاضية (علشان الـ Foreign Keys).
/// </summary>
public class ZakiRetentionService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ZakiRetentionService> _logger;

    public ZakiRetentionService(
        IDbContextFactory<db24804Context> dbFactory,
        IConfiguration config,
        ILogger<ZakiRetentionService> logger)
    {
        _dbFactory = dbFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // استنى شوية بعد ما التطبيق يقوم عشان ما نزحمش البداية
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var days = _config.GetValue<int>("Assistant:RetentionDays", 90);
                await PurgeAsync(days, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Zaki retention: purge failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (TaskCanceledException) { return; }
        }
    }

    /// <summary>يمسح السجلات الأقدم من عدد الأيام المحدد. بيرجّع عدد الصفوف اللي اتمسحت</summary>
    public async Task<(int Tools, int Messages, int Conversations)> PurgeAsync(int days, CancellationToken ct = default)
    {
        if (days < 7) days = 7; // حد أدنى أمان

        var cutoff = DateTime.Now.AddDays(-days);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var tools = await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE tc
            FROM dbo.AssistantToolCalls AS tc
            INNER JOIN dbo.AssistantMessages AS m ON m.MessageId = tc.MessageId
            WHERE m.CreatedAt < {cutoff};", ct);

        // التقييمات (👍/👎) بتتمسح مع الرسائل اللي تبعها (من غير FK عشان الحذف ما يقفش)
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            IF OBJECT_ID('dbo.AssistantFeedback', 'U') IS NOT NULL
                DELETE f
                FROM dbo.AssistantFeedback AS f
                INNER JOIN dbo.AssistantMessages AS m ON m.MessageId = f.MessageId
                WHERE m.CreatedAt < {cutoff};", ct);

        var messages = await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM dbo.AssistantMessages
            WHERE CreatedAt < {cutoff};", ct);

        var conversations = await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE c
            FROM dbo.AssistantConversations AS c
            WHERE c.StartedAt < {cutoff}
              AND NOT EXISTS (SELECT 1 FROM dbo.AssistantMessages AS m WHERE m.ConversationId = c.ConversationId);", ct);

        _logger.LogInformation(
            "Zaki retention: deleted tools={Tools} messages={Messages} conversations={Conversations} (older than {Days} days)",
            tools, messages, conversations, days);

        return (tools, messages, conversations);
    }
}
