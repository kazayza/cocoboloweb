using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class ProductPricingStatus
{
    public int PricingStatusId { get; set; }

    public string StatusName { get; set; } = null!;

    public string? Description { get; set; }

    public virtual ICollection<ProductPricingTransition> ProductPricingTransitionFromStatuses { get; set; } = new List<ProductPricingTransition>();

    public virtual ICollection<ProductPricingTransition> ProductPricingTransitionToStatuses { get; set; } = new List<ProductPricingTransition>();

    public virtual ICollection<Product> Products { get; set; } = new List<Product>();
}
