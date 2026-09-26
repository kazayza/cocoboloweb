using System;
using System.ComponentModel.DataAnnotations;

namespace COCOBOLOERPNEW.Models;

/// <summary>
/// إعدادات مالية عامة للنظام (سطر لكل مفتاح).
/// حالياً: OpeningCapital = رأس المال الافتتاحي لقائمة المركز المالي.
/// الجدول يُنشأ عبر SQL/FinancialSettings.sql — لا أعمدة على جداول موجودة.
/// </summary>
public partial class FinancialSetting
{
    [Key]
    public int FinancialSettingId { get; set; }

    public string SettingKey { get; set; } = null!;

    public decimal? SettingValue { get; set; }

    public string? Notes { get; set; }

    public string? UpdatedBy { get; set; }

    public DateTime UpdatedAt { get; set; }
}
