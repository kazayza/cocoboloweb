namespace COCOBOLOERPNEW.DTOs;

public class OpportunityWorkflowDto
{
    // نوع العميل
    public bool IsNewClient { get; set; } = false;

    // عميل موجود
    public int? ExistingPartyId { get; set; }
    public string? ExistingPartyPhone { get; set; }

    // عميل جديد
    public string? NewClientName { get; set; }
    public string? NewPhone { get; set; }
    public string? NewAddress { get; set; }

    /// <summary>
    /// 🏢 نمط «شركة B2B جديدة» — إنشاء عميل شركة معلَّم B2B من شاشة CRM الموحدة.
    /// يظهر فقط لمن له صلاحية إدارة B2B (موظف B2B المختص / المدراء).
    /// يستخدم نفس حقول NewClientName/NewPhone/NewAddress أعلاه كاسم الشركة/هاتفها/عنوانها.
    /// </summary>
    public bool IsCompanyB2B { get; set; } = false;

    // الفرصة
    public int? OpportunityId { get; set; }
    public int? EmployeeId { get; set; }
    public int? SourceId { get; set; }
    public int? AdTypeId { get; set; }
    public int? StageId { get; set; }
    public int? StatusId { get; set; }
    public int? CategoryId { get; set; }
    public string? InterestedProduct { get; set; }
    public decimal? ExpectedValue { get; set; }
    public DateTime? FirstContactDate { get; set; } = DateTime.Today;
    public DateTime? NextFollowUpDate { get; set; } = DateTime.Today.AddDays(1);
    public string? Location { get; set; }

    // الخسارة
    public int? LostReasonId { get; set; }
    public string? LostNotes { get; set; }

    // الملاحظات
    public string? Summary { get; set; }
    public string? Guidance { get; set; }

    // المهمة
    public int? TaskTypeId { get; set; }
    public string? Priority { get; set; } = "Medium";

    // داخلي
    public int StageBeforeId { get; set; }
    public bool HasActiveOpportunity { get; set; }
}