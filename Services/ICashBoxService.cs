using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface ICashBoxService
{
    // CashBoxes
    Task<List<CashBoxListDto>> GetCashBoxesAsync(bool activeOnly = false);
    Task<CashBoxListDto?> GetCashBoxByIdAsync(int id);
    Task<CashBoxFormDto?> GetCashBoxForEditAsync(int id);
    Task<(bool Success, string Message, int? Id)> SaveCashBoxAsync(CashBoxFormDto dto, string userName);
    Task<(bool Success, string Message)> DeleteCashBoxAsync(int id, string userName);
    Task<decimal> GetCurrentBalanceAsync(int cashBoxId);

    // Transactions
    Task<PagedResult<CashBoxTransactionDto>> GetTransactionsAsync(CashBoxTransactionFilterDto filter);
    Task<CashBoxTransactionDto?> GetTransactionByIdAsync(int id);

    // Manual
    Task<(bool Success, string Message, int? Id)> CreateManualOperationAsync(
        CashBoxManualFormDto dto, string userName);

    // Dashboard & Summary
    Task<CashBoxDashboardDto> GetDashboardAsync();
    Task<List<CashBoxBalanceSummaryDto>> GetSummaryAsync(DateTime? from = null, DateTime? to = null);
}
