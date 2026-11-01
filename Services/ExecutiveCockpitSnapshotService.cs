using System.Data;
using System.Data.Common;
using System.Text.Json;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace COCOBOLOERPNEW.Services;

// ════════════════════════════════════════════════════════════
//  ⭐ مرحلة 3: لقطات لوحة القيادة التنفيذية المحفوظة
//  (dbo.ExecutiveCockpitSnapshots)
//
//  - SQL مباشر (ADO.NET) بنفس اصطلاح جداول المساعد (ZakiLogService):
//    بلا كيانات EF وبلا Migrations — الجدول يُنشأ بسكربت مستقل
//    (executive-cockpit-snapshot-table.sql، آمن للتشغيل المتكرر).
//  - اللقطة = JSON كامل لـ ExecutiveCockpitDto كما ظهر للرئيس وقت
//    الحفظ (فترة/فرع/مستخدم/وقت) — تُفتح لاحقًا للقراءة والطباعة فقط.
//  - أي خطأ (الجدول غير موجود بعد مثلًا) يُسجَّل في اللوج ويُرجع
//    فشلًا/فراغًا — لا يكسر اللوحة أبدًا.
// ════════════════════════════════════════════════════════════
public class ExecutiveCockpitSnapshotService
{
    private readonly IDbContextFactory<db24804Context> _factory;
    private readonly ILogger<ExecutiveCockpitSnapshotService> _logger;

    public ExecutiveCockpitSnapshotService(
        IDbContextFactory<db24804Context> factory,
        ILogger<ExecutiveCockpitSnapshotService> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    /// <summary>هل جدول اللقطات موجود؟ (للصفحة: إظهار تلميح تشغيل السكربت)</summary>
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            var conn = await OpenAsync(db);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.tables
                                         WHERE name = 'ExecutiveCockpitSnapshots'
                                           AND schema_id = SCHEMA_ID('dbo'))
                            THEN 1 ELSE 0 END;";
            var result = await cmd.ExecuteScalarAsync();
            return result is not null && result is not DBNull && Convert.ToInt32(result) == 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cockpit snapshots: availability check failed");
            return false;
        }
    }

    /// <summary>حفظ لقطة من DTO حالي. false لو الجدول غير متاح/فشل الحفظ.</summary>
    public async Task<bool> SaveSnapshotAsync(ExecutiveCockpitDto dto, string userName)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            var conn = await OpenAsync(db);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO dbo.ExecutiveCockpitSnapshots
                    (CreatedAt, PeriodKey, PeriodLabel, BranchId, BranchName, CreatedBy, PayloadJson)
                VALUES
                    (SYSDATETIME(), @PeriodKey, @PeriodLabel, @BranchId, @BranchName, @CreatedBy, @PayloadJson);";

            AddParam(cmd, "@PeriodKey", dto.PeriodKey);
            AddParam(cmd, "@PeriodLabel", dto.PeriodLabel);
            AddParam(cmd, "@BranchId", dto.BranchId);
            AddParam(cmd, "@BranchName", string.IsNullOrWhiteSpace(dto.BranchName) ? null : dto.BranchName);
            AddParam(cmd, "@CreatedBy", userName);
            AddParam(cmd, "@PayloadJson", JsonSerializer.Serialize(dto));

            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            // غالبًا جدول اللقطات لسه ماتعملش (شغّل executive-cockpit-snapshot-table.sql)
            _logger.LogError(ex, "Cockpit snapshots: save failed");
            return false;
        }
    }

    /// <summary>أحدث اللقطات (الأحدث أولًا) — قائمة خفيفة بلا الـ JSON.</summary>
    public async Task<List<ExecutiveCockpitSnapshotItemDto>> ListSnapshotsAsync(int take = 30)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            var conn = await OpenAsync(db);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT TOP ({Math.Clamp(take, 1, 100)})
                       SnapshotId, CreatedAt, PeriodKey, PeriodLabel, BranchId, BranchName, CreatedBy
                FROM dbo.ExecutiveCockpitSnapshots
                ORDER BY CreatedAt DESC;";

            var items = new List<ExecutiveCockpitSnapshotItemDto>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                items.Add(new ExecutiveCockpitSnapshotItemDto
                {
                    SnapshotId = Convert.ToInt64(r.GetValue(0)),
                    CreatedAt = r.GetDateTime(1),
                    PeriodKey = r.IsDBNull(2) ? "" : r.GetString(2),
                    PeriodLabel = r.IsDBNull(3) ? "" : r.GetString(3),
                    BranchId = r.IsDBNull(4) ? null : Convert.ToInt32(r.GetValue(4)),
                    BranchName = r.IsDBNull(5) ? "" : r.GetString(5),
                    CreatedBy = r.IsDBNull(6) ? "" : r.GetString(6)
                });
            }
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cockpit snapshots: list failed");
            return new List<ExecutiveCockpitSnapshotItemDto>();
        }
    }

    /// <summary>لقطة كاملة (بيانات وصفية + الـ DTO كما حُفظ) أو null.</summary>
    public async Task<ExecutiveCockpitSnapshotDetailDto?> GetSnapshotAsync(long snapshotId)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            var conn = await OpenAsync(db);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT SnapshotId, CreatedAt, PeriodKey, PeriodLabel, BranchId, BranchName, CreatedBy, PayloadJson
                FROM dbo.ExecutiveCockpitSnapshots
                WHERE SnapshotId = @SnapshotId;";
            AddParam(cmd, "@SnapshotId", snapshotId);

            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;

            var payload = r.IsDBNull(7) ? null : r.GetString(7);
            if (string.IsNullOrWhiteSpace(payload)) return null;

            var data = JsonSerializer.Deserialize<ExecutiveCockpitDto>(payload);
            if (data == null) return null;

            return new ExecutiveCockpitSnapshotDetailDto
            {
                Meta = new ExecutiveCockpitSnapshotItemDto
                {
                    SnapshotId = Convert.ToInt64(r.GetValue(0)),
                    CreatedAt = r.GetDateTime(1),
                    PeriodKey = r.IsDBNull(2) ? "" : r.GetString(2),
                    PeriodLabel = r.IsDBNull(3) ? "" : r.GetString(3),
                    BranchId = r.IsDBNull(4) ? null : Convert.ToInt32(r.GetValue(4)),
                    BranchName = r.IsDBNull(5) ? "" : r.GetString(5),
                    CreatedBy = r.IsDBNull(6) ? "" : r.GetString(6)
                },
                Data = data
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cockpit snapshots: get {SnapshotId} failed", snapshotId);
            return null;
        }
    }

    // ─── أدوات ADO.NET (نفس نمط ZakiLogService) ──────────────
    private static async Task<DbConnection> OpenAsync(db24804Context db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync();
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
