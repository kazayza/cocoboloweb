using System.Data;
using System.Data.Common;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>عدد أخطاء بكود معين</summary>
public sealed record ZakiErrorCount(string ErrorCode, int Count);

/// <summary>رد اتعلّم عليه 👎</summary>
public sealed record ZakiBadAnswer(DateTime CreatedAt, string UserName, string? QuestionText, string? ErrorCode);

/// <summary>نظرة الجودة للأدمن</summary>
public sealed record ZakiQualityOverview(
    int Days,
    int Questions,
    int Failed,
    int Errors24h,
    int ThumbsUp,
    int ThumbsDown,
    IReadOnlyList<ZakiErrorCount> ErrorCodes,
    IReadOnlyList<ZakiBadAnswer> RecentThumbsDown);

/// <summary>صف من جدول الرسائل (سؤال + رد) عشان نرجّع المحادثة بعد الـ Refresh</summary>
public class ZakiHistoryRow
{
    public long MessageId { get; set; }
    public string? QuestionText { get; set; }
    public string? AnswerText { get; set; }
    public bool IsSuccess { get; set; }
}

/// <summary>محادثة واحدة في قائمة السجل</summary>
public class ZakiConversationRow
{
    public long ConversationId { get; set; }
    public string? Title { get; set; }
    public DateTime StartedAt { get; set; }
}

/// <summary>
/// تسجيل استخدام زكي في جداول المراجعة (AssistantConversations / Messages / ToolCalls / Usage).
/// ملاحظات:
/// - SQL مباشر (مش Entities) عشان ما نغيّرش db24804Context.
/// - كل القراءات والإدخالات اللي بترجّع أرقام بتتنفذ بـ ADO.NET عادي.
///   SqlQuery بيلف الاستعلام في Subquery، فالـ ';' والـ INSERT والـ ORDER BY بتفشل جواه.
/// - أي خطأ في التسجيل بيتسجل في اللوج بس، والرد للمستخدم ما يتأثرش.
/// </summary>
public class ZakiLogService
{
    private const int MaxArgsLength = 1000;

    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly AssistantOptions _opt;
    private readonly ILogger<ZakiLogService> _logger;

    public ZakiLogService(
        IDbContextFactory<db24804Context> dbFactory,
        IOptions<AssistantOptions> options,
        ILogger<ZakiLogService> logger)
    {
        _dbFactory = dbFactory;
        _opt = options.Value;
        _logger = logger;
    }

    /// <summary>يحمّل آخر محادثة للمستخدم (آخر 40 رسالة) عشان تفضل بعد الـ Refresh</summary>
    public async Task<(long? ConversationId, List<ZakiHistoryRow> Rows)> LoadLatestConversationAsync(
        string userName, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var conn = await OpenAsync(db, ct);

            // 1) آخر محادثة للمستخدم
            long? convId;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT TOP (1) ConversationId
                    FROM dbo.AssistantConversations
                    WHERE UserName = @UserName
                    ORDER BY ConversationId DESC;";
                AddParam(cmd, "@UserName", userName);

                var scalar = await cmd.ExecuteScalarAsync(ct);
                convId = (scalar is null || scalar is DBNull) ? null : Convert.ToInt64(scalar);
            }

            if (convId is null or <= 0)
                return (null, new List<ZakiHistoryRow>());

