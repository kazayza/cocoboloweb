using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class ProductGroup
{
    public int ProductGroupId { get; set; }

    public string GroupName { get; set; } = null!;

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<Product> Products { get; set; } = new List<Product>();
}
