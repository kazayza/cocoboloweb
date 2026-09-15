namespace COCOBOLOERPNEW.DTOs;

public class PartySearchDto
{
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? Phone { get; set; }
    public string? Phone2 { get; set; }
    public string? LastStageName { get; set; }
    public DateTime? LastContactDate { get; set; }
    public string? FullName { get; set; }

    /// <summary>🏢 هل العميل شركة B2B؟ — لحماية الفتح/الاختيار حسب الصلاحية.</summary>
    public bool IsB2B { get; set; }
}
