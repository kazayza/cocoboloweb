using System.ComponentModel.DataAnnotations;

namespace COCOBOLOERPNEW.DTOs;

/// <summary>
/// تغيير الباسورد بواسطة المستخدم نفسه
/// </summary>
public class ChangePasswordDto
{
    [Required(ErrorMessage = "الكلمة السرية الحالية مطلوبة")]
    public string CurrentPassword { get; set; } = "";

    [Required(ErrorMessage = "الكلمة السرية الجديدة مطلوبة")]
    [MinLength(8, ErrorMessage = "الكلمة السرية يجب أن تكون 8 أحرف على الأقل")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$",
        ErrorMessage = "الكلمة السرية يجب أن تحتوي على حرف واحد ورقم واحد على الأقل")]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "تأكيد الكلمة السرية مطلوب")]
    [Compare(nameof(NewPassword), ErrorMessage = "الكلمات السرية غير متطابقة")]
    public string ConfirmPassword { get; set; } = "";
}

/// <summary>
/// إعادة تعيين الباسورد بواسطة الأدمن (بدون current password)
/// </summary>
public class ResetUserPasswordDto
{
    [Required]
    public int UserId { get; set; }

    [Required(ErrorMessage = "الكلمة السرية الجديدة مطلوبة")]
    [MinLength(8, ErrorMessage = "الكلمة السرية يجب أن تكون 8 أحرف على الأقل")]
    public string NewPassword { get; set; } = "";

    [Required]
    [Compare(nameof(NewPassword), ErrorMessage = "الكلمات السرية غير متطابقة")]
    public string ConfirmPassword { get; set; } = "";

    /// <summary>إجبار المستخدم على تغيير الباسورد عند أول دخول</summary>
    public bool ForceChangeOnNextLogin { get; set; }
}

/// <summary>
/// بيانات المستخدم المختصرة (لاختياره في صفحة الأدمن)
/// </summary>
public class UserLookupDto
{
    public int    UserId    { get; set; }
    public string Username  { get; set; } = "";
    public string FullName  { get; set; } = "";
    public string? Email    { get; set; }
    public string? Role     { get; set; }
    public bool   IsActive  { get; set; }
    public DateTime? LastLogin { get; set; }
}
