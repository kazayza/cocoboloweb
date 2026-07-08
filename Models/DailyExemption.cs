using System;

namespace COCOBOLOERPNEW.Models;

// ═══════════════════════════════════════════════════════════
// DailyExemption - الاستثناءات اليومية
// الأعمدة الأصلية: ExemptionID, BioEmployeeID, ExemptionDate,
//                  ReasonCode, Description, ApprovedBy, CreatedDate
// الأعمدة الجديدة: EmployeeID, IsFullDay, Hours, IsDeducted,
//                  Notes, CreatedBy, Status
// ═══════════════════════════════════════════════════════════
public partial class DailyExemption
{
    // ── الأعمدة الأصلية ──
    public int ExemptionId { get; set; }
    public int BioEmployeeId { get; set; }
    public DateTime ExemptionDate { get; set; }
    public string ReasonCode { get; set; } = null!;
    public string? Description { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? CreatedDate { get; set; }

    // ── الأعمدة الجديدة ──
    public int? EmployeeId { get; set; }
    public bool IsFullDay { get; set; } = true;
    public decimal? Hours { get; set; }
    public bool IsDeducted { get; set; } = true;
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public string Status { get; set; } = "Approved";
}
