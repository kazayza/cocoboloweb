using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace COCOBOLOERPNEW.Models;

[Table("SpecLists")]
public class SpecList
{
    [Key]
    public int ListId { get; set; }
    [MaxLength(30)]
    public string ListCode { get; set; } = "";
    [MaxLength(100)]
    public string ListNameAr { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public virtual ICollection<SpecOption> Options { get; set; } = new List<SpecOption>();
}
