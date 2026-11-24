using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace COCOBOLOERPNEW.Models;

[Table("SpecAnswers")]
public class SpecAnswer
{
    [Key]
    public int AnswerId { get; set; }
    [MaxLength(20)]
    public string OwnerType { get; set; } = "";
    public int OwnerId { get; set; }
    public int QuestionId { get; set; }
    public int? OptionId { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal? NumberValue { get; set; }
    [MaxLength(300)]
    public string? TextValue { get; set; }
}
