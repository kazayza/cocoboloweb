using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace COCOBOLOERPNEW.Models;

[Table("LeadsCRM")]
public class LeadsCrm
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int LeadId { get; set; }

    // ═══ بيانات العميل ═══
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Phone2 { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? Area { get; set; }
    public string? Address { get; set; }

    // ═══ بيانات الحملة الإعلانية ═══
    public string? MetaLeadId { get; set; }
    public string? CampaignId { get; set; }
    public string? CampaignName { get; set; }
    public string? AdId { get; set; }
    public string? AdName { get; set; }
    public string? AdsetId { get; set; }
    public string? AdSetName { get; set; }
    public string? FormId { get; set; }
    public string? FormName { get; set; }
    public string? Platform { get; set; }
    public bool? IsOrganic { get; set; }
    public string? InboxUrl { get; set; }
    public string? FormLanguage { get; set; }

    // ═══ أسئلة الفورم الأساسية ═══
    public string? ProjectType { get; set; }
    public string? ProjectStage { get; set; }
    public string? Budget { get; set; }
    public string? DecisionMaker { get; set; }
    public string? NextAction { get; set; }
    public string? BestTimeToReach { get; set; }

    // ═══ أسئلة بديلة (فورم Segments / 500K) ═══
    public string? ProjectStageAlt { get; set; }
    public string? BudgetAlt { get; set; }

    // ═══ تاريخ الـ Lead ═══
    public DateTime? LeadDate { get; set; }

    // ═══ حالة الـ Lead ═══
    public string LeadStatus { get; set; } = "جديد";

    // ═══ تتبع التحويل للـ CRM ═══
    public bool IsConverted { get; set; } = false;
    public int? ConvertedPartyId { get; set; }
    public int? ConvertedOpportunityId { get; set; }
    public DateTime? ConvertedDate { get; set; }
    public string? ConvertedBy { get; set; }

    // ═══ تتبع التكرار ═══
    public bool IsDuplicate { get; set; } = false;
    public string? DuplicateOfPhone { get; set; }

    // ═══ معلومات الشيت ═══
    public string? SheetTabName { get; set; }
    public int? SheetRowNumber { get; set; }

    // ═══ ملاحظات ═══
    public string? Notes { get; set; }

    // ═══ فيدباك ومتابعة ═══
    public string? Feedback { get; set; }
    public int? AssignedEmployeeId { get; set; }
    public DateTime? LastContactDate { get; set; }
    public DateTime? QualifiedDate { get; set; }
    public string? RejectedReason { get; set; }

    // ═══ بيانات إضافية مرنة (JSON) - لأي أعمدة جديدة مستقبلاً ═══
    public string? ExtraData { get; set; }

    // ═══ Audit ═══
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string CreatedBy { get; set; } = "MetaIntegration";
}