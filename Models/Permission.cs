using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class Permission
{
    public int PermissionId { get; set; }

    public string PermissionName { get; set; } = null!;

    public string? Description { get; set; }

    public string? Category { get; set; }

    public string? FormName { get; set; }

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();
}
