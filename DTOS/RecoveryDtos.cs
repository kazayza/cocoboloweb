// ------------------------------------------------------------
// استرداد الفرص الخاسرة (Lost Opportunity Recovery) - DTOs
// فريق خدمة العملاء يتابع الفرص التي وصلت مرحلة خسارة / غير مهتم
// ------------------------------------------------------------
namespace COCOBOLOERPNEW.DTOs;

/// <summary>فرصة خاسرة معروضة في طابور الاسترداد (مع سياقها الكامل).</summary>
public class LostRecoveryItemDto
{
    public int OpportunityId { get; set; }
    public int PartyId { get; set; }
    public string ClientName { get; set; } = "";
    public string? Phone { get; set; }

    // المرحلة الحالية (4 = خسارة / 5 = غير مهتم)
    public int StageId { get; set; }
    public string? StageNameAr { get; set; }
    public string? StageColor { get; set; }
    public bool IsNotInterested { get; set; }

    public decimal? ExpectedValue { get; set; }
    public string? InterestedProduct { get; set; }

    public DateTime? ClosedAt { get; set; }
    public int DaysSinceClosed { get; set; }

    public string? LostReasonNameAr { get; set; }
    public string? LostNotes { get; set; }

    // تقييم مندوب المبيعات وقت الإغلاق (محرك الأولوية)
    public bool IsRecoveryCandidate { get; set; }
    public string? RecoveryNotes { get; set; }

    // المندوب الأصلي + فريق خدمة العملاء المسند إليه
    public string? PreviousEmployeeName { get; set; }
    public int? RecoveryEmployeeId { get; set; }
    public string? RecoveryEmployeeName { get; set; }
    public int? RecoveryTaskId { get; set; }

    public DateTime? LastContactDate { get; set; }
    public DateTime? NextFollowUpDate { get; set; }

    // آخر تواصل مسجل من خدمة العملاء بعد الخسارة (يظهر على البطاقة)
    public DateTime? LastCsDate { get; set; }
    public string? LastCsBy { get; set; }
    public string? LastCsSummary { get; set; }

    // موعد المتابعة القادم فات تاريخه (تنبيه "متأخرة")
    public bool IsFollowUpOverdue { get; set; }
}

/// <summary>صفحة واحدة من طابور الاسترداد (تحميل تدريجي).</summary>
public class LostRecoveryPageDto
{
    public List<LostRecoveryItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 12;
    public bool HasMore { get; set; }
}

/// <summary>فلتر طابور الاسترداد.</summary>
public class LostRecoveryFilterDto
{
    public string? SearchText { get; set; }

    // all | lost | notinterested
    public string Kind { get; set; } = "all";
    public bool? CandidateOnly { get; set; }
    public bool MineOnly { get; set; }
    public decimal? MinValue { get; set; }
    public bool? LateOnly { get; set; }

    // ترتيب الطابور: value | recent | days | followup
    public string SortBy { get; set; } = "value";
}

/// <summary>إحصائيات لوحة الاسترداد.</summary>
public class RecoveryStatsDto
{
    public int LostCount { get; set; }
    public decimal LostValue { get; set; }
    public int UnassignedCount { get; set; }
    public int RevivedThisMonth { get; set; }
    public int UncontactedCount { get; set; }
}

/// <summary>تسجيل محاولة تواصل مع عميل فرصة خاسرة.</summary>
public class RecoveryContactDto
{
    public int OpportunityId { get; set; }
    public int PartyId { get; set; }

    // قناة التواصل: اتصال / واتساب / زيارة / بريد
    public string Channel { get; set; } = "اتصال";

    // نتيجة التواصل: تم التواصل / لم يرد / طلب مهلة / رفض نهائي
    public string Outcome { get; set; } = "تم التواصل";
    public string Summary { get; set; } = "";
    public DateTime? NextFollowUpDate { get; set; }
}

/// <summary>سجل تواصل واحد بعد الخسارة (يظهر في شاشة الاسترداد).</summary>
public class RecoveryHistoryDto
{
    public DateTime Date { get; set; }
    public string Summary { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public string? EmployeeName { get; set; }
    public string? Channel { get; set; }
}

/// <summary>تنفيذ "العميل راجع" - نفس الفرصة أو فرصة جديدة.</summary>
public class RecoveryReviveDto
{
    public int OpportunityId { get; set; }
    public bool SameOpportunity { get; set; } = true;

    // المرحلة الجديدة (مرحلة بيع نشطة غير خسارة)
    public int NewStageId { get; set; }
    public decimal? ExpectedValue { get; set; }
    public DateTime? NextFollowUpDate { get; set; }

    // للفرصة الجديدة فقط
    public string? NewInterestedProduct { get; set; }

    public string? Notes { get; set; }
}

/// <summary>
/// حالة طابور شاشة الاسترداد المحفوظة (تثبيت الفلاتر ومكان التحميل عند التنقل).
/// تُخزَّن في RecoveryService (Scoped) فتبقى حيّة طول الجلسة.
/// </summary>
public class RecoveryQueueState
{
    public bool Saved { get; set; }
    public string? SearchText { get; set; }
    public string Kind { get; set; } = "all";
    public bool CandidateOnly { get; set; }
    public bool MineOnly { get; set; }
    public bool LateOnly { get; set; }
    public string SortBy { get; set; } = "value";
    public int LoadedCount { get; set; }
    public int TotalCount { get; set; }
}


/// <summary>موظف خدمة عملاء (لاختيار في فلاتر التقرير).</summary>
public class RecoveryEmployeeOptionDto
{
    public int EmployeeId { get; set; }
    public string FullName { get; set; } = "";
}

/// <summary>فلاتر تقرير الاسترداد.</summary>
public class RecoveryReportFilterDto
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int? CsEmployeeId { get; set; }

    // all | uncontacted | contacting | rejected | revived
    public string Status { get; set; } = "all";

    public string? SearchText { get; set; }
}

/// <summary>سطر واحد في تقرير الاسترداد.</summary>
public class RecoveryReportRowDto
{
    public int OpportunityId { get; set; }
    public int PartyId { get; set; }
    public string ClientName { get; set; } = "";
    public string? Phone { get; set; }
    public decimal? ExpectedValue { get; set; }
    public string? LostReasonName { get; set; }
    public string? StageNameAr { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? CsEmployeeName { get; set; }
    public int ContactCount { get; set; }
    public DateTime? LastCsDate { get; set; }
    public string? LastCsSummary { get; set; }
    public DateTime? RevivedDate { get; set; }

    // قيد المتابعة | لم يُتواصل | رفض نهائي | مُسترد
    public string StatusAr { get; set; } = "";

    // ── حقول مساعدة داخلية للفلترة (لا تُعرض في الجدول/التصدير) ──
    public DateTime? AnchorDate { get; set; }   // تاريخ "حدث الحالة" (أساس فلترة الفترة)
    public int? CsEmpId { get; set; }           // موظف خدمة العملاء المسؤول (أساس فلتر الموظف)
}

/// <summary>نتيجة تقرير الاسترداد (سطور مرقّمة + ملخص).</summary>
public class RecoveryReportResultDto
{
    public List<RecoveryReportRowDto> Rows { get; set; } = new();
    public int RowCount { get; set; }
    public decimal TotalValue { get; set; }
    public int UncontactedCount { get; set; }
    public int ContactedCount { get; set; }
    public int RejectedCount { get; set; }
    public int RevivedCount { get; set; }

    // ترقيم الصفحات — يحدّ إرسال الصفوف عبر WebSocket (يعالج ثقل البحث)
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public bool HasMore { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(RowCount / (double)PageSize) : 0;
}
