using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

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

        // فلاتر
        if (filter.EmployeeId.HasValue)
            query = query.Where(x => x.l.EmployeeId == filter.EmployeeId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(x => x.l.Status == filter.Status);

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();
            query = query.Where(x =>
                x.e.FullName.Contains(s) ||
                (x.e.Department != null && x.e.Department.Contains(s)));
        }

        var totalCount = await query.CountAsync();

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
                CashBoxName = x.l.CashBoxId == null ? null
                    : _db.CashBoxes.Where(c => c.CashBoxId == x.l.CashBoxId)
                                   .Select(c => c.CashBoxName).FirstOrDefault()
            })
            .ToListAsync();

        var items = rawData.Select(x =>
        {
            // حساب آخر شهر خصم متوقع
            string? expectedEnd = null;
            if (DateTime.TryParseExact(x.StartDeductionMonth + "-01", "yyyy-MM-dd",
                null, System.Globalization.DateTimeStyles.None, out var startDate))
            {
                var remaining = x.TotalInstallments - x.PaidInstallments;
                if (remaining > 0)
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
                CashBoxName         = x.CashBoxName,
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
            PageNumber  = filter.PageNumber,
            PageSize    = filter.PageSize
        };
    }

    // ============================================================
    // تفاصيل سلفة واحدة مع أقساطها
    // ============================================================
    public async Task<LoanDetailDto?> GetLoanDetailAsync(int loanId)
    {
        var loan = await (
            from l in _db.EmployeeLoans.AsNoTracking()
            join e in _db.Employees.AsNoTracking() on l.EmployeeId equals e.EmployeeId
            where l.LoanId == loanId
            select new LoanListDto
            {
                LoanId             = l.LoanId,
                EmployeeId         = l.EmployeeId,
                EmployeeName       = e.FullName,
                Department         = e.Department,
                JobTitle           = e.JobTitle,
                LoanAmount         = l.LoanAmount,
                MonthlyInstallment = l.MonthlyInstallment,
                TotalInstallments  = l.TotalInstallments,
                PaidInstallments   = l.PaidInstallments,
                RemainingAmount    = l.RemainingAmount,
                LoanDate           = l.LoanDate,
                StartDeductionMonth= l.StartDeductionMonth,
                Status             = l.Status,
                Notes              = l.Notes,
                ApprovedBy         = l.ApprovedBy,
                CreatedBy          = l.CreatedBy,
                CreatedAt          = l.CreatedAt
            }).FirstOrDefaultAsync();

        if (loan is null) return null;

        var installments = await _db.LoanInstallments
            .AsNoTracking()
            .Where(i => i.LoanId == loanId)
            .OrderBy(i => i.InstallmentNumber)
            .Select(i => new InstallmentListDto
            {
                InstallmentId     = i.InstallmentId,
                LoanId            = i.LoanId,
                InstallmentNumber = i.InstallmentNumber,
                DeductionMonth    = i.DeductionMonth,
                Amount            = i.Amount,
                Status            = i.Status,
                DeductionDate     = i.DeductionDate,
                Notes             = i.Notes,
                PayrollMonth      = i.PayrollId == null ? null
                    : _db.Payrolls.Where(p => p.PayrollId == i.PayrollId)
                                  .Select(p => p.PayrollMonth).FirstOrDefault()
            })
            .ToListAsync();

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
    // ⭐ إضافة سلفة جديدة - الـ Method الأهم
    // ============================================================
    public async Task<(bool Success, string Message, int? LoanId)> SaveLoanAsync(
        LoanFormDto dto, string userName)
    {
        // ── Validation ──────────────────────────────────────
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

        // ── تحقق من الموظف ──────────────────────────────────
        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EmployeeId == dto.EmployeeId);
        if (employee is null)
            return (false, "الموظف غير موجود", null);

        // ── تحقق من الخزينة ─────────────────────────────────
        var cashBox = await _db.CashBoxes.FirstOrDefaultAsync(c => c.CashBoxId == dto.CashBoxId);
        if (cashBox is null)
            return (false, "الخزينة غير موجودة", null);

        // ── حساب القسط الشهري ────────────────────────────────
        dto.MonthlyInstallment = Math.Round(dto.LoanAmount / dto.TotalInstallments, 2);

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // ── 1. إنشاء السلفة ─────────────────────────────
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
            await _db.SaveChangesAsync(); // عشان ناخد الـ LoanId

            // ── 2. إنشاء الأقساط تلقائياً ───────────────────
            if (!DateTime.TryParseExact(dto.StartDeductionMonth + "-01", "yyyy-MM-dd",
                null, System.Globalization.DateTimeStyles.None, out var startDate))
                throw new Exception("صيغة شهر البداية غير صحيحة");

            var installments = new List<LoanInstallment>();
            decimal cumulativeAmount = 0;

            for (int i = 0; i < dto.TotalInstallments; i++)
            {
                var month  = startDate.AddMonths(i).ToString("yyyy-MM");
                decimal amt;

                // آخر قسط = الباقي (لتفادي فروق التقريب)
                if (i == dto.TotalInstallments - 1)
                    amt = dto.LoanAmount - cumulativeAmount;
                else
                    amt = dto.MonthlyInstallment;

                cumulativeAmount += amt;

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

            // ── 3. حركة الخزينة (صرف من الخزينة) ───────────
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

            // ربط الحركة بالسلفة
            loan.CashboxTransactionId = cashTx.CashboxTransactionId;
            await _db.SaveChangesAsync();

            await _audit.LogAsync("EmployeeLoans", "Insert",
                loan.LoanId.ToString(), null, loan, userName);

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

        if (loan is null)          return (false, "السلفة غير موجودة");
        if (loan.Status != "Active") return (false, "لا يمكن إلغاء سلفة غير نشطة");

        var hasDeducted = loan.Installments.Any(i => i.Status == "Deducted");
        if (hasDeducted)
            return (false, "لا يمكن إلغاء سلفة تم خصم أقساط منها. استخدم الإلغاء الجزئي.");

        loan.Status        = "Cancelled";
        loan.LastUpdatedAt = DateTime.Now;

        // إلغاء الأقساط المتبقية
        foreach (var inst in loan.Installments.Where(i => i.Status == "Pending"))
            inst.Status = "Skipped";

        await _db.SaveChangesAsync();
        await _audit.LogAsync("EmployeeLoans", "Cancel", loanId.ToString(), null, loan, userName);

        return (true, "تم إلغاء السلفة");
    }

    // ============================================================
    // الأقساط المستحقة في شهر معين
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
                InstallmentNumber = i.InstallmentNumber,
                DeductionMonth    = i.DeductionMonth,
                Amount            = i.Amount,
                Status            = i.Status
            })
            .ToListAsync();
    }

    // ============================================================
    // تسجيل خصم قسط (يُستدعى من شاشة الراتب)
    // ============================================================
    public async Task<(bool Success, string Message)> DeductInstallmentAsync(
        int installmentId, int payrollDetailId, string userName)
    {
        var inst = await _db.LoanInstallments
            .Include(i => i.Loan)
            .FirstOrDefaultAsync(i => i.InstallmentId == installmentId);

        if (inst is null)               return (false, "القسط غير موجود");
        if (inst.Status != "Pending")   return (false, "القسط مش في حالة انتظار");

        inst.Status         = "Deducted";
        inst.PayrollDetailId= payrollDetailId;
        inst.DeductionDate  = DateTime.Now;

        // تحديث السلفة
        var loan = inst.Loan;
        loan.PaidInstallments++;
        loan.RemainingAmount -= inst.Amount;
        loan.LastUpdatedAt   = DateTime.Now;

        // لو كلمنا خلصت → أغلق السلفة
        if (loan.RemainingAmount <= 0 || loan.PaidInstallments >= loan.TotalInstallments)
        {
            loan.Status         = "Completed";
            loan.RemainingAmount = 0;
        }

        await _db.SaveChangesAsync();
        return (true, "تم تسجيل الخصم");
    }

    // ============================================================
    // تأجيل قسط
    // ============================================================
    public async Task<(bool Success, string Message)> SkipInstallmentAsync(
        int installmentId, string reason, string userName)
    {
        var inst = await _db.LoanInstallments
            .Include(i => i.Loan)
            .FirstOrDefaultAsync(i => i.InstallmentId == installmentId);

        if (inst is null)             return (false, "القسط غير موجود");
        if (inst.Status != "Pending") return (false, "القسط مش في حالة انتظار");

        inst.Status = "Skipped";
        inst.Notes  = reason;

        // أضف قسط جديد في الشهر التالي
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
                Notes             = $"مُرحَّل من {inst.DeductionMonth}",
                CreatedBy         = userName,
                CreatedAt         = DateTime.Now
            });

            // تحديث السلفة
            inst.Loan.TotalInstallments++;
            inst.Loan.LastUpdatedAt = DateTime.Now;
        }

        await _db.SaveChangesAsync();
        return (true, "تم تأجيل القسط للشهر القادم");
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
            .ToListAsync();

        var thisMonthDeductions = await _db.LoanInstallments
            .AsNoTracking()
            .Where(i => i.DeductionMonth == currentMonth && i.Status == "Pending")
            .SumAsync(i => (decimal?)i.Amount) ?? 0;

        var completedThisMonth = await _db.EmployeeLoans
            .AsNoTracking()
            .Where(l => l.Status == "Completed" &&
                        l.LastUpdatedAt.HasValue &&
                        l.LastUpdatedAt.Value.Month == DateTime.Today.Month &&
                        l.LastUpdatedAt.Value.Year  == DateTime.Today.Year)
            .CountAsync();

        return new LoanStatsDto
        {
            ActiveLoansCount     = activeLoans.Count,
            TotalRemainingAmount = activeLoans.Sum(l => l.RemainingAmount),
            ThisMonthDeductions  = thisMonthDeductions,
            CompletedThisMonth   = completedThisMonth,
            EmployeesWithLoans   = activeLoans.Select(l => l.EmployeeId).Distinct().Count()
        };
    }
    // ============================================================
// Lookups
// ============================================================
public async Task<List<EmployeeLookupDto>> GetEmployeesLookupAsync(string? search = null)
{
    var query = _db.Employees
        .AsNoTracking()
        .Where(e => e.Status == "نشط" || e.Status == "Active");

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
    return await _db.CashBoxes
        .AsNoTracking()
        .Where(c => c.IsActive == true)
        .OrderByDescending(c => c.IsDefault)
        .ThenBy(c => c.CashBoxName)
        .Select(c => new CashBoxLookupDto
        {
            CashBoxId      = c.CashBoxId,
            CashBoxName    = c.CashBoxName,
            CurrentBalance = c.CurrentBalance ?? 0,
            IsDefault      = c.IsDefault ?? false
        })
        .ToListAsync();
}
}
