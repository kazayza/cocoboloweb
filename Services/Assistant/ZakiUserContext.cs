using System.Security.Claims;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>بيانات المستخدم اللي زكي بيتعامل معاه</summary>
/// <param name="DisplayName">اسم الموظف لو اليوزر مربوط بموظف، وغير كده اسم اليوزر</param>
/// <param name="EmployeeId">رقم الموظف المربوط باليوزر (لو موجود)</param>
public record ZakiUserInfo(string DisplayName, int? EmployeeId);

/// <summary>
/// بيحدد اسم العرض للمستخدم:
/// - لو اليوزر مربوط بموظف (Users.EmployeeId) بياخد اسم الموظف.
/// - غير كده بياخد الاسم الكامل لليوزر، وفي الآخر اسم الدخول.
/// </summary>
public class ZakiUserContext
{
    private readonly IDbContextFactory<db24804Context> _dbFactory;

    public ZakiUserContext(IDbContextFactory<db24804Context> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<ZakiUserInfo> ResolveAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var username = user.Identity?.Name ?? "";
        if (string.IsNullOrWhiteSpace(username))
            return new ZakiUserInfo("المستخدم", null);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var row = await db.Users.AsNoTracking()
            .Where(u => u.Username == username)
            .Select(u => new { u.EmployeeId, u.FullName })
            .FirstOrDefaultAsync(ct);

        if (row?.EmployeeId is int empId && empId > 0)
        {
            var employeeName = await db.Employees.AsNoTracking()
                .Where(e => e.EmployeeId == empId)
                .Select(e => e.FullName)
                .FirstOrDefaultAsync(ct);

            if (!string.IsNullOrWhiteSpace(employeeName))
                return new ZakiUserInfo(employeeName, empId);
        }

        var userFullName = row?.FullName;
        var display = string.IsNullOrWhiteSpace(userFullName) ? username : userFullName;
        return new ZakiUserInfo(display, null);
    }
}
