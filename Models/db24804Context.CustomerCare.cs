using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Models;

/// <summary>
/// تسجيل كيانات موديول خدمة العملاء (فيدباك) — partial ثالث للـ DbContext.
/// ⚠️ لا يضيف أي تنفيذ لـ OnModelCreatingPartial (موجود مرة واحدة في db24804Context.B2B.cs)؛
/// التعيين يعتمد على conventions EF المطابقة للجدول المُنشأ في Database/CustomerCareModule.sql.
/// </summary>
public partial class db24804Context
{
    public virtual DbSet<CustomerFeedback> CustomerFeedbacks { get; set; } = null!;
}
