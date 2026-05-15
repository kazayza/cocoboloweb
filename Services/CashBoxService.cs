using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class CashBoxService : ICashBoxService
{
    private readonly db24804Context _db;
    private readonly IAuditService _audit;

    public CashBoxService(db24804Context db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    // ============================================================
    //  CashBoxes
    // ============================================================
    public async Task<List<CashBoxListDto>> GetCashBoxesAsync(bool activeOnly = false)
    {
        var query = _db.CashBoxes.AsNoTracking().AsQueryable();
        if (activeOnly) query = query.Where(c => c.IsActive);

        var items = await query
            .OrderByDescending(c => c.IsDefault)
            .ThenByDescending(c => c.IsActive)
            .ThenBy(c => c.CashBoxName)
            .Select(c => new CashBoxListDto
            {
                CashBoxId = c.CashBoxId,
                CashBoxName = c.CashBoxName,
                Description = c.Description,
                CashBoxKind = c.CashBoxKind,
                Icon = c.Icon,
                Color = c.Color,
                OpeningBalance = c.OpeningBalance,
                OpeningDate = c.OpeningDate,
                IsActive = c.IsActive,
                IsDefault = c.IsDefault,
                CreatedBy = c.CreatedBy,
                CreatedAt = c.CreatedAt,
                TotalIn = _db.CashboxTransactions
                    .Where(t => t.CashBoxId == c.CashBoxId && t.TransactionType == "قبض")
                    .Sum(t => (decimal?)t.Amount) ?? 0,
                TotalOut = _db.CashboxTransactions
                    .Where(t => t.CashBoxId == c.CashBoxId && t.TransactionType == "صرف")
                    .Sum(t => (decimal?)t.Amount) ?? 0,
                TransactionsCount = _db.CashboxTransactions.Count(t => t.CashBoxId == c.CashBoxId)
            })
            .ToListAsync();

        // Apply defaults للـ Icons والـ Colors لو فاضية
        foreach (var item in items)
        {
            item.CurrentBalance = item.OpeningBalance + item.TotalIn - item.TotalOut;
            if (string.IsNullOrEmpty(item.Icon))
                item.Icon = CashBoxKinds.DefaultIcons.GetValueOrDefault(item.CashBoxKind ?? "Other", "Wallet");
            if (string.IsNullOrEmpty(item.Color))
                item.Color = CashBoxKinds.DefaultColors.GetValueOrDefault(item.CashBoxKind ?? "Other", "#94a3b8");
        }

        return items;
    }

    public async Task<CashBoxListDto?> GetCashBoxByIdAsync(int id)
    {
        var list = await GetCashBoxesAsync(false);
        return list.FirstOrDefault(c => c.CashBoxId == id);
    }

    public async Task<CashBoxFormDto?> GetCashBoxForEditAsync(int id)
    {
        var c = await _db.CashBoxes.AsNoTracking().FirstOrDefaultAsync(x => x.CashBoxId == id);
        if (c == null) return null;

        return new CashBoxFormDto
        {
            CashBoxId = c.CashBoxId,
            CashBoxName = c.CashBoxName,
            Description = c.Description,
            CashBoxKind = c.CashBoxKind ?? "Cash",
            Icon = c.Icon,
            Color = c.Color,
            OpeningBalance = c.OpeningBalance,
            OpeningDate = c.OpeningDate,
            IsActive = c.IsActive,
            IsDefault = c.IsDefault
        };
    }

    public async Task<(bool Success, string Message, int? Id)> SaveCashBoxAsync(
        CashBoxFormDto dto, string userName)
    {
        if (string.IsNullOrWhiteSpace(dto.CashBoxName))
            return (false, "اسم الخزينة مطلوب", null);

        try
        {
            var isNew = dto.CashBoxId == 0;
            CashBox entity;

            if (isNew)
            {
                entity = new CashBox
                {
                    CreatedBy = userName,
                    CreatedAt = DateTime.Now
                };
                _db.CashBoxes.Add(entity);
            }
            else
            {
                entity = await _db.CashBoxes.FindAsync(dto.CashBoxId)
                    ?? throw new Exception("الخزينة غير موجودة");
                entity.LastUpdatedBy = userName;
                entity.LastUpdatedAt = DateTime.Now;
            }

            entity.CashBoxName = dto.CashBoxName;
            entity.Description = dto.Description;
            entity.CashBoxKind = dto.CashBoxKind;
            entity.Icon = string.IsNullOrEmpty(dto.Icon)
                ? CashBoxKinds.DefaultIcons.GetValueOrDefault(dto.CashBoxKind, "Wallet")
                : dto.Icon;
            entity.Color = string.IsNullOrEmpty(dto.Color)
                ? CashBoxKinds.DefaultColors.GetValueOrDefault(dto.CashBoxKind, "#94a3b8")
                : dto.Color;
            entity.OpeningBalance = dto.OpeningBalance;
            entity.OpeningDate = dto.OpeningDate;
            entity.IsActive = dto.IsActive;

            // IsDefault: واحد بس
            if (dto.IsDefault)
            {
                var others = await _db.CashBoxes
                    .Where(c => c.CashBoxId != entity.CashBoxId && c.IsDefault).ToListAsync();
                others.ForEach(c => c.IsDefault = false);
                entity.IsDefault = true;
            }
            else
            {
                entity.IsDefault = false;
            }

            await _db.SaveChangesAsync();

            // عند الإنشاء + رصيد افتتاحي > 0 → اعمل CashboxTransaction
            if (isNew && dto.OpeningBalance > 0)
            {
                _db.CashboxTransactions.Add(new CashboxTransaction
                {
                    CashBoxId = entity.CashBoxId,
                    TransactionType = "قبض",
                    ReferenceType = CashBoxRefTypes.OpeningBalance,
                    Amount = dto.OpeningBalance,
                    TransactionDate = dto.OpeningDate ?? DateTime.Now,
                    Notes = $"الرصيد الافتتاحي للخزينة {entity.CashBoxName}",
                    CreatedBy = userName,
                    CreatedAt = DateTime.Now
                });
                await _db.SaveChangesAsync();
            }

            await _audit.LogAsync<object>("CashBoxes", isNew ? "Insert" : "Update",
                entity.CashBoxId.ToString(), null, entity, userName);

            return (true, isNew ? "تم إضافة الخزينة" : "تم التحديث", entity.CashBoxId);
        }
        catch (Exception ex)
        {
            return (false, $"حدث خطأ: {ex.Message}", null);
        }
    }

    public async Task<(bool Success, string Message)> DeleteCashBoxAsync(int id, string userName)
    {
        var box = await _db.CashBoxes.FindAsync(id);
        if (box == null) return (false, "الخزينة غير موجودة");

        var hasTrans = await _db.CashboxTransactions.AnyAsync(t => t.CashBoxId == id);
        if (hasTrans) return (false, "لا يمكن حذف خزينة بها حركات. عطّلها بدلاً من ذلك.");

        _db.CashBoxes.Remove(box);
        await _db.SaveChangesAsync();
        await _audit.LogAsync<object>("CashBoxes", "Delete", id.ToString(), box, null, userName);

        return (true, "تم الحذف");
    }

    public async Task<decimal> GetCurrentBalanceAsync(int cashBoxId)
    {
        var box = await _db.CashBoxes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CashBoxId == cashBoxId);
        if (box == null) return 0;

        var totalIn = await _db.CashboxTransactions
            .Where(t => t.CashBoxId == cashBoxId && t.TransactionType == "قبض")
            .SumAsync(t => (decimal?)t.Amount) ?? 0;
        var totalOut = await _db.CashboxTransactions
            .Where(t => t.CashBoxId == cashBoxId && t.TransactionType == "صرف")
            .SumAsync(t => (decimal?)t.Amount) ?? 0;

        return totalIn - totalOut;
    }

    // ============================================================
    //  Transactions
    // ============================================================
    public async Task<PagedResult<CashBoxTransactionDto>> GetTransactionsAsync(
        CashBoxTransactionFilterDto filter)
    {
        var query = _db.CashboxTransactions.AsNoTracking().AsQueryable();

        if (filter.CashBoxId.HasValue)
            query = query.Where(t => t.CashBoxId == filter.CashBoxId.Value);

        if (!string.IsNullOrWhiteSpace(filter.TransactionType) && filter.TransactionType != "All")
            query = query.Where(t => t.TransactionType == filter.TransactionType);

        if (!string.IsNullOrWhiteSpace(filter.ReferenceType))
            query = query.Where(t => t.ReferenceType == filter.ReferenceType);

        if (filter.DateFrom.HasValue)
            query = query.Where(t => t.TransactionDate >= filter.DateFrom.Value.Date);
        if (filter.DateTo.HasValue)
            query = query.Where(t => t.TransactionDate <= filter.DateTo.Value.Date.AddDays(1).AddTicks(-1));

        if (filter.AmountFrom.HasValue)
            query = query.Where(t => t.Amount >= filter.AmountFrom.Value);
        if (filter.AmountTo.HasValue)
            query = query.Where(t => t.Amount <= filter.AmountTo.Value);

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();
            query = query.Where(t =>
                (t.Notes != null && t.Notes.Contains(s)) ||
                (t.CreatedBy != null && t.CreatedBy.Contains(s))
            );
        }

        var totalCount = await query.CountAsync();

        query = filter.SortDescending
            ? query.OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.CashboxTransactionId)
            : query.OrderBy(t => t.TransactionDate).ThenBy(t => t.CashboxTransactionId);

        var raw = await query
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(t => new
            {
                t.CashboxTransactionId,
                t.CashBoxId,
                CashBoxName = _db.CashBoxes.Where(c => c.CashBoxId == t.CashBoxId)
                    .Select(c => c.CashBoxName).FirstOrDefault() ?? "",
                CashBoxColor = _db.CashBoxes.Where(c => c.CashBoxId == t.CashBoxId)
                    .Select(c => c.Color).FirstOrDefault(),
                t.TransactionDate,
                t.TransactionType,
                t.Amount,
                t.Notes,
                t.CreatedBy,
                t.CreatedAt,
                t.PaymentId,
                t.ReferenceId,
                t.ReferenceType
            })
            .ToListAsync();

        var items = new List<CashBoxTransactionDto>();
        foreach (var t in raw)
        {
            var dto = new CashBoxTransactionDto
            {
                CashboxTransactionId = t.CashboxTransactionId,
                CashBoxId = t.CashBoxId,
                CashBoxName = t.CashBoxName,
                CashBoxColor = t.CashBoxColor,
                TransactionDate = t.TransactionDate,
                TransactionType = t.TransactionType,
                Amount = t.Amount,
                Notes = t.Notes,
                CreatedBy = t.CreatedBy,
                CreatedAt = t.CreatedAt,
                PaymentId = t.PaymentId,
                ReferenceId = t.ReferenceId,
                ReferenceType = t.ReferenceType,
                ReferenceTypeAr = CashBoxRefTypes.All.GetValueOrDefault(t.ReferenceType ?? "", t.ReferenceType ?? "-"),
                ReferenceColor = CashBoxRefTypes.Colors.GetValueOrDefault(t.ReferenceType ?? "", "#94a3b8")
            };

            (dto.SourceTitle, dto.SourceUrl, dto.PartyName, dto.PersonalAccountName) =
                await GetSourceInfoAsync(t.ReferenceType, t.ReferenceId, t.PaymentId);

            items.Add(dto);
        }

        return new PagedResult<CashBoxTransactionDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize
        };
    }

    public async Task<CashBoxTransactionDto?> GetTransactionByIdAsync(int id)
    {
        var t = await _db.CashboxTransactions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CashboxTransactionId == id);
        if (t == null) return null;

        var box = await _db.CashBoxes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CashBoxId == t.CashBoxId);

        var dto = new CashBoxTransactionDto
        {
            CashboxTransactionId = t.CashboxTransactionId,
            CashBoxId = t.CashBoxId,
            CashBoxName = box?.CashBoxName ?? "",
            CashBoxColor = box?.Color,
            TransactionDate = t.TransactionDate,
            TransactionType = t.TransactionType,
            Amount = t.Amount,
            Notes = t.Notes,
            CreatedBy = t.CreatedBy,
            CreatedAt = t.CreatedAt,
            PaymentId = t.PaymentId,
            ReferenceId = t.ReferenceId,
            ReferenceType = t.ReferenceType,
            ReferenceTypeAr = CashBoxRefTypes.All.GetValueOrDefault(t.ReferenceType ?? "", t.ReferenceType ?? "-")
        };

        (dto.SourceTitle, dto.SourceUrl, dto.PartyName, dto.PersonalAccountName) =
            await GetSourceInfoAsync(t.ReferenceType, t.ReferenceId, t.PaymentId);

        return dto;
    }

    // ============================================================
    //  Helper: استخرج معلومات المصدر
    // ============================================================
    // ============================================================
