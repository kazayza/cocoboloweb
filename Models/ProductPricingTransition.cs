using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class ProductPricingTransition
{
    public int TransitionId { get; set; }

    public int FromStatusId { get; set; }

    public int ToStatusId { get; set; }

    public string AllowedRole { get; set; } = null!;

    public bool RequiresApproval { get; set; }

    public bool AutoNotify { get; set; }

    public virtual ProductPricingStatus FromStatus { get; set; } = null!;

    public virtual ProductPricingStatus ToStatus { get; set; } = null!;
}
