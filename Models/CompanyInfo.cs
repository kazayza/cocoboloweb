using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class CompanyInfo
{
    public int CompanyId { get; set; }

    public string CompanyName { get; set; } = null!;

    public byte[]? Logo { get; set; }

    public string? LogoPath { get; set; }

    public string Address { get; set; } = null!;

    public string Phone1 { get; set; } = null!;

    public string? Phone2 { get; set; }

    public string SalesEmail { get; set; } = null!;

    public string InfoEmail { get; set; } = null!;

    public string Website { get; set; } = null!;

    public string? TocDropBox { get; set; }

    public string? CurrentVersion { get; set; }
}
