using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class User
{
    public int UserId { get; set; }

    public string Username { get; set; } = null!;

    public string Password { get; set; } = null!;
    public string? HashedPassword { get; set; }

    public string FullName { get; set; } = null!;

    public string? Email { get; set; }

    public bool? IsActive { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? LastLogin { get; set; }

    public int? EmployeeId { get; set; }

    public string? Fcmtoken { get; set; }

    public string? Role { get; set; }

    public virtual ICollection<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();
}
