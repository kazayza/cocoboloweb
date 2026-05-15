using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface ICashFlowService
{
    /// <summary>
    /// قائمة التدفقات النقدية الكاملة مع التحليل والتوقعات
    /// </summary>
    Task<CashFlowStatementDto> GetCashFlowStatementAsync(CashFlowFilterDto filter);
}