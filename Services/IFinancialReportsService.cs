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

    /// <summary>
    /// قائمة المركز المالي (الميزانية) — على مستوى الشركة، كما في تاريخ اليوم
    /// </summary>
    Task<BalanceSheetDto> GetBalanceSheetAsync(int? branchId = null);

    /// <summary>
    /// قراءة رأس المال الافتتاحي (سطر OpeningCapital من FinancialSettings)
    /// </summary>
    Task<(decimal Value, string? Notes, DateTime? UpdatedAt, string? UpdatedBy)> GetOpeningCapitalAsync(int? branchId = null);

    /// <summary>
    /// حفظ رأس المال الافتتاحي (Admin فقط — مع Audit)
    /// </summary>
    Task SaveOpeningCapitalAsync(int? branchId, decimal value, string? notes, string userName);

    /// <summary>اقتراح رأس المال من بضاعة المعرض الافتتاحية (حركات OpeningBalance)</summary>
    Task<decimal> GetOpeningStockValueAsync(int? branchId = null);
}
