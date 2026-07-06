namespace COCOBOLOERPNEW.DTOs;

// ═══════════════════════════════════════════════════════════════
// DTOs لإدارة Leads CRM
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// فلتر البحث في Leads
/// </summary>
public record LeadsCrmFilterDto
{
    public string? SearchText { get; set; }
    public string? SearchTerm { get; set; }
    public string? LeadStatus { get; set; }
    public string? CampaignName { get; set; }
    public string? Platform { get; set; }
    public string? FormLanguage { get; set; }
    public string? ProjectType { get; set; }
    public int? AssignedEmployeeId { get; set; }
    public bool? IsConverted { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public bool LateFollowUpOnly { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

/// <summary>
/// بيانات Lead في القائمة
/// </summary>
public class LeadsCrmListDto
{
    public int LeadId { get; set; }
    public string FullName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Phone2 { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }

    // بيانات الإعلان
    public string? CampaignName { get; set; }
    public string? AdName { get; set; }
    public string? Platform { get; set; }

    // أسئلة الفورم
    public string? ProjectType { get; set; }
    public string? ProjectStage { get; set; }
    public string? Budget { get; set; }
    public string? DecisionMaker { get; set; }
    public string? NextAction { get; set; }
    public string? BestTimeToReach { get; set; }

    // الحالة
    public string? LeadStatus { get; set; }
    public string? FormLanguage { get; set; }
    public int? AssignedEmployeeId { get; set; }
    public string? AssignedEmployeeName { get; set; }
    public string? Feedback { get; set; }
    public bool IsConverted { get; set; }
    public bool IsDuplicate { get; set; }
    public string? SheetTabName { get; set; }

    // التواريخ
    public DateTime? LeadDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastContactDate { get; set; }
}

/// <summary>
/// تفاصيل Lead كاملة
/// </summary>
public class LeadsCrmDetailDto
{
    public int LeadId { get; set; }
    public string FullName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Phone2 { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? Area { get; set; }
    public string? Address { get; set; }

    // بيانات الإعلان
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

    // أسئلة الفورم
    public string? ProjectType { get; set; }
    public string? ProjectStage { get; set; }
    public string? Budget { get; set; }
    public string? DecisionMaker { get; set; }
    public string? NextAction { get; set; }
    public string? BestTimeToReach { get; set; }
    public string? ProjectStageAlt { get; set; }
    public string? BudgetAlt { get; set; }

    // الحالة والتتبع
    public DateTime? LeadDate { get; set; }
    public string? LeadStatus { get; set; }
    public bool IsConverted { get; set; }
    public int? ConvertedPartyId { get; set; }
    public int? ConvertedOpportunityId { get; set; }
    public DateTime? ConvertedDate { get; set; }
    public string? ConvertedBy { get; set; }
    public bool IsDuplicate { get; set; }
    public string? DuplicateOfPhone { get; set; }
    public string? SheetTabName { get; set; }
    public int? SheetRowNumber { get; set; }
    public string? Notes { get; set; }
    public int? AssignedEmployeeId { get; set; }
    public string? AssignedEmployeeName { get; set; }
    public string? Feedback { get; set; }
    public string? RejectedReason { get; set; }
    public DateTime? LastContactDate { get; set; }
    public DateTime? QualifiedDate { get; set; }
    public string? ExtraData { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
/// <summary>
/// نوع تفاعل Lead
/// </summary>
public static class LeadInteractionTypes
{
    public const string Assigned = "إسناد";
    public const string Call = "اتصال";
    public const string WhatsApp = "واتساب";
    public const string Note = "ملاحظة";
    public const string FollowUp = "متابعة";
    public const string Converted = "تحويل";
    public const string Rejected = "رفض";

    public static readonly Dictionary<string, string> All = new()
    {
        { Assigned, "إسناد" },
        { Call, "اتصال" },
        { WhatsApp, "واتساب" },
        { Note, "ملاحظة" },
        { FollowUp, "متابعة" },
        { Converted, "تحويل" },
        { Rejected, "رفض" }
    };
}

/// <summary>
/// عنصر تواصل / حركة على Lead
/// </summary>
public class LeadInteractionDto
{
    public int LeadInteractionId { get; set; }
    public int LeadId { get; set; }
    public int? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }

    public string InteractionType { get; set; } = LeadInteractionTypes.Note;
    public DateTime InteractionDate { get; set; }

    public string? Summary { get; set; }
    public string? Notes { get; set; }

    public string? OldLeadStatus { get; set; }
    public string? NewLeadStatus { get; set; }

    public DateTime? NextFollowUpDate { get; set; }

    public bool IsCompleted { get; set; }
    public int? CompletedByEmployeeId { get; set; }
    public DateTime? CompletedDate { get; set; }

    public bool IsSystemGenerated { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// إضافة تواصل / حركة على Lead
/// </summary>
public class LeadInteractionCreateDto
{
    public int LeadId { get; set; }

    public int? EmployeeId { get; set; }

    public string InteractionType { get; set; } = LeadInteractionTypes.Note;

    public string? Summary { get; set; }

    public string? Notes { get; set; }

    public DateTime? NextFollowUpDate { get; set; }

    /// <summary>
    /// حالة جديدة اختيارية بعد تسجيل التواصل
    /// مثال: تم التواصل / مرفوض
    /// </summary>
    public string? NewLeadStatus { get; set; }

    public string? RejectedReason { get; set; }
}

/// <summary>
/// تحديث Lead
/// </summary>
public class LeadsCrmUpdateDto
{
    public int LeadId { get; set; }
    public string? LeadStatus { get; set; }
    public int? AssignedEmployeeId { get; set; }
    public string? Feedback { get; set; }
    public string? RejectedReason { get; set; }
}

/// <summary>
/// تحويل Lead لعميل
/// </summary>
public class LeadConvertDto
{
    public int LeadId { get; set; }
    public int? EmployeeId { get; set; }
    public int? SourceId { get; set; }
    public int? AdTypeId { get; set; }
    public int? CategoryId { get; set; }
    public int? TaskTypeId { get; set; }
    public decimal ExpectedValue { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// نتيجة تحويل Lead
/// </summary>
public class LeadConvertResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int? PartyId { get; set; }
    public int? OpportunityId { get; set; }
    public int? TaskId { get; set; }
}

/// <summary>
/// إحصائيات Leads — محسّن (استعلام واحد بس)
/// </summary>
public class LeadsCrmStatsDto
{
    public int TotalLeads { get; set; }
    public int NewLeads { get; set; }
    public int AssignedLeads { get; set; }
    public int ContactedLeads { get; set; }
    public int QualifiedLeads { get; set; }
    public int ConvertedLeads { get; set; }
    public int RejectedLeads { get; set; }
    public int LateFollowUpLeads { get; set; }
    public int DuplicateLeads { get; set; }
    public int TodayLeads { get; set; }
    public int ThisWeekLeads { get; set; }
    public int ThisMonthLeads { get; set; }
    public List<LeadsByCampaignDto> ByCampaign { get; set; } = new();
    public List<LeadsByPlatformDto> ByPlatform { get; set; } = new();
}

/// <summary>
/// Leads حسب الحملة
/// </summary>
public class LeadsByCampaignDto
{
    public string CampaignName { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>
/// Leads حسب المنصة
/// </summary>
public class LeadsByPlatformDto
{
    public string Platform { get; set; } = "";
    public int Count { get; set; }
}
/// <summary>
/// إنشاء Lead يدوياً
/// </summary>
public class LeadsCrmCreateDto
{
    public string FullName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Phone2 { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? Area { get; set; }
    public string? Address { get; set; }
    public string? ProjectType { get; set; }
    public string? ProjectStage { get; set; }
    public string? Budget { get; set; }
    public string? DecisionMaker { get; set; }
    public string? NextAction { get; set; }
    public string? BestTimeToReach { get; set; }
    public int? AssignedEmployeeId { get; set; }
    public string? Notes { get; set; }
}
