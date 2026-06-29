// ============================================================
// ✅ PayrollService.cs - النسخة المُصلَحة الكاملة
// بناءً على الـ SQL Script الفعلي للـ Database
// ============================================================
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;

namespace COCOBOLOERPNEW.Services;

public class PayrollService : IPayrollService
{
    private readonly db24804Context _db;
    private readonly IAuditService  _audit;

    public PayrollService(db24804Context db, IAuditService audit)
    {
        _db    = db;
        _audit = audit;
    }

    // ============================================================
    // قائمة المرتبات
    // ============================================================
    public async Task<PagedResult<PayrollListDto>> GetPayrollsAsync(PayrollFilterDto filter)
    {
        var q = from p in _db.Payrolls.AsNoTracking()
                join e in _db.Employees.AsNoTracking()
                    on p.EmployeeId equals e.EmployeeId
                select new { p, e };

        if (!string.IsNullOrWhiteSpace(filter.PayrollMonth))
            q = q.Where(x => x.p.PayrollMonth == filter.PayrollMonth);
        if (filter.EmployeeID.HasValue)
            q = q.Where(x => x.p.EmployeeId == filter.EmployeeID.Value);
        if (!string.IsNullOrWhiteSpace(filter.Department))
            q = q.Where(x => x.e.Department == filter.Department);
        if (!string.IsNullOrWhiteSpace(filter.PaymentStatus))
            q = q.Where(x => x.p.PaymentStatus == filter.PaymentStatus);
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
            q = q.Where(x => x.e.FullName.Contains(filter.SearchText.Trim()));

        var total = await q.CountAsync();
        var items = await q
            .OrderByDescending(x => x.p.CreatedAt)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(x => new PayrollListDto
            {
                PayrollID          = x.p.PayrollId,
                EmployeeID         = x.p.EmployeeId,
                EmployeeName       = x.e.FullName,
                Department         = x.e.Department,
                JobTitle           = x.e.JobTitle,
                PayrollMonth       = x.p.PayrollMonth,
                BasicSalary        = x.p.BasicSalary,
                BonusInPayroll     = x.p.BonusInPayroll    ?? 0,
                AbsenceDeduction   = x.p.AbsenceDeduction  ?? 0,
                LateDeduction      = x.p.LateDeduction     ?? 0,
                LoanDeduction      = x.p.LoanDeduction     ?? 0,
                PenaltyDeduction   = x.p.PenaltyDeduction  ?? 0,
                // ✅ NetSalary = Computed Column في DB
                NetSalary          = x.p.NetSalary         ?? x.p.BasicSalary,
                WorkingDaysInMonth = x.p.WorkingDaysInMonth ?? 0,
                PresentDays        = x.p.PresentDays        ?? 0,
                AbsenceDays        = x.p.AbsenceDays        ?? 0,
                LateMinutesTotal   = x.p.LateMinutesTotal   ?? 0,
                IsManualAttendance = x.p.IsManualAttendance ?? false,
                PaymentStatus      = x.p.PaymentStatus      ?? "غير مدفوع",
                PaymentDate        = x.p.PaymentDate,
                CreatedBy          = x.p.CreatedBy,
                CreatedAt          = x.p.CreatedAt
            })
            .ToListAsync();

        return new PagedResult<PayrollListDto>
        {
            Items = items, TotalCount = total,
            PageNumber = filter.PageNumber, PageSize = filter.PageSize
        };
    }

    // ============================================================
    // قسيمة الراتب
    // ============================================================
    public async Task<PayslipDto?> GetPayslipAsync(int payrollId)
    {
        var rec = await (
            from p  in _db.Payrolls.AsNoTracking()
            join e  in _db.Employees.AsNoTracking() on p.EmployeeId equals e.EmployeeId
            join ct in _db.CashboxTransactions.AsNoTracking()
                on p.CashboxTransactionId equals ct.CashboxTransactionId into cts
            from ct in cts.DefaultIfEmpty()
            join cb in _db.CashBoxes.AsNoTracking()
                on ct.CashBoxId equals cb.CashBoxId into cbs
            from cb in cbs.DefaultIfEmpty()
            where p.PayrollId == payrollId
            select new { p, e, BoxName = cb != null ? cb.CashBoxName : null }
        ).FirstOrDefaultAsync();

        if (rec is null) return null;

        var details = await _db.PayrollDetails.AsNoTracking()
            .Where(d => d.PayrollID == payrollId)
            .Select(d => new PayrollDetailDto
            {
                DetailID    = d.PayrollDetailID,
                DetailType  = d.DetailType,
                Description = d.DetailDescription ?? d.DetailType,
                Amount      = d.Amount,
                IsDeduction = d.IsDeduction,
                PaymentType = d.PaymentType ?? "InPayroll"
            })
            .ToListAsync();

        var loans = await (
            from li in _db.LoanInstallments.AsNoTracking()
            join l  in _db.EmployeeLoans.AsNoTracking() on li.LoanId equals l.LoanId
            where li.PayrollId == payrollId
            select new PayrollDetailDto
            {
                DetailID    = li.InstallmentId,
                DetailType  = "LoanDeduction",
                Description = $"قسط سلفة #{li.InstallmentNumber}/{l.TotalInstallments}",
                Amount      = li.Amount,
                IsDeduction = true,
                PaymentType = "InPayroll"
            }
        ).ToListAsync();

        return new PayslipDto
        {
            EmployeeID         = rec.p.EmployeeId,
            EmployeeName       = rec.e.FullName,
            Department         = rec.e.Department,
            JobTitle           = rec.e.JobTitle,
            NationalId         = rec.e.NationalId,
            HireDate           = rec.e.HireDate,
            PayrollMonth       = rec.p.PayrollMonth,
            BasicSalary        = rec.p.BasicSalary,
            BonusItems         = details.Where(d => !d.IsDeduction && d.PaymentType == "InPayroll").ToList(),
            WorkingDaysInMonth = rec.p.WorkingDaysInMonth ?? 0,
            PresentDays        = rec.p.PresentDays        ?? 0,
            AbsenceDays        = rec.p.AbsenceDays        ?? 0,
            LateMinutesTotal   = rec.p.LateMinutesTotal   ?? 0,
            IsManualAttendance = rec.p.IsManualAttendance ?? false,
            AbsenceDeduction   = rec.p.AbsenceDeduction   ?? 0,
            LateDeduction      = rec.p.LateDeduction      ?? 0,
            LoanDeduction      = rec.p.LoanDeduction      ?? 0,
            Penalties          = details.Where(d => d.IsDeduction && d.DetailType == "Penalty").ToList(),
            LoanItems          = loans,
            SeparateBonuses    = details.Where(d => d.PaymentType == "Separate").ToList(),
            NetSalary          = rec.p.NetSalary    ?? rec.p.BasicSalary,
            PaymentStatus      = rec.p.PaymentStatus ?? "غير مدفوع",
            PaymentDate        = rec.p.PaymentDate,
            CashBoxName        = rec.BoxName,
            Notes              = rec.p.Notes
        };
    }

