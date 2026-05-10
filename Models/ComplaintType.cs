using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class ComplaintType
{
    public int TypeId { get; set; }

    public string TypeName { get; set; } = null!;

    public string? TypeNameAr { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<Complaint> Complaints { get; set; } = new List<Complaint>();
}
