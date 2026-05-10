using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class SalesStage
{
    public int StageId { get; set; }

    public string StageName { get; set; } = null!;

    public string? StageNameAr { get; set; }

    public int StageOrder { get; set; }

    public string? StageColor { get; set; }

    public bool IsActive { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public string? LastUpdatedBy { get; set; }

    public DateTime? LastUpdatedAt { get; set; }

    public virtual ICollection<CustomerInteraction> CustomerInteractionStageAfters { get; set; } = new List<CustomerInteraction>();

    public virtual ICollection<CustomerInteraction> CustomerInteractionStageBefores { get; set; } = new List<CustomerInteraction>();

    public virtual ICollection<SalesOpportunity> SalesOpportunities { get; set; } = new List<SalesOpportunity>();
}
