using System.Security.Claims;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// ⭐ SalesInvoiceAccess — حماية فواتير المبيعات التي أنشأها مديرو الحسابات
/// الفواتير المنشأة بواسطة أي مستخدم دوره "AccountManager" لا يراها إلا:
/// Admin / AccountManager / Account — في القوائم والبحث والإحصائيات والتقارير والداشبورد
/// (مصدر واحد للحقيقة — كل الشاشات تستخدم هذا الملف)
/// </summary>
public static class SalesInvoiceAccess
{
    /// <summary>هل يستطيع المستخدم الحالي رؤية فواتير منشأة بواسطة مديري الحسابات؟</summary>
    public static bool CanViewAccountManagerInvoices(ClaimsPrincipal? user)
        => user != null && (
            user.IsInRole(SystemRoles.Admin)
            || user.IsInRole(SystemRoles.AccountManager)
            || user.IsInRole(SystemRoles.Account));

    /// <summary>أسماء المستخدمين أصحاب دور AccountManager (منشئو الفواتير المحمية)</summary>
    public static Task<List<string>> GetProtectedCreatorUsernamesAsync(db24804Context db)
        => db.Users.AsNoTracking()
            .Where(u => u.Role == SystemRoles.AccountManager)
            .Select(u => u.Username)
            .ToListAsync();

    /// <summary>
    /// ⭐ استثناء فواتير المبيعات المحمية من استعلام Transactions
    /// (المشتريات وباقي الحركات تبقى كما هي — ديناميكي: لو مفيش مديري حسابات مفيش تأثير)
    /// </summary>
    public static IQueryable<Transaction> ExcludeProtectedSales(
        this IQueryable<Transaction> query, List<string> protectedCreators)
        => protectedCreators.Count == 0
            ? query
            : query.Where(t => t.TransactionType != TransactionTypes.Sale
                            || !protectedCreators.Contains(t.CreatedBy));

    /// <summary>هل هذه الفاتورة محمية؟ (فاتورة بيع أنشأها مدير حسابات)</summary>
    public static Task<bool> IsProtectedSaleAsync(db24804Context db, Transaction? t, ClaimsPrincipal? user)
    {
        if (t == null
            || t.TransactionType != TransactionTypes.Sale
            || CanViewAccountManagerInvoices(user))
            return Task.FromResult(false);

        return db.Users.AsNoTracking()
            .AnyAsync(u => u.Role == SystemRoles.AccountManager && u.Username == t.CreatedBy);
    }

    /// <summary>
    /// ⭐ استثناء عروض الأسعار المنشأة بواسطة مديري الحسابات
    /// (نفس قاعدة الفواتير: لا يراها إلا Admin/AccountManager/Account)
    /// العروض بدون منشئ مسجل تبقى ظاهرة للجميع
    /// </summary>
    public static IQueryable<Quotation> ExcludeProtectedQuotations(
        this IQueryable<Quotation> query, List<string> protectedCreators)
        => protectedCreators.Count == 0
            ? query
            : query.Where(q => q.CreatedBy == null || !protectedCreators.Contains(q.CreatedBy));
}