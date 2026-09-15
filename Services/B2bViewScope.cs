using System.Security.Claims;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// 🏢 نطاق رؤية B2B الموحّد لشاشات CRM المشتركة (تواصلات/مهام/...) — نفس قاعدة الفرص بالضبط:
///  - All       = المدراء (Admin/GeneralManager/AccountManager) → يرون كل الصفوف (عادي + B2B).
///  - OwnB2B    = موظف B2B المختص (دور B2B) → صفوف B2B المرتبطة بفرصه/أعماله فقط.
///  - AllB2B    = حامل frm_B2BCrm:View فقط → كل صفوف B2B (قراءة).
///  - ExcludeB2B = باقي المستخدمين (مبيعات/خدمة عملاء/عادي) → B2B مخفي نهائياً، يرون الباقي.
/// مصدر الحقيقة: Parties.IsB2B (العميل شركة/مؤسسة) — وليس الأعمدة المنزّلة.
/// الاستخدام: B2bViewScope.KindOf(user) + B2bViewScope.ResolveB2bAsync(db) ثم تطبيق الفلترة حسب نوع الصف.
/// </summary>
public static class B2bViewScope
{
    public enum Kind { All, ExcludeB2B, AllB2B, OwnB2B }

    public static Kind KindOf(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) return Kind.All;      // نداءات داخلية/خلفية
        if (B2bCrmPermissions.CanViewAll(user)) return Kind.All;
        if (B2bCrmPermissions.IsB2bSpecialist(user)) return Kind.OwnB2B;
        if (user.HasClaim(c => c.Type == "Permission" && c.Value == B2bCrmPermissions.PermView)) return Kind.AllB2B;
        return Kind.ExcludeB2B;
    }

    /// <summary>معرّفات عملاء B2B + الفرص الواقعة عليهم (مجموعات صغيرة دائماً).</summary>
    public static async Task<(List<int> PartyIds, List<int> OpportunityIds)> ResolveB2bAsync(db24804Context db)
    {
        var partyIds = await db.Parties.AsNoTracking()
            .Where(p => p.IsB2B)
            .Select(p => p.PartyId)
            .ToListAsync();

        var oppIds = partyIds.Count == 0
            ? new List<int>()
            : await db.SalesOpportunities.AsNoTracking()
                .Where(o => partyIds.Contains(o.PartyId))
                .Select(o => o.OpportunityId)
                .ToListAsync();

        return (partyIds, oppIds);
    }

    /// <summary>موظف المستخدم الحالي (0 = غير مرتبط).</summary>
    public static async Task<int> ResolveEmployeeIdAsync(db24804Context db, ClaimsPrincipal? user)
    {
        var username = user?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username)) return 0;
        return await db.Users.AsNoTracking()
            .Where(u => u.Username == username && u.EmployeeId.HasValue)
            .Select(u => u.EmployeeId!.Value)
            .FirstOrDefaultAsync();
    }

    /// <summary>فرص B2B المسندة للموظف المحدد (نطاق المختص).</summary>
    public static async Task<List<int>> ResolveOwnB2bOpportunitiesAsync(db24804Context db, List<int> b2bPartyIds, int employeeId)
    {
        if (b2bPartyIds.Count == 0 || employeeId <= 0) return new List<int>();
        return await db.SalesOpportunities.AsNoTracking()
            .Where(o => o.EmployeeId == employeeId && b2bPartyIds.Contains(o.PartyId))
            .Select(o => o.OpportunityId)
            .ToListAsync();
    }
}
