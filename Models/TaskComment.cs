using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

/// <summary>
/// تعليق أو تحديث تقدم في خيط نقاش تكليف (عام أو مرتبط بمستند).
/// مكتوب يدويًا بنفس أسلوب الـScaffold؛ الجدول يُنشأ بـ general-tasks-2-platform.sql.
/// Kind: Comment (تعليق) | Progress (تحديث تقدم).
/// </summary>
public partial class TaskComment
{
    public int Id { get; set; }

    /// <summary>التكليف صاحب الخيط (CRM_Tasks.TaskID)</summary>
    public int TaskId { get; set; }

    /// <summary>اسم مستخدم كاتب التعليق</summary>
    public string AuthorUserName { get; set; } = null!;

    /// <summary>موظف الكاتب إن كان حسابه مربوطًا بموظف</summary>
    public int? AuthorEmployeeId { get; set; }

    /// <summary>لقطة الاسم المعروض للكاتب وقت الكتابة</summary>
    public string? AuthorName { get; set; }

    public string Body { get; set; } = null!;

    /// <summary>Comment | Progress</summary>
    public string Kind { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual CrmTask Task { get; set; } = null!;

    public virtual Employee? AuthorEmployee { get; set; }

    public virtual ICollection<TaskCommentMention> Mentions { get; set; } = new List<TaskCommentMention>();
}

/// <summary>ثوابت نوع مدخلات خيط النقاش</summary>
public static class TaskCommentKinds
{
    public const string Comment = "Comment";
    public const string Progress = "Progress";

    public static string NameAr(string? kind) => kind switch
    {
        Progress => "تحديث تقدم",
        _ => "تعليق"
    };
}
