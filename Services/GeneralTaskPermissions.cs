using System.Security.Claims;
using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// مرجع موحّد لصلاحيات التكليفات العامة على مستوى النظام — مصدر حقيقة واحد
/// تستخدمه الخدمة والواجهات معًا:
/// • الإنشاء: الأدوار الثلاثة (مدير عام/مدير حسابات/أدمن) أو حامل Claim
///   الإنشاء frm_GeneralTasks:Create — حتى يستطيع أي مدير قسم التكليف
///   بعد أن تُسند له الصلاحية من إدارة الأدوار.
/// • إدارة الكل (رؤية كل التكليفات/الملخص الإداري): الأدوار الثلاثة فقط.
/// • إلغاء/تعديل تكليف بعينه: منشؤه أو مدير (تُفحص في TaskService).
/// </summary>
public static class GeneralTaskPermissions
{
    public const string CreateClaim = "frm_GeneralTasks:Create";

    public static bool CanCreate(ClaimsPrincipal? user)
        => user?.Identity?.IsAuthenticated == true
           && (CanManageAll(user) || user.HasClaim("Permission", CreateClaim));

    public static bool CanManageAll(ClaimsPrincipal? user)
        => user?.Identity?.IsAuthenticated == true
           && (user.IsInRole(SystemRoles.GeneralManager)
               || user.IsInRole(SystemRoles.AccountManager)
               || user.IsInRole(SystemRoles.Admin)
               || user.IsInRole("Admin"));
}
