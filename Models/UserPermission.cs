using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class UserPermission
{
    public int UserPermissionId { get; set; }

    public int UserId { get; set; }

    public int PermissionId { get; set; }

    public bool CanView { get; set; }

    public bool CanAdd { get; set; }

    public bool CanEdit { get; set; }

    public bool CanDelete { get; set; }

    public string? AssignedBy { get; set; }

    public DateTime? AssignedDate { get; set; }

    public virtual Permission Permission { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
