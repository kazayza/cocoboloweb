namespace COCOBOLOERPNEW.Models;

/// <summary>
/// موظف تمّت مناداته (@) في تعليق — تُبنى منه تبويبة «إشاراتي» وإشعارات الذكر.
/// مكتوب يدويًا بنفس أسلوب الـScaffold؛ الجدول يُنشأ بـ general-tasks-2-platform.sql.
/// </summary>
public partial class TaskCommentMention
{
    public int Id { get; set; }

    public int CommentId { get; set; }

    public int MentionedEmployeeId { get; set; }

    public virtual TaskComment Comment { get; set; } = null!;

    public virtual Employee MentionedEmployee { get; set; } = null!;
}
