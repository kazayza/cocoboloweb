using System.Globalization;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

// ═══════════════════════════════════════════════════════════
// ✅ EmployeePenaltyService — نظام جزاءات الموظفين
// مرآة وحدة الـ API (modules/employee-penalties) قاعدةً قاعدة:
//  • بوابة الإنشاء: الأدوار الأربعة (Admin / GeneralManager /
//    AccountManager / ProductionManager) أو المدير المباشر للموظف.
//  • التوقيع المباشر على موظف بعينه: مطالبة القدرة (صف صلاحيات
//    frm_EmployeePenaltiesDirectSign بمنح View — نفس الصورة التي
//    يقرأها الـ API من جداول الصلاحيات) أو علاقة المدير المباشر.
//  • الولادة: موقّع مباشر ⇒ Approved بمبلغ محسوب ومجمّد؛ وإلا
//    Pending + إشعار أصحاب التوقيع المباشر وأدوار Admin.
//  • الاعتماد/الرفض: موقّع مباشر فقط، وبمنع صارم للاعتماد
//    الذاتي، وبحارس ذرّي على الحالة (لا يبتّ في جزاء مرتين).
//  • قاعدة التجاوز: الشهر المستهدف يتقدم ما دام راتبه محفوظًا.
//  • قيمة اليوم: الأساسي ÷ أيام العمل الفعلية من محرك الرواتب
//    نفسه (IPayrollService.GetWorkingDaysAsync) — لا نسخة موازية.
// ═══════════════════════════════════════════════════════════
public class EmployeePenaltyService : IEmployeePenaltyService
{
    private readonly db24804Context    _db;
    private readonly IPayrollService   _payroll;
    private readonly IAuditService     _audit;
    private readonly NotificationService _notify;

    public EmployeePenaltyService(db24804Context db, IPayrollService payroll,
                                  IAuditService audit, NotificationService notify)
    {
        _db      = db;
        _payroll = payroll;
        _audit   = audit;
        _notify  = notify;
    }

    private const string DirectSignFormName = "frm_EmployeePenaltiesDirectSign";
    private const string PenaltiesFormName  = "frm_EmployeePenalties";

    private static readonly string[] CreatorRoles =
        { SystemRoles.Admin, SystemRoles.GeneralManager, SystemRoles.AccountManager, SystemRoles.ProductionManager };

    private static readonly string[] ViewAllRoles =
        { SystemRoles.Admin, SystemRoles.GeneralManager, SystemRoles.AccountManager, SystemRoles.ProductionManager, SystemRoles.HrManager };

    // ─────────────────────────────────────────────────────────
    // سياق الفاعل — مرآة req.auth في الـ API
    // ─────────────────────────────────────────────────────────
    private async Task<PenaltyActorDto> ResolveActorAsync(string userName)
    {
        var actor = new PenaltyActorDto { Username = userName };

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == userName);
        if (user == null) return actor;

        actor.UserId   = user.UserId;
        actor.Role     = user.Role;
        actor.EmployeeId = user.EmployeeId;

        // مطالبة التوقيع المباشر بصورتها القابلة للتخزين: صف Permissions
        // بالاسم + منح CanView في UserPermissions (يبني منه الـ API والواجهة
        // المطالبة frm_EmployeePenaltiesDirectSign:View).
        actor.HasDirectSignClaim = await (
            from up in _db.UserPermissions.AsNoTracking()
            join p  in _db.Permissions.AsNoTracking() on up.PermissionId equals p.PermissionId
            where up.UserId == user.UserId && up.CanView && p.FormName == DirectSignFormName
            select up.UserPermissionId
        ).AnyAsync();

        actor.CanCreateByRole = user.Role != null && CreatorRoles.Contains(user.Role);
        actor.CanViewAll      = user.Role != null && ViewAllRoles.Contains(user.Role);

        if (user.EmployeeId is int myEmp)
        {
            actor.IsAnyDirectManager = await _db.Employees.AsNoTracking()
                .AnyAsync(e => e.DirectManagerId == myEmp);
        }

