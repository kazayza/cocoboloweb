using System.Text.Json;
using ClosedXML.Excel;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class AuditService : IAuditService
{
    private readonly IDbContextFactory<db24804Context> _factory;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        IDbContextFactory<db24804Context> factory,
        ILogger<AuditService> logger)
    {
        _factory = factory;
        _logger  = logger;
    }

    // ════════════════════════════════════════════════
    //                  ⭐ التسجيل (موجود)
    // ════════════════════════════════════════════════

    public async Task LogAsync<T>(
        string tableName, string actionType, string primaryKeyValue,
        T? oldData, T? newData, string userName)
    {
        try
        {
            using var db = await _factory.CreateDbContextAsync();

            var options = new JsonSerializerOptions
            {
                WriteIndented = false,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var log = new AuditLog
            {
                TableName       = tableName,
                ActionType      = actionType,
                PrimaryKeyValue = primaryKeyValue,
                OldData         = oldData != null ? JsonSerializer.Serialize(oldData, options) : null,
                NewData         = newData != null ? JsonSerializer.Serialize(newData, options) : null,
                ActionDate      = DateTime.Now,
                LoginName       = userName,
                AccessUserName  = userName,
                AppName         = "COCOBOLOERP",
                HostName        = Environment.MachineName
            };

            db.AuditLogs.Add(log);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AuditService.LogAsync failed");
        }
    }

    // ════════════════════════════════════════════════
    //                      LIST
    // ════════════════════════════════════════════════

    public async Task<PagedAuditLogsDto> GetLogsAsync(AuditLogFilterDto filter)
    {
        using var db = await _factory.CreateDbContextAsync();
        var query = db.AuditLogs.AsNoTracking().AsQueryable();

        if (filter.DateFrom.HasValue)
            query = query.Where(a => a.ActionDate >= filter.DateFrom.Value.Date);
        if (filter.DateTo.HasValue)
            query = query.Where(a => a.ActionDate <= filter.DateTo.Value.Date.AddDays(1).AddTicks(-1));

        if (!string.IsNullOrWhiteSpace(filter.TableName))
            query = query.Where(a => a.TableName == filter.TableName);

        if (!string.IsNullOrWhiteSpace(filter.ActionType))
            query = query.Where(a => a.ActionType == filter.ActionType);

        if (!string.IsNullOrWhiteSpace(filter.UserName))
            query = query.Where(a => a.LoginName == filter.UserName
                                  || a.AccessUserName == filter.UserName);

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();
            query = query.Where(a =>
                (a.PrimaryKeyValue != null && a.PrimaryKeyValue.Contains(s)) ||
                (a.OldData != null && a.OldData.Contains(s)) ||
                (a.NewData != null && a.NewData.Contains(s)) ||
                (a.LoginName != null && a.LoginName.Contains(s)));
        }

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(a => a.ActionDate)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(a => new AuditLogListItemDto
            {
                AuditId         = a.AuditId,
                TableName       = a.TableName,
                ActionType      = a.ActionType,
                PrimaryKeyValue = a.PrimaryKeyValue,
                ActionDate      = a.ActionDate,
                LoginName       = a.LoginName,
                AppName         = a.AppName,
                HostName        = a.HostName,
                AccessUserName  = a.AccessUserName,
                HasOldData      = a.OldData != null,
                HasNewData      = a.NewData != null
            })
            .ToListAsync();

        return new PagedAuditLogsDto
        {
            Items      = items,
            TotalCount = total,
            Page       = filter.Page,
            PageSize   = filter.PageSize
        };
    }

    public async Task<AuditLogDetailDto?> GetByIdAsync(long auditId)
    {
        using var db = await _factory.CreateDbContextAsync();
        var a = await db.AuditLogs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.AuditId == auditId);
        if (a is null) return null;

        return new AuditLogDetailDto
        {
            AuditId         = a.AuditId,
            TableName       = a.TableName,
            ActionType      = a.ActionType,
            PrimaryKeyValue = a.PrimaryKeyValue,
            OldData         = a.OldData,
            NewData         = a.NewData,
            ActionDate      = a.ActionDate,
            LoginName       = a.LoginName,
            AppName         = a.AppName,
            HostName        = a.HostName,
            AccessUserName  = a.AccessUserName
        };
    }

    // ════════════════════════════════════════════════
    //                    DASHBOARD
    // ════════════════════════════════════════════════

    public async Task<AuditDashboardDto> GetDashboardAsync(int days = 30)
    {
        using var db = await _factory.CreateDbContextAsync();
        var today    = DateTime.Today;
        var last7    = today.AddDays(-7);
        var last30   = today.AddDays(-days);
        var startWindow = today.AddDays(-days);

        var dto = new AuditDashboardDto
        {
            TotalLogs      = await db.AuditLogs.CountAsync(),
            TodayLogs      = await db.AuditLogs.CountAsync(a => a.ActionDate >= today),
            Last7DaysLogs  = await db.AuditLogs.CountAsync(a => a.ActionDate >= last7),
            Last30DaysLogs = await db.AuditLogs.CountAsync(a => a.ActionDate >= last30),

            InsertCount = await db.AuditLogs.CountAsync(a => a.ActionType == "INSERT" || a.ActionType == "Insert"),
            UpdateCount = await db.AuditLogs.CountAsync(a => a.ActionType == "UPDATE" || a.ActionType == "Update"),
            DeleteCount = await db.AuditLogs.CountAsync(a => a.ActionType == "DELETE" || a.ActionType == "Delete"),
        };
        dto.OtherCount = dto.TotalLogs - (dto.InsertCount + dto.UpdateCount + dto.DeleteCount);

        // Top Users
        dto.TopUsers = await db.AuditLogs.AsNoTracking()
            .Where(a => a.ActionDate >= startWindow && a.LoginName != null)
            .GroupBy(a => a.LoginName!)
            .Select(g => new TopActorDto
            {
                UserName     = g.Key,
                Count        = g.Count(),
                LastActionAt = g.Max(x => x.ActionDate)
            })
            .OrderByDescending(x => x.Count)
            .Take(7)
            .ToListAsync();

        // Top Tables
        dto.TopTables = await db.AuditLogs.AsNoTracking()
            .Where(a => a.ActionDate >= startWindow)
            .GroupBy(a => a.TableName)
            .Select(g => new TopTableDto
            {
                TableName = g.Key,
                Count     = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .Take(8)
            .ToListAsync();

        // Daily activity (آخر 14 يوم)
        var daysWindow = today.AddDays(-13);
        var dailyRaw = await db.AuditLogs.AsNoTracking()
            .Where(a => a.ActionDate >= daysWindow)
            .GroupBy(a => a.ActionDate!.Value.Date)
            .Select(g => new
            {
                Date   = g.Key,
                Total  = g.Count(),
                Insert = g.Count(x => x.ActionType == "INSERT" || x.ActionType == "Insert"),
                Update = g.Count(x => x.ActionType == "UPDATE" || x.ActionType == "Update"),
                Delete = g.Count(x => x.ActionType == "DELETE" || x.ActionType == "Delete")
            })
            .ToListAsync();

        // ملء الأيام الفاضية بـ 0
        dto.DailyActivity = Enumerable.Range(0, 14)
            .Select(i =>
            {
                var d = daysWindow.AddDays(i);
                var row = dailyRaw.FirstOrDefault(x => x.Date == d);
                return new DailyActivityDto
                {
                    Date   = d,
                    Label  = d.ToString("MM/dd"),
                    Total  = row?.Total  ?? 0,
                    Insert = row?.Insert ?? 0,
                    Update = row?.Update ?? 0,
                    Delete = row?.Delete ?? 0
                };
            })
            .ToList();

        // Recent (آخر 10)
        dto.RecentLogs = await db.AuditLogs.AsNoTracking()
            .OrderByDescending(a => a.ActionDate)
            .Take(10)
            .Select(a => new AuditLogListItemDto
            {
                AuditId         = a.AuditId,
                TableName       = a.TableName,
                ActionType      = a.ActionType,
                PrimaryKeyValue = a.PrimaryKeyValue,
                ActionDate      = a.ActionDate,
                LoginName       = a.LoginName,
                HasOldData      = a.OldData != null,
                HasNewData      = a.NewData != null
            })
            .ToListAsync();

        return dto;
    }

    // ════════════════════════════════════════════════
    //                    LOOKUPS
    // ════════════════════════════════════════════════

    public async Task<AuditLookupsDto> GetLookupsAsync()
    {
        using var db = await _factory.CreateDbContextAsync();
        var since = DateTime.Today.AddDays(-90);

        return new AuditLookupsDto
        {
            Tables = await db.AuditLogs.AsNoTracking()
                .Where(a => a.ActionDate >= since)
                .Select(a => a.TableName)
                .Distinct().OrderBy(x => x)
                .Take(100)
                .ToListAsync(),

            Actions = await db.AuditLogs.AsNoTracking()
                .Where(a => a.ActionType != null)
                .Select(a => a.ActionType!)
                .Distinct().OrderBy(x => x)
                .ToListAsync(),

            UserNames = await db.AuditLogs.AsNoTracking()
                .Where(a => a.ActionDate >= since && a.LoginName != null)
                .Select(a => a.LoginName!)
                .Distinct().OrderBy(x => x)
                .Take(100)
                .ToListAsync()
        };
    }

    // ════════════════════════════════════════════════
    //                    EXPORT
    // ════════════════════════════════════════════════

    public async Task<byte[]> ExportToExcelAsync(AuditLogFilterDto filter)
    {
        filter.Page = 1;
        filter.PageSize = int.MaxValue;
        var result = await GetLogsAsync(filter);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Audit Log");
        ws.RightToLeft = true;

        var headers = new[] { "ID", "التاريخ", "المستخدم", "الجدول", "العملية", "المعرّف", "الجهاز" };
        for (int i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        ws.Row(1).Style.Font.Bold = true;
        ws.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1a237e");
        ws.Row(1).Style.Font.FontColor = XLColor.White;
        ws.Row(1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        int row = 2;
        foreach (var l in result.Items)
        {
            ws.Cell(row, 1).Value = l.AuditId;
            ws.Cell(row, 2).Value = l.ActionDate?.ToString("yyyy/MM/dd HH:mm:ss") ?? "";
            ws.Cell(row, 3).Value = l.LoginName ?? "";
            ws.Cell(row, 4).Value = l.TableName;
            ws.Cell(row, 5).Value = l.ActionType ?? "";
            ws.Cell(row, 6).Value = l.PrimaryKeyValue ?? "";
            ws.Cell(row, 7).Value = l.HostName ?? "";
            row++;
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ════════════════════════════════════════════════
    //                    PURGE OLD
    // ════════════════════════════════════════════════

    public async Task<(int Deleted, string Message)> PurgeOldLogsAsync(DateTime olderThan)
    {
        using var db = await _factory.CreateDbContextAsync();
        try
        {
            var deleted = await db.AuditLogs
                .Where(a => a.ActionDate < olderThan)
                .ExecuteDeleteAsync();
            return (deleted, $"تم حذف {deleted} سجل قديم");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Purge old logs failed");
            return (0, "فشل الحذف: " + ex.Message);
        }
    }
}