    // ============================================================
    // ⭐ حساب راتب موظف واحد - مُصلَح
    // ============================================================
    public async Task<PayrollCalculationDto> CalculateOneAsync(int employeeId, string month)
    {
        var emp = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EmployeeId == employeeId)
            ?? throw new Exception($"الموظف {employeeId} غير موجود");

        var basic    = emp.CurrentSalaryBase ?? 0;
        var att = await GetAttendanceAsync(employeeId, month);
var workDays = att.WorkDays > 0 ? att.WorkDays : await GetWorkingDaysAsync(employeeId, month);
var dayRate  = workDays > 0 ? basic / workDays : 0;
var minRate  = workDays > 0 ? basic / workDays / 8m / 60m : 0;
var absDays  = Math.Max(0, workDays - att.PresentDays);
var absDed   = Math.Round(dayRate * absDays, 2);
var lateDed  = Math.Round(minRate * att.LateMinutes, 2);

        // ✅ أقساط السلف المستحقة في الشهر
        var loanItems = await (
            from li in _db.LoanInstallments.AsNoTracking()
            join l  in _db.EmployeeLoans.AsNoTracking() on li.LoanId equals l.LoanId
            where li.EmployeeId     == employeeId
               && li.DeductionMonth  == month
               && li.Status          == "Pending"
            select new PayrollDetailDto
            {
                DetailID    = li.InstallmentId,
                DetailType  = "LoanDeduction",
                Description = $"قسط سلفة #{li.InstallmentNumber}/{l.TotalInstallments}",
                Amount      = li.Amount,
                IsDeduction = true,
                PaymentType = "InPayroll"
            }
        ).ToListAsync();

        var existing = await _db.Payrolls.AsNoTracking()
            .Where(p => p.EmployeeId == employeeId && p.PayrollMonth == month)
            .Select(p => new { p.PayrollId, p.PaymentStatus })
            .FirstOrDefaultAsync();

