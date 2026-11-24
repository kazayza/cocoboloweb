using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace COCOBOLOERPNEW.Models;

[Table("SpecOptions")]
public class SpecOption
{
    [Key]
    public int OptionId { get; set; }
    public int ListId { get; set; }
    [MaxLength(100)]
    public string OptionNameAr { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public virtual SpecList List { get; set; } = null!;
}
