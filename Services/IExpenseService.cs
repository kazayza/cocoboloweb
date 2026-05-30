using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IExpenseService
{
    Task<PagedResult<ExpenseListDto>> GetExpensesAsync(ExpenseFilterDto filter);
    Task<ExpenseFormDto?> GetExpenseForEditAsync(int id);
    Task<ExpenseStatsDto> GetStatsAsync(DateTime? from = null, DateTime? to = null);
    Task<ExpenseDashboardDto> GetDashboardDataAsync();

    Task<(bool Success, string Message, int? Id)> SaveExpenseAsync(
        ExpenseFormDto dto, string userName);

    Task<(bool Success, string Message)> DeleteExpenseAsync(int id, string userName);

    // ⭐ دالة التصدير للإكسيل الجديدة
    Task<byte[]> ExportExpensesToExcelAsync(ExpenseFilterDto filter);

    // Expense Groups
    Task<List<ExpenseGroupDto>> GetExpenseGroupsAsync(bool asTree = false);
    Task<ExpenseGroupDto?> GetExpenseGroupByIdAsync(int id);

    Task<(bool Success, string Message, int? Id)> SaveExpenseGroupAsync(
        ExpenseGroupFormDto dto, string userName);

    Task<(bool Success, string Message)> DeleteExpenseGroupAsync(int id, string userName);

    Task<List<ExpenseListDto>> GetAdvanceChildrenAsync(int parentExpenseId);
    
    
}