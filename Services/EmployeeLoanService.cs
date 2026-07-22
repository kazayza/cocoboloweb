using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

// ============================================================
// ✅ EmployeeLoanService - النسخة المُصلَحة الكاملة
// استبدل الملف بالكامل
// ============================================================

public class EmployeeLoanService : IEmployeeLoanService
{
    private readonly db24804Context _db;
    private readonly IAuditService  _audit;

    public EmployeeLoanService(db24804Context db, IAuditService audit)
    {
        _db    = db;
        _audit = audit;
    }

    // ============================================================
    // قائمة السلف
    // ============================================================
    public async Task<PagedResult<LoanListDto>> GetLoansAsync(LoanFilterDto filter)
    {
        var query = from l in _db.EmployeeLoans.AsNoTracking()
                    join e in _db.Employees.AsNoTracking()
                        on l.EmployeeId equals e.EmployeeId
                    select new { l, e };

        if (filter.EmployeeId.HasValue)
            query = query.Where(x => x.l.EmployeeId == filter.EmployeeId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(x => x.l.Status == filter.Status);

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();
            query = query.Where(x =>
                x.e.FullName.Contains(s) ||
                (x.e.Department != null && x.e.Department.Contains(s)) ||
                (x.e.JobTitle != null && x.e.JobTitle.Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Month))
        {
            var month = filter.Month.Trim();
            query = query.Where(x => x.l.StartDeductionMonth == month
                || _db.LoanInstallments.Any(i => i.LoanId == x.l.LoanId && i.DeductionMonth == month));
        }

        if (filter.MinLoanAmount.HasValue)
            query = query.Where(x => x.l.LoanAmount >= filter.MinLoanAmount.Value);

        if (filter.MaxLoanAmount.HasValue)
            query = query.Where(x => x.l.LoanAmount <= filter.MaxLoanAmount.Value);

        var totalCount = await query.CountAsync();

        // ✅ جلب اسم الخزينة بـ Join مش subquery
        var rawData = await query
            .OrderByDescending(x => x.l.LoanDate)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(x => new
            {
                x.l.LoanId,
                x.l.EmployeeId,
                x.e.FullName,
                x.e.Department,
                x.e.JobTitle,
                x.l.LoanAmount,
                x.l.MonthlyInstallment,
                x.l.TotalInstallments,
                x.l.PaidInstallments,
                x.l.RemainingAmount,
                x.l.LoanDate,
                x.l.StartDeductionMonth,
                x.l.Status,
                x.l.Notes,
                x.l.ApprovedBy,
                x.l.CreatedBy,
                x.l.CreatedAt,
                x.l.CashBoxId
            })
            .ToListAsync();

        // ✅ جيب أسماء الخزن دفعة واحدة
        var cashBoxIds = rawData.Where(x => x.CashBoxId.HasValue)
                                .Select(x => x.CashBoxId!.Value).Distinct().ToList();
        var cashBoxNames = cashBoxIds.Any()
            ? await _db.CashBoxes.AsNoTracking()
                       .Where(c => cashBoxIds.Contains(c.CashBoxId))
                       .ToDictionaryAsync(c => c.CashBoxId, c => c.CashBoxName)
            : new Dictionary<int, string>();

        var items = rawData.Select(x =>
        {
            string? expectedEnd = null;
            if (DateTime.TryParseExact(x.StartDeductionMonth + "-01", "yyyy-MM-dd",
                null, System.Globalization.DateTimeStyles.None, out var startDate))
            {
                if (x.TotalInstallments - x.PaidInstallments > 0)
                    expectedEnd = startDate.AddMonths(x.TotalInstallments - 1).ToString("yyyy-MM");
            }

            return new LoanListDto
            {
                LoanId              = x.LoanId,
                EmployeeId          = x.EmployeeId,
                EmployeeName        = x.FullName,
                Department          = x.Department,
                JobTitle            = x.JobTitle,
                LoanAmount          = x.LoanAmount,
                MonthlyInstallment  = x.MonthlyInstallment,
                TotalInstallments   = x.TotalInstallments,
                PaidInstallments    = x.PaidInstallments,
                RemainingAmount     = x.RemainingAmount,
                LoanDate            = x.LoanDate,
                StartDeductionMonth = x.StartDeductionMonth,
                ExpectedEndMonth    = expectedEnd,
                Status              = x.Status,
                CashBoxName         = x.CashBoxId.HasValue
                                    ? cashBoxNames.GetValueOrDefault(x.CashBoxId.Value)
                                    : null,
                Notes               = x.Notes,
                ApprovedBy          = x.ApprovedBy,
                CreatedBy           = x.CreatedBy,
                CreatedAt           = x.CreatedAt
            };
        }).ToList();

        return new PagedResult<LoanListDto>
        {
            Items      = items,
            TotalCount = totalCount,
            PageNumber = filter.PageNumber,
            PageSize   = filter.PageSize
        };
    }

    // ============================================================
    // تفاصيل سلفة مع الأقساط
    // ============================================================
    public async Task<LoanDetailDto?> GetLoanDetailAsync(int loanId)
    {
        var loan = await (
            from l in _db.EmployeeLoans.AsNoTracking()
            join e in _db.Employees.AsNoTracking() on l.EmployeeId equals e.EmployeeId
            join cb in _db.CashBoxes.AsNoTracking() on l.CashBoxId equals cb.CashBoxId into cbs
            from cb in cbs.DefaultIfEmpty()
            where l.LoanId == loanId
            select new LoanListDto
            {
                LoanId              = l.LoanId,
                EmployeeId          = l.EmployeeId,
                EmployeeName        = e.FullName,
                Department          = e.Department,
                JobTitle            = e.JobTitle,
                LoanAmount          = l.LoanAmount,
                MonthlyInstallment  = l.MonthlyInstallment,
                TotalInstallments   = l.TotalInstallments,
                PaidInstallments    = l.PaidInstallments,
                RemainingAmount     = l.RemainingAmount,
                LoanDate            = l.LoanDate,
                StartDeductionMonth = l.StartDeductionMonth,
                Status              = l.Status,
                CashBoxName         = cb != null ? cb.CashBoxName : null,
                Notes               = l.Notes,
                ApprovedBy          = l.ApprovedBy,
                CreatedBy           = l.CreatedBy,
                CreatedAt           = l.CreatedAt
            }).FirstOrDefaultAsync();

        if (loan is null) return null;

        // جلب الأقساط مع اسم شهر الراتب
        var installments = await (
            from i in _db.LoanInstallments.AsNoTracking()
            join p in _db.Payrolls.AsNoTracking() on i.PayrollId equals p.PayrollId into payrolls
            from p in payrolls.DefaultIfEmpty()
            where i.LoanId == loanId
            orderby i.InstallmentNumber
            select new InstallmentListDto
            {
                InstallmentId     = i.InstallmentId,
                LoanId            = i.LoanId,
                EmployeeId        = i.EmployeeId,
                InstallmentNumber = i.InstallmentNumber,
                DeductionMonth    = i.DeductionMonth,
                Amount            = i.Amount,
                Status            = i.Status,
                DeductionDate     = i.DeductionDate,
                Notes             = i.Notes,
                PayrollMonth      = p != null ? p.PayrollMonth : null
            }).ToListAsync();

        return new LoanDetailDto { Loan = loan, Installments = installments };
    }

    // ============================================================
    // للفورم
    // ============================================================
    public async Task<LoanFormDto?> GetLoanForEditAsync(int loanId)
    {
        return await (
            from l in _db.EmployeeLoans.AsNoTracking()
            join e in _db.Employees.AsNoTracking() on l.EmployeeId equals e.EmployeeId
            where l.LoanId == loanId
            select new LoanFormDto
            {
                LoanId              = l.LoanId,
                EmployeeId          = l.EmployeeId,
                EmployeeName        = e.FullName,
                LoanAmount          = l.LoanAmount,
                TotalInstallments   = l.TotalInstallments,
                MonthlyInstallment  = l.MonthlyInstallment,
                LoanDate            = l.LoanDate,
                StartDeductionMonth = l.StartDeductionMonth,
                CashBoxId           = l.CashBoxId,
                Notes               = l.Notes,
                ApprovedBy          = l.ApprovedBy
            }).FirstOrDefaultAsync();
    }

    // ============================================================
    // ⭐ حفظ سلفة جديدة
    // ============================================================
    public async Task<(bool Success, string Message, int? LoanId)> SaveLoanAsync(
        LoanFormDto dto, string userName)
    {
        if (dto.EmployeeId == 0)
            return (false, "يرجى اختيار الموظف", null);
        if (dto.LoanAmount <= 0)
            return (false, "قيمة السلفة يجب أن تكون أكبر من صفر", null);
        if (dto.TotalInstallments <= 0)
            return (false, "عدد الأقساط يجب أن يكون أكبر من صفر", null);
        if (string.IsNullOrWhiteSpace(dto.StartDeductionMonth))
            return (false, "يرجى تحديد شهر بداية الخصم", null);
        if (!dto.CashBoxId.HasValue || dto.CashBoxId == 0)
            return (false, "يرجى اختيار الخزينة", null);

        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EmployeeId == dto.EmployeeId);
        if (employee is null)
            return (false, "الموظف غير موجود", null);

        var cashBox = await _db.CashBoxes
            .FirstOrDefaultAsync(c => c.CashBoxId == dto.CashBoxId);
        if (cashBox is null)
            return (false, "الخزينة غير موجودة", null);

        // ✅ التحقق من رصيد الخزينة قبل الصرف
        var balance = await GetCashBoxBalanceAsync(dto.CashBoxId!.Value);
        if (balance < dto.LoanAmount)
            return (false, $"رصيد الخزينة ({balance:N2} جـ) أقل من قيمة السلفة ({dto.LoanAmount:N2} جـ)", null);

        dto.MonthlyInstallment = Math.Round(dto.LoanAmount / dto.TotalInstallments, 2);

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // 1. إنشاء السلفة
            var loan = new EmployeeLoan
            {
                EmployeeId          = dto.EmployeeId,
                LoanAmount          = dto.LoanAmount,
                TotalInstallments   = dto.TotalInstallments,
                MonthlyInstallment  = dto.MonthlyInstallment,
                RemainingAmount     = dto.LoanAmount,
                PaidInstallments    = 0,
                LoanDate            = dto.LoanDate,
                StartDeductionMonth = dto.StartDeductionMonth,
                Status              = "Active",
                CashBoxId           = dto.CashBoxId,
                Notes               = dto.Notes,
                ApprovedBy          = dto.ApprovedBy,
                CreatedBy           = userName,
                CreatedAt           = DateTime.Now
            };

            _db.EmployeeLoans.Add(loan);
            await _db.SaveChangesAsync();

            // 2. إنشاء الأقساط
            if (!DateTime.TryParseExact(dto.StartDeductionMonth + "-01", "yyyy-MM-dd",
                null, System.Globalization.DateTimeStyles.None, out var startDate))
                throw new Exception("صيغة شهر البداية غير صحيحة (مثال: 2026-06)");

            decimal cumulative = 0;
            var installments   = new List<LoanInstallment>();

            for (int i = 0; i < dto.TotalInstallments; i++)
            {
                var month = startDate.AddMonths(i).ToString("yyyy-MM");
                decimal amt = (i == dto.TotalInstallments - 1)
                    ? dto.LoanAmount - cumulative   // آخر قسط = الباقي
                    : dto.MonthlyInstallment;
                cumulative += amt;

                installments.Add(new LoanInstallment
                {
                    LoanId            = loan.LoanId,
                    EmployeeId        = dto.EmployeeId,
                    InstallmentNumber = i + 1,
                    DeductionMonth    = month,
                    Amount            = amt,
                    Status            = "Pending",
                    CreatedBy         = userName,
                    CreatedAt         = DateTime.Now
                });
            }

            _db.LoanInstallments.AddRange(installments);

            // 3. حركة خزينة (صرف)
            var cashTx = new CashboxTransaction
            {
                CashBoxId       = dto.CashBoxId!.Value,
                TransactionType = "صرف",
                ReferenceType   = "Loan",
                Amount          = dto.LoanAmount,
                TransactionDate = dto.LoanDate,
                Notes           = $"سلفة للموظف {employee.FullName} - {dto.TotalInstallments} قسط",
                CreatedBy       = userName,
                CreatedAt       = DateTime.Now
            };
            _db.CashboxTransactions.Add(cashTx);
            await _db.SaveChangesAsync();

            loan.CashboxTransactionId = cashTx.CashboxTransactionId;
            await _db.SaveChangesAsync();

            await _audit.LogAsync("EmployeeLoans", "Insert",
                loan.LoanId.ToString(), null, new { loan.LoanId, loan.LoanAmount, loan.EmployeeId }, userName);

            await tx.CommitAsync();
            return (true, $"✅ تم صرف سلفة {dto.LoanAmount:N2} جـ للموظف {employee.FullName}", loan.LoanId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return (false, $"حدث خطأ: {ex.Message}", null);
        }
    }

