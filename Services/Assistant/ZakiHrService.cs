using System.Security.Claims;
using System.Text.Json;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using COCOBOLOERPNEW.Services;
using Microsoft.EntityFrameworkCore;
using static COCOBOLOERPNEW.Services.Assistant.ZakiInvoiceQueries;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// أدوات الموارد البشرية في زكي (من غير المرتبات ولا السلف ولا البيانات الشخصية الحساسة).
/// البيانات بتيجي من نفس خدمات الشاشات (IAttendanceService / IEmployeeService / IEmployeeShiftService)
/// عشان الأرقام تطابق الشاشات بالظبط.
/// الصلاحيات (زي الشاشات):
/// - الحضور والغياب: Admin أو frm_empAttendance:View.
/// - الموظفين: Admin أو frm_Employeeslist:View.
/// - الاستثناءات والإذونات: Admin أو frmDailyExemptions:View.
/// - شيفتاتي: Admin أو frm_MySchedule:View (بيانات المستخدم نفسه).
/// </summary>
public class ZakiHrService
{
    public const string GetAttendanceToday = "get_attendance_today";
    public const string GetEmployeeAttendance = "get_employee_attendance";
    public const string GetEmployeesOverview = "get_employees_overview";
    public const string GetExemptions = "get_exemptions";
    public const string GetMySchedule = "get_my_schedule";

    private readonly IAttendanceService _attendance;
    private readonly IEmployeeService _employees;
    private readonly IEmployeeShiftService _shifts;
    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly ILogger<ZakiHrService> _logger;

