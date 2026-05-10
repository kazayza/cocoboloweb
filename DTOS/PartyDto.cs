namespace COCOBOLOERPNEW.DTOs;

// ============================
// DTO للعرض في القائمة
// ============================
public class PartyListDto
{
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public int PartyType { get; set; }
    public string PartyTypeName { get; set; } = "";
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? CustomerStage { get; set; }
    public int? StageId { get; set; }
public string? StageName { get; set; }
public string? StageNameAr { get; set; }
public string? StageColor { get; set; }

    public string? JobTitle { get; set; }
    public int? Rating { get; set; }
    public bool? IsActive { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? LastContactDate { get; set; }
    public string? ParentPartyName { get; set; }
    public string? ContactSourceName { get; set; }
    public bool HasInvoices { get; set; }
    public int ContactsCount { get; set; }
    public decimal? OpeningBalance { get; set; }
    public string? BalanceType { get; set; }
}

// ============================
// DTO للفورم (إضافة / تعديل)
// ============================
public class PartyFormDto
{
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public int PartyType { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Phone2 { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? TaxNumber { get; set; }
    public decimal? OpeningBalance { get; set; }
    public string? BalanceType { get; set; }
    public string? Notes { get; set; }
    public bool? IsActive { get; set; }
    public int? ReferralSourceId { get; set; }
    public int? ReferralSourceClient { get; set; }
    public string? NationalId { get; set; }
    public string? FloorNumber { get; set; }
    public string? DataDone { get; set; }

    // New Fields
    public string? CustomerStage { get; set; }
    public int? StageId { get; set; }
    public string? JobTitle { get; set; }
    public int? ParentPartyId { get; set; }
    public string? City { get; set; }
    public string? Area { get; set; }
    public int? ContactSourceId { get; set; }
    public DateTime? LastContactDate { get; set; }
    public int? Rating { get; set; }

    // جهات الاتصال
    public List<PartyContactDto> Contacts { get; set; } = new();
}

// ============================
// DTO لجهة الاتصال
// ============================
public class PartyContactDto
{
    public int ContactId { get; set; }
    public string ContactName { get; set; } = "";
    public string? JobTitle { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
}

// ============================
// فلتر البحث
// ============================
public class PartyFilterDto
{
    public string? SearchText { get; set; }
    public int? PartyType { get; set; }
    public string? CustomerStage { get; set; }
    public int? StageId { get; set; }
    public int? ContactSourceId { get; set; }
    public string? City { get; set; }
    public int? Rating { get; set; }
    public bool? IsActive { get; set; }
    public bool? HasInvoices { get; set; }
    public int? ParentPartyId { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string SortBy { get; set; } = "CreatedAt";
    public bool SortDescending { get; set; } = true;
}

// ============================
// نتيجة مع Pagination
// ============================
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => PageNumber > 1;
    public bool HasNext => PageNumber < TotalPages;
}

// ============================
// ثوابت
// ============================
public static class CustomerStages
{
    public const string Lead = "Lead";
    public const string Prospect = "Prospect";
    public const string Qualified = "Qualified";
    public const string Client = "Client";
    public const string Lost = "Lost";

    public static readonly Dictionary<string, string> All = new()
    {
        { Lead, "عميل محتمل" },
        { Prospect, "تم التواصل" },
        { Qualified, "مؤهل للشراء" },
        { Client, "عميل فعلي" },
        { Lost, "خسرناه" }
    };
}