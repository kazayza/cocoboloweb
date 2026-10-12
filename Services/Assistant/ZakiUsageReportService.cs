using System.Data;
using System.Data.Common;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services.Assistant;

// ════════════════════════════════════════════════════════════
//  تقرير استخدام زكي — قراءة فقط من جداول السجلات الموجودة
//  (AssistantMessages / AssistantConversations / AssistantToolCalls)
// ════════════════════════════════════════════════════════════

public class ZakiDailyStat
{
    public DateTime Day { get; set; }
    public int Questions { get; set; }
    public int Success { get; set; }
    public long Tokens { get; set; }
    public int AvgDurationMs { get; set; }
}

public class ZakiUserStat
{
    public string UserName { get; set; } = "";
    public int Questions { get; set; }
    public long Tokens { get; set; }
    public DateTime LastUsed { get; set; }
}

public class ZakiToolStat
{
    public string ToolName { get; set; } = "";
    public int Calls { get; set; }
    public int Denied { get; set; }
    public int AvgDurationMs { get; set; }
}

public class ZakiErrorStat
{
    public string ErrorCode { get; set; } = "";
    public int Count { get; set; }
}

public class ZakiUsageReport
{
    public int TotalQuestions { get; set; }
    public int TotalSuccess { get; set; }
    public long TotalTokens { get; set; }
    public int AvgDurationMs { get; set; }
    public List<ZakiDailyStat> Daily { get; } = new();
    public List<ZakiUserStat> TopUsers { get; } = new();
    public List<ZakiToolStat> Tools { get; } = new();
    public List<ZakiErrorStat> Errors { get; } = new();

    public int SuccessRate => TotalQuestions == 0 ? 0 : (int)(TotalSuccess * 100.0 / TotalQuestions);
}

public class ZakiUsageReportService
{
    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly ILogger<ZakiUsageReportService> _logger;

    public ZakiUsageReportService(
        IDbContextFactory<db24804Context> dbFactory,
        ILogger<ZakiUsageReportService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<ZakiUsageReport> GetReportAsync(int days, CancellationToken ct = default)
    {
        var report = new ZakiUsageReport();
        var safeDays = Math.Clamp(days, 1, 365);

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var conn = await OpenAsync(db, ct);

            // ── الحركة اليومية ──
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT CAST(CreatedAt AS DATE)                                        AS Day,
                           COUNT(*)                                                        AS Questions,
                           SUM(CASE WHEN IsSuccess = 1 THEN 1 ELSE 0 END)                  AS SuccessCount,
                           SUM(CAST(TotalTokens AS bigint))                                AS Tokens,
                           CAST(AVG(CAST(DurationMs AS float)) AS int)                     AS AvgMs
                    FROM dbo.AssistantMessages
                    WHERE CreatedAt >= DATEADD(DAY, -@Days, CAST(SYSDATETIME() AS DATE))
                    GROUP BY CAST(CreatedAt AS DATE)
                    ORDER BY CAST(CreatedAt AS DATE) DESC;";
                AddParam(cmd, "@Days", safeDays);

                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    report.Daily.Add(new ZakiDailyStat
                    {
                        Day = r.GetDateTime(0),
                        Questions = r.GetInt32(1),
                        Success = r.GetInt32(2),
                        Tokens = r.IsDBNull(3) ? 0 : r.GetInt64(3),
                        AvgDurationMs = r.IsDBNull(4) ? 0 : r.GetInt32(4)
                    });
                }
            }

            report.TotalQuestions = report.Daily.Sum(d => d.Questions);
            report.TotalSuccess = report.Daily.Sum(d => d.Success);
            report.TotalTokens = report.Daily.Sum(d => d.Tokens);
            var timed = report.Daily.Where(d => d.AvgDurationMs > 0).ToList();
            report.AvgDurationMs = timed.Count == 0 ? 0 : (int)timed.Average(d => d.AvgDurationMs);

            // ── أكتر المستخدمين استخدامًا ──
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT TOP (15)
                           c.UserName                                  AS UserName,
                           COUNT(*)                                    AS Questions,
                           SUM(CAST(m.TotalTokens AS bigint))          AS Tokens,
                           MAX(m.CreatedAt)                            AS LastUsed
                    FROM dbo.AssistantMessages m
                    JOIN dbo.AssistantConversations c ON c.ConversationId = m.ConversationId
                    WHERE m.CreatedAt >= DATEADD(DAY, -@Days, CAST(SYSDATETIME() AS DATE))
                    GROUP BY c.UserName
                    ORDER BY COUNT(*) DESC;";
                AddParam(cmd, "@Days", safeDays);

                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    report.TopUsers.Add(new ZakiUserStat
                    {
                        UserName = r.GetString(0),
                        Questions = r.GetInt32(1),
                        Tokens = r.IsDBNull(2) ? 0 : r.GetInt64(2),
                        LastUsed = r.GetDateTime(3)
                    });
                }
            }

            // ── الأدوات ──
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT ToolName,
                           COUNT(*)                                            AS Calls,
                           SUM(CASE WHEN IsDenied = 1 THEN 1 ELSE 0 END)       AS Denied,
                           CAST(AVG(CAST(DurationMs AS float)) AS int)         AS AvgMs
                    FROM dbo.AssistantToolCalls
                    WHERE CreatedAt >= DATEADD(DAY, -@Days, CAST(SYSDATETIME() AS DATE))
                    GROUP BY ToolName
                    ORDER BY COUNT(*) DESC;";
                AddParam(cmd, "@Days", safeDays);

                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    report.Tools.Add(new ZakiToolStat
                    {
                        ToolName = r.GetString(0),
                        Calls = r.GetInt32(1),
                        Denied = r.GetInt32(2),
                        AvgDurationMs = r.IsDBNull(3) ? 0 : r.GetInt32(3)
                    });
                }
            }

            // ── الأخطاء ──
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT ErrorCode, COUNT(*) AS Cnt
                    FROM dbo.AssistantMessages
                    WHERE CreatedAt >= DATEADD(DAY, -@Days, CAST(SYSDATETIME() AS DATE))
                      AND IsSuccess = 0 AND ErrorCode IS NOT NULL
                    GROUP BY ErrorCode
                    ORDER BY COUNT(*) DESC;";
                AddParam(cmd, "@Days", safeDays);

                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    report.Errors.Add(new ZakiErrorStat
                    {
                        ErrorCode = r.GetString(0),
                        Count = r.GetInt32(1)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki usage report failed");
        }

        return report;
    }

    private static async Task<DbConnection> OpenAsync(db24804Context db, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);
        return conn;
    }

    private static void AddParam(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}
