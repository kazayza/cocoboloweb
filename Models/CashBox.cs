using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class CashBox
{
    public int CashBoxId { get; set; }

    public string CashBoxName { get; set; } = null!;

    public string? Description { get; set; }

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public string? LastUpdatedBy { get; set; }

    public DateTime? LastUpdatedAt { get; set; }

    public virtual ICollection<CashboxTransaction> CashboxTransactions { get; set; } = new List<CashboxTransaction>();
}