    // ============================================================
    // إلغاء سلفة
    // ============================================================
    public async Task<(bool Success, string Message)> CancelLoanAsync(int loanId, string userName)
    {
        var loan = await _db.EmployeeLoans
            .Include(l => l.Installments)
            .FirstOrDefaultAsync(l => l.LoanId == loanId);

        if (loan is null)             return (false, "السلفة غير موجودة");
        if (loan.Status != "Active") return (false, "لا يمكن إلغاء سلفة غير نشطة");

        var hasDeducted = loan.Installments.Any(i => i.Status == "Deducted");
        if (hasDeducted)
            return (false, "لا يمكن إلغاء سلفة تم خصم أقساط منها");

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            if (loan.CashBoxId.HasValue && loan.LoanAmount > 0)
            {
                _db.CashboxTransactions.Add(new CashboxTransaction
                {
                    CashBoxId = loan.CashBoxId.Value,
                    TransactionType = "قبض",
                    ReferenceType = "LoanCancel",
                    ReferenceId = loan.LoanId,
                    Amount = loan.LoanAmount,
                    TransactionDate = DateTime.Now,
                    Notes = $"عكس صرف السلفة رقم {loan.LoanId} بعد الإلغاء",
                    CreatedBy = userName,
                    CreatedAt = DateTime.Now
                });
            }

            loan.Status = "Cancelled";
            loan.LastUpdatedAt = DateTime.Now;
            loan.RemainingAmount = 0;

            foreach (var inst in loan.Installments.Where(i => i.Status == "Pending"))
                inst.Status = "Skipped";

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            await _audit.LogAsync("EmployeeLoans", "Cancel", loanId.ToString(), null,
                new { loanId, ReverseCashMovement = loan.CashBoxId.HasValue, loan.LoanAmount }, userName);

            return (true, "تم إلغاء السلفة وعكس الأثر المالي بنجاح");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return (false, $"حدث خطأ أثناء إلغاء السلفة: {ex.Message}");
        }
    }

    // ============================================================
    // أقساط شهر معين (كل الموظفين)
    // ============================================================
    public async Task<List<InstallmentListDto>> GetMonthInstallmentsAsync(string month)
    {
        return await (
            from i in _db.LoanInstallments.AsNoTracking()
            join e in _db.Employees.AsNoTracking() on i.EmployeeId equals e.EmployeeId
            where i.DeductionMonth == month && i.Status == "Pending"
            orderby e.FullName
            select new InstallmentListDto
            {
                InstallmentId     = i.InstallmentId,
                LoanId            = i.LoanId,
                EmployeeId        = i.EmployeeId,
                EmployeeName      = e.FullName,
                Department        = e.Department,
                InstallmentNumber = i.InstallmentNumber,
                DeductionMonth    = i.DeductionMonth,
                Amount            = i.Amount,
                Status            = i.Status
            }).ToListAsync();
    }

    // ============================================================
    // أقساط موظف في شهر (للراتب)
    // ============================================================
    public async Task<List<InstallmentListDto>> GetEmployeeInstallmentsForMonth(
        int employeeId, string month)
    {
        return await _db.LoanInstallments
            .AsNoTracking()
            .Where(i => i.EmployeeId == employeeId
                     && i.DeductionMonth == month
                     && i.Status == "Pending")
            .OrderBy(i => i.LoanId)
            .ThenBy(i => i.InstallmentNumber)
            .Select(i => new InstallmentListDto
            {
                InstallmentId     = i.InstallmentId,
                LoanId            = i.LoanId,
                EmployeeId        = i.EmployeeId,
                InstallmentNumber = i.InstallmentNumber,
                DeductionMonth    = i.DeductionMonth,
                Amount            = i.Amount,
                Status            = i.Status
            })
            .ToListAsync();
    }

    // ============================================================
    // خصم قسط
    // ============================================================
    public async Task<(bool Success, string Message)> DeductInstallmentAsync(
        int installmentId, int payrollDetailId, string userName)
    {
        var inst = await _db.LoanInstallments
            .Include(i => i.Loan)
            .FirstOrDefaultAsync(i => i.InstallmentId == installmentId);

        if (inst is null)             return (false, "القسط غير موجود");
        if (inst.Status != "Pending") return (false, "القسط تم خصمه مسبقاً");

        inst.Status          = "Deducted";
        inst.PayrollDetailId = payrollDetailId;
        inst.DeductionDate   = DateTime.Now;

        var loan = inst.Loan;
        loan.PaidInstallments++;
        loan.RemainingAmount -= inst.Amount;
        loan.LastUpdatedAt    = DateTime.Now;

        if (loan.RemainingAmount <= 0 || loan.PaidInstallments >= loan.TotalInstallments)
        {
            loan.Status          = "Completed";
            loan.RemainingAmount = 0;
        }

        await _db.SaveChangesAsync();
        return (true, "تم تسجيل الخصم");
    }

    // ============================================================
    // تأجيل قسط للشهر التالي
    // ============================================================
    public async Task<(bool Success, string Message)> SkipInstallmentAsync(
        int installmentId, string reason, string userName)
    {
        var inst = await _db.LoanInstallments
            .Include(i => i.Loan)
            .FirstOrDefaultAsync(i => i.InstallmentId == installmentId);

        if (inst is null)             return (false, "القسط غير موجود");
        if (inst.Status != "Pending") return (false, "لا يمكن تأجيل هذا القسط");

        inst.Status = "Skipped";
        inst.Notes  = reason;

        var lastInst = await _db.LoanInstallments
            .Where(i => i.LoanId == inst.LoanId)
            .OrderByDescending(i => i.InstallmentNumber)
            .FirstOrDefaultAsync();

        if (lastInst != null &&
            DateTime.TryParseExact(lastInst.DeductionMonth + "-01", "yyyy-MM-dd",
                null, System.Globalization.DateTimeStyles.None, out var lastDate))
        {
            _db.LoanInstallments.Add(new LoanInstallment
            {
                LoanId            = inst.LoanId,
                EmployeeId        = inst.EmployeeId,
                InstallmentNumber = lastInst.InstallmentNumber + 1,
                DeductionMonth    = lastDate.AddMonths(1).ToString("yyyy-MM"),
                Amount            = inst.Amount,
                Status            = "Pending",
                Notes             = $"مُرحَّل من {inst.DeductionMonth} - {reason}",
                CreatedBy         = userName,
                CreatedAt         = DateTime.Now
            });

            inst.Loan.TotalInstallments++;
            inst.Loan.LastUpdatedAt = DateTime.Now;
        }

        await _db.SaveChangesAsync();
        return (true, "تم تأجيل القسط للشهر القادم");
    }

    public async Task<(bool Success, string Message)> SplitInstallmentAsync(
        int installmentId, decimal amountToKeepThisMonth, string reason, string userName)
    {
        var inst = await _db.LoanInstallments
            .Include(i => i.Loan)
            .FirstOrDefaultAsync(i => i.InstallmentId == installmentId);

        if (inst is null)
            return (false, "القسط غير موجود");

        if (inst.Status != "Pending")
            return (false, "يمكن تعديل أو تجزئة القسط فقط وهو في حالة لم يُخصم بعد");

        if (amountToKeepThisMonth <= 0)
            return (false, "قيمة الخصم في هذا الشهر يجب أن تكون أكبر من صفر");

        if (amountToKeepThisMonth >= inst.Amount)
            return (false, "استخدم القسط الحالي كما هو أو التأجيل الكامل؛ التجزئة تتطلب مبلغًا أقل من قيمة القسط");

        if (!DateTime.TryParseExact(inst.DeductionMonth + "-01", "yyyy-MM-dd",
            null, System.Globalization.DateTimeStyles.None, out var currentMonthDate))
            return (false, "شهر القسط الحالي غير صحيح");

        var remainingAmount = inst.Amount - amountToKeepThisMonth;
        var originalAmount = inst.Amount;

        var lastInstallment = await _db.LoanInstallments
            .Where(i => i.LoanId == inst.LoanId)
            .OrderByDescending(i => i.InstallmentNumber)
            .FirstOrDefaultAsync();

        var nextInstallmentNumber = (lastInstallment?.InstallmentNumber ?? inst.InstallmentNumber) + 1;
        var nextMonth = currentMonthDate.AddMonths(1).ToString("yyyy-MM");

        inst.Amount = amountToKeepThisMonth;
        inst.Notes = string.Join(" | ", new[]
        {
            inst.Notes,
            $"تعديل جزئي: أصل القسط {originalAmount:N2} جـ، المستحق هذا الشهر {amountToKeepThisMonth:N2} جـ، المتبقي {remainingAmount:N2} جـ مرحّل إلى {nextMonth}",
            string.IsNullOrWhiteSpace(reason) ? null : $"السبب: {reason}",
            $"بواسطة: {userName} - {DateTime.Now:yyyy-MM-dd HH:mm}"
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        _db.LoanInstallments.Add(new LoanInstallment
        {
            LoanId = inst.LoanId,
            EmployeeId = inst.EmployeeId,
            InstallmentNumber = nextInstallmentNumber,
            DeductionMonth = nextMonth,
            Amount = remainingAmount,
            Status = "Pending",
            Notes = $"متبقي مرحّل من قسط شهر {inst.DeductionMonth} بقيمة {remainingAmount:N2} جـ" +
                    (!string.IsNullOrWhiteSpace(reason) ? $" - {reason}" : string.Empty),
            CreatedBy = userName,
            CreatedAt = DateTime.Now
        });

        inst.Loan.TotalInstallments++;
        inst.Loan.LastUpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync();
        return (true, $"تم تجزئة القسط: {amountToKeepThisMonth:N2} جـ لهذا الشهر، وترحيل {remainingAmount:N2} جـ للشهر القادم");
    }

    public async Task<EmployeeLoanStatementDto?> GetEmployeeStatementAsync(int employeeId)
    {
        var employee = await _db.Employees.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId)
            .Select(e => new { e.EmployeeId, e.FullName, e.Department, e.JobTitle })
            .FirstOrDefaultAsync();

        if (employee is null)
            return null;

        var loansResult = await GetLoansAsync(new LoanFilterDto
        {
            EmployeeId = employeeId,
            PageNumber = 1,
            PageSize = 200
        });

        var installments = await _db.LoanInstallments.AsNoTracking()
            .Where(i => i.EmployeeId == employeeId)
            .OrderByDescending(i => i.DeductionMonth)
            .ThenBy(i => i.InstallmentNumber)
            .Select(i => new InstallmentListDto
            {
                InstallmentId = i.InstallmentId,
                LoanId = i.LoanId,
                EmployeeId = i.EmployeeId,
                EmployeeName = employee.FullName,
                Department = employee.Department,
                InstallmentNumber = i.InstallmentNumber,
                DeductionMonth = i.DeductionMonth,
                Amount = i.Amount,
                Status = i.Status,
                DeductionDate = i.DeductionDate,
                Notes = i.Notes,
                PayrollMonth = _db.Payrolls.Where(p => p.PayrollId == i.PayrollId).Select(p => p.PayrollMonth).FirstOrDefault()
            })
            .ToListAsync();

        var loans = loansResult.Items.OrderByDescending(x => x.LoanDate).ToList();
        var currentMonth = DateTime.Today.ToString("yyyy-MM");

        return new EmployeeLoanStatementDto
        {
            EmployeeId = employee.EmployeeId,
            EmployeeName = employee.FullName,
            Department = employee.Department,
            JobTitle = employee.JobTitle,
            ActiveLoansCount = loans.Count(x => x.Status == "Active"),
            TotalLoanAmount = loans.Sum(x => x.LoanAmount),
            TotalPaidAmount = loans.Sum(x => x.PaidAmount),
            TotalRemainingAmount = loans.Sum(x => x.RemainingAmount),
            CurrentMonthDue = installments.Where(x => x.DeductionMonth == currentMonth && x.Status == "Pending").Sum(x => x.Amount),
            PendingInstallmentsCount = installments.Count(x => x.Status == "Pending"),
            Loans = loans,
            Installments = installments
        };
    }

    // ============================================================
    // إحصائيات
    // ============================================================
    public async Task<LoanStatsDto> GetStatsAsync()
    {
        var currentMonth = DateTime.Today.ToString("yyyy-MM");

        var activeLoans = await _db.EmployeeLoans
            .AsNoTracking()
            .Where(l => l.Status == "Active")
            .Select(l => new { l.RemainingAmount, l.EmployeeId })
            .ToListAsync();

        var thisMonthDeductions = await _db.LoanInstallments
            .AsNoTracking()
            .Where(i => i.DeductionMonth == currentMonth && i.Status == "Pending")
            .SumAsync(i => (decimal?)i.Amount) ?? 0;

        var completedThisMonth = await _db.EmployeeLoans
            .AsNoTracking()
            .Where(l => l.Status == "Completed"
                     && l.LastUpdatedAt.HasValue
                     && l.LastUpdatedAt.Value.Month == DateTime.Today.Month
                     && l.LastUpdatedAt.Value.Year  == DateTime.Today.Year)
            .CountAsync();

        var totalLoanAmount = await _db.EmployeeLoans.AsNoTracking()
            .Where(l => l.Status == "Active")
            .SumAsync(l => (decimal?)l.LoanAmount) ?? 0;

        var avgLoanAmount = activeLoans.Any()
            ? Math.Round(totalLoanAmount / activeLoans.Count, 2)
            : 0;

        var pendingInstallmentsCount = await _db.LoanInstallments.AsNoTracking()
            .CountAsync(i => i.Status == "Pending");

        return new LoanStatsDto
        {
            ActiveLoansCount        = activeLoans.Count,
            TotalRemainingAmount    = activeLoans.Sum(l => l.RemainingAmount),
            ThisMonthDeductions     = thisMonthDeductions,
            CompletedThisMonth      = completedThisMonth,
            EmployeesWithLoans      = activeLoans.Select(l => l.EmployeeId).Distinct().Count(),
            TotalLoanAmount         = totalLoanAmount,
            AverageLoanAmount       = avgLoanAmount,
            PendingInstallmentsCount = pendingInstallmentsCount
        };
    }

    // ============================================================
    // Lookups
    // ============================================================
    public async Task<List<EmployeeLookupDto>> GetEmployeesLookupAsync(string? search = null)
    {
        var query = _db.Employees.AsNoTracking()
            .Where(e => e.Status == EmployeeStatuses.Active || e.Status == "Active");

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(e =>
                e.FullName.Contains(s) ||
                (e.Department != null && e.Department.Contains(s)));
        }

        return await query
            .OrderBy(e => e.FullName)
            .Take(50)
            .Select(e => new EmployeeLookupDto
            {
                EmployeeId = e.EmployeeId,
                FullName   = e.FullName,
                Department = e.Department,
                JobTitle   = e.JobTitle
            })
            .ToListAsync();
    }

    public async Task<List<CashBoxLookupDto>> GetCashBoxesLookupAsync()
{
    var boxes = await _db.CashBoxes
        .AsNoTracking()
        .Where(c => c.IsActive == true)
        .OrderByDescending(c => c.IsDefault)
        .ThenBy(c => c.CashBoxName)
        .Select(c => new
        {
            c.CashBoxId,
            c.CashBoxName,
            c.OpeningBalance,  // decimal (non-nullable) - مش محتاج ??
            c.IsDefault        // bool (non-nullable) - مش محتاج ??
        })
        .ToListAsync();

    if (!boxes.Any()) return new();

    var boxIds = boxes.Select(b => b.CashBoxId).ToList();

    var txSummary = await _db.CashboxTransactions
        .AsNoTracking()
        .Where(t => boxIds.Contains(t.CashBoxId))
        .GroupBy(t => new { t.CashBoxId, t.TransactionType })
        .Select(g => new
        {
            g.Key.CashBoxId,
            g.Key.TransactionType,
            Total = g.Sum(x => (decimal?)x.Amount) ?? 0
        })
        .ToListAsync();

    var inflows  = txSummary
        .Where(x => x.TransactionType == "قبض")
        .ToDictionary(x => x.CashBoxId, x => x.Total);

    var outflows = txSummary
        .Where(x => x.TransactionType == "صرف")
        .ToDictionary(x => x.CashBoxId, x => x.Total);

    return boxes.Select(b =>
    {
        // ✅ OpeningBalance non-nullable → مش محتاج ??
        var balance = b.OpeningBalance
                    + inflows.GetValueOrDefault(b.CashBoxId, 0)
                    - outflows.GetValueOrDefault(b.CashBoxId, 0);

        return new CashBoxLookupDto
        {
            CashBoxId      = b.CashBoxId,
            CashBoxName    = b.CashBoxName,
            CurrentBalance = balance,
            IsDefault      = b.IsDefault  // ✅ bool non-nullable → مش محتاج ??
        };
    }).ToList();
}

    // ============================================================
    // Helper خاص - رصيد خزينة
    // ============================================================
    private async Task<decimal> GetCashBoxBalanceAsync(int cashBoxId)
{
    var box = await _db.CashBoxes
        .AsNoTracking()
        .Where(c => c.CashBoxId == cashBoxId)
        .Select(c => new { c.OpeningBalance })
        .FirstOrDefaultAsync();

    if (box is null) return 0;

    var txIn = await _db.CashboxTransactions
        .AsNoTracking()
        .Where(t => t.CashBoxId == cashBoxId && t.TransactionType == "قبض")
        .SumAsync(t => (decimal?)t.Amount) ?? 0;

    var txOut = await _db.CashboxTransactions
        .AsNoTracking()
        .Where(t => t.CashBoxId == cashBoxId && t.TransactionType == "صرف")
        .SumAsync(t => (decimal?)t.Amount) ?? 0;

    // ✅ OpeningBalance non-nullable → مش محتاج ??
    return box.OpeningBalance + txIn - txOut;
}
}
