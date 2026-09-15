namespace COCOBOLOERPNEW.DTOs;

// ═══════════════════════════════════════════════════════════
// DTOs — موديول B2B CRM (الشركات وجهات الاتصال والفرص)
// ═══════════════════════════════════════════════════════════

/// <summary>إحصاءات عامة لنطاق B2B الحالي (تُعرض أعلى قائمة الشركات).</summary>
public class B2bCompaniesSummaryDto
{
    public int CompanyCount { get; set; }            // عدد الشركات ضمن النطاق
    public int OpenOpportunitiesCount { get; set; }  // الفرص النشطة
    public decimal PipelineValue { get; set; }       // مجموع قيمها المتوقعة
    public int ContactsCount { get; set; }           // جهات الاتصال النشطة
}

/// <summary>صف شركة في قائمة الشركات.</summary>
public class B2bCompanyRowDto
{
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? City { get; set; }
    public string? Area { get; set; }
    public string? Phone { get; set; }
    public string? Phone2 { get; set; }
    public string? Email { get; set; }
    public string? TaxNumber { get; set; }
    public DateTime? LastContactDate { get; set; }
    public int ContactCount { get; set; }
    public List<string> ContactNames { get; set; } = new(); // أول 3 أسماء للعرض السريع
    public int OpenCount { get; set; }                       // فرص نشطة
    public decimal PipelineValue { get; set; }               // مجموع قيم الفرص النشطة
}

/// <summary>بطاقة شركة كاملة (تُفتح عند النقر على الشركة).</summary>
public class B2bCompanyCardDto
{
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? TypeName { get; set; }
    public string? Phone { get; set; }
    public string? Phone2 { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Area { get; set; }
    public string? TaxNumber { get; set; }
    public string? Notes { get; set; }
    public DateTime? LastContactDate { get; set; }
    public int ActiveOpportunities { get; set; }
    public decimal ActivePipeline { get; set; }
    public List<B2bContactDto> Contacts { get; set; } = new();
    public List<B2bOpportunityMiniDto> Opportunities { get; set; } = new();
}

/// <summary>جهة اتصال داخل الشركة.</summary>
public class B2bContactDto
{
    public int ContactId { get; set; }
    public string ContactName { get; set; } = "";
    public string? JobTitle { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }
    public bool IsPrimary { get; set; }
}

/// <summary>سطر فرصة مصغّر داخل بطاقة الشركة.</summary>
public class B2bOpportunityMiniDto
{
    public int OpportunityId { get; set; }
    public string StageNameAr { get; set; } = "";
    public string? StageColor { get; set; }
    public decimal? ExpectedValue { get; set; }
    public int? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public DateTime? NextFollowUpDate { get; set; }
    public bool IsOverdue { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}

/// <summary>خيار شركة لقائمة الاختيار عند إنشاء فرصة جديدة.</summary>
public class B2bCompanyOptionDto
{
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? City { get; set; }
}

/// <summary>خيار فرصة نشطة لاختيارها في شاشات المتابعة/المهام.</summary>
public class B2bOppSelectDto
{
    public int OpportunityId { get; set; }
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string StageNameAr { get; set; } = "";
    public string? StageColor { get; set; }
}

/// <summary>سطر تواصل في سجل تواصلات B2B.</summary>
public class B2bInteractionRowDto
{
    public int InteractionId { get; set; }
    public DateTime InteractionDate { get; set; }
    public int PartyId { get; set; }
    public int? OpportunityId { get; set; }
    public string ClientName { get; set; } = "";
    public string? Summary { get; set; }
    public string? EmployeeName { get; set; }
    public string? StageAfterNameAr { get; set; }
    public string? StageAfterColor { get; set; }
}

/// <summary>سطر مهمة في قائمة مهام B2B.</summary>
public class B2bTaskRowDto
{
    public int TaskId { get; set; }
    public int? OpportunityId { get; set; }
    public int? PartyId { get; set; }
    public string ClientName { get; set; } = "";
    public string TaskDescription { get; set; } = "";
    public string? TaskTypeNameAr { get; set; }
    public string? AssignedToName { get; set; }
    public DateTime DueDate { get; set; }
    public string Priority { get; set; } = "Medium";
    public string Status { get; set; } = "Pending";
}

/// <summary>إحصاءات سريعة لشاشتي التواصل والمهام.</summary>
public class B2bLogSummaryDto
{
    public int InteractionsTotal { get; set; }
    public int InteractionsThisMonth { get; set; }
    public int OpenTasks { get; set; }
    public int OverdueTasks { get; set; }
    public int TasksThisMonth { get; set; }
}

/// <summary>سطر شركة في تقرير إحصاءات B2B.</summary>
public class B2bReportCompanyDto
{
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? City { get; set; }
    public int WorkingOpps { get; set; }          // فرص قيد العمل (ليست مراحل خروج)
    public decimal PipelineValue { get; set; }
    public int ContactsCount { get; set; }
    public int InteractionsCount { get; set; }
    public int OpenTasksCount { get; set; }
    public DateTime? LastContactDate { get; set; }
}

/// <summary>سطر توزيع المراحل في تقرير B2B.</summary>
public class B2bReportStageDto
{
    public int StageId { get; set; }
    public string StageNameAr { get; set; } = "";
    public string? StageColor { get; set; }
    public int OpportunitiesCount { get; set; }
    public decimal PipelineValue { get; set; }
}

/// <summary>نقطة شهرية في سلسلة آخر ٦ شهور.</summary>
public class B2bReportMonthDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string Label { get; set; } = "";
    public int InteractionsCount { get; set; }
}

/// <summary>بيانات تقرير/إحصاءات B2B.</summary>
public class B2bReportsDto
{
    public int CompaniesCount { get; set; }
    public int WorkingOpps { get; set; }
    public decimal PipelineValue { get; set; }
    public int ClosedLost { get; set; }
    public int ClosedNotInterested { get; set; }
    public int InteractionsTotal { get; set; }
    public int InteractionsThisMonth { get; set; }
    public int ContactsCount { get; set; }
    public int OpenTasks { get; set; }
    public int OverdueTasks { get; set; }
    public List<B2bReportStageDto> Stages { get; set; } = new();
    public List<B2bReportCompanyDto> Companies { get; set; } = new();
    public List<B2bReportMonthDto> Months { get; set; } = new();
}
