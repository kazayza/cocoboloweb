using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class TaskType
{
    public int TaskTypeId { get; set; }

    public string TaskTypeName { get; set; } = null!;

    public string? TaskTypeNameAr { get; set; }

    public bool IsActive { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public string? LastUpdatedBy { get; set; }

    public DateTime? LastUpdatedAt { get; set; }

    public virtual ICollection<CrmTask> CrmTasks { get; set; } = new List<CrmTask>();
}