    public ZakiHrService(
        IAttendanceService attendance,
        IEmployeeService employees,
        IEmployeeShiftService shifts,
        IDbContextFactory<db24804Context> dbFactory,
        ILogger<ZakiHrService> logger)
    {
        _attendance = attendance;
        _employees = employees;
        _shifts = shifts;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public static bool Handles(string toolName) =>
        toolName is GetAttendanceToday or GetEmployeeAttendance or GetEmployeesOverview
            or GetExemptions or GetMySchedule;

    public static bool CanViewAttendance(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("Permission", "frm_empAttendance:View");

    public static bool CanViewEmployees(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("Permission", "frm_Employeeslist:View");

    public static bool CanViewExemptions(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("Permission", "frmDailyExemptions:View");

    public static bool CanViewMySchedule(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("Permission", "frm_MySchedule:View");

    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user)
    {
        var list = new List<AiToolDefinition>();

        if (CanViewAttendance(user))
        {
            list.Add(AttendanceTodayDefinition());
            list.Add(EmployeeAttendanceDefinition());
        }

        if (CanViewEmployees(user))
            list.Add(EmployeesOverviewDefinition());

        if (CanViewExemptions(user))
            list.Add(ExemptionsDefinition());

        if (CanViewMySchedule(user))
            list.Add(MyScheduleDefinition());

        return list;
    }

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string name, string argumentsJson, CancellationToken ct = default)
    {
        var allowed = name switch
        {
            GetAttendanceToday or GetEmployeeAttendance => CanViewAttendance(user),
            GetEmployeesOverview => CanViewEmployees(user),
            GetExemptions => CanViewExemptions(user),
            GetMySchedule => CanViewMySchedule(user),
            _ => false
        };

        if (!allowed)
            return Denied();

        var args = ParseArgs(argumentsJson);

        try
        {
            return name switch
            {
                GetAttendanceToday => await AttendanceTodayAsync(ct),
                GetEmployeeAttendance => await EmployeeAttendanceAsync(args, ct),
                GetEmployeesOverview => await EmployeesOverviewAsync(args, ct),
                GetExemptions => await ExemptionsAsync(args, ct),
                GetMySchedule => await MyScheduleAsync(user, ct),
                _ => Message("أداة غير معروفة.")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki HR: query failed ({Tool})", name);
            return Message("ماقدرتش أجيب بيانات الموارد البشرية دلوقتي، حاول تاني بعد شوية.");
        }
    }

    // ════════════════════════════════════════════════════════════
    // 1) الحضور والغياب اليوم
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> AttendanceTodayAsync(CancellationToken ct)
    {
        var today = DateTime.Today;

        var stats = await _attendance.GetTodayStatisticsAsync(null);

        var page = await _attendance.GetAttendanceAsync(new AttendanceFilterDto
        {
            DateFrom = today,
            DateTo = today,
            PageNumber = 1,
            PageSize = 500
        });

        var exceptions = page.Items
            .Where(a => a.IsAbsent || a.IsLate)
            .OrderByDescending(a => a.IsAbsent)
            .ThenByDescending(a => a.LateMinutes ?? 0)
            .ToList();

        var headers = new[] { "الموظف", "القسم", "الحالة", "وقت الحضور", "دقائق التأخير" };

        var rows = exceptions.Select(a => (IReadOnlyList<string>)new[]
        {
            a.EmployeeName,
            a.Department ?? "—",
            a.IsAbsent ? "غائب" : "متأخر",
            a.TimeInDisplay,
            (a.LateMinutes ?? 0) > 0 ? (a.LateMinutes ?? 0).ToString(Culture) : "—"
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["التاريخ"] = Date(today),
            ["حاضرين"] = stats.TodayPresent,
            ["غايبين"] = stats.TodayAbsent,
            ["متأخرين"] = stats.TodayLate,
            ["نسبة الحضور %"] = stats.AttendanceRate,
            ["الموظفين المتابَعين"] = stats.TotalEmployees,
            ["عدد الغياب والتأخير المعروض"] = rows.Count,
            ["الغياب والتأخير"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json,
            rows.Count == 0 ? null : new ZakiTable("الغياب والتأخير اليوم", headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 2) حضور موظف معين لفترة
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> EmployeeAttendanceAsync(JsonElement args, CancellationToken ct)
    {
        var search = ReadRawString(args, "employeeName");
        if (search.Length < 2)
            return Message("اكتب اسم الموظف اللي عايز حضوره.");

        var today = DateTime.Today;
        var (start, label) = PeriodRange(ReadString(args, "period", "month"), today);

        var (employeeId, employeeName, error) = await FindEmployeeAsync(search, ct);
        if (error != null) return error;

        var page = await _attendance.GetAttendanceAsync(new AttendanceFilterDto
        {
            EmployeeId = employeeId,
            DateFrom = start,
            DateTo = today,
            PageNumber = 1,
            PageSize = 400
        });

        var items = page.Items.OrderByDescending(a => a.LogDate).ToList();

        var presentDays = items.Count(a => a.IsPresent);
        var absentDays = items.Count(a => a.IsAbsent);
        var lateDays = items.Count(a => a.IsLate);
        var lateMinutes = items.Sum(a => a.LateMinutes ?? 0);
        var earlyLeaves = items.Count(a => a.HasEarlyLeave);
        var hours = items.Sum(a => a.TotalHours ?? 0m);

        var headers = new[] { "التاريخ", "اليوم", "الحضور", "الانصراف", "الحالة", "التأخير" };

        var rows = items.Take(40).Select(a => (IReadOnlyList<string>)new[]
        {
            a.LogDateDisplay,
            a.DayName,
            a.TimeInDisplay,
            a.TimeOutDisplay,
            a.StatusDisplay,
            a.LateDisplay
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الموظف"] = employeeName,
            ["الفترة"] = label,
            ["أيام الحضور"] = presentDays,
            ["أيام الغياب"] = absentDays,
            ["أيام التأخير"] = lateDays,
            ["إجمالي دقائق التأخير"] = lateMinutes,
            ["مرات الانصراف المبكر"] = earlyLeaves,
            ["إجمالي ساعات العمل"] = hours.ToString("#,0.0", Culture),
            ["عدد السجلات"] = items.Count,
            ["آخر السجلات"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json,
            rows.Count == 0 ? null : new ZakiTable("حضور " + employeeName + " — " + label, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 3) نظرة عامة على الموظفين (بدون مرتبات)
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> EmployeesOverviewAsync(JsonElement args, CancellationToken ct)
    {
        var limit = ReadInt(args, "limit", 20, 1, 50);
        var department = ReadRawString(args, "department");
        var status = ReadRawString(args, "status");

        var stats = await _employees.GetStatsAsync(null);

        var page = await _employees.GetEmployeesAsync(new EmployeeFilterDto
        {
            Department = department.Length == 0 ? null : department,
            Status = status.Length == 0 ? null : status,
            PageNumber = 1,
            PageSize = limit
        });

        var headers = new[] { "الموظف", "الوظيفة", "القسم", "الفرع", "الحالة", "تاريخ التعيين" };

        // ملاحظة: مفيش أي حقل مرتب أو بيانات شخصية حساسة في الجدول
        var rows = page.Items.Select(e => (IReadOnlyList<string>)new[]
        {
            e.FullName,
            e.JobTitle ?? "—",
            e.Department ?? "—",
            e.BranchName ?? "—",
            e.Status,
            Date(e.HireDate)
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["إجمالي الموظفين"] = stats.TotalEmployees,
            ["نشط"] = stats.ActiveCount,
            ["موقوف"] = stats.SuspendedCount,
            ["مستقيل"] = stats.ResignedCount,
            ["في إجازة"] = stats.OnLeaveCount,
            ["عدد الأقسام"] = stats.DepartmentCount,
            ["عدد نتائج الفلتر"] = page.TotalCount,
            ["الموظفين"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json,
            rows.Count == 0 ? null : new ZakiTable("الموظفين", headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 4) الاستثناءات والإذونات
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> ExemptionsAsync(JsonElement args, CancellationToken ct)
    {
        var limit = ReadInt(args, "limit", 15, 1, 30);
        var status = ReadString(args, "status", "pending");
        var (start, label) = PeriodRange(ReadString(args, "period", PeriodAll), DateTime.Today);

        string? dbStatus = status switch
        {
            "approved" => ExemptionStatus.Approved,
            "rejected" => ExemptionStatus.Rejected,
            "all" => null,
            _ => ExemptionStatus.Pending
        };

        var page = await _attendance.GetExemptionsAsync(new ExemptionFilterDto
        {
            Status = dbStatus,
            DateFrom = start,
            PageNumber = 1,
            PageSize = limit
        });

        var stats = await _attendance.GetExemptionStatsAsync(new ExemptionFilterDto { DateFrom = start });

        var headers = new[] { "الموظف", "التاريخ", "السبب", "المدة", "الخصم", "الحالة", "ملاحظات" };

        var rows = page.Items.Select(e => (IReadOnlyList<string>)new[]
        {
            e.EmployeeName,
            e.DateDisplay,
            e.ReasonCode,
            e.DurationDisplay,
            e.DeductionDisplay,
            e.StatusAr,
            e.Notes ?? "—"
        }).ToList();

        var statusText = status switch
        {
            "approved" => "المعتمدة",
            "rejected" => "المرفوضة",
            "all" => "الكل",
            _ => "المعلقة"
        };

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفترة"] = label,
            ["الفلتر"] = statusText,
            ["معلقة"] = stats.PendingCount,
            ["معتمدة"] = stats.ApprovedCount,
            ["مرفوضة"] = stats.RejectedCount,
            ["عدد نتائج الفلتر"] = page.TotalCount,
            ["الاستثناءات"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json,
            rows.Count == 0 ? null : new ZakiTable("الاستثناءات والإذونات — " + statusText + " — " + label, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 5) شيفتاتي (بيانات المستخدم نفسه)
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> MyScheduleAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var shifts = await _shifts.GetMyShiftsAsync(user.Identity?.Name ?? "");

        if (shifts.Count == 0)
            return Message("مفيش شيفتات مربوطة بحسابك، أو حسابك مش مربوط بموظف.");

        var headers = new[] { "نوع الشيفت", "من", "إلى", "ساري من", "ساري لغاية", "الحالة" };

        var rows = shifts.Select(s => (IReadOnlyList<string>)new[]
        {
            s.ShiftType,
            s.StartTimeDisplay,
            s.EndTimeDisplay,
            s.EffectiveFromDisplay,
            s.EffectiveToDisplay ?? "مستمر",
            s.IsActive ? "ساري" : "منتهي"
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["عدد الشيفتات"] = rows.Count,
            ["شيفتاتي"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, new ZakiTable("شيفتاتي", headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // مساعدات
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// البحث عن موظف بالاسم (بيتجاهل الهمزات والياء). بيرجّع خطأ لو مش واضح أو مش موجود.
    /// </summary>
    private async Task<(int? Id, string Name, ZakiToolOutcome? Error)> FindEmployeeAsync(string search, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var employees = await db.Employees
            .AsNoTracking()
            .Select(e => new EmployeeLite(e.EmployeeId, e.FullName))
            .ToListAsync(ct);

        var key = SearchKey(search);
        var matched = employees.Where(e => SearchKey(e.FullName).Contains(key)).ToList();

        if (matched.Count == 0)
            return (null, "", Message("مالقيتش موظف بالاسم ده."));

        var exact = matched.Where(e => SearchKey(e.FullName) == key).ToList();
        var candidates = exact.Count > 0 ? exact : matched;

        if (candidates.Count > 1)
        {
            return (null, "", new ZakiToolOutcome(ToJson(new Dictionary<string, object?>
            {
                ["ملاحظة"] = "في أكتر من موظف بالاسم ده. اطلب من المستخدم يكتب الاسم بالكامل.",
                ["الموظفين المطابقين"] = candidates.Take(8).Select(c => c.FullName).ToList()
            }), null));
        }

        return (candidates[0].EmployeeId, candidates[0].FullName, null);
    }

    /// <summary>مفتاح مطابقة الأسماء: بدون همزات ولا ياء ولا مسافات</summary>
    private static string SearchKey(string text) =>
        text.NormalizeArabic().Replace("ي", "").Replace(" ", "").ToLowerInvariant();

    // ════════════════════════════════════════════════════════════
    // تعريفات الأدوات (وصف قصير عشان نوفر التوكنز)
    // ════════════════════════════════════════════════════════════
    private static object PeriodSchema(string description) => new
    {
        type = "string",
        description,
        @enum = new[] { "today", "week", "month", "year", "all" }
    };

    private static AiToolDefinition AttendanceTodayDefinition() => new(
        GetAttendanceToday,
        "الحضور والغياب اليوم: عدد الحاضرين والغايبين والمتأخرين، ونسبة الحضور، وقائمة الغايبين والمتأخرين.",
        new { type = "object", properties = new { } });

    private static AiToolDefinition EmployeeAttendanceDefinition() => new(
        GetEmployeeAttendance,
        "حضور موظف معين بالاسم لفترة: أيام الحضور والغياب والتأخير، وإجمالي دقائق التأخير، وآخر السجلات. الافتراضي month.",
        new
        {
            type = "object",
            properties = new
            {
                employeeName = new { type = "string", description = "اسم الموظف أو جزء منه" },
                period = PeriodSchema("الافتراضي month")
            },
            required = new[] { "employeeName" }
        });

    private static AiToolDefinition EmployeesOverviewDefinition() => new(
        GetEmployeesOverview,
        "عدد الموظفين حسب الحالة (نشط، موقوف، مستقيل، في إجازة) وقائمة الموظفين. ممكن فلترة بالقسم أو الحالة. مفيش مرتبات.",
        new
        {
            type = "object",
            properties = new
            {
                department = new { type = "string", description = "القسم (اختياري)" },
                status = new { type = "string", description = "الحالة (اختياري)" },
                limit = new { type = "integer", minimum = 1, maximum = 50 }
            }
        });

    private static AiToolDefinition ExemptionsDefinition() => new(
        GetExemptions,
        "الاستثناءات والإذونات (مأمورية، إذن، انصراف مبكر...): المعلقة (الافتراضي) أو المعتمدة أو المرفوضة، مع عدد كل حالة.",
        new
        {
            type = "object",
            properties = new
            {
                status = new { type = "string", @enum = new[] { "pending", "approved", "rejected", "all" } },
                period = PeriodSchema("الافتراضي all"),
                limit = new { type = "integer", minimum = 1, maximum = 30 }
            }
        });

    private static AiToolDefinition MyScheduleDefinition() => new(
        GetMySchedule,
        "شيفتات المستخدم الحالي: نوع الشيفت ومواعيده وفترة سريانه.",
        new { type = "object", properties = new { } });

    private sealed record EmployeeLite(int EmployeeId, string FullName);
}
