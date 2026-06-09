using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.DTOs;

// ═══ فلتر البحث ═══
public class LeadsCrmFilterDto
{
    public string? SearchTerm { get; set; }
    public string? SearchText { get; set; }
    public string? LeadStatus { get; set; }
    public string? CampaignName { get; set; }
    public string? Platform { get; set; }
    public string? FormLanguage { get; set; }
    public int? AssignedEmployeeId { get; set; }
    public bool? IsConverted { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int PageNumber { get; set; } = 1;       // ← اتغير من Page لـ PageNumber
    public int PageSize { get; set; } = 25;
}

// ═══ عرض Lead في الجدول ═══
public class LeadsCrmListDto
{
    public int LeadId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Phone2 { get; set; }
    public string? Email { get; set; }              // ← أضف
    public string? City { get; set; }
    public string? Address { get; set; }            // ← أضف
    public string? CampaignName { get; set; }
    public string? AdName { get; set; }
    public string? Platform { get; set; }
    public string? ProjectType { get; set; }
    public string? ProjectStage { get; set; }       // ← أضف
    public string? Budget { get; set; }
    public string? DecisionMaker { get; set; }      // ← أضف
    public string? NextAction { get; set; }         // ← أضف
    public string? BestTimeToReach { get; set; }    // ← أضف
    public string LeadStatus { get; set; } = "جديد";  // ← اتغير من New
    public string? FormLanguage { get; set; }
    public bool IsConverted { get; set; }
    public bool IsDuplicate { get; set; }
    public int? AssignedEmployeeId { get; set; }
    public string? AssignedEmployeeName { get; set; }
    public string? Feedback { get; set; }
    public string? SheetTabName { get; set; }
    public DateTime? LeadDate { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ═══ تفاصيل Lead كاملة ═══
public class LeadsCrmDetailDto
{
    public int LeadId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Phone2 { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? Area { get; set; }
    public string? Address { get; set; }
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
    public string? ProjectType { get; set; }
    public string? ProjectStage { get; set; }
    public string? Budget { get; set; }
    public string? DecisionMaker { get; set; }
    public string? NextAction { get; set; }
    public string? BestTimeToReach { get; set; }
    public string? ProjectStageAlt { get; set; }
    public string? BudgetAlt { get; set; }
    public DateTime? LeadDate { get; set; }
    public string LeadStatus { get; set; } = "جديد";  // ← اتغير من New
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
    public string? Feedback { get; set; }
    public int? AssignedEmployeeId { get; set; }
    public string? AssignedEmployeeName { get; set; }
    public DateTime? LastContactDate { get; set; }
    public DateTime? QualifiedDate { get; set; }
    public string? RejectedReason { get; set; }
    public string? ExtraData { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "MetaIntegration";
}

// ═══ تحديث Lead ═══
public class LeadsCrmUpdateDto
{
    public int LeadId { get; set; }
    public string? LeadStatus { get; set; }
    public string? Feedback { get; set; }
    public int? AssignedEmployeeId { get; set; }
    public string? RejectedReason { get; set; }
}

// ═══ تحويل Lead لعميل ═══
public class LeadConvertDto
{
    public int LeadId { get; set; }               // ← الرازور هيحط الـ ID هنا
    public int? EmployeeId { get; set; }
    public int? SourceId { get; set; }
    public int? AdTypeId { get; set; }
    public int? CategoryId { get; set; }
    public int? TaskTypeId { get; set; }
    public decimal ExpectedValue { get; set; }    // ← أضف
    public string? Notes { get; set; }            // ← أضف
}

// ═══ إحصائيات Leads ═══
public class LeadsCrmStatsDto
{
    public int TotalLeads { get; set; }
    public int NewLeads { get; set; }
    public int ContactedLeads { get; set; }
    public int QualifiedLeads { get; set; }
    public int ConvertedLeads { get; set; }
    public int RejectedLeads { get; set; }
    public int DuplicateLeads { get; set; }
    public int TodayLeads { get; set; }
    public int ThisWeekLeads { get; set; }
    public int ThisMonthLeads { get; set; }
    public List<LeadsByCampaignDto> ByCampaign { get; set; } = new();
    public List<LeadsByPlatformDto> ByPlatform { get; set; } = new();
}

public class LeadsByCampaignDto
{
    public string CampaignName { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class LeadsByPlatformDto
{
    public string Platform { get; set; } = string.Empty;
    public int Count { get; set; }
}