        return actor;
    }

    public Task<PenaltyActorDto> GetActorAsync(string userName) => ResolveActorAsync(userName);

    // صاحب توقيع مباشر على هذا الموظف: حامل مطالبة القدرة، أو مديره
    // المباشر بحكم علاقة البيانات (Employees.DirectManagerID).
    private static bool IsDirectSigner(PenaltyActorDto actor, int? directManagerId)
        => actor.HasDirectSignClaim
           || (actor.EmployeeId != null && directManagerId != null
               && actor.EmployeeId.Value == directManagerId.Value);

    // بوابة الإنشاء لموظف بعينه: أحد الأدوار الأربعة، أو مديره المباشر.
    private static bool CanCreateFor(PenaltyActorDto actor, Employee target)
        => actor.CanCreateByRole
           || (actor.EmployeeId != null && target.DirectManagerId != null
               && actor.EmployeeId.Value == target.DirectManagerId.Value);

    // ─────────────────────────────────────────────────────────
    // مساعدات الشهر والحساب (مرآة employee-penalties.queries)
    // ─────────────────────────────────────────────────────────
    private static bool IsValidMonth(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && DateTime.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture,
                                     DateTimeStyles.None, out _);

    private static string NextMonth(string month)
    {
        var dt = DateTime.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return dt.AddMonths(1).ToString("yyyy-MM");
    }

    // قيمة اليوم والمبلغ المحسوب لشهر معين — بنفس تعريف المحرك:
    // الأساسي ÷ أيام العمل الفعلية، والتقريب منزلتان على الناتج النهائي.
    private async Task<(int WorkingDays, decimal DayValue, decimal ComputedAmount)>
        ComputeAsync(Employee employee, string type, decimal? daysCount, decimal? fixedAmount, string month)
    {
        if (type == PenaltyTypes.Amount)
            return (0, 0m, Math.Round(fixedAmount ?? 0m, 2));

        var workingDays = await _payroll.GetWorkingDaysAsync(employee.EmployeeId, month);
        var basic       = employee.CurrentSalaryBase ?? 0m;
        var dayRate     = workingDays > 0 ? basic / workingDays : 0m;
        var computed    = Math.Round(dayRate * (daysCount ?? 0m), 2);
        return (workingDays, Math.Round(dayRate, 2), computed);
    }

    // قاعدة التجاوز: ما دام راتب الشهر المستهدف محفوظًا أصلًا (أي صف
    // Payroll للموظف في ذلك الشهر) يتقدم الشهر حتى أول شهر بلا راتب.
    private async Task<(string Month, string? RolledFrom)> RollOverAsync(int employeeId, string requestedMonth)
    {
        var month = requestedMonth;
        for (int guard = 0; guard < 120; guard++)
        {
            var exists = await _db.Payrolls.AsNoTracking()
                .AnyAsync(p => p.EmployeeId == employeeId && p.PayrollMonth == month);
            if (!exists)
                return (month, month != requestedMonth ? requestedMonth : null);
            month = NextMonth(month);
        }
        throw new InvalidOperationException("تعذر إيجاد شهر مفتوح لتطبيق الجزاء");
    }

    // ─────────────────────────────────────────────────────────
    // القائمة والإحصائيات — نطاق الرؤية مرآة listPenalties:
    // seeAll → الكل؛ غير ذلك → جزاءات الموظف نفسه + مرؤوسيه
    // (DirectManagerID = رقمه) + ما أنشأه؛ وغير المرتبط بموظف: ما أنشأه فقط.
    // ─────────────────────────────────────────────────────────
    private sealed class PenaltyRow
    {
        public EmployeePenalty Penalty          { get; set; } = null!;
        public string          EmployeeName     { get; set; } = "";
        public string?         Department       { get; set; }
        public int?            DirectManagerId  { get; set; }
        public string?         CreatorUsername  { get; set; }
        public string?         CreatorFullName  { get; set; }
        public string?         ApproverUsername { get; set; }
        public string?         ApproverFullName { get; set; }
    }

    private IQueryable<PenaltyRow> BuildScopedQuery(PenaltyActorDto actor)
    {
        var query =
            from p  in _db.EmployeePenalties.AsNoTracking()
            join e  in _db.Employees.AsNoTracking() on p.EmployeeId equals e.EmployeeId
            join cu in _db.Users.AsNoTracking() on p.CreatedBy equals cu.UserId into cuGroup
            from cu in cuGroup.DefaultIfEmpty()
            join au in _db.Users.AsNoTracking() on p.ApprovedBy equals (int?)au.UserId into auGroup
            from au in auGroup.DefaultIfEmpty()
            select new PenaltyRow
            {
                Penalty          = p,
                EmployeeName     = e.FullName,
                Department       = e.Department,
                DirectManagerId  = e.DirectManagerId,
                CreatorUsername  = cu != null ? cu.Username : null,
                CreatorFullName  = cu != null ? cu.FullName : null,
                ApproverUsername = au != null ? au.Username : null,
                ApproverFullName = au != null ? au.FullName : null
            };

        if (actor.CanViewAll) return query;

        if (actor.EmployeeId is int myEmp)
        {
            return query.Where(x => x.Penalty.EmployeeId == myEmp
                                 || x.Penalty.CreatedBy == actor.UserId
                                 || x.DirectManagerId == myEmp);
        }

        return query.Where(x => x.Penalty.CreatedBy == actor.UserId);
    }

    private static IQueryable<PenaltyRow> ApplyFilters(
        IQueryable<PenaltyRow> query, PenaltyFilterDto filter, bool includeStatus)
    {
        if (includeStatus && !string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(x => x.Penalty.Status == filter.Status);

        if (!string.IsNullOrWhiteSpace(filter.Month))
            query = query.Where(x => x.Penalty.TargetMonth == filter.Month);

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var term = filter.SearchText.Trim();
            query = query.Where(x => x.EmployeeName.Contains(term));
        }

        return query;
    }

    private static PenaltyListDto ToDto(PenaltyRow row, PenaltyActorDto actor)
    {
        var p = row.Penalty;
        var signer = IsDirectSigner(actor, row.DirectManagerId);

        return new PenaltyListDto
        {
            Id               = p.Id,
            EmployeeId       = p.EmployeeId,
            EmployeeName     = row.EmployeeName,
            Department       = row.Department,
            PenaltyType      = p.PenaltyType,
            DaysCount        = p.DaysCount,
            FixedAmount      = p.FixedAmount,
            ComputedAmount   = p.ComputedAmount,
            TargetMonth      = p.TargetMonth,
            Reason           = p.Reason,
            Status           = p.Status,
            CreatedByUserId  = p.CreatedBy,
            CreatedByName    = !string.IsNullOrWhiteSpace(row.CreatorFullName) ? row.CreatorFullName : row.CreatorUsername,
            ApprovedByUserId = p.ApprovedBy,
            ApprovedByName   = !string.IsNullOrWhiteSpace(row.ApproverFullName) ? row.ApproverFullName : row.ApproverUsername,
            CreatedAt        = p.CreatedAt,
            ApprovedAt       = p.ApprovedAt,
            AppliedPayrollId = p.AppliedPayrollId,
            // الاعتماد: موقّع مباشر على الموظف + ليس المنشئ + الحالة معلقة
            CanApprove = p.Status == PenaltyStatuses.Pending
                         && p.CreatedBy != actor.UserId
                         && signer,
            // الإلغاء: المنشئ أو موقّع مباشر، من Pending/Approved فقط
            CanCancel = (p.Status == PenaltyStatuses.Pending || p.Status == PenaltyStatuses.Approved)
                        && (p.CreatedBy == actor.UserId || signer)
        };
    }

    public async Task<PagedResult<PenaltyListDto>> GetPenaltiesAsync(PenaltyFilterDto filter, string userName)
    {
        var actor = await ResolveActorAsync(userName);
        var query = ApplyFilters(BuildScopedQuery(actor), filter, includeStatus: true);

        var total = await query.CountAsync();

        var pageSize   = Math.Clamp(filter.PageSize, 1, 200);
        var pageNumber = Math.Max(1, filter.PageNumber);

        var rows = await query
            .OrderByDescending(x => x.Penalty.CreatedAt)
            .ThenByDescending(x => x.Penalty.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<PenaltyListDto>
        {
            Items      = rows.Select(r => ToDto(r, actor)).ToList(),
            TotalCount = total,
            PageNumber = pageNumber,
            PageSize   = pageSize
        };
    }

    public async Task<PenaltyStatsDto> GetStatsAsync(PenaltyFilterDto filter, string userName)
    {
        var actor = await ResolveActorAsync(userName);
        var query = ApplyFilters(BuildScopedQuery(actor), filter, includeStatus: false);

        var counts = await query
            .GroupBy(x => x.Penalty.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        int Of(string status) => counts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;

        return new PenaltyStatsDto
        {
            TotalCount     = counts.Sum(c => c.Count),
            PendingCount   = Of(PenaltyStatuses.Pending),
            ApprovedCount  = Of(PenaltyStatuses.Approved),
            RejectedCount  = Of(PenaltyStatuses.Rejected),
            CancelledCount = Of(PenaltyStatuses.Cancelled),
            AppliedCount   = Of(PenaltyStatuses.Applied)
        };
    }

    // ─────────────────────────────────────────────────────────
    // معاينة حية — مرآة /preview وموسعة بعرض الشهر النهائي بعد
    // التجاوز وحالة الولادة لهذا الفاعل (ما سيحدث فعليًا عند الحفظ).
    // ─────────────────────────────────────────────────────────
    public async Task<PenaltyPreviewDto> GetPreviewAsync(int employeeId, string month, decimal days, string userName)
    {
        var result = new PenaltyPreviewDto { FinalMonth = month };

        var target = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EmployeeId == employeeId);
        if (target == null || !IsValidMonth(month) || days <= 0) return result;

        var actor = await ResolveActorAsync(userName);

        var (finalMonth, rolledFrom) = await RollOverAsync(employeeId, month);
        var (workingDays, dayValue, computed) =
            await ComputeAsync(target, PenaltyTypes.Days, days, null, finalMonth);

        result.WorkingDays    = workingDays;
        result.DayValue       = dayValue;
        result.ComputedAmount = computed;
        result.FinalMonth     = finalMonth;
        result.RolledFrom     = rolledFrom;
        result.WillBeApproved = IsDirectSigner(actor, target.DirectManagerId);
        return result;
    }

    // ─────────────────────────────────────────────────────────
    // إنشاء جزاء — مرآة POST /api/employee-penalties
    // ─────────────────────────────────────────────────────────
    public async Task<PenaltyMutationResult> CreateAsync(PenaltyCreateDto dto, string userName)
    {
        var actor = await ResolveActorAsync(userName);

        if (dto.EmployeeId <= 0)
            return PenaltyMutationResult.Fail("معرف الموظف مطلوب ويجب أن يكون رقمًا صحيحًا موجبًا.");

        if (dto.PenaltyType != PenaltyTypes.Days && dto.PenaltyType != PenaltyTypes.Amount)
            return PenaltyMutationResult.Fail("نوع الجزاء مطلوب: أيام أو مبلغ.");

        if (dto.PenaltyType == PenaltyTypes.Days && (!dto.DaysCount.HasValue || dto.DaysCount.Value <= 0))
            return PenaltyMutationResult.Fail("عدد أيام الجزاء مطلوب ويجب أن يكون أكبر من صفر.");

        if (dto.PenaltyType == PenaltyTypes.Amount && (!dto.FixedAmount.HasValue || dto.FixedAmount.Value <= 0))
            return PenaltyMutationResult.Fail("مبلغ الجزاء مطلوب ويجب أن يكون أكبر من صفر.");

        var reason = (dto.Reason ?? "").Trim();
        if (string.IsNullOrWhiteSpace(reason))
            return PenaltyMutationResult.Fail("سبب الجزاء مطلوب.");

        var requestedMonth = string.IsNullOrWhiteSpace(dto.TargetMonth)
            ? DateTime.Now.ToString("yyyy-MM")
            : dto.TargetMonth.Trim();
        if (!IsValidMonth(requestedMonth))
            return PenaltyMutationResult.Fail("صيغة الشهر المستهدف غير صحيحة (YYYY-MM).");

        var target = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EmployeeId == dto.EmployeeId);
        if (target == null)
            return PenaltyMutationResult.Fail("الموظف غير موجود");

        if (!CanCreateFor(actor, target))
            return PenaltyMutationResult.Fail("غير مصرح لك بإنشاء جزاء لهذا الموظف.");

        var signer = IsDirectSigner(actor, target.DirectManagerId);

        var status         = PenaltyStatuses.Pending;
        decimal? computed  = null;
        int? approvedBy    = null;
        DateTime? approvedAt = null;
        var finalMonth     = requestedMonth;
        string? rolledFrom = null;

        if (signer)
        {
            // الولادة المعتمدة: التجاوز أولًا (بشهر مفتوح)، ثم حساب المبلغ
            // بقيمة يوم الشهر النهائي وتجميده في ComputedAmount.
            var rolled = await RollOverAsync(dto.EmployeeId, requestedMonth);
            finalMonth = rolled.Month;
            rolledFrom = rolled.RolledFrom;

            var calc = await ComputeAsync(target, dto.PenaltyType, dto.DaysCount, dto.FixedAmount, finalMonth);
            computed   = calc.ComputedAmount;
            status     = PenaltyStatuses.Approved;
            approvedBy = actor.UserId;
            approvedAt = DateTime.Now;
        }

        var penalty = new EmployeePenalty
        {
            EmployeeId     = dto.EmployeeId,
            PenaltyType    = dto.PenaltyType,
            DaysCount      = dto.PenaltyType == PenaltyTypes.Days   ? dto.DaysCount   : null,
            FixedAmount    = dto.PenaltyType == PenaltyTypes.Amount ? dto.FixedAmount : null,
            ComputedAmount = computed,
            TargetMonth    = finalMonth,
            Reason         = reason,
            Status         = status,
            CreatedBy      = actor.UserId,
            ApprovedBy     = approvedBy,
            CreatedAt      = DateTime.Now,
            ApprovedAt     = approvedAt
        };

        _db.EmployeePenalties.Add(penalty);
        await _db.SaveChangesAsync();

        await _audit.LogAsync<object>("EmployeePenalties", "Create",
            penalty.Id.ToString(), null,
            new { penalty.EmployeeId, penalty.PenaltyType, penalty.DaysCount, penalty.FixedAmount,
                  penalty.ComputedAmount, penalty.TargetMonth, penalty.Status },
            userName);

        if (status == PenaltyStatuses.Pending)
        {
            // إشعار أصحاب التوقيع المباشر وأدوار Admin (باستثناء المنشئ)
            var recipients = (await GetPendingNotificationUsernamesAsync())
                .Where(u => !string.Equals(u, userName, StringComparison.OrdinalIgnoreCase));
            await NotifyUsernamesAsync(recipients,
                "جزاء جديد بانتظار الاعتماد",
                $"تم تسجيل جزاء جديد للموظف {target.FullName} عن شهر {finalMonth} وهو بانتظار الاعتماد. السبب: {reason}",
                userName, penalty.Id);
        }

        return new PenaltyMutationResult
        {
            Success    = true,
            Message    = status == PenaltyStatuses.Approved ? "تم تسجيل الجزاء واعتماده." : "تم تسجيل الجزاء وهو بانتظار الاعتماد.",
            PenaltyId  = penalty.Id,
            Status     = status,
            FinalMonth = finalMonth,
            RolledFrom = rolledFrom
        };
    }

    // ─────────────────────────────────────────────────────────
    // اعتماد — موقّع مباشر على موظف الجزاء فقط، وبمنع صارم
    // للاعتماد الذاتي، وبحارس ذرّي على الحالة المعلقة.
    // ─────────────────────────────────────────────────────────
    public async Task<PenaltyMutationResult> ApproveAsync(int penaltyId, string userName)
    {
        var actor = await ResolveActorAsync(userName);

        var row = await LoadPenaltyRowAsync(penaltyId);
        if (row == null)
            return PenaltyMutationResult.Fail("الجزاء غير موجود");

        var penalty = row.Penalty;

        if (penalty.CreatedBy == actor.UserId)
            return PenaltyMutationResult.Fail("لا يجوز اعتماد جزاء أنشأه المعتمِد نفسه.");

        if (!IsDirectSigner(actor, row.DirectManagerId))
            return PenaltyMutationResult.Fail("غير مصرح لك باعتماد جزاءات هذا الموظف.");

        if (penalty.Status != PenaltyStatuses.Pending)
            return PenaltyMutationResult.Fail("لا يمكن اعتماد جزاء غير معلق.");

        var target = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EmployeeId == penalty.EmployeeId);

        var rolled = await RollOverAsync(penalty.EmployeeId, penalty.TargetMonth);
        var calc   = await ComputeAsync(target!, penalty.PenaltyType, penalty.DaysCount, penalty.FixedAmount, rolled.Month);

        var now = DateTime.Now;
        var affected = await _db.EmployeePenalties
            .Where(p => p.Id == penaltyId && p.Status == PenaltyStatuses.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PenaltyStatuses.Approved)
                .SetProperty(p => p.ComputedAmount, (decimal?)calc.ComputedAmount)
                .SetProperty(p => p.TargetMonth, rolled.Month)
                .SetProperty(p => p.ApprovedBy, (int?)actor.UserId)
                .SetProperty(p => p.ApprovedAt, (DateTime?)now));

        if (affected == 0)
            return PenaltyMutationResult.Fail("تم البت في هذا الجزاء مسبقًا ولا يمكن إعادة البت فيه.");

        await _audit.LogAsync<object>("EmployeePenalties", "Approve",
            penaltyId.ToString(),
            new { Status = PenaltyStatuses.Pending },
            new { Status = PenaltyStatuses.Approved, ComputedAmount = calc.ComputedAmount, TargetMonth = rolled.Month, ApprovedBy = actor.UserId },
            userName);

        // إشعار المنشئ + إشعار الموظف المعني عبر حسابه المرتبط إن وجد
        var creatorUsername  = await GetUsernameByUserIdAsync(penalty.CreatedBy);
        var employeeUsername = await GetUsernameByEmployeeIdAsync(penalty.EmployeeId);

        await NotifyUsernamesAsync(new[] { creatorUsername },
            "تم اعتماد الجزاء",
            $"تم اعتماد الجزاء المسجل للموظف {row.EmployeeName} عن شهر {rolled.Month} بمبلغ {calc.ComputedAmount} جنيه.",
            userName, penaltyId);

        await NotifyUsernamesAsync(new[] { employeeUsername },
            "تم اعتماد جزاء",
            $"تم اعتماد جزاء بمبلغ {calc.ComputedAmount} جنيه يخصم من راتب شهر {rolled.Month}. السبب: {penalty.Reason}",
            userName, penaltyId);

        return new PenaltyMutationResult
        {
            Success    = true,
            Message    = "تم اعتماد الجزاء.",
            PenaltyId  = penaltyId,
            Status     = PenaltyStatuses.Approved,
            FinalMonth = rolled.Month,
            RolledFrom = rolled.RolledFrom
        };
    }

    // ─────────────────────────────────────────────────────────
    // رفض — نفس بوابة الاعتماد؛ سبب الجزاء الأصلي يبقى كما هو
    // (مرآة الـ API: لا ملاحظة رفض تُلحق في هذه المرحلة).
    // ─────────────────────────────────────────────────────────
    public async Task<PenaltyMutationResult> RejectAsync(int penaltyId, string userName)
    {
        var actor = await ResolveActorAsync(userName);

        var row = await LoadPenaltyRowAsync(penaltyId);
        if (row == null)
            return PenaltyMutationResult.Fail("الجزاء غير موجود");

        var penalty = row.Penalty;

        if (penalty.CreatedBy == actor.UserId)
            return PenaltyMutationResult.Fail("لا يجوز رفض جزاء أنشأه صاحب القرار نفسه.");

        if (!IsDirectSigner(actor, row.DirectManagerId))
            return PenaltyMutationResult.Fail("غير مصرح لك برفض جزاءات هذا الموظف.");

        if (penalty.Status != PenaltyStatuses.Pending)
            return PenaltyMutationResult.Fail("لا يمكن رفض جزاء غير معلق.");

        var affected = await _db.EmployeePenalties
            .Where(p => p.Id == penaltyId && p.Status == PenaltyStatuses.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PenaltyStatuses.Rejected));

        if (affected == 0)
            return PenaltyMutationResult.Fail("تم البت في هذا الجزاء مسبقًا ولا يمكن إعادة البت فيه.");

        await _audit.LogAsync<object>("EmployeePenalties", "Reject",
            penaltyId.ToString(),
            new { Status = PenaltyStatuses.Pending },
            new { Status = PenaltyStatuses.Rejected },
            userName);

        var creatorUsername = await GetUsernameByUserIdAsync(penalty.CreatedBy);
        await NotifyUsernamesAsync(new[] { creatorUsername },
            "تم رفض الجزاء",
            $"تم رفض الجزاء المسجل للموظف {row.EmployeeName} عن شهر {penalty.TargetMonth}.",
            userName, penaltyId);

        return new PenaltyMutationResult
        {
            Success   = true,
            Message   = "تم رفض الجزاء.",
            PenaltyId = penaltyId,
            Status    = PenaltyStatuses.Rejected
        };
    }

    // ─────────────────────────────────────────────────────────
    // إلغاء — للمنشئ أو لموقّع مباشر على الموظف، ما دام الجزاء
    // لم يدخل راتبًا محفوظًا (Applied)؛ ومن Pending/Approved فقط.
    // ─────────────────────────────────────────────────────────
    public async Task<PenaltyMutationResult> CancelAsync(int penaltyId, string userName)
    {
        var actor = await ResolveActorAsync(userName);

        var row = await LoadPenaltyRowAsync(penaltyId);
        if (row == null)
            return PenaltyMutationResult.Fail("الجزاء غير موجود");

        var penalty = row.Penalty;

        if (penalty.Status == PenaltyStatuses.Applied)
            return PenaltyMutationResult.Fail("لا يمكن إلغاء جزاء تم تطبيقه في راتب محفوظ.");

        if (penalty.Status != PenaltyStatuses.Pending && penalty.Status != PenaltyStatuses.Approved)
            return PenaltyMutationResult.Fail("لا يمكن إلغاء جزاء منتهي الحالة.");

        var isCreator = penalty.CreatedBy == actor.UserId;
        if (!isCreator && !IsDirectSigner(actor, row.DirectManagerId))
            return PenaltyMutationResult.Fail("غير مصرح لك بإلغاء هذا الجزاء.");

        var affected = await _db.EmployeePenalties
            .Where(p => p.Id == penaltyId
                     && (p.Status == PenaltyStatuses.Pending || p.Status == PenaltyStatuses.Approved))
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PenaltyStatuses.Cancelled));

        if (affected == 0)
            return PenaltyMutationResult.Fail("تغيرت حالة الجزاء ولا يمكن إلغاؤه الآن.");

        await _audit.LogAsync<object>("EmployeePenalties", "Cancel",
            penaltyId.ToString(),
            new { Status = penalty.Status },
            new { Status = PenaltyStatuses.Cancelled },
            userName);

        return new PenaltyMutationResult
        {
            Success   = true,
            Message   = "تم إلغاء الجزاء.",
            PenaltyId = penaltyId,
            Status    = PenaltyStatuses.Cancelled
        };
    }

    // ─────────────────────────────────────────────────────────
    // مساعدات التحميل والإشعارات
    // ─────────────────────────────────────────────────────────
    private async Task<PenaltyRow?> LoadPenaltyRowAsync(int penaltyId)
    {
        return await (
            from p  in _db.EmployeePenalties.AsNoTracking()
            join e  in _db.Employees.AsNoTracking() on p.EmployeeId equals e.EmployeeId
            where p.Id == penaltyId
            select new PenaltyRow
            {
                Penalty         = p,
                EmployeeName    = e.FullName,
                Department      = e.Department,
                DirectManagerId = e.DirectManagerId
            }
        ).FirstOrDefaultAsync();
    }

    // مستلمو إشعار «جزاء معلق»: أصحاب التوقيع المباشر (صف الصلاحيات
    // بمنح View) + كل مستخدمي دور Admin — بأسماء مستخدمين فردية.
    private async Task<List<string>> GetPendingNotificationUsernamesAsync()
    {
        var fromPermissions = await (
            from up in _db.UserPermissions.AsNoTracking()
            join p  in _db.Permissions.AsNoTracking() on up.PermissionId equals p.PermissionId
            join u  in _db.Users.AsNoTracking() on up.UserId equals u.UserId
            where up.CanView && p.FormName == DirectSignFormName && u.IsActive != false
            select u.Username
        ).ToListAsync();

        var admins = await _db.Users.AsNoTracking()
            .Where(u => u.IsActive != false && u.Role == SystemRoles.Admin)
            .Select(u => u.Username)
            .ToListAsync();

        return fromPermissions.Concat(admins)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<string?> GetUsernameByUserIdAsync(int userId)
        => await _db.Users.AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => u.Username)
            .FirstOrDefaultAsync();

    private async Task<string?> GetUsernameByEmployeeIdAsync(int employeeId)
        => await _db.Users.AsNoTracking()
            .Where(u => u.EmployeeId == employeeId && u.IsActive != false)
            .Select(u => u.Username)
            .FirstOrDefaultAsync();

    // إشعارات فردية بأسماء المستخدمين؛ فشل الإشعار لا يُفشل العملية أبدًا.
    private async Task NotifyUsernamesAsync(IEnumerable<string?> usernames, string title,
                                            string message, string actor, int penaltyId)
    {
        var unique = usernames
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u!)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var username in unique)
        {
            try
            {
                await _notify.AddAsync(title, message, username, actor,
                    PenaltiesFormName, "EmployeePenalties", penaltyId);
            }
            catch
            {
                // مرآة الـ API: فشل الإشعار لا يُفشل العملية نفسها.
            }
        }
    }
}
