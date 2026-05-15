using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IFinancialDashboardService
{
    /// <summary>
    /// الـ Dashboard المالي الشامل - يجمع كل المؤشرات والتحليلات
    /// </summary>
    Task<FinancialDashboardDto> GetDashboardAsync(FinancialDashboardFilterDto filter);
}
