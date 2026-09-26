using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Models;

/// <summary>
/// تسجيل كيان الإعدادات المالية — partial رابع للـ DbContext.
/// ⚠️ لا يضيف أي تنفيذ لـ OnModelCreatingPartial (موجود مرة واحدة فقط)؛
/// التعيين يعتمد على conventions EF المطابقة للجدول المُنشأ في SQL/FinancialSettings.sql.
/// </summary>
public partial class db24804Context
{
    public virtual DbSet<FinancialSetting> FinancialSettings { get; set; } = null!;
}
