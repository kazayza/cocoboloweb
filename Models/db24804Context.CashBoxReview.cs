using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Models;

/// <summary>
/// تسجيل كيان أختام مراجعة الخزينة — partial للـ DbContext.
/// ⚠️ لا يضيف أي تنفيذ لـ OnModelCreatingPartial (موجود مرة واحدة في db24804Context.B2B.cs)؛
/// تكوين الكيان موجود في OnModelCreating الرئيسي، والجدول يُنشأ بـ cashbox-review.sql.
/// </summary>
public partial class db24804Context
{
    /// <summary>أختام مراجعة الخزينة — الجدول يُنشأ بـ cashbox-review.sql</summary>
    public virtual DbSet<CashBoxReview> CashBoxReviews { get; set; }
}
