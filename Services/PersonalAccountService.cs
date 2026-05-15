using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class PersonalAccountService : IPersonalAccountService
{
    private readonly db24804Context _db;
    private readonly IAuditService _audit;

    public PersonalAccountService(db24804Context db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<PagedResult<PersonalAccountListDto>> GetAccountsAsync(
        PersonalAccountFilterDto filter)
    {
        var query = _db.PersonalAccounts.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();
            query = query.Where(a => a.AccountName.Contains(s)
                || (a.Phone != null && a.Phone.Contains(s))
                || (a.NationalId != null && a.NationalId.Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(filter.AccountType))
            query = query.Where(a => a.AccountType == filter.AccountType);

        if (filter.IsActive.HasValue)
            query = query.Where(a => a.IsActive == filter.IsActive.Value);

        var totalCount = await query.CountAsync();

        var accounts = await query
            .OrderByDescending(a => a.IsActive)
            .ThenBy(a => a.AccountName)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        var items = new List<PersonalAccountListDto>();
        foreach (var a in accounts)
            items.Add(await BuildAccountDtoAsync(a));

        if (!string.IsNullOrWhiteSpace(filter.BalanceFilter) && filter.BalanceFilter != "All")
        {
            items = filter.BalanceFilter switch
            {
                "Positive" => items.Where(i => i.CurrentBalance > 0).ToList(),
                "Negative" => items.Where(i => i.CurrentBalance < 0).ToList(),
                "Zero" => items.Where(i => i.CurrentBalance == 0).ToList(),
                _ => items
            };
        }

        return new PagedResult<PersonalAccountListDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize
        };
    }

    public async Task<List<PersonalAccountListDto>> GetAccountsLookupAsync(string? search = null)
    {
        var query = _db.PersonalAccounts.AsNoTracking().Where(a => a.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(a => a.AccountName.Contains(s)
                || (a.Phone != null && a.Phone.Contains(s)));
        }

        var accounts = await query.OrderBy(a => a.AccountName).Take(20).ToListAsync();
        var items = new List<PersonalAccountListDto>();
        foreach (var a in accounts)
            items.Add(await BuildAccountDtoAsync(a));
        return items;
    }

    public async Task<PersonalAccountListDto?> GetAccountByIdAsync(int id)
    {
        var a = await _db.PersonalAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.PersonalAccountId == id);
        return a == null ? null : await BuildAccountDtoAsync(a);
    }

    public async Task<PersonalAccountFormDto?> GetAccountForEditAsync(int id)
    {
        var a = await _db.PersonalAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.PersonalAccountId == id);
        if (a == null) return null;

        return new PersonalAccountFormDto
        {
            PersonalAccountId = a.PersonalAccountId,
            AccountName = a.AccountName,
            AccountType = a.AccountType,
            Phone = a.Phone,
            Email = a.Email,
            NationalId = a.NationalId,
            Notes = a.Notes,
            OpeningBalance = a.OpeningBalance,
            OpeningDate = a.OpeningDate,
            OpeningType = a.OpeningType,
            IsActive = a.IsActive
        };
    }

    public async Task<(bool Success, string Message, int? Id)> SaveAccountAsync(
        PersonalAccountFormDto dto, string userName)
    {
        if (string.IsNullOrWhiteSpace(dto.AccountName))
            return (false, "اسم الحساب مطلوب", null);

        try
        {
            var isNew = dto.PersonalAccountId == 0;
            PersonalAccount entity;

            if (isNew)
            {
                entity = new PersonalAccount
                {
                    CreatedBy = userName,
                    CreatedAt = DateTime.Now
                };
                _db.PersonalAccounts.Add(entity);
            }
            else
            {
                entity = await _db.PersonalAccounts.FindAsync(dto.PersonalAccountId)
                    ?? throw new Exception("الحساب غير موجود");
                entity.LastUpdatedBy = userName;
                entity.LastUpdatedAt = DateTime.Now;
            }

            entity.AccountName = dto.AccountName;
            entity.AccountType = dto.AccountType;
            entity.Phone = dto.Phone;
            entity.Email = dto.Email;
            entity.NationalId = dto.NationalId;
            entity.Notes = dto.Notes;
            entity.OpeningBalance = dto.OpeningBalance;
            entity.OpeningDate = dto.OpeningDate;
            entity.OpeningType = dto.OpeningType;
            entity.IsActive = dto.IsActive;

            await _db.SaveChangesAsync();
            await _audit.LogAsync<object>("PersonalAccounts", isNew ? "Insert" : "Update",
                entity.PersonalAccountId.ToString(), null, entity, userName);

            return (true, isNew ? "تم إضافة الحساب" : "تم التحديث", entity.PersonalAccountId);
        }
        catch (Exception ex)
        {
            return (false, $"حدث خطأ: {ex.Message}", null);
        }
    }

    public async Task<(bool Success, string Message)> DeleteAccountAsync(int id, string userName)
    {
        var acc = await _db.PersonalAccounts.FindAsync(id);
        if (acc == null) return (false, "الحساب غير موجود");

        var hasTrans = await _db.CashboxTransactions.AnyAsync(t =>
            t.ReferenceType == CashBoxRefTypes.Loan && t.ReferenceId == id);
        if (hasTrans)
            return (false, "لا يمكن حذف حساب به حركات. عطّله بدلاً من ذلك.");

        _db.PersonalAccounts.Remove(acc);
        await _db.SaveChangesAsync();
        await _audit.LogAsync<object>("PersonalAccounts", "Delete",
            id.ToString(), acc, null, userName);

        return (true, "تم الحذف");
    }

    public async Task<PersonalAccountStatementDto?> GetStatementAsync(
        int accountId, DateTime? from = null, DateTime? to = null)
    {
        var account = await GetAccountByIdAsync(accountId);
        if (account == null) return null;

        var dto = new PersonalAccountStatementDto
        {
            Account = account,
            FromDate = from,
            ToDate = to
        };

        var openingDebit = account.OpeningType == "Debit" ? account.OpeningBalance : 0;
        var openingCredit = account.OpeningType == "Credit" ? account.OpeningBalance : 0;
        decimal openingAtStart = openingCredit - openingDebit;

        if (from.HasValue)
        {
            var beforeIn = await _db.CashboxTransactions.AsNoTracking()
                .Where(t => t.ReferenceType == CashBoxRefTypes.Loan
                    && t.ReferenceId == accountId
                    && t.TransactionType == "قبض"
                    && t.TransactionDate < from.Value.Date)
                .SumAsync(t => (decimal?)t.Amount) ?? 0;
            var beforeOut = await _db.CashboxTransactions.AsNoTracking()
                .Where(t => t.ReferenceType == CashBoxRefTypes.Loan
                    && t.ReferenceId == accountId
                    && t.TransactionType == "صرف"
                    && t.TransactionDate < from.Value.Date)
                .SumAsync(t => (decimal?)t.Amount) ?? 0;

            openingAtStart += (beforeIn - beforeOut);
        }

        dto.OpeningBalanceAtStart = openingAtStart;

        var query = _db.CashboxTransactions.AsNoTracking()
            .Where(t => t.ReferenceType == CashBoxRefTypes.Loan && t.ReferenceId == accountId);

        if (from.HasValue) query = query.Where(t => t.TransactionDate >= from.Value.Date);
        if (to.HasValue) query = query.Where(t => t.TransactionDate <= to.Value.Date.AddDays(1).AddTicks(-1));

        var trans = await query.OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CashboxTransactionId)
            .Select(t => new
            {
                t.CashboxTransactionId,
                t.TransactionDate,
                t.TransactionType,
                t.Amount,
                t.CashBoxId,
                t.Notes,
                t.CreatedBy,
                CashBoxName = _db.CashBoxes.Where(c => c.CashBoxId == t.CashBoxId)
                    .Select(c => c.CashBoxName).FirstOrDefault()
            })
            .ToListAsync();

        decimal running = openingAtStart;
        foreach (var t in trans)
        {
            var amountIn = t.TransactionType == "قبض" ? t.Amount : 0;
            var amountOut = t.TransactionType == "صرف" ? t.Amount : 0;
            running += (amountIn - amountOut);

            dto.Transactions.Add(new PersonalAccountTransactionDto
            {
                CashboxTransactionId = t.CashboxTransactionId,
                TransactionDate = t.TransactionDate,
                Description = amountIn > 0 ? "قرض دخل من الحساب" : "تسديد قرض للحساب",
                AmountIn = amountIn,
                AmountOut = amountOut,
                RunningBalance = running,
                CashBoxName = t.CashBoxName,
                Notes = t.Notes,
                CreatedBy = t.CreatedBy
            });
        }

        dto.ClosingBalanceAtEnd = running;
        return dto;
    }

    public async Task<(bool Success, string Message, int? Id)> CreateLoanTransactionAsync(
        PersonalAccountTransactionFormDto dto, string userName)
    {
        if (dto.Amount <= 0) return (false, "المبلغ يجب أن يكون أكبر من صفر", null);
        if (dto.CashBoxId == null) return (false, "الخزينة مطلوبة", null);

        var account = await _db.PersonalAccounts.FindAsync(dto.PersonalAccountId);
        if (account == null) return (false, "الحساب غير موجود", null);
        if (!account.IsActive) return (false, "الحساب غير نشط", null);

        try
        {
            var transType = dto.OperationType == "LoanIn" ? "قبض" : "صرف";

            if (transType == "صرف")
            {
                var totalIn = await _db.CashboxTransactions
                    .Where(t => t.CashBoxId == dto.CashBoxId.Value && t.TransactionType == "قبض")
                    .SumAsync(t => (decimal?)t.Amount) ?? 0;
                var totalOut = await _db.CashboxTransactions
                    .Where(t => t.CashBoxId == dto.CashBoxId.Value && t.TransactionType == "صرف")
                    .SumAsync(t => (decimal?)t.Amount) ?? 0;
                var balance = totalIn - totalOut;
                if (balance < dto.Amount)
                    return (false, $"رصيد الخزينة غير كافي. المتاح: {balance:N2}", null);
            }

            var entity = new CashboxTransaction
            {
                CashBoxId = dto.CashBoxId.Value,
                TransactionType = transType,
                ReferenceType = CashBoxRefTypes.Loan,
                ReferenceId = dto.PersonalAccountId,
                Amount = dto.Amount,
                TransactionDate = dto.TransactionDate,
                Notes = dto.Notes ?? (transType == "قبض"
                    ? $"قرض دخل من {account.AccountName}"
                    : $"تسديد قرض إلى {account.AccountName}"),
                CreatedBy = userName,
                CreatedAt = DateTime.Now
            };
            _db.CashboxTransactions.Add(entity);
            await _db.SaveChangesAsync();

            await _audit.LogAsync<object>("CashboxTransactions", "Loan",
                entity.CashboxTransactionId.ToString(), null, entity, userName);

            return (true,
                transType == "قبض" ? "تم تسجيل القرض الداخل" : "تم تسجيل التسديد",
                entity.CashboxTransactionId);
        }
        catch (Exception ex)
        {
            return (false, $"حدث خطأ: {ex.Message}", null);
        }
    }

    public async Task<(decimal TotalCreditors, decimal TotalDebtors)> GetTotalBalancesAsync()
    {
        var accounts = await _db.PersonalAccounts.AsNoTracking().ToListAsync();
        decimal creditors = 0, debtors = 0;

        foreach (var a in accounts)
        {
            var dto = await BuildAccountDtoAsync(a);
            if (dto.CurrentBalance > 0) creditors += dto.CurrentBalance;
            else if (dto.CurrentBalance < 0) debtors += Math.Abs(dto.CurrentBalance);
        }

        return (creditors, debtors);
    }

    public async Task<List<PersonalAccountSummaryDto>> GetTopAccountsAsync(int max = 5)
    {
        var accounts = await _db.PersonalAccounts.AsNoTracking().ToListAsync();
        var list = new List<PersonalAccountSummaryDto>();

        foreach (var a in accounts)
        {
            var dto = await BuildAccountDtoAsync(a);
            list.Add(new PersonalAccountSummaryDto
            {
                PersonalAccountId = dto.PersonalAccountId,
                AccountName = dto.AccountName,
                AccountType = dto.AccountType,
                CurrentBalance = dto.CurrentBalance
            });
        }

        return list
            .Where(x => x.CurrentBalance != 0)
            .OrderByDescending(x => Math.Abs(x.CurrentBalance))
            .Take(max).ToList();
    }

    private async Task<PersonalAccountListDto> BuildAccountDtoAsync(PersonalAccount a)
    {
        var totalIn = await _db.CashboxTransactions.AsNoTracking()
            .Where(t => t.ReferenceType == CashBoxRefTypes.Loan
                && t.ReferenceId == a.PersonalAccountId
                && t.TransactionType == "قبض")
            .SumAsync(t => (decimal?)t.Amount) ?? 0;
        var totalOut = await _db.CashboxTransactions.AsNoTracking()
            .Where(t => t.ReferenceType == CashBoxRefTypes.Loan
                && t.ReferenceId == a.PersonalAccountId
                && t.TransactionType == "صرف")
            .SumAsync(t => (decimal?)t.Amount) ?? 0;
        var lastDate = await _db.CashboxTransactions.AsNoTracking()
            .Where(t => t.ReferenceType == CashBoxRefTypes.Loan
                && t.ReferenceId == a.PersonalAccountId)
            .OrderByDescending(t => t.TransactionDate)
            .Select(t => (DateTime?)t.TransactionDate)
            .FirstOrDefaultAsync();
        var count = await _db.CashboxTransactions.AsNoTracking()
            .Where(t => t.ReferenceType == CashBoxRefTypes.Loan
                && t.ReferenceId == a.PersonalAccountId)
            .CountAsync();

        var openingDebit = a.OpeningType == "Debit" ? a.OpeningBalance : 0;
        var openingCredit = a.OpeningType == "Credit" ? a.OpeningBalance : 0;
        var balance = (openingCredit - openingDebit) + (totalIn - totalOut);

        return new PersonalAccountListDto
        {
            PersonalAccountId = a.PersonalAccountId,
            AccountName = a.AccountName,
            AccountType = a.AccountType,
            Phone = a.Phone,
            Email = a.Email,
            NationalId = a.NationalId,
            Notes = a.Notes,
            IsActive = a.IsActive,
            OpeningBalance = a.OpeningBalance,
            OpeningType = a.OpeningType,
            TotalIn = totalIn,
            TotalOut = totalOut,
            CurrentBalance = balance,
            TransactionsCount = count,
            LastTransactionDate = lastDate,
            CreatedBy = a.CreatedBy,
            CreatedAt = a.CreatedAt
        };
    }
}