        return new PayrollCalculationDto
        {
            EmployeeID         = employeeId,
            EmployeeName       = emp.FullName,
            Department         = emp.Department,
            BasicSalary        = basic,
            WorkingDaysInMonth = workDays,
            PresentDays        = att.PresentDays,
            AbsenceDays        = absDays,
            LateMinutesTotal   = att.LateMinutes,
            IsManualAttendance = att.IsManual,
            AbsenceDeduction   = absDed,
            LateDeduction      = lateDed,
            LoanDeduction      = loanItems.Sum(i => i.Amount),
            LoanItems          = loanItems,
            Penalties          = new(),
            BonusItems         = new(),
            SeparateBonuses    = new(),
            HasExistingPayroll = existing != null,
            ExistingPayrollID  = existing?.PayrollId,
            Warning            = existing != null
                ? $"⚠️ راتب مسجل ({existing.PaymentStatus})" : null
        };
    }

    // ============================================================
    // حساب شهر كامل
    // ============================================================
    public async Task<List<PayrollCalculationDto>> CalculateMonthAsync(string month)
    {
        var ids = await _db.Employees.AsNoTracking()
            .Where(e => e.Status == "نشط" || e.Status == "Active")
            .OrderBy(e => e.Department).ThenBy(e => e.FullName)
            .Select(e => e.EmployeeId)
            .ToListAsync();

        var results = new List<PayrollCalculationDto>();
        foreach (var id in ids)
        {
            try
            {
                results.Add(await CalculateOneAsync(id, month));
            }
            catch (Exception ex)
            {
                results.Add(new PayrollCalculationDto
                {
                    EmployeeID = id,
                    Warning    = $"خطأ في الحساب: {ex.Message}"
                });
            }
        }
        return results;
    }

    // ============================================================
    // ⭐ حفظ بدون صرف
    // ============================================================
    public async Task<(bool Ok, string Msg, int? RunId)> SaveOnlyAsync(
        string month,
        List<PayrollCalculationDto> data,
        string user)
    {
        var selected = data.Where(d => d.IsSelected && !d.HasExistingPayroll).ToList();
        if (!selected.Any())
            return (false, "لم يتم اختيار أي موظف", null);

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // 1. جلسة الصرف
            var run = new PayrollRun
            {
                PayrollMonth = month,
                Status       = "Draft",
                CreatedBy    = user,
                CreatedAt    = DateTime.Now
            };
            _db.PayrollRuns.Add(run);
            await _db.SaveChangesAsync();

            decimal sumGross = 0, sumDed = 0, sumNet = 0;
            int count = 0;

            foreach (var calc in selected)
            {
                if (calc.BasicSalary == 0) continue;

                var bonusIn  = calc.TotalBonusInPayroll;
                var totalDed = calc.TotalDeductions;
                var netVal   = calc.BasicSalary + bonusIn - totalDed;

                // 2. رأس الراتب
                var payroll = new Payroll
                {
                    EmployeeId         = calc.EmployeeID,
                    PayrollMonth       = month,
                    BasicSalary        = calc.BasicSalary,
                    BonusInPayroll     = bonusIn,
                    Allowances         = bonusIn,
                    Deductions         = totalDed,
                    AbsenceDeduction   = calc.AbsenceDeduction,
                    LateDeduction      = calc.LateDeduction,
                    LoanDeduction      = calc.LoanDeduction,
                    PenaltyDeduction   = calc.TotalPenalties,
                    WorkingDaysInMonth = calc.WorkingDaysInMonth,
                    PresentDays        = calc.PresentDays,
                    AbsenceDays        = calc.AbsenceDays,
                    LateMinutesTotal   = calc.LateMinutesTotal,
                    IsManualAttendance = calc.IsManualAttendance,
                    PaymentStatus      = "غير مدفوع",
                    PayrollRunId       = run.RunId,
                    CreatedBy          = user,
                    CreatedAt          = DateTime.Now
                };
                _db.Payrolls.Add(payroll);
                await _db.SaveChangesAsync();

                // 3. البنود التفصيلية
                var details = new List<PayrollDetail>();

                if (calc.AbsenceDeduction > 0)
                    details.Add(MakeDetail(payroll.PayrollId, "AbsenceDeduction",
                        $"خصم غياب {calc.AbsenceDays} يوم",
                        calc.AbsenceDeduction, true, user));

                if (calc.LateDeduction > 0)
                    details.Add(MakeDetail(payroll.PayrollId, "LateDeduction",
                        $"خصم تأخير {calc.LateMinutesTotal} دقيقة",
                        calc.LateDeduction, true, user));

                foreach (var pen in calc.Penalties)
                    details.Add(MakeDetail(payroll.PayrollId, "Penalty",
                        pen.Description, pen.Amount, true, user));

                foreach (var bon in calc.BonusItems.Where(b => b.PaymentType == "InPayroll"))
                    details.Add(MakeDetail(payroll.PayrollId, bon.DetailType,
                        bon.Description, bon.Amount, false, user, paymentType: "InPayroll"));

                if (details.Any())
                {
                    _db.PayrollDetails.AddRange(details);
                    await _db.SaveChangesAsync();
                }

                sumGross += calc.GrossSalary;
                sumDed   += totalDed;
                sumNet   += netVal;
                count++;
            }

            // 4. تحديث جلسة الصرف
            run.Status          = "Draft";
            run.TotalEmployees  = count;
            run.TotalGross      = sumGross;
            run.TotalDeductions = sumDed;
            run.TotalNet        = sumNet;
            await _db.SaveChangesAsync();

            await tx.CommitAsync();
            return (true, $"✅ تم حفظ بيانات {count} موظف بدون صرف", run.RunId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            var inner = ex.InnerException?.Message ?? "";
            return (false, $"خطأ: {ex.Message} | {inner}", null);
        }
    }

    // ============================================================
    // ⭐ معالجة وصرف المرتبات
    // ============================================================
    public async Task<(bool Ok, string Msg, int? RunId)> ProcessMonthAsync(
    string month,
    List<PayrollCalculationDto> data,
    int cashBoxId,
    string user)
{
    var selected = data.Where(d => d.IsSelected && !d.HasExistingPayroll).ToList();
    if (!selected.Any())
        return (false, "لم يتم اختيار أي موظف", null);

    var totalNet = selected.Sum(d => d.NetSalary);
    var balance  = await GetCashBalanceAsync(cashBoxId);

    if (balance < totalNet)
        return (false,
            $"رصيد الخزينة ({balance:N2} جـ) أقل من إجمالي المرتبات ({totalNet:N2} جـ)",
            null);

    using var tx = await _db.Database.BeginTransactionAsync();
    try
    {
        // 1. جلسة الصرف
        var run = new PayrollRun
        {
            PayrollMonth = month,
            Status       = "Draft",
            CashBoxId    = cashBoxId,
            CreatedBy    = user,
            CreatedAt    = DateTime.Now
        };
        _db.PayrollRuns.Add(run);
        await _db.SaveChangesAsync();

        decimal sumGross = 0, sumDed = 0, sumNet = 0;
        int count = 0;

        foreach (var calc in selected)
        {
            if (calc.BasicSalary == 0) continue;

            var bonusIn  = calc.TotalBonusInPayroll;
            var totalDed = calc.TotalDeductions;

            // ✅ نحسب NetSalary من الكود (نفس الـ formula في DB)
            // NetSalary = BasicSalary + BonusInPayroll - Deductions
            var netVal = calc.BasicSalary + bonusIn - totalDed;

            // 2. رأس الراتب
            var payroll = new Payroll
            {
                EmployeeId         = calc.EmployeeID,
                PayrollMonth       = month,
                BasicSalary        = calc.BasicSalary,
                BonusInPayroll     = bonusIn,
                Allowances         = bonusIn,
                Deductions         = totalDed,
                AbsenceDeduction   = calc.AbsenceDeduction,
                LateDeduction      = calc.LateDeduction,
                LoanDeduction      = calc.LoanDeduction,
                PenaltyDeduction   = calc.TotalPenalties,
                WorkingDaysInMonth = calc.WorkingDaysInMonth,
                PresentDays        = calc.PresentDays,
                AbsenceDays        = calc.AbsenceDays,
                LateMinutesTotal   = calc.LateMinutesTotal,
                IsManualAttendance = calc.IsManualAttendance,
                PaymentStatus      = "غير مدفوع",
                PayrollRunId       = run.RunId,
                CreatedBy          = user,
                CreatedAt          = DateTime.Now
                // ✅ NetSalary لا تحدده - بتتحسب تلقائياً في DB
            };
            _db.Payrolls.Add(payroll);
            await _db.SaveChangesAsync();

            // 3. البنود التفصيلية
            var details = new List<PayrollDetail>();

            if (calc.AbsenceDeduction > 0)
                details.Add(MakeDetail(payroll.PayrollId, "AbsenceDeduction",
                    $"خصم غياب {calc.AbsenceDays} يوم",
                    calc.AbsenceDeduction, true, user));

            if (calc.LateDeduction > 0)
                details.Add(MakeDetail(payroll.PayrollId, "LateDeduction",
                    $"خصم تأخير {calc.LateMinutesTotal} دقيقة",
                    calc.LateDeduction, true, user));

            foreach (var pen in calc.Penalties)
                details.Add(MakeDetail(payroll.PayrollId, "Penalty",
                    pen.Description, pen.Amount, true, user));

            foreach (var bon in calc.BonusItems.Where(b => b.PaymentType == "InPayroll"))
                details.Add(MakeDetail(payroll.PayrollId, bon.DetailType,
                    bon.Description, bon.Amount, false, user, paymentType: "InPayroll"));

            if (details.Any())
            {
                _db.PayrollDetails.AddRange(details);
                await _db.SaveChangesAsync();
            }

            // 4. حركة الخزينة
            var cashTx = new CashboxTransaction
            {
                CashBoxId       = cashBoxId,
                TransactionType = "صرف",
                ReferenceType   = "Payroll",
                Amount          = netVal,
                TransactionDate = DateTime.Now,
                Notes           = $"راتب {calc.EmployeeName} - {month}",
                CreatedBy       = user,
                CreatedAt       = DateTime.Now
            };
            _db.CashboxTransactions.Add(cashTx);
            await _db.SaveChangesAsync();

            // تحديث الراتب بالحالة والخزينة
            payroll.CashboxTransactionId = cashTx.CashboxTransactionId;
            payroll.PaymentStatus         = "مدفوع";
            payroll.PaymentDate           = DateTime.Now;
            await _db.SaveChangesAsync();

            // 5. مكافآت منفصلة
            foreach (var sb in calc.SeparateBonuses)
            {
                var sepTx = new CashboxTransaction
                {
                    CashBoxId       = cashBoxId,
                    TransactionType = "صرف",
                    ReferenceType   = "BonusSeparate",
                    Amount          = sb.Amount,
                    TransactionDate = DateTime.Now,
                    Notes           = $"{sb.Description} - {calc.EmployeeName} - {month}",
                    CreatedBy       = user,
                    CreatedAt       = DateTime.Now
                };
                _db.CashboxTransactions.Add(sepTx);
                await _db.SaveChangesAsync();

                _db.PayrollDetails.Add(new PayrollDetail
                {
                    PayrollID            = payroll.PayrollId,
                    DetailType           = sb.DetailType,
                    DetailDescription    = sb.Description,
                    Amount               = sb.Amount,
                    IsDeduction          = false,
                    PaymentType          = "Separate",
                    CashboxTransactionID = sepTx.CashboxTransactionId,
                    CreatedBy            = user,
                    CreatedAt            = DateTime.Now
                });
                await _db.SaveChangesAsync();
            }

            // 6. ⭐ خصم أقساط السلف تلقائياً
            foreach (var loanItem in calc.LoanItems)
            {
                var inst = await _db.LoanInstallments
                    .Include(i => i.Loan)
                    .FirstOrDefaultAsync(i => i.InstallmentId == loanItem.DetailID);

                if (inst?.Status != "Pending") continue;

                var loanDetail = new PayrollDetail
                {
                    PayrollID         = payroll.PayrollId,
                    DetailType        = "LoanDeduction",
                    DetailDescription = loanItem.Description,
                    Amount            = loanItem.Amount,
                    IsDeduction       = true,
                    PaymentType       = "InPayroll",
                    CreatedBy         = user,
                    CreatedAt         = DateTime.Now
                };
                _db.PayrollDetails.Add(loanDetail);
                await _db.SaveChangesAsync();

                inst.Status          = "Deducted";
                inst.PayrollId       = payroll.PayrollId;
                inst.PayrollDetailId = loanDetail.PayrollDetailID;
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
            }

            sumGross += calc.GrossSalary;
            sumDed   += totalDed;
            sumNet   += netVal;
            count++;
        }

        // 7. تحديث جلسة الصرف
        run.Status          = "Completed";
        run.TotalEmployees  = count;
        run.TotalGross      = sumGross;
        run.TotalDeductions = sumDed;
        run.TotalNet        = sumNet;
        run.ProcessedBy     = user;
        run.ProcessedAt     = DateTime.Now;
        await _db.SaveChangesAsync();

        await tx.CommitAsync();
        return (true, $"✅ تم صرف مرتبات {count} موظف | الإجمالي: {sumNet:N2} جـ", run.RunId);
    }
    catch (Exception ex)
    {
        await tx.RollbackAsync();
        var inner = ex.InnerException?.Message ?? "";
        return (false, $"خطأ: {ex.Message} | {inner}", null);
    }
}

    // ============================================================
    // صرف راتب واحد
    // ============================================================
    public async Task<(bool Ok, string Msg)> PayOneAsync(int payrollId, int cashBoxId, string user)
    {
        var p = await _db.Payrolls.Include(x => x.Employee)
            .FirstOrDefaultAsync(x => x.PayrollId == payrollId);
        if (p is null)                  return (false, "الراتب غير موجود");
        if (p.PaymentStatus == "مدفوع") return (false, "الراتب تم صرفه بالفعل");

        var ct = new CashboxTransaction
        {
            CashBoxId       = cashBoxId,
            TransactionType = "صرف",
            ReferenceType   = "Payroll",
            Amount          = p.NetSalary ?? p.BasicSalary,
            TransactionDate = DateTime.Now,
            Notes           = $"راتب {p.Employee.FullName} - {p.PayrollMonth}",
            CreatedBy       = user,
            CreatedAt       = DateTime.Now
        };
        _db.CashboxTransactions.Add(ct);
        await _db.SaveChangesAsync();

        p.CashboxTransactionId = ct.CashboxTransactionId;
        p.PaymentStatus        = "مدفوع";
        p.PaymentDate          = DateTime.Now;
        await _db.SaveChangesAsync();

        return (true, $"✅ تم صرف راتب {p.Employee.FullName}");
    }

    // ============================================================
    // إلغاء راتب
    // ============================================================
    public async Task<(bool Ok, string Msg)> CancelAsync(int payrollId, string user)
    {
        var p = await _db.Payrolls.FirstOrDefaultAsync(x => x.PayrollId == payrollId);
        if (p is null)                  return (false, "الراتب غير موجود");
        if (p.PaymentStatus == "مدفوع") return (false, "لا يمكن إلغاء راتب مدفوع");
        p.PaymentStatus = "ملغي";
        await _db.SaveChangesAsync();
        return (true, "تم الإلغاء");
    }

    // ============================================================
    // إحصائيات
    // ============================================================
    public async Task<PayrollStatsDto> GetStatsAsync(string month)
    {
        var list = await _db.Payrolls.AsNoTracking()
            .Where(p => p.PayrollMonth == month)
            .Select(p => new
            {
                p.NetSalary, p.LoanDeduction, p.PenaltyDeduction,
                p.PaymentStatus, p.BonusInPayroll,
                // ✅ Gross = BasicSalary + BonusInPayroll (لأن GrossSalary مش موجود في DB)
                Gross = p.BasicSalary + (p.BonusInPayroll ?? 0)
            })
            .ToListAsync();

        return new PayrollStatsDto
        {
            TotalNetThisMonth   = list.Sum(p => p.NetSalary        ?? 0),
            TotalGrossThisMonth = list.Sum(p => p.Gross),
            TotalLoanDeductions = list.Sum(p => p.LoanDeduction    ?? 0),
            TotalPenalties      = list.Sum(p => p.PenaltyDeduction ?? 0),
            PaidCount           = list.Count(p => p.PaymentStatus == "مدفوع"),
            PendingCount        = list.Count(p => p.PaymentStatus == "غير مدفوع"),
            AverageNetSalary    = list.Any()
                ? Math.Round(list.Average(p => p.NetSalary ?? 0), 2) : 0
        };
    }

    public async Task<List<PayrollRunDto>> GetRunsAsync(string? month = null)
    {
        var q = _db.PayrollRuns.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(month)) q = q.Where(r => r.PayrollMonth == month);

        return await q.OrderByDescending(r => r.CreatedAt)
            .Select(r => new PayrollRunDto
            {
                RunID          = r.RunId,
                PayrollMonth   = r.PayrollMonth,
                Status         = r.Status,
                TotalEmployees = r.TotalEmployees ?? 0,
                TotalGross     = r.TotalGross     ?? 0,
                TotalDeductions= r.TotalDeductions?? 0,
                TotalNet       = r.TotalNet       ?? 0,
                ProcessedBy    = r.ProcessedBy,
                ProcessedAt    = r.ProcessedAt,
                CreatedAt      = r.CreatedAt
            }).ToListAsync();
    }

    // ── الحضور اليدوي ──────────────────────────────────────────
    public async Task<List<ManualAttendanceDto>> GetManualAttendanceAsync(string month)
{
    // أيام العمل في الشهر
    var workingDays = 26; // سيتم حسابها per-employee في GetAttendanceAsync

    // ── الموظفين المعفيين من البصمة (IsPermanentlyExempt = true) ──
    // هؤلاء دايماً يظهرون في الحضور اليدوي
    var exemptEmployees = await _db.Employees.AsNoTracking()
        .Where(e => (e.Status == "نشط" || e.Status == "Active")
                 && e.IsPermanentlyExempt == true)
        .OrderBy(e => e.Department)
        .ThenBy(e => e.FullName)
        .Select(e => new { e.EmployeeId, e.FullName, e.Department })
        .ToListAsync();

    if (!exemptEmployees.Any()) return new();

    var exemptIds = exemptEmployees.Select(e => e.EmployeeId).ToList();

    // ── جلب البيانات اليدوية الموجودة مسبقاً ──
    var existingRecords = await _db.AttendanceManuals.AsNoTracking()
        .Where(a => a.AttendanceMonth == month
                 && exemptIds.Contains(a.EmployeeId))
        .ToDictionaryAsync(a => a.EmployeeId, a => a);

    // ── بناء القائمة ──
    return exemptEmployees.Select(emp =>
    {
        if (existingRecords.TryGetValue(emp.EmployeeId, out var existing))
        {
            // بيانات موجودة مسبقاً → عرضها
            return new ManualAttendanceDto
            {
                ManualID        = existing.ManualId,
                EmployeeID      = emp.EmployeeId,
                EmployeeName    = emp.FullName,
                Department      = emp.Department,
                AttendanceMonth = month,
                PresentDays     = existing.PresentDays,
                AbsenceDays     = existing.AbsenceDays,
                LateMinutes     = existing.LateMinutes,
                Notes           = existing.Notes
            };
        }
        else
        {
            // ✅ معفى من البصمة → افتراضياً حاضر كل الشهر
            return new ManualAttendanceDto
            {
                ManualID        = 0,
                EmployeeID      = emp.EmployeeId,
                EmployeeName    = emp.FullName,
                Department      = emp.Department,
                AttendanceMonth = month,
                PresentDays     = workingDays,  // ✅ حاضر كل الشهر افتراضياً
                AbsenceDays     = 0,
                LateMinutes     = 0,
                Notes           = "معفى من البصمة"
            };
        }
    }).ToList();
}

    public async Task<(bool Ok, string Msg)> SaveManualAttendanceAsync(
        ManualAttendanceDto dto, string user)
    {
        var rec = await _db.AttendanceManuals.FirstOrDefaultAsync(
            a => a.EmployeeId == dto.EmployeeID && a.AttendanceMonth == dto.AttendanceMonth);

        if (rec is null)
            _db.AttendanceManuals.Add(new AttendanceManual
            {
                EmployeeId      = dto.EmployeeID,
                AttendanceMonth = dto.AttendanceMonth,
                PresentDays     = dto.PresentDays,
                AbsenceDays     = dto.AbsenceDays,
                LateMinutes     = dto.LateMinutes,
                Notes           = dto.Notes,
                CreatedBy       = user,
                CreatedAt       = DateTime.Now
            });
        else
        {
            rec.PresentDays = dto.PresentDays;
            rec.AbsenceDays = dto.AbsenceDays;
            rec.LateMinutes = dto.LateMinutes;
            rec.Notes       = dto.Notes;
        }
        await _db.SaveChangesAsync();
        return (true, "تم حفظ بيانات الحضور");
    }

    public async Task<byte[]> ExportMonthExcelAsync(string month)
{
    // ── جيب البيانات من DB ───────────────────────────────────
    var data = await (
        from p in _db.Payrolls.AsNoTracking()
        join e in _db.Employees.AsNoTracking() on p.EmployeeId equals e.EmployeeId
        where p.PayrollMonth == month
        orderby e.Department, e.FullName
        select new
        {
            EmployeeName     = e.FullName,
            Department       = e.Department ?? "—",
            JobTitle         = e.JobTitle ?? "—",
            BasicSalary      = p.BasicSalary,
            BonusInPayroll   = p.BonusInPayroll ?? 0,
            AbsenceDeduction = p.AbsenceDeduction ?? 0,
            LateDeduction    = p.LateDeduction ?? 0,
            LoanDeduction    = p.LoanDeduction ?? 0,
            PenaltyDeduction = p.PenaltyDeduction ?? 0,
            NetSalary        = p.NetSalary ?? p.BasicSalary,
            WorkingDays      = p.WorkingDaysInMonth ?? 0,
            PresentDays      = p.PresentDays ?? 0,
            AbsenceDays      = p.AbsenceDays ?? 0,
            LateMinutes      = p.LateMinutesTotal ?? 0,
            PaymentStatus    = p.PaymentStatus ?? "غير مدفوع",
            PaymentDate      = p.PaymentDate
        }
    ).ToListAsync();

    // ── إنشاء ملف Excel ──────────────────────────────────────
    using var wb = new XLWorkbook();
    var ws = wb.Worksheets.Add("مسير الرواتب");
    ws.RightToLeft = true;

    // ── السطر 1: العنوان الرئيسي ─────────────────────────────
    var titleCell = ws.Cell(1, 1);
    titleCell.Value = $"مسير الرواتب الشهري — {month}";
    ws.Range(1, 1, 1, 15).Merge();
    titleCell.Style
        .Font.SetBold(true)
        .Font.SetFontSize(15)
        .Font.SetFontColor(XLColor.White)
        .Fill.SetBackgroundColor(XLColor.FromHtml("#1E293B"))
        .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
        .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
    ws.Row(1).Height = 30;

    // ── السطر 2: تاريخ الطباعة ───────────────────────────────
    ws.Cell(2, 1).Value = $"تاريخ الطباعة: {DateTime.Now:yyyy-MM-dd HH:mm}";
    ws.Range(2, 1, 2, 15).Merge();
    ws.Cell(2, 1).Style
        .Font.SetFontSize(9)
        .Font.SetItalic(true)
        .Font.SetFontColor(XLColor.Gray)
        .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
    ws.Row(2).Height = 16;

    // ── السطر 3: رؤوس الأعمدة ────────────────────────────────
    var headers = new[]
    {
        "م", "اسم الموظف", "القسم", "الوظيفة",
        "أيام العمل", "أيام الحضور", "أيام الغياب", "دقائق التأخير",
        "الراتب الأساسي", "المكافآت",
        "خصم غياب", "خصم تأخير", "خصم سلفة", "خصم جزاء",
        "صافي الراتب", "الحالة"
    };

    for (int i = 0; i < headers.Length; i++)
    {
        var cell = ws.Cell(3, i + 1);
        cell.Value = headers[i];
        cell.Style
            .Font.SetBold(true)
            .Font.SetFontColor(XLColor.White)
            .Fill.SetBackgroundColor(XLColor.FromHtml("#334155"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
            .Border.SetOutsideBorder(XLBorderStyleValues.Thin)
            .Border.SetOutsideBorderColor(XLColor.FromHtml("#1E293B"));
    }
    ws.Row(3).Height = 22;

    // ── السطر 4+: البيانات ────────────────────────────────────
    int row = 4, serial = 1;
    decimal totalBasic = 0, totalBonus = 0, totalAbsDed = 0,
            totalLateDed = 0, totalLoanDed = 0, totalPenDed = 0, totalNet = 0;

    foreach (var item in data)
    {
        bool isEven = row % 2 == 0;
        var rowBg = isEven
            ? XLColor.FromHtml("#F8FAFC")
            : XLColor.White;

        ws.Cell(row, 1).Value  = serial++;
        ws.Cell(row, 2).Value  = item.EmployeeName;
        ws.Cell(row, 3).Value  = item.Department;
        ws.Cell(row, 4).Value  = item.JobTitle;
        ws.Cell(row, 5).Value  = item.WorkingDays;
        ws.Cell(row, 6).Value  = item.PresentDays;
        ws.Cell(row, 7).Value  = item.AbsenceDays;
        ws.Cell(row, 8).Value  = item.LateMinutes;
        ws.Cell(row, 9).Value  = (double)item.BasicSalary;
        ws.Cell(row, 10).Value = (double)item.BonusInPayroll;
        ws.Cell(row, 11).Value = (double)item.AbsenceDeduction;
        ws.Cell(row, 12).Value = (double)item.LateDeduction;
        ws.Cell(row, 13).Value = (double)item.LoanDeduction;
        ws.Cell(row, 14).Value = (double)item.PenaltyDeduction;
        ws.Cell(row, 15).Value = (double)item.NetSalary;
        ws.Cell(row, 16).Value = item.PaymentStatus;

        // تجميع للإجماليات
        totalBasic   += item.BasicSalary;
        totalBonus   += item.BonusInPayroll;
        totalAbsDed  += item.AbsenceDeduction;
        totalLateDed += item.LateDeduction;
        totalLoanDed += item.LoanDeduction;
        totalPenDed  += item.PenaltyDeduction;
        totalNet     += item.NetSalary;

        // تنسيق الصف
        var rowRange = ws.Range(row, 1, row, 16);
        rowRange.Style
            .Fill.SetBackgroundColor(rowBg)
            .Border.SetOutsideBorder(XLBorderStyleValues.Hair)
            .Border.SetOutsideBorderColor(XLColor.FromHtml("#E2E8F0"));

        // تنسيق الأرقام (الأعمدة 9 → 15)
        for (int c = 9; c <= 15; c++)
            ws.Cell(row, c).Style.NumberFormat.Format = "#,##0.00";

        // تلوين أيام الغياب لو أكبر من صفر
        if (item.AbsenceDays > 0)
            ws.Cell(row, 7).Style.Font.SetFontColor(XLColor.Red);

        // تلوين خلية الحالة
        var statusCell = ws.Cell(row, 16);
        statusCell.Style
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Font.SetBold(true);
        statusCell.Style.Font.SetFontColor(
            item.PaymentStatus == "مدفوع" ? XLColor.Green : XLColor.OrangeRed);

        // تلوين الصافي
        ws.Cell(row, 15).Style.Font.SetBold(true);

        row++;
    }

    // ── صف الإجماليات ────────────────────────────────────────
    ws.Cell(row, 1).Value  = "الإجمالي";
    ws.Cell(row, 9).Value  = (double)totalBasic;
    ws.Cell(row, 10).Value = (double)totalBonus;
    ws.Cell(row, 11).Value = (double)totalAbsDed;
    ws.Cell(row, 12).Value = (double)totalLateDed;
    ws.Cell(row, 13).Value = (double)totalLoanDed;
    ws.Cell(row, 14).Value = (double)totalPenDed;
    ws.Cell(row, 15).Value = (double)totalNet;

    var totalRange = ws.Range(row, 1, row, 16);
    totalRange.Style
        .Font.SetBold(true)
        .Fill.SetBackgroundColor(XLColor.FromHtml("#1E293B"))
        .Font.SetFontColor(XLColor.White)
        .Border.SetOutsideBorder(XLBorderStyleValues.Medium);

    for (int c = 9; c <= 15; c++)
        ws.Cell(row, c).Style.NumberFormat.Format = "#,##0.00";

    // ── تجميد الرؤوس ─────────────────────────────────────────
    ws.SheetView.FreezeRows(3);

    // ── AutoFit الأعمدة ───────────────────────────────────────
    ws.Columns().AdjustToContents();
    ws.Column(2).Width = Math.Max(ws.Column(2).Width, 25); // اسم الموظف
    ws.Column(3).Width = Math.Max(ws.Column(3).Width, 15); // القسم

    // ── حفظ وإرجاع ───────────────────────────────────────────
    using var ms = new MemoryStream();
    wb.SaveAs(ms);
    return ms.ToArray();
}

    // ============================================================
    // ⭐ Helper: الحضور من البصمة أو اليدوي - مُصلَح
    // ============================================================
    private async Task<(int WorkDays, int PresentDays, int LateMinutes, bool IsManual)>
    GetAttendanceAsync(int employeeId, string month)
{
    // ── أولوية للحضور اليدوي (المعفيين من البصمة) ───────────
    var manual = await _db.AttendanceManuals.AsNoTracking()
        .FirstOrDefaultAsync(a => a.EmployeeId == employeeId
                               && a.AttendanceMonth == month);
    if (manual != null)
    {
        var manualWorkDays = await GetWorkingDaysAsync(employeeId, month);
        return (manualWorkDays, manual.PresentDays, manual.LateMinutes, true);
    }

    // ── جيب الشيفت الفعال للموظف ─────────────────────────────
    if (!DateTime.TryParseExact(month + "-01", "yyyy-MM-dd",
        null, System.Globalization.DateTimeStyles.None, out var monthStart))
        return (0, 0, 0, false);
    var monthEnd = monthStart.AddMonths(1);

    var shift = await _db.EmployeeShifts.AsNoTracking()
        .Where(s => s.EmployeeId == employeeId
                 && s.EffectiveFrom <= monthStart
                 && (s.EffectiveTo == null || s.EffectiveTo >= monthStart))
        .OrderByDescending(s => s.EffectiveFrom)
        .Select(s => new { s.BiometricCode, s.OffDay1, s.OffDay2 })
        .FirstOrDefaultAsync();

    // ✅ نحسب أيام العمل أولاً (حتى لو مفيش BiometricCode)
    var workDays = await GetWorkingDaysAsync(employeeId, month);

    // ✅ جيب كود البصمة: من الشيفت أولاً، ومن Employee.BioEmployeeId كبديل
    int? bioCode = shift?.BiometricCode;
    if (bioCode == null)
    {
        bioCode = await _db.Employees.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId)
            .Select(e => e.BioEmployeeId)
            .FirstOrDefaultAsync();
    }

    // ✅ لو مفيش كود بصمة خالص → نرجع workDays صحيحة مع presentDays = 0
    if (bioCode == null)
        return (workDays, 0, 0, false);

    // ── الحضور الفعلي من Attendance ──────────────────────────
    var attRecords = await _db.Attendances.AsNoTracking()
        .Where(a => a.BiometricCode == bioCode.Value
                 && a.LogDate >= monthStart
                 && a.LogDate < monthEnd
                 && a.TimeIn != null)
        .Select(a => new { a.LogDate, a.LateMinutes })
        .ToListAsync();

    // ── أيام الراحة (فقط لو مسجلة في الشيفت) ────────────────
    var offDays = new List<DayOfWeek>();
    if (shift?.OffDay1 != null) offDays.Add((DayOfWeek)shift.OffDay1.Value);
    if (shift?.OffDay2 != null) offDays.Add((DayOfWeek)shift.OffDay2.Value);

    // شيل أيام الراحة من الحضور (فقط لو مسجلة)
    var validAtt = offDays.Any()
        ? attRecords.Where(a => !offDays.Contains(a.LogDate.DayOfWeek)).ToList()
        : attRecords.ToList();

    int presentDays = validAtt.Count;
    int lateMinutes = validAtt.Sum(a => a.LateMinutes ?? 0);

    return (workDays, presentDays, lateMinutes, false);
}

    // ============================================================
    // Helpers
    // ============================================================
    private static PayrollDetail MakeDetail(
        int payrollId, string type, string desc, decimal amount,
        bool isDeduction, string user, string? notes = null,
        string paymentType = "InPayroll") => new()
    {
        PayrollID         = payrollId,
        DetailType        = type,
        DetailDescription = desc,
        Amount            = amount,
        IsDeduction       = isDeduction,
        PaymentType       = paymentType,
        CreatedBy         = user,
        CreatedAt         = DateTime.Now
    };

    private async Task<int> GetWorkingDaysAsync(int employeeId, string month)
{
    if (!DateTime.TryParseExact(month + "-01", "yyyy-MM-dd",
        null, System.Globalization.DateTimeStyles.None, out var monthStart))
        return 26;

    var monthEnd = monthStart.AddMonths(1);

    // ── جيب أيام الراحة من شيفت الموظف ──────────────────────
    var shift = await _db.EmployeeShifts.AsNoTracking()
        .Where(s => s.EmployeeId == employeeId
                 && s.EffectiveFrom <= monthStart
                 && (s.EffectiveTo == null || s.EffectiveTo >= monthStart))
        .OrderByDescending(s => s.EffectiveFrom)
        .Select(s => new { s.OffDay1, s.OffDay2 })
        .FirstOrDefaultAsync();

    // أيام الراحة الخاصة بالموظف (فقط لو مسجلة في الشيفت)
    var offDays = new List<DayOfWeek>();
    if (shift?.OffDay1 != null) offDays.Add((DayOfWeek)shift.OffDay1.Value);
    if (shift?.OffDay2 != null) offDays.Add((DayOfWeek)shift.OffDay2.Value);

    // ── احسب أيام العمل الفعلية ──────────────────────────────
    int count = 0;
    int totalDays = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
    for (int i = 1; i <= totalDays; i++)
    {
        var day = new DateTime(monthStart.Year, monthStart.Month, i);
        if (!offDays.Any() || !offDays.Contains(day.DayOfWeek))
            count++;
    }
    return count;
}

    private async Task<decimal> GetCashBalanceAsync(int cashBoxId)
    {
        var box = await _db.CashBoxes.AsNoTracking()
            .Where(c => c.CashBoxId == cashBoxId)
            .Select(c => new { c.OpeningBalance })
            .FirstOrDefaultAsync();
        if (box is null) return 0;

        var inflow  = await _db.CashboxTransactions.AsNoTracking()
            .Where(t => t.CashBoxId == cashBoxId && t.TransactionType == "قبض")
            .SumAsync(t => (decimal?)t.Amount) ?? 0;
        var outflow = await _db.CashboxTransactions.AsNoTracking()
            .Where(t => t.CashBoxId == cashBoxId && t.TransactionType == "صرف")
            .SumAsync(t => (decimal?)t.Amount) ?? 0;

        return box.OpeningBalance + inflow - outflow;
    }
}
