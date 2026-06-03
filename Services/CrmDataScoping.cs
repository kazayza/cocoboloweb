using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// CRM Data Scoping — يستخرج تاريخ بدء الاطلاع من Claims المستخدم
/// </summary>
public static class CrmDataScoping
{
    /// <summary>استخراج تاريخ بدء الاطلاع من الـ ClaimsPrincipal</summary>
    public static DateTime? GetAccessFromDate(this ClaimsPrincipal? user)
    {
        if (user == null) return null;
        if (user.IsInRole("Admin")) return null;
        var claim = user.FindFirst("CrmAccessFrom")?.Value;
        if (string.IsNullOrWhiteSpace(claim)) return null;
        return DateTime.TryParse(claim, out var dt) ? dt : null;
    }

    /// <summary>استخراج تاريخ بدء الاطلاع من HttpContext</summary>
    public static DateTime? GetCrmAccessFrom(this IHttpContextAccessor? http)
    {
        return http?.HttpContext?.User?.GetAccessFromDate();
    }
}