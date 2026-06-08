namespace COCOBOLOERPNEW.DTOs;

// ═══════════════════════════════════════════════════════════════
// DTO لاستقبال بيانات الـ Leads من Google Sheets / Meta Ads
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// طلب استيراد عميل محتمل من إعلان Meta عبر Google Sheets
/// </summary>
public class LeadImportRequest
{
    public string ApiKey { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Phone2 { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Area { get; set; }

    // بيانات الإعلان
    public string? LeadId { get; set; }
    public string? CampaignName { get; set; }
    public string? AdName { get; set; }
    public string? AdSet { get; set; }
    public string? FormName { get; set; }
    public string? FormId { get; set; }
    public string? Platform { get; set; }

    // أسئلة فورم Meta
    public string? ProjectType { get; set; }
    public string? ProjectStage { get; set; }
    public string? Budget { get; set; }
    public string? DecisionMaker { get; set; }
    public string? NextAction { get; set; }
    public string? BestTimeToReach { get; set; }

    public DateTime? LeadDate { get; set; }
    public string? LeadStatus { get; set; }

    // ربط بالـ CRM Lookup IDs
    public int? ContactSourceId { get; set; }
    public int? AdTypeId { get; set; }
    public int? CategoryId { get; set; }

    // معرف التاب في الجوجل شيت
    public string? SheetTabName { get; set; }
    public int? SheetRowNumber { get; set; }
}

public class LeadImportResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public bool IsDuplicate { get; set; }
    public int? PartyId { get; set; }
    public int? OpportunityId { get; set; }
    public string? SheetTabName { get; set; }
    public int? SheetRowNumber { get; set; }
}

public class BatchLeadImportRequest
{
    public string ApiKey { get; set; } = "";
    public List<LeadImportRequest> Leads { get; set; } = new();
}

public class BatchLeadImportResult
{
    public int TotalReceived { get; set; }
    public int TotalCreated { get; set; }
    public int TotalDuplicates { get; set; }
    public int TotalFailed { get; set; }
    public List<LeadImportResult> Results { get; set; } = new();
}

// ═══════════════════════════════════════════════════════════════
// ترجمة قيم أسئلة Meta من الإنجليزي للعربي
// ═══════════════════════════════════════════════════════════════

public static class MetaFormTranslations
{
    public static readonly Dictionary<string, string> ProjectTypeMap = new()
    {
        { "full_villa", "فيلا كاملة" },
        { "apartment", "شقة" },
        { "duplex_or_penthouse", "دوبلكس أو بنتهاوس" },
        { "chalet_or_coastal_unit", "شاليه أو وحدة ساحلية" },
        { "فيلا_كاملة_", "فيلا كاملة" },
        { "دوبلكس_أو_بنتهاوس", "دوبلكس أو بنتهاوس" },
        { "شقة", "شقة" },
        { "شاليه_أو_وحدة_ساحلية", "شاليه أو وحدة ساحلية" }
    };

    public static readonly Dictionary<string, string> ProjectStageMap = new()
    {
        { "currently_under_finishing_or_construction", "تحت التشطيب" },
        { "finishing_completed_and_ready_for_furnishing", "خلص التشطيب وجاهز للفرش" },
        { "تحت_التشطيب", "تحت التشطيب" },
        { "خلص_التشطيب_وجاهز_للفرش", "خلص التشطيب وجاهز للفرش" }
    };

    public static readonly Dictionary<string, string> BudgetMap = new()
    {
        { "egp_500k_–_1_million", "من 500 ألف لـ 1 مليون" },
        { "من_٣٠٠_لـ_٥٠٠_ألف_جنيه", "من 300 لـ 500 ألف جنيه" },
        { "أكتر_من_مليون_جنيه", "أكتر من مليون جنيه" }
    };

    public static readonly Dictionary<string, string> DecisionMakerMap = new()
    {
        { "myself", "أنا لوحدي" },
        { "with_interior_designer", "معانا مهندس ديكور" },
        { "أنا_لوحدي", "أنا لوحدي" },
        { "معانا_مهندس_ديكور", "معانا مهندس ديكور" }
    };

    public static readonly Dictionary<string, string> NextActionMap = new()
    {
        { "book_a_free_design_consultation", "جلسة استشارة مجانية" },
        { "initial_price_quote", "عرض سعر مبدئي" },
        { "عرض_سعر_مبدئي", "عرض سعر مبدئي" },
        { "_جلسة_استشارة_مجانية", "جلسة استشارة مجانية" }
    };

    public static readonly Dictionary<string, string> BestTimeMap = new()
    {
        { "early_afternoon_(12_pm_–_4_pm)", "بعد الظهر (12م – 4م)" },
        { "morning_(9_am_–_12_pm)", "الصبح (9ص – 12م)" },
        { "الصبح_(٩ص_–_١٢م)", "الصبح (9ص – 12م)" },
        { "بعد_الظهر_(١٢م_–_٤م)", "بعد الظهر (12م – 4م)" }
    };

    public static string Translate(Dictionary<string, string> map, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (map.TryGetValue(value, out var translated)) return translated;
        return value.Replace("_", " ").Trim();
    }
}