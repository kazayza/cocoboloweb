using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IFinancialReportsService
{
    /// <summary>
    /// قائمة الدخل الكاملة مع التحليل الذكي والتوصيات
    /// </summary>
    Task<IncomeStatementDto> GetIncomeStatementAsync(IncomeStatementFilterDto filter);

    /// <summary>
    /// تحليل سريع للحصول على نسبة الربح فقط
    /// </summary>
    Task<decimal> GetNetProfitMarginAsync(DateTime from, DateTime to);
}
