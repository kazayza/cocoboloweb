using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface ISalesAnalyticsService
{
    Task<SalesAnalyticsFilterDataDto> GetFilterDataAsync();
    Task<SalesAnalyticsDashboardDto> GetDashboardAsync(SalesAnalyticsFilterDto filter);
    Task<(byte[] Content, string Mime, string FileName)> ExportAsync(SalesAnalyticsFilterDto filter);
}
