using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class AuditLog
{
    public long AuditId { get; set; }

    public string TableName { get; set; } = null!;

    public string? ActionType { get; set; }

    public string? PrimaryKeyValue { get; set; }

    public string? OldData { get; set; }

    public string? NewData { get; set; }

    public DateTime? ActionDate { get; set; }

    public string? LoginName { get; set; }

    public string? AppName { get; set; }

    public string? HostName { get; set; }

    public string? AccessUserName { get; set; }
}
