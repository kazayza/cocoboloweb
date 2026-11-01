using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Models;

/// <summary>
/// تسجيل كيانات خيط نقاش التكليفات — partial للـ DbContext.
/// ⚠️ لا يضيف أي تنفيذ لـ OnModelCreatingPartial (موجود مرة واحدة في db24804Context.B2B.cs)؛
/// تكوين الكيانين موجود في OnModelCreating الرئيسي، والجدولان يُنشآن بـ general-tasks-2-platform.sql.
/// عند غياب الجدولين تتحلل قراءات الخيط بأمان (قائمة فارغة) وتفشل الكتابة برسالة واضحة.
/// </summary>
public partial class db24804Context
{
    public virtual DbSet<TaskComment> TaskComments { get; set; }

    public virtual DbSet<TaskCommentMention> TaskCommentMentions { get; set; }
}
