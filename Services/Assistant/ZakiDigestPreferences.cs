using System.Data;
using System.Data.Common;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// تفضيلات الملخص الصباحي: مين ملغي الاشتراك.
/// الجدول dbo.AssistantDigestOptOut (شغّل Database/ZakiAssistant_DigestOptOut.sql). وجود الصف = ملغي.
/// Singleton عشان الـ BackgroundService يقدر يستخدمه.
/// </summary>
public class ZakiDigestPreferences
{
    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly ILogger<ZakiDigestPreferences> _logger;

    public ZakiDigestPreferences(
        IDbContextFactory<db24804Context> dbFactory,
        ILogger<ZakiDigestPreferences> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>كل المستخدمين اللي ملغيين الاشتراك (لو القراءة فشلت بنرجع فاضي ونسجل الخطأ)</summary>
    public async Task<HashSet<string>> GetOptedOutUsersAsync(CancellationToken ct = default)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var conn = await OpenAsync(db, ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT UserName FROM dbo.AssistantDigestOptOut;";

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                set.Add(reader.GetString(0));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki digest prefs: read failed");
        }
        return set;
    }

    public async Task<bool> IsOptedOutAsync(string userName, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var conn = await OpenAsync(db, ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM dbo.AssistantDigestOptOut WHERE UserName = @UserName;";
            AddParam(cmd, "@UserName", userName);

            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki digest prefs: status read failed");
            return false;
        }
    }

    /// <summary>يغيّر الاشتراك. بيرجّع true لو الحفظ نجح.</summary>
    public async Task<bool> SetOptOutAsync(string userName, bool optOut, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var conn = await OpenAsync(db, ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = optOut
                ? @"IF NOT EXISTS (SELECT 1 FROM dbo.AssistantDigestOptOut WHERE UserName = @UserName)
                        INSERT INTO dbo.AssistantDigestOptOut (UserName, OptedOutAt) VALUES (@UserName, SYSDATETIME());"
                : "DELETE FROM dbo.AssistantDigestOptOut WHERE UserName = @UserName;";
            AddParam(cmd, "@UserName", userName);

            await cmd.ExecuteNonQueryAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki digest prefs: save failed");
            return false;
        }
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
