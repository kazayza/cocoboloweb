using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class CompanyLocation
{
    public int LocationId { get; set; }

    public string LocationName { get; set; } = null!;

    public decimal Latitude { get; set; }

    public decimal Longitude { get; set; }

    public int? AllowedRadius { get; set; }

    public bool? IsActive { get; set; }
}
