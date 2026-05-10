using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class PartyType
{
    public int PartyTypeId { get; set; }

    public string PartyTypeName { get; set; } = null!;

    public virtual ICollection<Party> Parties { get; set; } = new List<Party>();
}
