using System.Security.Claims;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;
using static COCOBOLOERPNEW.Services.Assistant.ZakiInvoiceQueries;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// أدوات HR إضافية في زكي — قراءة فقط وتجميعي:
/// - get_employee_loans: السلف النشطة (أسماء + متبقي) من جدول EmployeeLoan.
/// - get_payroll_summary: ملخص شهور الرواتب (إجماليات فقط — مفيش راتب فردي لأي حد).
/// الصلاحية: Admin / HrManager / AccountManager (بيانات حساسة).
/// </summary>
public class ZakiHrExtraService
{
    public const string GetEmployeeLoans = "get_employee_loans";
    public const string GetPayrollSummary = "get_payroll_summary";

    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly IPayrollService _payroll;
    private readonly ILogger<ZakiHrExtraService> _logger;

    public ZakiHrExtraService(
        IDbContextFactory<db24804Context> dbFactory,
        IPayrollService payroll,
        ILogger<ZakiHrExtraService> logger)
    {
        _dbFactory = dbFactory;
        _payroll = payroll;
        _logger = logger;
    }

    public static bool Handles(string toolName) =>
        toolName is GetEmployeeLoans or GetPayrollSummary;

    public static bool CanView(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.IsInRole("HrManager") || user.IsInRole("AccountManager");

    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user) =>
        CanView(user)
            ? new[] { LoansDefinition(), PayrollDefinition() }
            : Array.Empty<AiToolDefinition>();

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string name, string argumentsJson, CancellationToken ct = default)
    {
        if (!CanView(user)) return Denied();

        var args = ParseArgs(argumentsJson);

        try
        {
            return name == GetEmployeeLoans
                ? await LoansAsync(args, ct)
                : await PayrollAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki hr-extra: query failed ({Tool})", name);
            return Message("ماقدرتش أجيب البيانات دلوقتي، حاول تاني بعد شوية.");
        }
    }

    // ── السلف النشطة ──
    private async Task<ZakiToolOutcome> LoansAsync(System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var limit = ReadInt(args, "limit", 20, 1, 50);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var loans = await db.EmployeeLoans.AsNoTracking()
            .Where(l => l.Status == "Active" && l.RemainingAmount > 0)
            .OrderByDescending(l => l.RemainingAmount)
            .Take(limit)
            .Select(l => new
            {
                l.EmployeeId,
                l.LoanAmount,
                l.TotalInstallments,
                l.PaidInstallments,
                l.RemainingAmount,
                l.StartDeductionMonth
            })
            .ToListAsync(ct);

        if (loans.Count == 0)
            return Message("مفيش سلف نشطة عليها متبقي.");

        var names = await EmployeeNamesAsync(db, loans.Select(l => l.EmployeeId), ct);

        var headers = new[] { "الموظف", "السلفة", "الأقساط", "المتبقي", "بداية الخصم" };
        var rows = loans.Select(l => (IReadOnlyList<string>)new[]
        {
            NameOf(l.EmployeeId, names),
            Money(l.LoanAmount),
            $"{l.PaidInstallments.ToString(Culture)}/{l.TotalInstallments.ToString(Culture)}",
            Money(l.RemainingAmount),
            l.StartDeductionMonth
        }).ToList();

        var totalRemaining = loans.Sum(l => l.RemainingAmount);

        var json = ToJson(new Dictionary<string, object?>
        {
            ["عدد السلف النشطة المعروضة"] = loans.Count,
            ["إجمالي المتبقي من السلف"] = Money(totalRemaining),
            ["السلف النشطة"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, new ZakiTable("السلف النشطة", headers, rows));
    }

    // ── ملخص الرواتب (تجميعي فقط) ──
    private async Task<ZakiToolOutcome> PayrollAsync(CancellationToken ct)
    {
        var months = await _payroll.GetMonthSummariesAsync(6);

        if (months.Count == 0)
            return Message("مفيش مسيرات رواتب مسجلة.");

        var headers = new[] { "الشهر", "عدد المسيرات", "إجمالي الصافي" };
        var rows = months.Select(m => (IReadOnlyList<string>)new[]
        {
            m.Month,
            m.PayrollCount.ToString(Culture),
            Money(m.TotalNet)
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الشهور المعروضة"] = months.Count,
            ["ملخص الرواتب (إجماليات فقط)"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, new ZakiTable("ملخص الرواتب — آخر شهور", headers, rows));
    }

    private static AiToolDefinition LoansDefinition() => new(
        GetEmployeeLoans,
        "السلف النشطة على الموظفين: المتبقي والأقساط لكل موظف وإجمالي المتبقي.",
        new
        {
            type = "object",
            properties = new
            {
                limit = new { type = "integer", description = "عدد الصفوف (1-50، الافتراضي 20)" }
            }
        });

    private static AiToolDefinition PayrollDefinition() => new(
        GetPayrollSummary,
        "ملخص الرواتب الشهري (إجماليات تجميعية فقط: عدد المسيرات وإجمالي الصافي لكل شهر) — من غير أي راتب فردي.",
        new { type = "object", properties = new { } });
}
