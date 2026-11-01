using System;

namespace COCOBOLOERPNEW.Models;

// ═══════════════════════════════════════════════════════════
// EmployeePenalty - جزاءات الموظفين (جدول EmployeePenalties)
// يُنشأ الجدول بسكريبت SQL مستقل (Database-First) — هذا الكيان
// الأدنى للوصول إليه فقط من PayrollService: تُقرأ الجزاءات
// المعتمدة (Status = "Approved") لشهر الراتب المستهدف
// (TargetMonth بصيغة YYYY-MM) وتُعلَّم "Applied" بعد حفظ الراتب.
// PenaltyType: "Days" | "Amount"
// Status: Pending | Approved | Rejected | Cancelled | Applied
// ═══════════════════════════════════════════════════════════
public partial class EmployeePenalty
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public string PenaltyType { get; set; } = null!;

    public decimal? DaysCount { get; set; }

    public decimal? FixedAmount { get; set; }

    public decimal? ComputedAmount { get; set; }

    public string TargetMonth { get; set; } = null!;

    public string Reason { get; set; } = null!;

    public string Status { get; set; } = null!;

    // CreatedBy/ApprovedBy: معرّفات مستخدمين (Users.UserID) — أعمدة INT
    // في مخطط penalties-schema.sql كما تكتبها وحدة الـ API حرفيًا.
    public int CreatedBy { get; set; }

    public int? ApprovedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public int? AppliedPayrollId { get; set; }
}
