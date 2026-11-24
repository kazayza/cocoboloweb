using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace COCOBOLOERPNEW.Models;

[Table("SpecQuestions")]
public class SpecQuestion
{
    [Key]
    public int QuestionId { get; set; }
    public int ProductGroupId { get; set; }
    [MaxLength(100)]
    public string QuestionNameAr { get; set; } = "";
    [MaxLength(20)]
    public string DataType { get; set; } = "Text";
    public int? ListId { get; set; }
    [MaxLength(20)]
    public string? UnitName { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
