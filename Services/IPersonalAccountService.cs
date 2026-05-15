using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IPersonalAccountService
{
    Task<PagedResult<PersonalAccountListDto>> GetAccountsAsync(PersonalAccountFilterDto filter);
    Task<List<PersonalAccountListDto>> GetAccountsLookupAsync(string? search = null);
    Task<PersonalAccountListDto?> GetAccountByIdAsync(int id);
    Task<PersonalAccountFormDto?> GetAccountForEditAsync(int id);

    Task<(bool Success, string Message, int? Id)> SaveAccountAsync(
        PersonalAccountFormDto dto, string userName);

    Task<(bool Success, string Message)> DeleteAccountAsync(int id, string userName);

    Task<PersonalAccountStatementDto?> GetStatementAsync(
        int accountId, DateTime? from = null, DateTime? to = null);

    Task<(bool Success, string Message, int? Id)> CreateLoanTransactionAsync(
        PersonalAccountTransactionFormDto dto, string userName);

    Task<(decimal TotalCreditors, decimal TotalDebtors)> GetTotalBalancesAsync();
    Task<List<PersonalAccountSummaryDto>> GetTopAccountsAsync(int max = 5);
}
