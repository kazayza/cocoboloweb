using System;

namespace COCOBOLOERPNEW.DTOs;

/// <summary>
/// DTO لعرض بيانات الشركة
/// </summary>
public class CompanyInfoDto
{
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? LogoBase64 { get; set; }
    public string? LogoPath { get; set; }
    public string Address { get; set; } = string.Empty;
    public string Phone1 { get; set; } = string.Empty;
    public string? Phone2 { get; set; }
    public string SalesEmail { get; set; } = string.Empty;
    public string InfoEmail { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string? TocDropBox { get; set; }
    public string? CurrentVersion { get; set; }

    /// <summary>
    /// عدد فروع الشركة
    /// </summary>
    public int LocationsCount { get; set; }
}

/// <summary>
/// DTO لتعديل بيانات الشركة
/// </summary>
public class CompanyInfoUpdateDto
{
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? LogoBase64 { get; set; }
    public string Address { get; set; } = string.Empty;
    public string Phone1 { get; set; } = string.Empty;
    public string? Phone2 { get; set; }
    public string SalesEmail { get; set; } = string.Empty;
    public string InfoEmail { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string? TocDropBox { get; set; }
    public string? CurrentVersion { get; set; }
}

/// <summary>
/// DTO لموقع الشركة (فرع)
/// </summary>
public class CompanyLocationDto
{
    public int LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public int? AllowedRadius { get; set; }
    public bool? IsActive { get; set; }
}

/// <summary>
/// DTO لإضافة/تعديل موقع الشركة
/// </summary>
public class CompanyLocationFormDto
{
    public int? LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public int? AllowedRadius { get; set; }
    public bool IsActive { get; set; } = true;
}
