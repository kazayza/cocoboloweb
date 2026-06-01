using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IAuditService
{
    // ⭐ الميثود الأصلية (للتسجيل) — لا تتغير
    Task LogAsync<T>(string tableName, string actionType, string primaryKeyValue,
                     T? oldData, T? newData, string userName);

    // ─── جديدة (للقراءة في شاشة العرض) ─────────────────
    Task<PagedAuditLogsDto>    GetLogsAsync(AuditLogFilterDto filter);
    Task<AuditLogDetailDto?>   GetByIdAsync(long auditId);
    Task<AuditDashboardDto>    GetDashboardAsync(int days = 30);
    Task<AuditLookupsDto>      GetLookupsAsync();
    Task<byte[]>               ExportToExcelAsync(AuditLogFilterDto filter);
    Task<(int Deleted, string Message)> PurgeOldLogsAsync(DateTime olderThan);
}