            // 2) آخر 40 رسالة في المحادثة دي، بالترتيب الزمني
            var rows = new List<ZakiHistoryRow>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT MessageId, QuestionText, AnswerText, IsSuccess
                    FROM (
                        SELECT TOP (40) MessageId, QuestionText, AnswerText, IsSuccess
                        FROM dbo.AssistantMessages
                        WHERE ConversationId = @ConversationId
                        ORDER BY MessageId DESC
                    ) AS last40
                    ORDER BY MessageId ASC;";
                AddParam(cmd, "@ConversationId", convId.Value);

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    rows.Add(new ZakiHistoryRow
                    {
                        MessageId = Convert.ToInt64(reader.GetValue(0)),
                        QuestionText = reader.IsDBNull(1) ? null : Convert.ToString(reader.GetValue(1)),
                        AnswerText = reader.IsDBNull(2) ? null : Convert.ToString(reader.GetValue(2)),
                        IsSuccess = !reader.IsDBNull(3) && Convert.ToBoolean(reader.GetValue(3))
                    });
                }
            }

            return (convId, rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki log: LoadLatestConversation failed");
            return (null, new List<ZakiHistoryRow>());
        }
    }

    /// <summary>قائمة محادثات المستخدم (الأحدث الأول) — بتستبعد المحادثات الفاضية</summary>
    public async Task<List<ZakiConversationRow>> ListConversationsAsync(
        string userName, int take = 30, CancellationToken ct = default)
    {
        var list = new List<ZakiConversationRow>();
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var conn = await OpenAsync(db, ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT TOP ({Math.Clamp(take, 1, 100)}) c.ConversationId, c.Title, c.StartedAt
                FROM dbo.AssistantConversations AS c
                WHERE c.UserName = @UserName
                  AND EXISTS (SELECT 1 FROM dbo.AssistantMessages AS m WHERE m.ConversationId = c.ConversationId)
                ORDER BY c.ConversationId DESC;";
            AddParam(cmd, "@UserName", userName);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new ZakiConversationRow
                {
                    ConversationId = Convert.ToInt64(reader.GetValue(0)),
                    Title = reader.IsDBNull(1) ? null : Convert.ToString(reader.GetValue(1)),
                    StartedAt = reader.IsDBNull(2) ? DateTime.MinValue : Convert.ToDateTime(reader.GetValue(2))
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki log: ListConversations failed");
        }
        return list;
    }

    /// <summary>يفتح محادثة معينة (آخر 40 رسالة). بيتأكد إنها بتاعة المستخدم نفسه</summary>
    public async Task<List<ZakiHistoryRow>> LoadConversationAsync(
        long conversationId, string userName, CancellationToken ct = default)
    {
        var rows = new List<ZakiHistoryRow>();
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var conn = await OpenAsync(db, ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT MessageId, QuestionText, AnswerText, IsSuccess
                FROM (
                    SELECT TOP (40) m.MessageId, m.QuestionText, m.AnswerText, m.IsSuccess
                    FROM dbo.AssistantMessages AS m
                    INNER JOIN dbo.AssistantConversations AS c ON c.ConversationId = m.ConversationId
                    WHERE m.ConversationId = @ConversationId AND c.UserName = @UserName
                    ORDER BY m.MessageId DESC
                ) AS last40
                ORDER BY MessageId ASC;";
            AddParam(cmd, "@ConversationId", conversationId);
            AddParam(cmd, "@UserName", userName);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new ZakiHistoryRow
                {
                    MessageId = Convert.ToInt64(reader.GetValue(0)),
                    QuestionText = reader.IsDBNull(1) ? null : Convert.ToString(reader.GetValue(1)),
                    AnswerText = reader.IsDBNull(2) ? null : Convert.ToString(reader.GetValue(2)),
                    IsSuccess = !reader.IsDBNull(3) && Convert.ToBoolean(reader.GetValue(3))
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki log: LoadConversation failed");
        }
        return rows;
    }

    /// <summary>يفتح محادثة جديدة ويرجّع رقمها (أو null لو فشل)</summary>
    public async Task<long?> StartConversationAsync(string userName, int? employeeId, string firstQuestion, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return await ScalarLongAsync(db, @"
                INSERT INTO dbo.AssistantConversations (UserName, EmployeeId, StartedAt, Title)
                VALUES (@UserName, @EmployeeId, SYSDATETIME(), @Title);
                SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
                ct,
                ("@UserName", userName),
                ("@EmployeeId", employeeId),
                ("@Title", Truncate(firstQuestion, 200)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki log: StartConversation failed");
            return null;
        }
    }

    /// <summary>يسجل سؤال + رد + التوكنز. بيرجّع رقم الرسالة (أو null لو فشل)</summary>
    public async Task<long?> LogMessageAsync(
        long conversationId,
        string question,
        string? answer,
        bool isSuccess,
        string? errorCode,
        int promptTokens,
        int completionTokens,
        int totalTokens,
        int durationMs,
        CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            string? questionText = _opt.StoreQuestionText ? question : null;

            return await ScalarLongAsync(db, @"
                INSERT INTO dbo.AssistantMessages
                    (ConversationId, QuestionText, AnswerText, IsSuccess, ErrorCode,
                     PromptTokens, CompletionTokens, TotalTokens, DurationMs, CreatedAt)
                VALUES
                    (@ConversationId, @QuestionText, @AnswerText, @IsSuccess, @ErrorCode,
                     @PromptTokens, @CompletionTokens, @TotalTokens, @DurationMs, SYSDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
                ct,
                ("@ConversationId", conversationId),
                ("@QuestionText", questionText),
                ("@AnswerText", answer),
                ("@IsSuccess", isSuccess),
                ("@ErrorCode", errorCode),
                ("@PromptTokens", promptTokens),
                ("@CompletionTokens", completionTokens),
                ("@TotalTokens", totalTokens),
                ("@DurationMs", durationMs));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki log: LogMessage failed");
            return null;
        }
    }

    /// <summary>يسجل كل أداة اتنفذت (أو اتمنع منها)</summary>
    public async Task LogToolCallAsync(
        long messageId,
        string toolName,
        string argumentsJson,
        int rowsReturned,
        bool isDenied,
        int durationMs,
        CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var args = Truncate(argumentsJson, MaxArgsLength);

            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO dbo.AssistantToolCalls
                    (MessageId, ToolName, ArgumentsJson, RowsReturned, IsDenied, DurationMs, CreatedAt)
                VALUES
                    ({messageId}, {toolName}, {args}, {rowsReturned}, {isDenied}, {durationMs}, SYSDATETIME());", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki log: LogToolCall failed");
        }
    }

    /// <summary>يزود عداد الاستهلاك اليومي للمستخدم</summary>
    public async Task AddUsageAsync(string userName, int totalTokens, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            await db.Database.ExecuteSqlInterpolatedAsync($@"
                MERGE dbo.AssistantUsage AS t
                USING (SELECT {userName} AS UserName, CAST(SYSDATETIME() AS DATE) AS UsageDate) AS s
                    ON t.UserName = s.UserName AND t.UsageDate = s.UsageDate
                WHEN MATCHED THEN
                    UPDATE SET QuestionCount = t.QuestionCount + 1,
                               TotalTokens   = t.TotalTokens + {totalTokens}
                WHEN NOT MATCHED THEN
                    INSERT (UserName, UsageDate, QuestionCount, TotalTokens)
                    VALUES (s.UserName, s.UsageDate, 1, {totalTokens});", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki log: AddUsage failed");
        }
    }

    /// <summary>إجمالي التوكنز اللي اتصرفت النهارده على كل المستخدمين (ميزانية الحساب)</summary>
    /// <summary>توكنز المستخدم النهارده (عشان ميزانية المستخدم)</summary>
    public async Task<long> GetTodayTotalTokensForUserAsync(string userName, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var conn = await OpenAsync(db, ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT ISNULL(SUM(CAST(TotalTokens AS BIGINT)), 0)
                FROM dbo.AssistantUsage
                WHERE UsageDate = CAST(SYSDATETIME() AS DATE) AND UserName = @UserName;";
            AddParam(cmd, "@UserName", userName);

            var result = await cmd.ExecuteScalarAsync(ct);
            return result is null or DBNull ? 0 : Convert.ToInt64(result);
        }
        catch (Exception ex)
        {
            // لو فشل القراءة ما نقفلش على المستخدم
            _logger.LogError(ex, "Zaki log: user tokens read failed");
            return 0;
        }
    }

    /// <summary>
    /// حجز التوكنز للسؤال الواحد = متوسط الأسئلة الفعلية في آخر 7 أيام،
    /// محدود بين 4,000 و 30,000. لو مفيش بيانات بنرجع 12,000.
    /// </summary>
    public async Task<int> GetReserveTokensPerQuestionAsync(CancellationToken ct = default)
    {
        const int fallback = 12000;
        const int min = 4000;
        const int max = 30000;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var conn = await OpenAsync(db, ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT ISNULL(SUM(CAST(TotalTokens AS BIGINT)), 0), ISNULL(SUM(CAST(QuestionCount AS BIGINT)), 0)
                FROM dbo.AssistantUsage
                WHERE UsageDate >= DATEADD(DAY, -7, CAST(SYSDATETIME() AS DATE));";

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return fallback;

            var tokens = reader.GetInt64(0);
            var questions = reader.GetInt64(1);
            if (questions <= 0)
                return fallback;

            var avg = (int)(tokens / questions);
            return Math.Clamp(avg, min, max);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki log: reserve read failed");
            return fallback;
        }
    }

    public async Task<long> GetTodayTotalTokensAsync(CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var conn = await OpenAsync(db, ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT ISNULL(SUM(CAST(TotalTokens AS BIGINT)), 0)
                FROM dbo.AssistantUsage
                WHERE UsageDate = CAST(SYSDATETIME() AS DATE);";

            var result = await cmd.ExecuteScalarAsync(ct);
            return (result is null || result is DBNull) ? 0 : Convert.ToInt64(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki log: GetTodayTotalTokens failed");
            return 0;
        }
    }

    /// <summary>عدد أسئلة المستخدم النهارده (للحد اليومي)</summary>
    public async Task<int> GetTodayQuestionCountAsync(string userName, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var conn = await OpenAsync(db, ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT ISNULL(MAX(QuestionCount), 0)
                FROM dbo.AssistantUsage
                WHERE UserName = @UserName AND UsageDate = CAST(SYSDATETIME() AS DATE);";
            AddParam(cmd, "@UserName", userName);

            var result = await cmd.ExecuteScalarAsync(ct);
            if (result is null || result is DBNull)
                return 0;

            return Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki log: GetTodayQuestionCount failed");
            return 0;
        }
    }

    // ════════════════════════════════════════════════════════════
    // 👍 / 👎 على الردود + نظرة الجودة للأدمن
    // ════════════════════════════════════════════════════════════

    /// <summary>يسجل 👍 (true) أو 👎 (false) على رد. بيتحقق إن الرسالة تبع المستخدم. بيحدّث التقييم لو اتسجل قبل كده.</summary>
    public async Task<bool> SaveFeedbackAsync(long messageId, string userName, bool isUseful, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var conn = await OpenAsync(db, ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                IF EXISTS (SELECT 1 FROM dbo.AssistantFeedback WHERE MessageId = @MessageId AND UserName = @UserName)
                    UPDATE dbo.AssistantFeedback
                    SET IsUseful = @IsUseful, CreatedAt = SYSDATETIME()
                    WHERE MessageId = @MessageId AND UserName = @UserName;
                ELSE
                    INSERT INTO dbo.AssistantFeedback (MessageId, UserName, IsUseful, CreatedAt)
                    SELECT m.MessageId, @UserName, @IsUseful, SYSDATETIME()
                    FROM dbo.AssistantMessages AS m
                    JOIN dbo.AssistantConversations AS c ON c.ConversationId = m.ConversationId
                    WHERE m.MessageId = @MessageId AND c.UserName = @UserName;";

            AddParam(cmd, "@MessageId", messageId);
            AddParam(cmd, "@UserName", userName);
            AddParam(cmd, "@IsUseful", isUseful);

            var affected = await cmd.ExecuteNonQueryAsync(ct);
            return affected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki log: SaveFeedback failed");
            return false;
        }
    }

    /// <summary>نظرة جودة على آخر N يوم: الأسئلة، الأخطاء، 👍/👎، وأكثر أكواد الخطأ تكراراً.</summary>
    public async Task<ZakiQualityOverview?> GetQualityOverviewAsync(int days, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var conn = await OpenAsync(db, ct);
            var from = DateTime.Today.AddDays(-days);

            var questions = 0;
            var failed = 0;
            var errors24h = 0;
            var thumbsUp = 0;
            var thumbsDown = 0;
            var errorCodes = new List<ZakiErrorCount>();
            var recentDown = new List<ZakiBadAnswer>();

            // 1) أرقام الأسئلة والأخطاء
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COUNT(*),
                           ISNULL(SUM(CASE WHEN IsSuccess = 0 THEN 1 ELSE 0 END), 0),
                           ISNULL(SUM(CASE WHEN IsSuccess = 0 AND CreatedAt >= DATEADD(HOUR, -24, SYSDATETIME()) THEN 1 ELSE 0 END), 0)
                    FROM dbo.AssistantMessages
                    WHERE CreatedAt >= @From;";
                AddParam(cmd, "@From", from);

                await using var r = await cmd.ExecuteReaderAsync(ct);
                if (await r.ReadAsync(ct))
                {
                    questions = r.GetInt32(0);
                    failed = r.GetInt32(1);
                    errors24h = r.GetInt32(2);
                }
            }

            // 2) أكواد الخطأ الأكثر تكراراً
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT ISNULL(ErrorCode, 'UNKNOWN'), COUNT(*)
                    FROM dbo.AssistantMessages
                    WHERE CreatedAt >= @From AND IsSuccess = 0
                    GROUP BY ISNULL(ErrorCode, 'UNKNOWN')
                    ORDER BY COUNT(*) DESC;";
                AddParam(cmd, "@From", from);

                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    errorCodes.Add(new ZakiErrorCount(r.GetString(0), r.GetInt32(1)));
            }

            // 3) التقييمات
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT ISNULL(SUM(CASE WHEN IsUseful = 1 THEN 1 ELSE 0 END), 0),
                           ISNULL(SUM(CASE WHEN IsUseful = 0 THEN 1 ELSE 0 END), 0)
                    FROM dbo.AssistantFeedback
                    WHERE CreatedAt >= @From;";
                AddParam(cmd, "@From", from);

                await using var r = await cmd.ExecuteReaderAsync(ct);
                if (await r.ReadAsync(ct))
                {
                    thumbsUp = r.GetInt32(0);
                    thumbsDown = r.GetInt32(1);
                }
            }

            // 4) آخر الردود اللي اتعلّم عليها مش مفيد
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT TOP (10) f.CreatedAt, f.UserName, m.QuestionText, m.ErrorCode
                    FROM dbo.AssistantFeedback AS f
                    JOIN dbo.AssistantMessages AS m ON m.MessageId = f.MessageId
                    WHERE f.IsUseful = 0 AND f.CreatedAt >= @From
                    ORDER BY f.CreatedAt DESC;";
                AddParam(cmd, "@From", from);

                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    recentDown.Add(new ZakiBadAnswer(
                        r.GetDateTime(0),
                        r.IsDBNull(1) ? "—" : r.GetString(1),
                        r.IsDBNull(2) ? null : r.GetString(2),
                        r.IsDBNull(3) ? null : r.GetString(3)));
                }
            }

            return new ZakiQualityOverview(days, questions, failed, errors24h, thumbsUp, thumbsDown, errorCodes, recentDown);
        }
        catch (Exception ex)
        {
            // غالباً جدول AssistantFeedback لسه ماتعملش (شغّل Database/ZakiAssistant_Feedback.sql)
            _logger.LogError(ex, "Zaki log: quality overview failed");
            return null;
        }
    }

    /// <summary>ينفذ أمر INSERT + SCOPE_IDENTITY ويرجّع الرقم الناتج</summary>
    private static async Task<long?> ScalarLongAsync(
        db24804Context db, string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        var conn = await OpenAsync(db, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        foreach (var (name, value) in parameters)
            AddParam(cmd, name, value);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null || result is DBNull)
            return null;

        return Convert.ToInt64(result);
    }

    /// <summary>يفتح الاتصال بالداتابيز لو كان مقفول ويرجّعه</summary>
    private static async Task<DbConnection> OpenAsync(db24804Context db, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);
        return conn;
    }

    /// <summary>يضيف باراميتر للأمر (null بيتحول لـ DBNull)</summary>
    private static void AddParam(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= max ? text : text[..max];
    }
}
