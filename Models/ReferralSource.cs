using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class ReferralSource
{
    public int ReferralSourceId { get; set; }

    public string SourceName { get; set; } = null!;

    public bool? IsActive { get; set; }

    public virtual ICollection<Party> Parties { get; set; } = new List<Party>();
}
