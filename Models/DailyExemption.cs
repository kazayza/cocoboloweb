using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class DailyExemption
{
    public int ExemptionId { get; set; }

    public int BioEmployeeId { get; set; }

    public DateTime ExemptionDate { get; set; }

    public string ReasonCode { get; set; } = null!;

    public string? Description { get; set; }

    public string ApprovedBy { get; set; } = null!;

    public DateTime? CreatedDate { get; set; }
}
