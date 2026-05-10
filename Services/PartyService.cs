using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class PartyService
{
    private readonly db24804Context _db;

    public PartyService(db24804Context db)
    {
        _db = db;
    }

    // ============================
    // قائمة العملاء مع الفلترة والصفحات
    // ============================
    public async Task<PagedResult<PartyListDto>> GetPartiesAsync(PartyFilterDto filter)
    {
        var query = _db.Parties
            .AsNoTracking()
            .Include(p => p.PartyTypeNavigation)
            .Include(p => p.ParentParty)
            .Include(p => p.ContactSource)
            .Include(p => p.Stage)
            .Include(p => p.PartyContacts)
            .AsQueryable();

        // ============================
        // Filters
        // ============================

        // نوع الطرف
        if (filter.PartyType.HasValue)
            query = query.Where(p => p.PartyType == filter.PartyType.Value);

        // بحث نصي
        // بحث نصي (بحث في الداتابيز أولاً ثم تصفية بالعربي)
if (!string.IsNullOrWhiteSpace(filter.SearchText))
{
    var search = filter.SearchText.Trim();

    // بحث أولي في الداتابيز (بالأرقام والإيميل)
    var isNumericOrEmail = search.All(c =>
        char.IsDigit(c) || c == '+' || c == '@' || c == '.' || c == '-');

    if (isNumericOrEmail)
{
    query = query.Where(p =>
        (p.Phone != null && p.Phone.Contains(search)) ||
        (p.Phone2 != null && p.Phone2.Contains(search)) ||
        (p.Email != null && p.Email.Contains(search)) ||
        (p.NationalId != null && p.NationalId.Contains(search)) ||
        (p.TaxNumber != null && p.TaxNumber.Contains(search)) ||
        p.PartyContacts.Any(c =>
            c.IsActive &&
            ((c.Phone != null && c.Phone.Contains(search)) ||
             (c.Email != null && c.Email.Contains(search)))
        )
    );
}
    else
{
    // بحث عربي محسّن + جهات الاتصال
    query = query.Where(p =>
        p.PartyName.Contains(search) ||
        (p.ContactPerson != null && p.ContactPerson.Contains(search)) ||
        (p.City != null && p.City.Contains(search)) ||
        (p.JobTitle != null && p.JobTitle.Contains(search)) ||
        (p.Phone != null && p.Phone.Contains(search)) ||
        (p.Email != null && p.Email.Contains(search)) ||
        p.PartyContacts.Any(c =>
            c.IsActive &&
            (c.ContactName.Contains(search) ||
             (c.Phone != null && c.Phone.Contains(search)) ||
             (c.Email != null && c.Email.Contains(search)))
        )
    );
}
}

        // مرحلة العميل
        if (!string.IsNullOrWhiteSpace(filter.CustomerStage))
            query = query.Where(p => p.CustomerStage == filter.CustomerStage);
        // مرحلة العميل (من جدول SalesStages)
if (filter.StageId.HasValue)
    query = query.Where(p => p.StageId == filter.StageId.Value);

        // مصدر التواصل
        if (filter.ContactSourceId.HasValue)
            query = query.Where(p => p.ContactSourceId == filter.ContactSourceId.Value);

        // المدينة
        if (!string.IsNullOrWhiteSpace(filter.City))
            query = query.Where(p => p.City == filter.City);

        // التقييم
        if (filter.Rating.HasValue)
            query = query.Where(p => p.Rating == filter.Rating.Value);

        // نشط / غير نشط
        if (filter.IsActive.HasValue)
            query = query.Where(p => p.IsActive == filter.IsActive.Value);

        // تابع لمكتب
        if (filter.ParentPartyId.HasValue)
            query = query.Where(p => p.ParentPartyId == filter.ParentPartyId.Value);

        // هل عنده فواتير
        if (filter.HasInvoices.HasValue)
        {
            if (filter.HasInvoices.Value)
                query = query.Where(p => _db.Transactions.Any(t => t.PartyId == p.PartyId));
            else
                query = query.Where(p => !_db.Transactions.Any(t => t.PartyId == p.PartyId));
        }

        // ============================
        // Total Count
        // ============================
        var totalCount = await query.CountAsync();

        // ============================
        // Sorting
        // ============================
        query = filter.SortBy switch
        {
            "PartyName" => filter.SortDescending
                ? query.OrderByDescending(p => p.PartyName)
                : query.OrderBy(p => p.PartyName),
            "CustomerStage" => filter.SortDescending
                ? query.OrderByDescending(p => p.CustomerStage)
                : query.OrderBy(p => p.CustomerStage),
            "City" => filter.SortDescending
                ? query.OrderByDescending(p => p.City)
                : query.OrderBy(p => p.City),
            "Rating" => filter.SortDescending
                ? query.OrderByDescending(p => p.Rating)
                : query.OrderBy(p => p.Rating),
            "LastContactDate" => filter.SortDescending
                ? query.OrderByDescending(p => p.LastContactDate)
                : query.OrderBy(p => p.LastContactDate),
            _ => filter.SortDescending
                ? query.OrderByDescending(p => p.CreatedAt)
                : query.OrderBy(p => p.CreatedAt)
        };

        // ============================
        // Pagination
        // ============================
        var items = await query
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(p => new PartyListDto
            {
                PartyId = p.PartyId,
                PartyName = p.PartyName,
                PartyType = p.PartyType,
                PartyTypeName = p.PartyTypeNavigation.PartyTypeName,
                ContactPerson = p.ContactPerson,
                Phone = p.Phone,
                Email = p.Email,
                City = p.City,
                CustomerStage = p.CustomerStage,
                StageId = p.StageId,
StageName = p.Stage != null ? p.Stage.StageName : null,
StageNameAr = p.Stage != null ? p.Stage.StageNameAr : null,
StageColor = p.Stage != null ? p.Stage.StageColor : null,
                JobTitle = p.JobTitle,
                Rating = p.Rating,
                IsActive = p.IsActive,
                CreatedAt = p.CreatedAt,
                LastContactDate = p.LastContactDate,
                ParentPartyName = p.ParentParty != null ? p.ParentParty.PartyName : null,
                ContactSourceName = p.ContactSource != null ? p.ContactSource.SourceName : null,
                HasInvoices = _db.Transactions.Any(t => t.PartyId == p.PartyId),
                ContactsCount = p.PartyContacts.Count(c => c.IsActive),
                OpeningBalance = p.OpeningBalance,
                BalanceType = p.BalanceType
            })
            .ToListAsync();

        return new PagedResult<PartyListDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize
        };
    }

    // ============================
    // جلب عميل واحد للفورم
    // ============================
    public async Task<PartyFormDto?> GetPartyForEditAsync(int partyId)
    {
        var party = await _db.Parties
            .AsNoTracking()
            .Include(p => p.PartyContacts.Where(c => c.IsActive))
            .FirstOrDefaultAsync(p => p.PartyId == partyId);

        if (party == null) return null;

        return new PartyFormDto
        {
            PartyId = party.PartyId,
            PartyName = party.PartyName,
            PartyType = party.PartyType,
            ContactPerson = party.ContactPerson,
            Phone = party.Phone,
            Phone2 = party.Phone2,
            Email = party.Email,
            Address = party.Address,
            TaxNumber = party.TaxNumber,
            OpeningBalance = party.OpeningBalance,
            BalanceType = party.BalanceType,
            Notes = party.Notes,
            IsActive = party.IsActive,
            ReferralSourceId = party.ReferralSourceId,
            ReferralSourceClient = party.ReferralSourceClient,
            NationalId = party.NationalId,
            FloorNumber = party.FloorNumber,
            DataDone = party.DataDone,
            CustomerStage = party.CustomerStage,
            StageId = party.StageId,
            JobTitle = party.JobTitle,
            ParentPartyId = party.ParentPartyId,
            City = party.City,
            Area = party.Area,
            ContactSourceId = party.ContactSourceId,
            LastContactDate = party.LastContactDate,
            Rating = party.Rating,
            Contacts = party.PartyContacts.Select(c => new PartyContactDto
            {
                ContactId = c.ContactId,
                ContactName = c.ContactName,
                JobTitle = c.JobTitle,
                Phone = c.Phone,
                Email = c.Email,
                Notes = c.Notes,
                IsPrimary = c.IsPrimary,
                IsActive = c.IsActive
            }).ToList()
        };
    }

    // ============================
    // حفظ عميل (إضافة / تعديل)
    // ============================
    public async Task<(bool Success, string Message, int PartyId)> SavePartyAsync(
        PartyFormDto dto, string userName)
    {
        try
        {
            Party party;
            bool isNew = dto.PartyId == 0;

            if (isNew)
{
    // التحقق من صحة رقم الهاتف
    if (!string.IsNullOrWhiteSpace(dto.Phone))
    {
        var phoneValidation = ValidatePhone(dto.Phone);
        if (!phoneValidation.IsValid)
            return (false, phoneValidation.Message, 0);
    }

    // التحقق من تكرار رقم الهاتف
    if (!string.IsNullOrWhiteSpace(dto.Phone))
    {
        var phoneExists = await _db.Parties.AnyAsync(p =>
            p.Phone == dto.Phone || p.Phone2 == dto.Phone);

        if (phoneExists)
            return (false, $"رقم الهاتف {dto.Phone} مسجل بالفعل لعميل آخر", 0);
    }

    // التحقق من تكرار الهاتف الثاني
    if (!string.IsNullOrWhiteSpace(dto.Phone2))
    {
        var phone2Exists = await _db.Parties.AnyAsync(p =>
            p.Phone == dto.Phone2 || p.Phone2 == dto.Phone2);

        if (phone2Exists)
            return (false, $"رقم الهاتف {dto.Phone2} مسجل بالفعل لعميل آخر", 0);
    }

    // التحقق من تكرار الاسم مع نفس الهاتف
    var nameExists = await _db.Parties.AnyAsync(p =>
        p.PartyName == dto.PartyName && p.Phone == dto.Phone);

    if (nameExists)
        return (false, "يوجد عميل بنفس الاسم ورقم الهاتف", 0);

                party = new Party
                {
                    CreatedAt = DateTime.Now,
                    CreatedBy = userName,
                    IsActive = true,
                    CustomerStage = dto.CustomerStage ?? CustomerStages.Lead
                };
                _db.Parties.Add(party);
            }
            else
{
    party = await _db.Parties
        .Include(p => p.PartyContacts)
        .FirstOrDefaultAsync(p => p.PartyId == dto.PartyId)
        ?? throw new Exception("العميل غير موجود");

    // التحقق من صحة رقم الهاتف
    if (!string.IsNullOrWhiteSpace(dto.Phone))
    {
        var phoneValidation = ValidatePhone(dto.Phone);
        if (!phoneValidation.IsValid)
            return (false, phoneValidation.Message, 0);
    }

    // التحقق من تكرار الهاتف (مع استثناء العميل الحالي)
    if (!string.IsNullOrWhiteSpace(dto.Phone))
    {
        var phoneExists = await _db.Parties.AnyAsync(p =>
            p.PartyId != dto.PartyId &&
            (p.Phone == dto.Phone || p.Phone2 == dto.Phone));

        if (phoneExists)
            return (false, $"رقم الهاتف {dto.Phone} مسجل بالفعل لعميل آخر", 0);
    }

    if (!string.IsNullOrWhiteSpace(dto.Phone2))
    {
        var phone2Exists = await _db.Parties.AnyAsync(p =>
            p.PartyId != dto.PartyId &&
            (p.Phone == dto.Phone2 || p.Phone2 == dto.Phone2));

        if (phone2Exists)
            return (false, $"رقم الهاتف {dto.Phone2} مسجل بالفعل لعميل آخر", 0);
    }

    party.LastUpdatedBy = userName;
    party.LastUpdatedAt = DateTime.Now;
}

            // ============================
            // Map DTO to Entity
            // ============================
            party.PartyName = dto.PartyName;
            party.PartyType = dto.PartyType;
            party.ContactPerson = dto.ContactPerson;
            party.Phone = dto.Phone;
            party.Phone2 = dto.Phone2;
            party.Email = dto.Email;
            party.Address = dto.Address;
            party.TaxNumber = dto.TaxNumber;
            party.OpeningBalance = dto.OpeningBalance;
            party.BalanceType = dto.BalanceType;
            party.Notes = dto.Notes;
            party.IsActive = dto.IsActive ?? true;
            party.ReferralSourceId = dto.ReferralSourceId;
            party.ReferralSourceClient = dto.ReferralSourceClient;
            party.NationalId = dto.NationalId;
            party.FloorNumber = dto.FloorNumber;
            party.DataDone = dto.DataDone;
            party.CustomerStage = dto.CustomerStage;
            party.StageId = dto.StageId;
            party.JobTitle = dto.JobTitle;
            party.ParentPartyId = dto.ParentPartyId;
            party.City = dto.City;
            party.Area = dto.Area;
            party.ContactSourceId = dto.ContactSourceId;
            party.LastContactDate = dto.LastContactDate;
            party.Rating = dto.Rating;

            // ============================
            // Contacts
            // ============================
            if (!isNew)
            {
                // حذف جهات اتصال قديمة مش موجودة في الـ DTO
                var existingIds = dto.Contacts
                    .Where(c => c.ContactId > 0)
                    .Select(c => c.ContactId)
                    .ToHashSet();

                var toRemove = party.PartyContacts
                    .Where(c => !existingIds.Contains(c.ContactId))
                    .ToList();

                _db.PartyContacts.RemoveRange(toRemove);
            }

            foreach (var contactDto in dto.Contacts)
            {
                if (contactDto.ContactId > 0 && !isNew)
                {
                    // تحديث جهة اتصال موجودة
                    var existing = party.PartyContacts
                        .FirstOrDefault(c => c.ContactId == contactDto.ContactId);

                    if (existing != null)
                    {
                        existing.ContactName = contactDto.ContactName;
                        existing.JobTitle = contactDto.JobTitle;
                        existing.Phone = contactDto.Phone;
                        existing.Email = contactDto.Email;
                        existing.Notes = contactDto.Notes;
                        existing.IsPrimary = contactDto.IsPrimary;
                        existing.IsActive = contactDto.IsActive;
                    }
                }
                else
                {
                    // جهة اتصال جديدة
                    party.PartyContacts.Add(new PartyContact
                    {
                        ContactName = contactDto.ContactName,
                        JobTitle = contactDto.JobTitle,
                        Phone = contactDto.Phone,
                        Email = contactDto.Email,
                        Notes = contactDto.Notes,
                        IsPrimary = contactDto.IsPrimary,
                        IsActive = contactDto.IsActive,
                        CreatedBy = userName,
                        CreatedAt = DateTime.Now
                    });
                }
            }

            await _db.SaveChangesAsync();

            return (true,
                isNew ? "تم إضافة العميل بنجاح" : "تم تعديل بيانات العميل بنجاح",
                party.PartyId);
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.InnerException?.Message ?? ex.Message}", 0);
        }
    }

    // ============================
    // حذف عميل (Soft Delete)
    // ============================
    public async Task<(bool Success, string Message)> DeletePartyAsync(
        int partyId, string userName)
    {
        try
        {
            var party = await _db.Parties
                .FirstOrDefaultAsync(p => p.PartyId == partyId);

            if (party == null)
                return (false, "العميل غير موجود");

            // تحقق من وجود فواتير
            var hasTransactions = await _db.Transactions
                .AnyAsync(t => t.PartyId == partyId);

            if (hasTransactions)
                return (false, "لا يمكن حذف العميل لوجود فواتير مرتبطة به");

            // Soft Delete
            party.IsActive = false;
            party.LastUpdatedBy = userName;
            party.LastUpdatedAt = DateTime.Now;

            await _db.SaveChangesAsync();
            return (true, "تم حذف العميل بنجاح");
        }
        catch (Exception ex)
        {
            return (false, $"خطأ: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    // ============================
    // Lookups
    // ============================
    public async Task<List<PartyType>> GetPartyTypesAsync()
    {
        return await _db.PartyTypes
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<List<ReferralSource>> GetReferralSourcesAsync()
    {
        return await _db.ReferralSources
            .AsNoTracking()
            .Where(r => r.IsActive == true)
            .ToListAsync();
    }

    public async Task<List<ContactSource>> GetContactSourcesAsync()
    {
        return await _db.ContactSources
            .AsNoTracking()
            .Where(s => s.IsActive)
            .ToListAsync();
    }

    public async Task<List<PartyListDto>> GetParentPartiesAsync()
    {
        return await _db.Parties
            .AsNoTracking()
            .Where(p => p.IsActive == true)
            .Select(p => new PartyListDto
            {
                PartyId = p.PartyId,
                PartyName = p.PartyName
            })
            .OrderBy(p => p.PartyName)
            .ToListAsync();
    }

    public async Task<List<string>> GetCitiesAsync()
    {
        return await _db.Parties
            .AsNoTracking()
            .Where(p => p.City != null && p.City != "")
            .Select(p => p.City!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
    }

    // ============================
    // إحصائيات سريعة
    // ============================
    public async Task<PartyStatsDto> GetStatsAsync(int? partyType = null)
    {
        var query = _db.Parties.AsNoTracking().AsQueryable();

        if (partyType.HasValue)
            query = query.Where(p => p.PartyType == partyType.Value);

        var total = await query.CountAsync();
        var active = await query.CountAsync(p => p.IsActive == true);
        var leads = await query.CountAsync(p => p.StageId == 1 || p.CustomerStage == "Lead");
var clients = await query.CountAsync(p => p.StageId == 3 || p.CustomerStage == "Client");
        var thisMonth = await query.CountAsync(p =>
            p.CreatedAt.HasValue &&
            p.CreatedAt.Value.Month == DateTime.Now.Month &&
            p.CreatedAt.Value.Year == DateTime.Now.Year);

        return new PartyStatsDto
        {
            TotalCount = total,
            ActiveCount = active,
            LeadsCount = leads,
            ClientsCount = clients,
            ThisMonthCount = thisMonth
        };
    }
    // ============================
// تصدير Excel
// ============================
public async Task<List<PartyListDto>> GetAllForExportAsync(PartyFilterDto filter)
{
    // نفس الفلترة بدون pagination
    var exportFilter = new PartyFilterDto
    {
        SearchText = filter.SearchText,
        PartyType = filter.PartyType,
        CustomerStage = filter.CustomerStage,
        ContactSourceId = filter.ContactSourceId,
        City = filter.City,
        Rating = filter.Rating,
        IsActive = filter.IsActive,
        HasInvoices = filter.HasInvoices,
        ParentPartyId = filter.ParentPartyId,
        PageNumber = 1,
        PageSize = 999999,
        SortBy = filter.SortBy,
        SortDescending = filter.SortDescending
    };

    var result = await GetPartiesAsync(exportFilter);
    return result.Items;
}
public async Task<List<SalesStage>> GetSalesStagesAsync()
{
    return await _db.SalesStages
        .AsNoTracking()
        .Where(s => s.IsActive == true)
        .OrderBy(s => s.StageOrder)
        .ToListAsync();
}
// ============================
// Phone Validation
// ============================
private static (bool IsValid, string Message) ValidatePhone(string phone)
{
    if (string.IsNullOrWhiteSpace(phone))
        return (true, "");

    // إزالة المسافات
    var cleaned = phone.Trim().Replace(" ", "").Replace("-", "");

    // التحقق من الطول
    if (cleaned.Length < 10)
        return (false, $"رقم الهاتف {phone} قصير جداً (أقل من 10 أرقام)");

    if (cleaned.Length > 15)
        return (false, $"رقم الهاتف {phone} طويل جداً (أكثر من 15 رقم)");

    // التحقق من أن الرقم يحتوي على أرقام فقط (مع + في البداية)
    var toCheck = cleaned.StartsWith("+") ? cleaned.Substring(1) : cleaned;

    if (!toCheck.All(char.IsDigit))
        return (false, $"رقم الهاتف {phone} يحتوي على حروف غير صالحة");

    // التحقق من أرقام مصر
    if (cleaned.StartsWith("01") && cleaned.Length != 11)
        return (false, $"رقم الهاتف المصري لازم يكون 11 رقم");

    if (cleaned.StartsWith("+201") && cleaned.Length != 13)
        return (false, $"رقم الهاتف المصري الدولي لازم يكون 13 رقم");

    return (true, "");
}
}

// ============================
// DTO الإحصائيات
// ============================
public class PartyStatsDto
{
    public int TotalCount { get; set; }
    public int ActiveCount { get; set; }
    public int LeadsCount { get; set; }
    public int ClientsCount { get; set; }
    public int ThisMonthCount { get; set; }
}