//  Helper: استخرج معلومات المصدر + Routes صحيحة
// ============================================================
private async Task<(string? Title, string? Url, string? PartyName, string? PersonalAccountName)>
    GetSourceInfoAsync(string? refType, int? refId, int? paymentId)
{
    try
    {
        switch (refType)
        {
            case CashBoxRefTypes.SaleInvoice:
                if (refId.HasValue)
                {
                    var inv = await _db.Transactions.AsNoTracking()
                        .Where(t => t.TransactionId == refId.Value)
                        .Select(t => new { t.ReferenceNumber, t.PartyId })
                        .FirstOrDefaultAsync();
                    if (inv != null)
                    {
                        var pname = await _db.Parties.AsNoTracking()
                            .Where(p => p.PartyId == inv.PartyId)
                            .Select(p => p.PartyName).FirstOrDefaultAsync();
                        return ($"فاتورة {inv.ReferenceNumber}",
                                $"/sales/invoices/{refId.Value}", pname, null);
                    }
                }
                break;

            case CashBoxRefTypes.PurchaseInvoice:
                if (refId.HasValue)
                {
                    var inv = await _db.Transactions.AsNoTracking()
                        .Where(t => t.TransactionId == refId.Value)
                        .Select(t => new { t.ReferenceNumber })
                        .FirstOrDefaultAsync();
                    if (inv != null)
                        return ($"فاتورة شراء {inv.ReferenceNumber}",
                                $"/sales/invoices/{refId.Value}", null, null);
                }
                break;

            case CashBoxRefTypes.Expense:
                if (refId.HasValue)
                {
                    // ⭐ FIX: نقرأ المصروف ونتحقق لو كان شهر فرعي → نوديه على الأصل
                    var exp = await _db.Expenses.AsNoTracking()
                        .Where(e => e.ExpenseId == refId.Value)
                        .Select(e => new {
                            e.ExpenseId,
                            e.ExpenseName,
                            e.Torecipient,
                            e.AdvanceParentExpenseId
                        })
                        .FirstOrDefaultAsync();

                    if (exp != null)
                    {
                        // ⭐ لو شهر فرعي، نوديه على الأصل
                        var targetId = exp.AdvanceParentExpenseId ?? exp.ExpenseId;
                        return ($"مصروف: {exp.ExpenseName}",
                                $"/expenses/{targetId}/edit",  // ⭐ الـ Route الصحيح
                                exp.Torecipient, null);
                    }
                }
                break;

            case CashBoxRefTypes.Loan:
                if (refId.HasValue)
                {
                    var acc = await _db.PersonalAccounts.AsNoTracking()
                        .Where(p => p.PersonalAccountId == refId.Value)
                        .Select(p => new { p.AccountName })
                        .FirstOrDefaultAsync();
                    if (acc != null)
                        return ($"حساب: {acc.AccountName}",
                                $"/cashbox/personal-accounts/{refId.Value}/statement",
                                null, acc.AccountName);
                }
                break;

            case CashBoxRefTypes.TransferIn:
            case CashBoxRefTypes.TransferOut:
                if (refId.HasValue)
                {
                    var box = await _db.CashBoxes.AsNoTracking()
                        .Where(c => c.CashBoxId == refId.Value)
                        .Select(c => new { c.CashBoxName })
                        .FirstOrDefaultAsync();
                    if (box != null)
                        return ($"تحويل ↔ {box.CashBoxName}",
                                $"/cashbox/transactions?cashBoxId={refId.Value}",
                                null, null);
                }
                break;

            case CashBoxRefTypes.AdvanceCharge:
                return ("رسوم معاينة", "/additional-charges", null, null);

            case CashBoxRefTypes.OpeningBalance:
                // ⭐ نوديه على إدارة الخزينة
                return ("رصيد افتتاحي", "/cashbox/manage", null, null);

            case CashBoxRefTypes.ManualReceipt:
                return ("سند قبض يدوي", "/cashbox/transactions", null, null);

            case CashBoxRefTypes.ManualPayment:
                return ("سند صرف يدوي", "/cashbox/transactions", null, null);
        }
    }
    catch { }

    return (null, null, null, null);
}

    // ============================================================
    //  Manual Operations
    // ============================================================
    public async Task<(bool Success, string Message, int? Id)> CreateManualOperationAsync(
        CashBoxManualFormDto dto, string userName)
    {
        if (dto.Amount <= 0) return (false, "المبلغ يجب أن يكون أكبر من صفر", null);
        if (dto.CashBoxId == null) return (false, "الخزينة مطلوبة", null);

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            int? id = null;

            switch (dto.OperationType)
            {
                case ManualOperationTypes.ManualReceipt:
                    id = await CreateManualTransAsync(dto.CashBoxId.Value, "قبض",
                        CashBoxRefTypes.ManualReceipt, null, dto.Amount, dto.TransactionDate,
                        BuildNote("سند قبض يدوي", dto.Recipient, dto.Notes), userName);
                    break;

                case ManualOperationTypes.ManualPayment:
                    var balanceP = await GetCurrentBalanceAsync(dto.CashBoxId.Value);
                    if (balanceP < dto.Amount)
                        return (false, $"الرصيد غير كافي. المتاح: {balanceP:N2}", null);

                    id = await CreateManualTransAsync(dto.CashBoxId.Value, "صرف",
                        CashBoxRefTypes.ManualPayment, null, dto.Amount, dto.TransactionDate,
                        BuildNote("سند صرف يدوي", dto.Recipient, dto.Notes), userName);
                    break;

                case ManualOperationTypes.LoanIn:
                    if (dto.PersonalAccountId == null)
                        return (false, "يرجى اختيار الحساب الشخصي", null);
                    id = await CreateManualTransAsync(dto.CashBoxId.Value, "قبض",
                        CashBoxRefTypes.Loan, dto.PersonalAccountId, dto.Amount, dto.TransactionDate,
                        BuildNote("قرض دخل", null, dto.Notes), userName);
                    break;

                case ManualOperationTypes.LoanRepayment:
                    if (dto.PersonalAccountId == null)
                        return (false, "يرجى اختيار الحساب الشخصي", null);
                    var balanceL = await GetCurrentBalanceAsync(dto.CashBoxId.Value);
                    if (balanceL < dto.Amount)
                        return (false, $"الرصيد غير كافي. المتاح: {balanceL:N2}", null);

                    id = await CreateManualTransAsync(dto.CashBoxId.Value, "صرف",
                        CashBoxRefTypes.Loan, dto.PersonalAccountId, dto.Amount, dto.TransactionDate,
                        BuildNote("تسديد قرض", null, dto.Notes), userName);
                    break;

                case ManualOperationTypes.Transfer:
                    if (dto.ToCashBoxId == null)
                        return (false, "يرجى اختيار الخزينة المستقبلة", null);
                    if (dto.ToCashBoxId == dto.CashBoxId)
                        return (false, "لا يمكن التحويل لنفس الخزينة", null);

                    var balance = await GetCurrentBalanceAsync(dto.CashBoxId.Value);
                    if (balance < dto.Amount)
                        return (false, $"الرصيد غير كافي. المتاح: {balance:N2}", null);

                    await CreateManualTransAsync(dto.CashBoxId.Value, "صرف",
                        CashBoxRefTypes.TransferOut, dto.ToCashBoxId, dto.Amount, dto.TransactionDate,
                        BuildNote("تحويل صادر", null, dto.Notes), userName);

                    id = await CreateManualTransAsync(dto.ToCashBoxId.Value, "قبض",
                        CashBoxRefTypes.TransferIn, dto.CashBoxId, dto.Amount, dto.TransactionDate,
                        BuildNote("تحويل وارد", null, dto.Notes), userName);
                    break;

                case ManualOperationTypes.OpeningBalance:
                    id = await CreateManualTransAsync(dto.CashBoxId.Value, "قبض",
                        CashBoxRefTypes.OpeningBalance, null, dto.Amount, dto.TransactionDate,
                        BuildNote("رصيد افتتاحي", null, dto.Notes), userName);
                    break;

                default:
                    return (false, "نوع العملية غير صحيح", null);
            }

            await tx.CommitAsync();
            await _audit.LogAsync<object>("CashboxTransactions", "Manual",
                id?.ToString() ?? "0", null, dto, userName);

            var msg = ManualOperationTypes.All.GetValueOrDefault(dto.OperationType, "العملية");
            return (true, $"تم تسجيل {msg} بنجاح", id);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return (false, $"حدث خطأ: {ex.Message}", null);
        }
    }

    private string BuildNote(string title, string? recipient, string? notes)
    {
        var parts = new List<string> { title };
        if (!string.IsNullOrWhiteSpace(recipient)) parts.Add($"المستلم: {recipient}");
        if (!string.IsNullOrWhiteSpace(notes)) parts.Add(notes);
        return string.Join(" - ", parts);
    }

    private async Task<int> CreateManualTransAsync(int cashBoxId, string transType,
        string refType, int? refId, decimal amount, DateTime date, string? notes, string userName)
    {
        var entity = new CashboxTransaction
        {
            CashBoxId = cashBoxId,
            TransactionType = transType,
            ReferenceType = refType,
            ReferenceId = refId,
            Amount = amount,
            TransactionDate = date,
            Notes = notes,
            CreatedBy = userName,
            CreatedAt = DateTime.Now
        };
        _db.CashboxTransactions.Add(entity);
        await _db.SaveChangesAsync();
        return entity.CashboxTransactionId;
    }

    // ============================================================
    //  Dashboard
    // ============================================================
    public async Task<CashBoxDashboardDto> GetDashboardAsync()
    {
        var dashboard = new CashBoxDashboardDto();
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);

        dashboard.CashBoxes = await GetCashBoxesAsync(activeOnly: false);
        dashboard.TotalBalance = dashboard.CashBoxes
            .Where(c => c.IsActive)
            .Sum(c => c.CurrentBalance);
        dashboard.CashBoxesCount = dashboard.CashBoxes.Count;
        dashboard.ActiveCashBoxesCount = dashboard.CashBoxes.Count(c => c.IsActive);

        // اليوم
        dashboard.TodayIn = await _db.CashboxTransactions.AsNoTracking()
            .Where(t => t.TransactionType == "قبض" && t.TransactionDate.Date == today)
            .SumAsync(t => (decimal?)t.Amount) ?? 0;
        dashboard.TodayOut = await _db.CashboxTransactions.AsNoTracking()
            .Where(t => t.TransactionType == "صرف" && t.TransactionDate.Date == today)
            .SumAsync(t => (decimal?)t.Amount) ?? 0;

        // الشهر
        dashboard.MonthIn = await _db.CashboxTransactions.AsNoTracking()
            .Where(t => t.TransactionType == "قبض" && t.TransactionDate >= monthStart)
            .SumAsync(t => (decimal?)t.Amount) ?? 0;
        dashboard.MonthOut = await _db.CashboxTransactions.AsNoTracking()
            .Where(t => t.TransactionType == "صرف" && t.TransactionDate >= monthStart)
            .SumAsync(t => (decimal?)t.Amount) ?? 0;

        // آخر 30 يوم
        var fromDate = today.AddDays(-29);
        var dailyData = await _db.CashboxTransactions.AsNoTracking()
            .Where(t => t.TransactionDate >= fromDate)
            .GroupBy(t => t.TransactionDate.Date)
            .Select(g => new
            {
                Date = g.Key,
                In = g.Where(x => x.TransactionType == "قبض").Sum(x => (decimal?)x.Amount) ?? 0,
                Out = g.Where(x => x.TransactionType == "صرف").Sum(x => (decimal?)x.Amount) ?? 0
            })
            .ToListAsync();

        dashboard.Last30Days = Enumerable.Range(0, 30)
            .Select(i => fromDate.AddDays(i))
            .Select(d => new DailyMovementDto
            {
                Date = d,
                In = dailyData.FirstOrDefault(x => x.Date == d)?.In ?? 0,
                Out = dailyData.FirstOrDefault(x => x.Date == d)?.Out ?? 0
            }).ToList();

        // تفصيل النوع
        var typeData = await _db.CashboxTransactions.AsNoTracking()
            .Where(t => t.TransactionDate >= fromDate && t.ReferenceType != null)
            .GroupBy(t => t.ReferenceType)
            .Select(g => new
            {
                Type = g.Key,
                Total = g.Sum(x => x.Amount),
                Count = g.Count()
            })
            .ToListAsync();

        dashboard.TypeBreakdown = typeData.Select(x => new TypeBreakdownDto
        {
            ReferenceType = x.Type ?? "Other",
            ReferenceTypeAr = CashBoxRefTypes.All.GetValueOrDefault(x.Type ?? "", x.Type ?? "-"),
            Total = x.Total,
            Count = x.Count,
            Color = CashBoxRefTypes.Colors.GetValueOrDefault(x.Type ?? "", "#94a3b8")
        }).OrderByDescending(x => x.Total).ToList();

        // آخر 10 حركات
        dashboard.RecentTransactions = await _db.CashboxTransactions.AsNoTracking()
            .OrderByDescending(t => t.CashboxTransactionId)
            .Take(10)
            .Select(t => new RecentTransactionDto
            {
                TransactionId = t.CashboxTransactionId,
                TransactionDate = t.TransactionDate,
                CashBoxName = _db.CashBoxes.Where(c => c.CashBoxId == t.CashBoxId)
                    .Select(c => c.CashBoxName).FirstOrDefault() ?? "",
                TransactionType = t.TransactionType,
                Amount = t.Amount,
                Description = t.Notes,
                ReferenceType = t.ReferenceType
            }).ToListAsync();

        // الذمم
        var allAccounts = await _db.PersonalAccounts.AsNoTracking().ToListAsync();
        decimal totalCreditors = 0, totalDebtors = 0;
        var accountSummaries = new List<PersonalAccountSummaryDto>();

        foreach (var acc in allAccounts)
        {
            var loansIn = await _db.CashboxTransactions
                .Where(t => t.ReferenceType == CashBoxRefTypes.Loan
                    && t.ReferenceId == acc.PersonalAccountId
                    && t.TransactionType == "قبض")
                .SumAsync(t => (decimal?)t.Amount) ?? 0;
            var loansOut = await _db.CashboxTransactions
                .Where(t => t.ReferenceType == CashBoxRefTypes.Loan
                    && t.ReferenceId == acc.PersonalAccountId
                    && t.TransactionType == "صرف")
                .SumAsync(t => (decimal?)t.Amount) ?? 0;

            var openingDebit = acc.OpeningType == "Debit" ? acc.OpeningBalance : 0;
            var openingCredit = acc.OpeningType == "Credit" ? acc.OpeningBalance : 0;
            var balance = (openingCredit - openingDebit) + (loansIn - loansOut);

            if (balance > 0) totalCreditors += balance;
            else if (balance < 0) totalDebtors += Math.Abs(balance);

            if (balance != 0)
            {
                accountSummaries.Add(new PersonalAccountSummaryDto
                {
                    PersonalAccountId = acc.PersonalAccountId,
                    AccountName = acc.AccountName,
                    AccountType = acc.AccountType,
                    CurrentBalance = balance
                });
            }
        }

        dashboard.TotalCreditors = totalCreditors;
        dashboard.TotalDebtors = totalDebtors;
        dashboard.TopPersonalAccounts = accountSummaries
            .OrderByDescending(x => Math.Abs(x.CurrentBalance))
            .Take(5).ToList();

        return dashboard;
    }

    // ============================================================
    //  Summary
    // ============================================================
    public async Task<List<CashBoxBalanceSummaryDto>> GetSummaryAsync(
        DateTime? from = null, DateTime? to = null)
    {
        var boxes = await _db.CashBoxes.AsNoTracking().ToListAsync();
        var result = new List<CashBoxBalanceSummaryDto>();

        foreach (var box in boxes)
        {
            var query = _db.CashboxTransactions.AsNoTracking()
                .Where(t => t.CashBoxId == box.CashBoxId);

            if (from.HasValue) query = query.Where(t => t.TransactionDate >= from.Value.Date);
            if (to.HasValue) query = query.Where(t => t.TransactionDate <= to.Value.Date.AddDays(1).AddTicks(-1));

            var totalIn = await query.Where(t => t.TransactionType == "قبض")
                .SumAsync(t => (decimal?)t.Amount) ?? 0;
            var totalOut = await query.Where(t => t.TransactionType == "صرف")
                .SumAsync(t => (decimal?)t.Amount) ?? 0;
            var inCount = await query.Where(t => t.TransactionType == "قبض").CountAsync();
            var outCount = await query.Where(t => t.TransactionType == "صرف").CountAsync();

            result.Add(new CashBoxBalanceSummaryDto
            {
                CashBoxId = box.CashBoxId,
                CashBoxName = box.CashBoxName,
                CashBoxKind = box.CashBoxKind,
                Color = box.Color ?? CashBoxKinds.DefaultColors.GetValueOrDefault(box.CashBoxKind ?? "Other"),
                OpeningBalance = box.OpeningBalance,
                TotalIn = totalIn,
                TotalOut = totalOut,
                InCount = inCount,
                OutCount = outCount,
                IsActive = box.IsActive
            });
        }

        return result.OrderByDescending(r => r.IsActive)
                     .ThenByDescending(r => r.CurrentBalance).ToList();
    }
}
