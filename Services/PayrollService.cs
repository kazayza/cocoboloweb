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
    private const string OffPayrollMarker = "[OFFPAYROLL]";

    private readonly db24804Context _db;
    private readonly IAuditService  _audit;
    private readonly NotificationService _notify;

    public PayrollService(db24804Context db, IAuditService audit, NotificationService notify)
    {
        _db    = db;
        _audit = audit;
        _notify = notify;
    }

    // ============================================================
    // قائمة المرتبات
    // ============================================================
    public async Task<PagedResult<PayrollListDto>> GetPayrollsAsync(PayrollFilterDto filter)
    {
        var q = from p in _db.Payrolls.AsNoTracking()
                join e in _db.Employees.AsNoTracking()
                    on p.EmployeeId equals e.EmployeeId
                where p.Notes == null || !p.Notes.Contains(OffPayrollMarker)
                select new { p, e };

        if (filter.PayrollRunId.HasValue)
            q = q.Where(x => x.p.PayrollRunId == filter.PayrollRunId.Value);

        // شهر واحد له أولوية، ثم الفترة
        if (!string.IsNullOrWhiteSpace(filter.PayrollMonth))
        {
            q = q.Where(x => x.p.PayrollMonth == filter.PayrollMonth);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(filter.MonthFrom))
                q = q.Where(x => string.Compare(x.p.PayrollMonth, filter.MonthFrom) >= 0);
            if (!string.IsNullOrWhiteSpace(filter.MonthTo))
                q = q.Where(x => string.Compare(x.p.PayrollMonth, filter.MonthTo) <= 0);
        }

        if (filter.EmployeeID.HasValue)
            q = q.Where(x => x.p.EmployeeId == filter.EmployeeID.Value);

        if (!string.IsNullOrWhiteSpace(filter.Department))
            q = q.Where(x => x.e.Department == filter.Department);

        if (!string.IsNullOrWhiteSpace(filter.JobTitle))
        {
            var job = filter.JobTitle.Trim();
            q = q.Where(x => x.e.JobTitle != null && x.e.JobTitle.Contains(job));
        }

        if (!string.IsNullOrWhiteSpace(filter.PaymentStatus))
            q = q.Where(x => x.p.PaymentStatus == filter.PaymentStatus);

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var term = filter.SearchText.Trim();
            q = q.Where(x => x.e.FullName.Contains(term)
                          || (x.e.Department != null && x.e.Department.Contains(term))
                          || (x.e.JobTitle != null && x.e.JobTitle.Contains(term))
                          || x.p.PayrollMonth.Contains(term));
        }

        if (filter.HasLoans.HasValue)
        {
            if (filter.HasLoans.Value)
                q = q.Where(x => (x.p.LoanDeduction ?? 0) > 0);
            else
                q = q.Where(x => (x.p.LoanDeduction ?? 0) == 0);
        }

        if (filter.HasAbsence.HasValue)
        {
            if (filter.HasAbsence.Value)
                q = q.Where(x => (x.p.AbsenceDays ?? 0) > 0);
            else
                q = q.Where(x => (x.p.AbsenceDays ?? 0) == 0);
        }

        if (filter.HasLate.HasValue)
        {
            if (filter.HasLate.Value)
                q = q.Where(x => (x.p.LateMinutesTotal ?? 0) > 0);
            else
                q = q.Where(x => (x.p.LateMinutesTotal ?? 0) == 0);
        }

        if (!string.IsNullOrWhiteSpace(filter.AttendanceSource))
        {
            if (filter.AttendanceSource == "Manual")
                q = q.Where(x => x.p.IsManualAttendance == true);
            else if (filter.AttendanceSource == "Biometric")
                q = q.Where(x => x.p.IsManualAttendance != true);
        }

        if (filter.MinLateMinutes.HasValue)
            q = q.Where(x => (x.p.LateMinutesTotal ?? 0) >= filter.MinLateMinutes.Value);
        if (filter.MaxLateMinutes.HasValue)
            q = q.Where(x => (x.p.LateMinutesTotal ?? 0) <= filter.MaxLateMinutes.Value);

        if (filter.MinNetSalary.HasValue)
            q = q.Where(x => (x.p.NetSalary ?? x.p.BasicSalary) >= filter.MinNetSalary.Value);
        if (filter.MaxNetSalary.HasValue)
            q = q.Where(x => (x.p.NetSalary ?? x.p.BasicSalary) <= filter.MaxNetSalary.Value);

        if (filter.MinDeductions.HasValue)
            q = q.Where(x => (x.p.Deductions ?? 0) >= filter.MinDeductions.Value);
        if (filter.MaxDeductions.HasValue)
            q = q.Where(x => (x.p.Deductions ?? 0) <= filter.MaxDeductions.Value);

        var total = await q.CountAsync();
        var items = await q
            .OrderByDescending(x => x.p.PayrollMonth)
            .ThenBy(x => x.e.FullName)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(x => new PayrollListDto
            {
                PayrollID = x.p.PayrollId,
                PayrollRunId = x.p.PayrollRunId,
                EmployeeID = x.p.EmployeeId,
                EmployeeName = x.e.FullName,
                Department = x.e.Department,
                JobTitle = x.e.JobTitle,
                PayrollMonth = x.p.PayrollMonth,
                BasicSalary = x.p.BasicSalary,
                BonusInPayroll = x.p.BonusInPayroll ?? 0,
                AbsenceDeduction = x.p.AbsenceDeduction ?? 0,
                LateDeduction = x.p.LateDeduction ?? 0,
                LoanDeduction = x.p.LoanDeduction ?? 0,
                PenaltyDeduction = x.p.PenaltyDeduction ?? 0,
                NetSalary = x.p.NetSalary ?? x.p.BasicSalary,
                WorkingDaysInMonth = x.p.WorkingDaysInMonth ?? 0,
                PresentDays = x.p.PresentDays ?? 0,
                AbsenceDays = x.p.AbsenceDays ?? 0,
                LateMinutesTotal = x.p.LateMinutesTotal ?? 0,
                IsManualAttendance = x.p.IsManualAttendance ?? false,
                PaymentStatus = x.p.PaymentStatus ?? "غير مدفوع",
                PaymentDate = x.p.PaymentDate,
                CreatedBy = x.p.CreatedBy,
                CreatedAt = x.p.CreatedAt
            })
            .ToListAsync();

        return new PagedResult<PayrollListDto>
        {
            Items = items,
            TotalCount = total,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize
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
            Penalties          = details.Where(d => d.IsDeduction && (d.DetailType == "Penalty" || d.DetailType == "ManualDeduction")).ToList(),
            LoanItems          = details.Any(d => d.DetailType == "LoanDeduction") ? details.Where(d => d.DetailType == "LoanDeduction").ToList() : loans,
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
            .Where(p => p.EmployeeId == employeeId
                     && p.PayrollMonth == month
                     && (p.Notes == null || !p.Notes.Contains(OffPayrollMarker)))
            .Select(p => new { p.PayrollId, p.PaymentStatus })
            .FirstOrDefaultAsync();

        return new PayrollCalculationDto
        {
            EmployeeID            = employeeId,
            EmployeeName          = emp.FullName,
            Department            = emp.Department,
            BasicSalary           = basic,
            WorkingDaysInMonth    = workDays,
            PresentDays           = att.PresentDays,
            AbsenceDays           = absDays,
            LateMinutesTotal      = att.LateMinutes,
            IsManualAttendance    = att.IsManual,
            AutoAbsenceDeduction  = absDed,
            AutoLateDeduction     = lateDed,
            AbsenceDeduction      = absDed,
            LateDeduction         = lateDed,
            LoanDeduction         = loanItems.Sum(i => i.Amount),
            LoanItems             = loanItems,
            Penalties             = new(),
            BonusItems            = new(),
            SeparateBonuses       = new(),
            HasExistingPayroll    = existing != null,
            ExistingPayrollID     = existing?.PayrollId,
            Warning               = existing != null
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
        PayrollRun? run = null;

        try
        {
            run = new PayrollRun
            {
                PayrollMonth = month,
                Status       = "PendingReview",
                Notes        = $"تم إنشاء كشف الرواتب وإرساله للمراجعة بواسطة {user}",
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
                    PaymentStatus      = PayrollPaymentStatuses.PendingReview,
                    Notes              = BuildPayrollNotes(calc, user),
                    PayrollRunId       = run.RunId,
                    CreatedBy          = user,
                    CreatedAt          = DateTime.Now
                };
                _db.Payrolls.Add(payroll);
                await _db.SaveChangesAsync();

                var details = BuildPayrollDetails(payroll.PayrollId, calc, user);
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

            if (count == 0)
                throw new Exception("لم يتم إنشاء أي رواتب صالحة للحفظ");

            run.Status          = "PendingReview";
            run.TotalEmployees  = count;
            run.TotalGross      = sumGross;
            run.TotalDeductions = sumDed;
            run.TotalNet        = sumNet;
            await _db.SaveChangesAsync();

            await tx.CommitAsync();

            try
            {
                await NotifyPayrollRunSubmittedAsync(run, user);
            }
            catch
            {
                // لا نُفشل حفظ الكشف إذا تعذّر إرسال الإشعار
            }

            return (true, $"✅ تم حفظ وإرسال {count} راتب للمراجعة", run.RunId);
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
        var reviewer = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == user);
        if (!IsPayrollReviewerRole(reviewer?.Role))
            return (false, "ليس لديك صلاحية صرف الرواتب");

        var p = await _db.Payrolls
            .Include(x => x.Employee)
            .Include(x => x.PayrollDetails)
            .FirstOrDefaultAsync(x => x.PayrollId == payrollId);

        if (p is null) return (false, "الراتب غير موجود");
        if (p.PaymentStatus == PayrollPaymentStatuses.Paid) return (false, "الراتب تم صرفه بالفعل");
        if (p.PaymentStatus == PayrollPaymentStatuses.Cancelled) return (false, "لا يمكن صرف راتب ملغي");
        if (p.PaymentStatus != PayrollPaymentStatuses.Approved && p.PaymentStatus != PayrollPaymentStatuses.Unpaid)
            return (false, "لا يمكن صرف الراتب قبل الاعتماد");

        var isOffPayroll = IsOffPayrollRecord(p.Notes);
        var recordLabel = isOffPayroll ? "الدفعة خارج الراتب" : "الراتب";
        var netAmount = p.NetSalary ?? p.BasicSalary;
        var separateDetails = p.PayrollDetails
            .Where(d => d.PaymentType == "Separate" && d.CashboxTransactionID == null)
            .ToList();
        var totalSeparate = separateDetails.Sum(d => d.Amount);
        var totalPayout = netAmount + totalSeparate;

        var balance = await GetCashBalanceAsync(cashBoxId);
        if (balance < totalPayout)
            return (false, $"رصيد الخزينة ({balance:N2} جـ) أقل من إجمالي المطلوب للصرف ({totalPayout:N2} جـ)");

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            CashboxTransaction? salaryTx = null;

            if (netAmount > 0)
            {
                salaryTx = new CashboxTransaction
                {
                    CashBoxId       = cashBoxId,
                    TransactionType = "صرف",
                    ReferenceType   = CashBoxRefTypes.Payroll,
                    ReferenceId     = p.PayrollId,
                    Amount          = netAmount,
                    TransactionDate = DateTime.Now,
                    Notes           = $"{recordLabel} {p.Employee.FullName} - {p.PayrollMonth}",
                    CreatedBy       = user,
                    CreatedAt       = DateTime.Now
                };
                _db.CashboxTransactions.Add(salaryTx);
                await _db.SaveChangesAsync();

                p.CashboxTransactionId = salaryTx.CashboxTransactionId;
            }

            foreach (var detail in separateDetails)
            {
                var sepTx = new CashboxTransaction
                {
                    CashBoxId       = cashBoxId,
                    TransactionType = "صرف",
                    ReferenceType   = detail.DetailType,
                    ReferenceId     = p.PayrollId,
                    Amount          = detail.Amount,
                    TransactionDate = DateTime.Now,
                    Notes           = $"{detail.DetailDescription ?? detail.DetailType} - {p.Employee.FullName} - {p.PayrollMonth}",
                    CreatedBy       = user,
                    CreatedAt       = DateTime.Now
                };
                _db.CashboxTransactions.Add(sepTx);
                await _db.SaveChangesAsync();

                detail.CashboxTransactionID = sepTx.CashboxTransactionId;
            }

            await DeductLoanInstallmentsForPayrollAsync(p, user);

            p.PaymentStatus = PayrollPaymentStatuses.Paid;
            p.PaymentDate   = DateTime.Now;
            p.Notes         = AppendLine(p.Notes, $"[صرف: {user} - {DateTime.Now:yyyy-MM-dd HH:mm} | خزينة #{cashBoxId}]");
            await _db.SaveChangesAsync();

            await UpdatePayrollRunStatusAsync(p.PayrollRunId);
            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            var inner = ex.InnerException?.Message ?? "";
            return (false, $"خطأ أثناء صرف {recordLabel}: {ex.Message} | {inner}");
        }

        if (!string.IsNullOrWhiteSpace(p.CreatedBy))
        {
            await _notify.AddAsync("💸 تم الصرف",
                $"تم صرف {recordLabel} الخاصة بـ {p.Employee.FullName} لشهر {p.PayrollMonth}" +
                (totalSeparate > 0 ? $" بقيمة إضافات منفصلة {totalSeparate:N2} جـ" : string.Empty),
                p.CreatedBy, user, "frm_Payroll", "Payroll", p.PayrollId);
        }

        return (true, $"✅ تم صرف {recordLabel} لـ {p.Employee.FullName}" +
            (totalSeparate > 0 ? $" + إضافات منفصلة {totalSeparate:N2} جـ" : string.Empty));
    }

    public async Task<(bool Ok, string Msg)> ApprovePayrollAsync(int payrollId, string user)
    {
        var reviewer = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == user);
        if (!IsPayrollReviewerRole(reviewer?.Role))
            return (false, "ليس لديك صلاحية اعتماد الرواتب");

        var payroll = await _db.Payrolls.Include(p => p.Employee).FirstOrDefaultAsync(p => p.PayrollId == payrollId);
        if (payroll is null) return (false, "الراتب غير موجود");
        if (payroll.PaymentStatus == PayrollPaymentStatuses.Paid) return (false, "لا يمكن اعتماد راتب مدفوع");
        if (payroll.PaymentStatus == PayrollPaymentStatuses.Cancelled) return (false, "لا يمكن اعتماد راتب ملغي");
        if (payroll.PaymentStatus == PayrollPaymentStatuses.Approved) return (false, "الراتب معتمد بالفعل");

        var recordLabel = IsOffPayrollRecord(payroll.Notes) ? "الدفعة خارج الراتب" : "الراتب";

        payroll.PaymentStatus = PayrollPaymentStatuses.Approved;
        payroll.Notes = AppendLine(payroll.Notes, $"[اعتماد: {user} - {DateTime.Now:yyyy-MM-dd HH:mm}]");
        await _db.SaveChangesAsync();

        await UpdatePayrollRunStatusAsync(payroll.PayrollRunId);

        if (!string.IsNullOrWhiteSpace(payroll.CreatedBy))
        {
            await _notify.AddAsync("✅ تم الاعتماد",
                $"تم اعتماد {recordLabel} الخاصة بـ {payroll.Employee.FullName} لشهر {payroll.PayrollMonth} بواسطة {user}",
                payroll.CreatedBy, user, "frm_Payroll", "Payroll", payroll.PayrollId);
        }

        return (true, $"تم اعتماد {recordLabel} بنجاح");
    }

    public async Task<(bool Ok, string Msg)> RejectPayrollAsync(int payrollId, string user, string? reason = null)
    {
        var reviewer = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == user);
        if (!IsPayrollReviewerRole(reviewer?.Role))
            return (false, "ليس لديك صلاحية رفض الرواتب");

        var payroll = await _db.Payrolls.Include(p => p.Employee).FirstOrDefaultAsync(p => p.PayrollId == payrollId);
        if (payroll is null) return (false, "الراتب غير موجود");
        if (payroll.PaymentStatus == PayrollPaymentStatuses.Paid) return (false, "لا يمكن رفض راتب مدفوع");
        if (payroll.PaymentStatus == PayrollPaymentStatuses.Cancelled) return (false, "لا يمكن رفض راتب ملغي");

        var recordLabel = IsOffPayrollRecord(payroll.Notes) ? "الدفعة خارج الراتب" : "الراتب";

        payroll.PaymentStatus = PayrollPaymentStatuses.Rejected;
        payroll.Notes = AppendLine(payroll.Notes, $"[رفض: {user} - {DateTime.Now:yyyy-MM-dd HH:mm}] {reason}");
        await _db.SaveChangesAsync();

        await UpdatePayrollRunStatusAsync(payroll.PayrollRunId);

        if (!string.IsNullOrWhiteSpace(payroll.CreatedBy))
        {
            await _notify.AddAsync("❌ تم الرفض",
                $"تم رفض {recordLabel} الخاصة بـ {payroll.Employee.FullName} لشهر {payroll.PayrollMonth}" +
                (!string.IsNullOrWhiteSpace(reason) ? $". السبب: {reason}" : ""),
                payroll.CreatedBy, user, "frm_Payroll", "Payroll", payroll.PayrollId);
        }

        return (true, $"تم رفض {recordLabel}");
    }

    // ============================================================
    // إلغاء راتب
    // ============================================================
    public async Task<(bool Ok, string Msg)> CancelAsync(int payrollId, string user)
    {
        var p = await _db.Payrolls.FirstOrDefaultAsync(x => x.PayrollId == payrollId);
        if (p is null) return (false, "الراتب غير موجود");

        var recordLabel = IsOffPayrollRecord(p.Notes) ? "الدفعة خارج الراتب" : "الراتب";

        if (p.PaymentStatus == PayrollPaymentStatuses.Paid) return (false, $"لا يمكن إلغاء {recordLabel} بعد الصرف");
        if (p.PaymentStatus == PayrollPaymentStatuses.Cancelled) return (false, $"{recordLabel} ملغاة بالفعل");

        p.PaymentStatus = PayrollPaymentStatuses.Cancelled;
        p.Notes = AppendLine(p.Notes, $"[إلغاء: {user} - {DateTime.Now:yyyy-MM-dd HH:mm}]");
        await _db.SaveChangesAsync();

        await UpdatePayrollRunStatusAsync(p.PayrollRunId);
        return (true, $"تم إلغاء {recordLabel}");
    }

    // ============================================================
    // إحصائيات
    // ============================================================
    public async Task<PayrollStatsDto> GetStatsAsync(string month)
    {
        var list = await _db.Payrolls.AsNoTracking()
            .Where(p => p.PayrollMonth == month && (p.Notes == null || !p.Notes.Contains(OffPayrollMarker)))
            .Select(p => new
            {
                p.PayrollId,
                p.NetSalary, p.LoanDeduction, p.PenaltyDeduction,
                p.PaymentStatus, p.BonusInPayroll,
                // ✅ Gross = BasicSalary + BonusInPayroll (لأن GrossSalary مش موجود في DB)
                Gross = p.BasicSalary + (p.BonusInPayroll ?? 0)
            })
            .ToListAsync();

        var payrollIds = list.Select(p => p.PayrollId).ToList();
        var totalSeparateBonuses = await _db.PayrollDetails.AsNoTracking()
            .Where(d => payrollIds.Contains(d.PayrollID) && d.PaymentType == "Separate")
            .SumAsync(d => (decimal?)d.Amount) ?? 0;

        return new PayrollStatsDto
        {
            TotalNetThisMonth   = list.Sum(p => p.NetSalary        ?? 0),
            TotalGrossThisMonth = list.Sum(p => p.Gross),
            TotalLoanDeductions = list.Sum(p => p.LoanDeduction    ?? 0),
            TotalPenalties      = list.Sum(p => p.PenaltyDeduction ?? 0),
            TotalSeparateBonuses = totalSeparateBonuses,
            PaidCount           = list.Count(p => p.PaymentStatus == PayrollPaymentStatuses.Paid),
            PendingCount        = list.Count(p => p.PaymentStatus == PayrollPaymentStatuses.Unpaid),
            ReviewCount         = list.Count(p => p.PaymentStatus == PayrollPaymentStatuses.PendingReview),
            ApprovedCount       = list.Count(p => p.PaymentStatus == PayrollPaymentStatuses.Approved),
            AverageNetSalary    = list.Any()
                ? Math.Round(list.Average(p => p.NetSalary ?? 0), 2) : 0
        };
    }

    public async Task<List<PayrollRunDto>> GetRunsAsync(string? month = null)
    {
        var q = _db.PayrollRuns.AsNoTracking()
            .Where(r => r.Notes == null || !r.Notes.Contains(OffPayrollMarker))
            .AsQueryable();
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

    public async Task<List<OffPayrollPaymentListDto>> GetOffPayrollPaymentsAsync(OffPayrollPaymentFilterDto filter)
    {
        var q = from p in _db.Payrolls.AsNoTracking()
                join e in _db.Employees.AsNoTracking() on p.EmployeeId equals e.EmployeeId
                where p.Notes != null && p.Notes.Contains(OffPayrollMarker)
                select new { p, e };

        if (!string.IsNullOrWhiteSpace(filter.Month))
            q = q.Where(x => x.p.PayrollMonth == filter.Month);

        if (filter.EmployeeID.HasValue)
            q = q.Where(x => x.p.EmployeeId == filter.EmployeeID.Value);

        if (!string.IsNullOrWhiteSpace(filter.PaymentStatus))
            q = q.Where(x => x.p.PaymentStatus == filter.PaymentStatus);

        if (!string.IsNullOrWhiteSpace(filter.PaymentType))
            q = q.Where(x => _db.PayrollDetails.Any(d => d.PayrollID == x.p.PayrollId && d.DetailType == filter.PaymentType));

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var term = filter.SearchText.Trim();
            q = q.Where(x => x.e.FullName.Contains(term)
                          || (x.e.Department != null && x.e.Department.Contains(term))
                          || (x.p.Notes != null && x.p.Notes.Contains(term))
                          || _db.PayrollDetails.Any(d => d.PayrollID == x.p.PayrollId
                                                      && ((d.DetailDescription != null && d.DetailDescription.Contains(term))
                                                          || d.DetailType.Contains(term))));
        }

        return await q
            .OrderByDescending(x => x.p.CreatedAt)
            .Select(x => new OffPayrollPaymentListDto
            {
                PayrollID     = x.p.PayrollId,
                EmployeeID    = x.p.EmployeeId,
                EmployeeName  = x.e.FullName,
                Department    = x.e.Department,
                PayrollMonth  = x.p.PayrollMonth,
                PaymentType   = _db.PayrollDetails.Where(d => d.PayrollID == x.p.PayrollId && d.PaymentType == "Separate")
                                   .Select(d => d.DetailType)
                                   .FirstOrDefault() ?? "BonusSeparate",
                Description   = _db.PayrollDetails.Where(d => d.PayrollID == x.p.PayrollId && d.PaymentType == "Separate")
                                   .Select(d => d.DetailDescription)
                                   .FirstOrDefault() ?? "دفعة خارج الراتب",
                Amount        = _db.PayrollDetails.Where(d => d.PayrollID == x.p.PayrollId && d.PaymentType == "Separate")
                                   .Sum(d => (decimal?)d.Amount) ?? 0,
                PaymentStatus = x.p.PaymentStatus ?? PayrollPaymentStatuses.PendingReview,
                RequestedAt   = x.p.CreatedAt,
                PaidAt        = x.p.PaymentDate,
                CreatedBy     = x.p.CreatedBy,
                Notes         = x.p.Notes
            })
            .ToListAsync();
    }

    public async Task<OffPayrollPaymentStatsDto> GetOffPayrollPaymentStatsAsync(OffPayrollPaymentFilterDto filter)
    {
        var items = await GetOffPayrollPaymentsAsync(filter);
        return new OffPayrollPaymentStatsDto
        {
            TotalAmount    = items.Sum(x => x.Amount),
            ReviewCount    = items.Count(x => x.PaymentStatus == PayrollPaymentStatuses.PendingReview),
            ApprovedCount  = items.Count(x => x.PaymentStatus == PayrollPaymentStatuses.Approved),
            PaidCount      = items.Count(x => x.PaymentStatus == PayrollPaymentStatuses.Paid),
            RejectedCount  = items.Count(x => x.PaymentStatus == PayrollPaymentStatuses.Rejected),
            CancelledCount = items.Count(x => x.PaymentStatus == PayrollPaymentStatuses.Cancelled)
        };
    }

    public async Task<(bool Ok, string Msg, int? PayrollId)> SaveOffPayrollPaymentAsync(OffPayrollPaymentFormDto dto, string user)
    {
        if (!dto.EmployeeID.HasValue)
            return (false, "يجب اختيار الموظف", null);

        if (dto.Amount <= 0)
            return (false, "قيمة الدفعة يجب أن تكون أكبر من صفر", null);

        if (string.IsNullOrWhiteSpace(dto.Description))
            return (false, "وصف الدفعة مطلوب", null);

        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.EmployeeId == dto.EmployeeID.Value);
        if (employee is null)
            return (false, "الموظف غير موجود", null);

        using var tx = await _db.Database.BeginTransactionAsync();
        PayrollRun? run = null;

        try
        {
            run = new PayrollRun
            {
                PayrollMonth    = dto.PaymentMonth,
                Status          = "PendingReview",
                Notes           = $"{OffPayrollMarker} دفعة خارج الراتب للموظف {employee.FullName}",
                TotalEmployees  = 1,
                TotalGross      = dto.Amount,
                TotalDeductions = 0,
                TotalNet        = dto.Amount,
                CreatedBy       = user,
                CreatedAt       = DateTime.Now
            };
            _db.PayrollRuns.Add(run);
            await _db.SaveChangesAsync();

            var payroll = new Payroll
            {
                EmployeeId         = employee.EmployeeId,
                PayrollMonth       = dto.PaymentMonth,
                BasicSalary        = 0,
                BonusInPayroll     = 0,
                Allowances         = 0,
                Deductions         = 0,
                AbsenceDeduction   = 0,
                LateDeduction      = 0,
                LoanDeduction      = 0,
                PenaltyDeduction   = 0,
                WorkingDaysInMonth = 0,
                PresentDays        = 0,
                AbsenceDays        = 0,
                LateMinutesTotal   = 0,
                IsManualAttendance = false,
                PaymentStatus      = PayrollPaymentStatuses.PendingReview,
                Notes              = BuildOffPayrollNotes(dto, employee.FullName, user),
                PayrollRunId       = run.RunId,
                CreatedBy          = user,
                CreatedAt          = DateTime.Now
            };
            _db.Payrolls.Add(payroll);
            await _db.SaveChangesAsync();

            _db.PayrollDetails.Add(new PayrollDetail
            {
                PayrollID         = payroll.PayrollId,
                DetailType        = dto.PaymentType,
                DetailDescription = dto.Description.Trim(),
                Amount            = dto.Amount,
                IsDeduction       = false,
                PaymentType       = "Separate",
                CreatedBy         = user,
                CreatedAt         = DateTime.Now
            });
            await _db.SaveChangesAsync();

            await tx.CommitAsync();

            try
            {
                await NotifyOffPayrollSubmittedAsync(payroll.PayrollId, employee.FullName, dto.Amount, dto.PaymentType, dto.PaymentMonth, user);
            }
            catch
            {
                // لا نفشل العملية لو الإشعار تعطل
            }

            return (true, "✅ تم حفظ الدفعة خارج الراتب وإرسالها للمراجعة", payroll.PayrollId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            var inner = ex.InnerException?.Message ?? string.Empty;
            return (false, $"خطأ: {ex.Message} | {inner}", null);
        }
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
        return await ExportPayrollsExcelAsync(new PayrollFilterDto
        {
            PayrollMonth = month,
            PageNumber = 1,
            PageSize = 100000
        });
    }

    public async Task<byte[]> ExportPayrollsExcelAsync(PayrollFilterDto filter)
    {
        var exportFilter = new PayrollFilterDto
        {
            PayrollRunId = filter.PayrollRunId,
            PayrollMonth = filter.PayrollMonth,
            MonthFrom = filter.MonthFrom,
            MonthTo = filter.MonthTo,
            EmployeeID = filter.EmployeeID,
            Department = filter.Department,
            JobTitle = filter.JobTitle,
            PaymentStatus = filter.PaymentStatus,
            SearchText = filter.SearchText,
            HasLoans = filter.HasLoans,
            HasAbsence = filter.HasAbsence,
            HasLate = filter.HasLate,
            AttendanceSource = filter.AttendanceSource,
            MinLateMinutes = filter.MinLateMinutes,
            MaxLateMinutes = filter.MaxLateMinutes,
            MinNetSalary = filter.MinNetSalary,
            MaxNetSalary = filter.MaxNetSalary,
            MinDeductions = filter.MinDeductions,
            MaxDeductions = filter.MaxDeductions,
            PageNumber = 1,
            PageSize = 100000
        };

        var result = await GetPayrollsAsync(exportFilter);
        var data = result.Items;

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("كشف الرواتب");
        ws.RightToLeft = true;

        string rangeLabel = !string.IsNullOrWhiteSpace(filter.PayrollMonth)
            ? filter.PayrollMonth!
            : !string.IsNullOrWhiteSpace(filter.MonthFrom) || !string.IsNullOrWhiteSpace(filter.MonthTo)
                ? $"{filter.MonthFrom ?? "—"} → {filter.MonthTo ?? "—"}"
                : "كل الفترات";

        var titleCell = ws.Cell(1, 1);
        titleCell.Value = $"تقرير الرواتب — {rangeLabel}";
        ws.Range(1, 1, 1, 18).Merge();
        titleCell.Style
            .Font.SetBold(true)
            .Font.SetFontSize(15)
            .Font.SetFontColor(XLColor.White)
            .Fill.SetBackgroundColor(XLColor.FromHtml("#1E293B"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        ws.Row(1).Height = 30;

        ws.Cell(2, 1).Value = $"تاريخ الطباعة: {DateTime.Now:yyyy-MM-dd HH:mm}";
        ws.Range(2, 1, 2, 18).Merge();
        ws.Cell(2, 1).Style
            .Font.SetFontSize(9)
            .Font.SetItalic(true)
            .Font.SetFontColor(XLColor.Gray)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        var headers = new[]
        {
            "م", "الشهر", "اسم الموظف", "القسم", "الوظيفة",
            "أيام العمل", "أيام الحضور", "أيام الغياب", "دقائق التأخير",
            "الراتب الأساسي", "المكافآت", "خصم غياب", "خصم تأخير", "خصم سلفة", "خصم جزاء",
            "صافي الراتب", "الحالة", "مصدر الحضور"
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

        int row = 4, serial = 1;
        decimal totalBasic = 0, totalBonus = 0, totalAbsDed = 0, totalLateDed = 0, totalLoanDed = 0, totalPenDed = 0, totalNet = 0;

        foreach (var item in data)
        {
            bool isEven = row % 2 == 0;
            var rowBg = isEven ? XLColor.FromHtml("#F8FAFC") : XLColor.White;

            ws.Cell(row, 1).Value = serial++;
            ws.Cell(row, 2).Value = item.PayrollMonth;
            ws.Cell(row, 3).Value = item.EmployeeName;
            ws.Cell(row, 4).Value = item.Department ?? "—";
            ws.Cell(row, 5).Value = item.JobTitle ?? "—";
            ws.Cell(row, 6).Value = item.WorkingDaysInMonth;
            ws.Cell(row, 7).Value = item.PresentDays;
            ws.Cell(row, 8).Value = item.AbsenceDays;
            ws.Cell(row, 9).Value = item.LateMinutesTotal;
            ws.Cell(row, 10).Value = (double)item.BasicSalary;
            ws.Cell(row, 11).Value = (double)item.BonusInPayroll;
            ws.Cell(row, 12).Value = (double)item.AbsenceDeduction;
            ws.Cell(row, 13).Value = (double)item.LateDeduction;
            ws.Cell(row, 14).Value = (double)item.LoanDeduction;
            ws.Cell(row, 15).Value = (double)item.PenaltyDeduction;
            ws.Cell(row, 16).Value = (double)item.NetSalary;
            ws.Cell(row, 17).Value = item.PaymentStatus;
            ws.Cell(row, 18).Value = item.AttendanceSource;

            totalBasic += item.BasicSalary;
            totalBonus += item.BonusInPayroll;
            totalAbsDed += item.AbsenceDeduction;
            totalLateDed += item.LateDeduction;
            totalLoanDed += item.LoanDeduction;
            totalPenDed += item.PenaltyDeduction;
            totalNet += item.NetSalary;

            var rowRange = ws.Range(row, 1, row, 18);
            rowRange.Style.Fill.SetBackgroundColor(rowBg)
                .Border.SetOutsideBorder(XLBorderStyleValues.Hair)
                .Border.SetOutsideBorderColor(XLColor.FromHtml("#E2E8F0"));

            for (int c = 10; c <= 16; c++)
                ws.Cell(row, c).Style.NumberFormat.Format = "#,##0.00";

            row++;
        }

        ws.Cell(row, 1).Value = "الإجمالي";
        ws.Cell(row, 10).Value = (double)totalBasic;
        ws.Cell(row, 11).Value = (double)totalBonus;
        ws.Cell(row, 12).Value = (double)totalAbsDed;
        ws.Cell(row, 13).Value = (double)totalLateDed;
        ws.Cell(row, 14).Value = (double)totalLoanDed;
        ws.Cell(row, 15).Value = (double)totalPenDed;
        ws.Cell(row, 16).Value = (double)totalNet;

        var totalRange = ws.Range(row, 1, row, 18);
        totalRange.Style
            .Font.SetBold(true)
            .Fill.SetBackgroundColor(XLColor.FromHtml("#1E293B"))
            .Font.SetFontColor(XLColor.White)
            .Border.SetOutsideBorder(XLBorderStyleValues.Medium);

        for (int c = 10; c <= 16; c++)
            ws.Cell(row, c).Style.NumberFormat.Format = "#,##0.00";

        ws.SheetView.FreezeRows(3);
        ws.Columns().AdjustToContents();
        ws.Column(3).Width = Math.Max(ws.Column(3).Width, 22);
        ws.Column(4).Width = Math.Max(ws.Column(4).Width, 14);

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

    private async Task NotifyPayrollRunSubmittedAsync(PayrollRun run, string actor)
    {
        var title = "🧾 كشف رواتب جديد بانتظار المراجعة";
        var message = $"تم تسجيل كشف رواتب شهر {run.PayrollMonth} بعدد {run.TotalEmployees ?? 0} موظف وبإجمالي صافي {run.TotalNet ?? 0:N2} جـ وهو الآن بانتظار مراجعة مدير الحسابات.";
        await _notify.NotifyRoleAsync(title, message, SystemRoles.AccountManager, actor, "frm_Payroll", "PayrollRuns", run.RunId);
        await _notify.NotifyRoleAsync(title, message, SystemRoles.Admin, actor, "frm_Payroll", "PayrollRuns", run.RunId);
    }

    private async Task NotifyOffPayrollSubmittedAsync(int payrollId, string employeeName, decimal amount, string paymentType, string month, string actor)
    {
        var typeAr = paymentType switch
        {
            "CommissionSeparate" => "عمولة منفصلة",
            _                     => "مكافأة/دفعة خارج الراتب"
        };
        var title = "🎁 دفعة خارج الراتب بانتظار المراجعة";
        var message = $"تم تسجيل {typeAr} للموظف {employeeName} لشهر {month} بقيمة {amount:N2} جـ وهي الآن بانتظار مراجعة مدير الحسابات.";
        await _notify.NotifyRoleAsync(title, message, SystemRoles.AccountManager, actor, "frm_PayrollPayment", "Payroll", payrollId);
        await _notify.NotifyRoleAsync(title, message, SystemRoles.Admin, actor, "frm_PayrollPayment", "Payroll", payrollId);
    }

    private async Task UpdatePayrollRunStatusAsync(int? payrollRunId)
    {
        if (!payrollRunId.HasValue) return;

        var run = await _db.PayrollRuns.FirstOrDefaultAsync(r => r.RunId == payrollRunId.Value);
        if (run == null) return;

        var statuses = await _db.Payrolls.AsNoTracking()
            .Where(p => p.PayrollRunId == payrollRunId.Value)
            .Select(p => p.PaymentStatus)
            .ToListAsync();

        statuses = statuses.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (!statuses.Any()) return;

        if (statuses.All(s => s == PayrollPaymentStatuses.Cancelled))
            run.Status = "Cancelled";
        else if (statuses.All(s => s == PayrollPaymentStatuses.Paid || s == PayrollPaymentStatuses.Cancelled))
            run.Status = "Completed";
        else if (statuses.Any(s => s == PayrollPaymentStatuses.Rejected))
            run.Status = "Rejected";
        else if (statuses.Any(s => s == PayrollPaymentStatuses.PendingReview))
            run.Status = "PendingReview";
        else if (statuses.All(s => s == PayrollPaymentStatuses.Approved || s == PayrollPaymentStatuses.Paid || s == PayrollPaymentStatuses.Unpaid || s == PayrollPaymentStatuses.Cancelled))
            run.Status = "Approved";
        else
            run.Status = "Draft";

        await _db.SaveChangesAsync();
    }

    private static bool IsPayrollReviewerRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return false;
        return role == SystemRoles.Admin || role == SystemRoles.AccountManager;
    }

    private static string AppendLine(string? notes, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return notes ?? string.Empty;
        return string.IsNullOrWhiteSpace(notes) ? line : $"{notes}\n{line}";
    }

    private static bool IsOffPayrollRecord(string? notes)
    {
        return !string.IsNullOrWhiteSpace(notes) && notes.Contains(OffPayrollMarker, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildOffPayrollNotes(OffPayrollPaymentFormDto dto, string employeeName, string user)
    {
        var lines = new List<string>
        {
            OffPayrollMarker,
            $"[نوع العملية] {dto.PaymentTypeAr}",
            $"[الموظف] {employeeName}",
            $"[الوصف] {dto.Description.Trim()}",
            $"[المبلغ] {dto.Amount:N2} جـ",
            $"[تم الإنشاء بواسطة] {user} - {DateTime.Now:yyyy-MM-dd HH:mm}"
        };

        if (!string.IsNullOrWhiteSpace(dto.Reason))
            lines.Add($"[السبب] {dto.Reason.Trim()}");

        return string.Join("\n", lines);
    }

    private static string? BuildPayrollNotes(PayrollCalculationDto calc, string user)
    {
        var lines = new List<string>();

        if (calc.HasAbsenceOverride)
        {
            lines.Add($"[تعديل خصم الغياب] من {calc.AutoAbsenceDeduction:N2} إلى {calc.AbsenceDeduction:N2}" +
                (!string.IsNullOrWhiteSpace(calc.AbsenceOverrideReason) ? $" | السبب: {calc.AbsenceOverrideReason}" : string.Empty));
        }

        if (calc.HasLateOverride)
        {
            lines.Add($"[تعديل خصم التأخير] من {calc.AutoLateDeduction:N2} إلى {calc.LateDeduction:N2}" +
                (!string.IsNullOrWhiteSpace(calc.LateOverrideReason) ? $" | السبب: {calc.LateOverrideReason}" : string.Empty));
        }

        if (calc.SeparateBonuses.Any())
            lines.Add($"[إضافات منفصلة] {calc.SeparateBonuses.Count} بند بإجمالي {calc.TotalSeparateBonuses:N2} جـ");

        if (!lines.Any())
            return null;

        lines.Insert(0, $"[تم إعداد الراتب بواسطة: {user} - {DateTime.Now:yyyy-MM-dd HH:mm}]");
        return string.Join("\n", lines);
    }

    private List<PayrollDetail> BuildPayrollDetails(int payrollId, PayrollCalculationDto calc, string user)
    {
        var details = new List<PayrollDetail>();

        if (calc.AbsenceDeduction > 0)
            details.Add(MakeDetail(payrollId, "AbsenceDeduction", BuildAbsenceDescription(calc), calc.AbsenceDeduction, true, user));

        if (calc.LateDeduction > 0)
            details.Add(MakeDetail(payrollId, "LateDeduction", BuildLateDescription(calc), calc.LateDeduction, true, user));

        foreach (var loanItem in calc.LoanItems.Where(i => i.Amount > 0))
            details.Add(MakeDetail(payrollId, string.IsNullOrWhiteSpace(loanItem.DetailType) ? "LoanDeduction" : loanItem.DetailType,
                loanItem.Description, loanItem.Amount, true, user, paymentType: "InPayroll"));

        foreach (var pen in calc.Penalties.Where(p => p.Amount > 0 && !string.IsNullOrWhiteSpace(p.Description)))
            details.Add(MakeDetail(payrollId, string.IsNullOrWhiteSpace(pen.DetailType) ? "Penalty" : pen.DetailType,
                pen.Description, pen.Amount, true, user, paymentType: "InPayroll"));

        foreach (var bon in calc.BonusItems.Where(b => b.Amount > 0 && !string.IsNullOrWhiteSpace(b.Description)))
            details.Add(MakeDetail(payrollId, string.IsNullOrWhiteSpace(bon.DetailType) ? "Bonus" : bon.DetailType,
                bon.Description, bon.Amount, false, user, paymentType: bon.PaymentType ?? "InPayroll"));

        foreach (var item in calc.SeparateBonuses.Where(b => b.Amount > 0 && !string.IsNullOrWhiteSpace(b.Description)))
            details.Add(MakeDetail(payrollId, string.IsNullOrWhiteSpace(item.DetailType) ? "BonusSeparate" : item.DetailType,
                item.Description, item.Amount, false, user, paymentType: "Separate"));

        return details;
    }

    private async Task DeductLoanInstallmentsForPayrollAsync(Payroll payroll, string user)
    {
        var loanDetails = payroll.PayrollDetails
            .Where(d => d.DetailType == "LoanDeduction")
            .OrderBy(d => d.PayrollDetailID)
            .ToList();

        if (!loanDetails.Any())
            return;

        var installments = await _db.LoanInstallments
            .Include(i => i.Loan)
            .Where(i => i.EmployeeId == payroll.EmployeeId
                     && i.DeductionMonth == payroll.PayrollMonth
                     && i.Status == "Pending")
            .OrderBy(i => i.InstallmentNumber)
            .ToListAsync();

        if (!installments.Any())
            return;

        var usedDetailIds = new HashSet<int>();

        foreach (var inst in installments)
        {
            var matchedDetail = loanDetails.FirstOrDefault(d => !usedDetailIds.Contains(d.PayrollDetailID) && d.Amount == inst.Amount)
                ?? loanDetails.FirstOrDefault(d => !usedDetailIds.Contains(d.PayrollDetailID));

            if (matchedDetail == null)
                continue;

            usedDetailIds.Add(matchedDetail.PayrollDetailID);

            inst.Status = "Deducted";
            inst.PayrollId = payroll.PayrollId;
            inst.PayrollDetailId = matchedDetail.PayrollDetailID;
            inst.DeductionDate = DateTime.Now;
            inst.Notes = AppendLine(inst.Notes, $"تم خصم القسط مع صرف راتب شهر {payroll.PayrollMonth} بواسطة {user}");

            var loan = inst.Loan;
            loan.PaidInstallments++;
            loan.RemainingAmount -= inst.Amount;
            loan.LastUpdatedAt = DateTime.Now;

            if (loan.RemainingAmount <= 0 || loan.PaidInstallments >= loan.TotalInstallments)
            {
                loan.Status = "Completed";
                loan.RemainingAmount = 0;
            }
        }

        await _db.SaveChangesAsync();
    }

    private static string BuildAbsenceDescription(PayrollCalculationDto calc)
    {
        var text = $"خصم غياب {calc.AbsenceDays} يوم";
        if (calc.HasAbsenceOverride)
            text += $" (معدل يدويًا من {calc.AutoAbsenceDeduction:N2} إلى {calc.AbsenceDeduction:N2})";
        return text;
    }

    private static string BuildLateDescription(PayrollCalculationDto calc)
    {
        var text = $"خصم تأخير {calc.LateMinutesTotal} دقيقة";
        if (calc.HasLateOverride)
            text += $" (معدل يدويًا من {calc.AutoLateDeduction:N2} إلى {calc.LateDeduction:N2})";
        return text;
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
    if (!offDays.Any()) offDays.AddRange(new[] { DayOfWeek.Friday, DayOfWeek.Saturday });

    var holidayDates = await _db.Calendars.AsNoTracking()
        .Where(c => c.CalendarDate >= monthStart.Date && c.CalendarDate < monthEnd.Date && c.IsHoliday == true)
        .Select(c => c.CalendarDate.Date)
        .ToListAsync();
    var holidaySet = holidayDates.ToHashSet();

    // ── احسب أيام العمل الفعلية ──────────────────────────────
    int count = 0;
    int totalDays = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
    for (int i = 1; i <= totalDays; i++)
    {
        var day = new DateTime(monthStart.Year, monthStart.Month, i);
        if (!offDays.Contains(day.DayOfWeek) && !holidaySet.Contains(day.Date))
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
