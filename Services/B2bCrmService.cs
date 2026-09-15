using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// خدمات نطاق B2B CRM (شاشات /crm/b2b/*):
///  1) تحديد «عميل B2B» = شركة. المنطق (قابل للضبط من مكان واحد هنا):
///     - نوع العميل (PartyTypeName) يطابق CompanyTypeTokens، أو
///     - العميل له مستخدم بوابة B2B فعّال (B2BPortalUser).
///  2) نطاق المشاهدة: مدير → كل الشركات | مختص (دور B2B) → الشركات اللي ليه فيها فرص فقط.
///  3) قائمة الشركات + البطاقة (جهات الاتصال والفرص) + إدارة جهات الاتصال.
///  4) ترجمة اسم مستخدم الحالي → EmployeeId (لحصر «شغله فقط» للمختص).
/// يستخدم DbContextFactory (مثيل لكل عملية) — آمن مع Blazor InteractiveServer.
/// ⚠️ حماية النطاق تُفرض في هذه الخدمة بتمرير scopeIds المحسوبة لكل مستخدم.
/// </summary>
public class B2bCrmService
{
    private readonly IDbContextFactory<db24804Context> _dbFactory;

    /// <summary>
    /// ⚙️ كلمات مفتاحية لأسماء أنواع العملاء التي تُعتبر «شركة B2B».
    /// تُطابق بـ Contains على PartyTypeName (مثال: "شركة" تغطي "شركة" و"عميل شركة"...).
    /// لو الأنواع عندك بأسماء مختلفة أضفها هنا، أو استعلم:
    ///     SELECT PartyTypeId, PartyTypeName FROM PartyTypes
    /// </summary>
    public static readonly string[] CompanyTypeTokens = { "شركة", "مؤسسة" };

    /// <summary>هل نضيف عملاء البوابة B2B (لديهم B2BPortalUser نشط) تلقائياً كشركات؟</summary>
    public const bool IncludePortalCompanies = true;

    public B2bCrmService(IDbContextFactory<db24804Context> dbFactory)
        => _dbFactory = dbFactory;

    // ═══════════════════════════════════════════════════════════
    //  تحديد الشركات B2B (النطاق)
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// كل معرّفات العملاء (Parties) الذين يُعدّون «شركة B2B» (دون قيد الموظف).
    /// </summary>
    public async Task<List<int>> ResolveCompanyPartyIdsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await ResolveCompanyPartyIdsCoreAsync(db);
    }

    /// <summary>
    /// معرّفات الشركات ضمن نطاق المستخدم الحالي:
    ///  - employeeId = null  → كل شركات B2B (للمدير).
    ///  - employeeId = X     → الشركات اللي فيها فرص مُسندة للموظف X (للمختص/تصفية مدير).
    /// </summary>
    public async Task<List<int>> ResolveScopedCompanyIdsAsync(int? employeeId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var all = await ResolveCompanyPartyIdsCoreAsync(db);
        if (!employeeId.HasValue || all.Count == 0) return all;

        return await db.SalesOpportunities.AsNoTracking()
            .Where(o => o.EmployeeId == employeeId.Value && all.Contains(o.PartyId))
            .Select(o => o.PartyId)
            .Distinct()
            .ToListAsync();
    }

    private static async Task<List<int>> ResolveCompanyPartyIdsCoreAsync(db24804Context db)
    {
        var companyIds = new HashSet<int>();

        var tokens = CompanyTypeTokens;
        if (tokens.Length > 0)
        {
            var companyTypes = await db.PartyTypes.AsNoTracking()
                .Where(t => tokens.Any(tok => t.PartyTypeName.Contains(tok)))
                .Select(t => t.PartyTypeId)   // ⚠️ مفتاح الجدول PartyTypeId (وليس PartyType)
                .ToListAsync();

            if (companyTypes.Count > 0)
            {
                var ids = await db.Parties.AsNoTracking()
                    .Where(p => p.IsActive != false && companyTypes.Contains(p.PartyType))
                    .Select(p => p.PartyId)
                    .ToListAsync();
                companyIds.UnionWith(ids);
            }
        }

        if (IncludePortalCompanies)
        {
            var portalIds = await db.B2BPortalUsers.AsNoTracking()
                .Where(u => u.IsActive)
                .Select(u => u.PartyId)
                .Distinct()
                .ToListAsync();
            companyIds.UnionWith(portalIds);
        }

        // 🏢 أي عميل مُعلَّم IsB2B يُعتبر شركة B2B (مصدر الحقيقة الجديد بعد الدمج)
        //    — يضمن أن الشركات الجديدة المُنشأة من شاشة CRM الموحدة تظهر هنا أيضاً
        var flaggedIds = await db.Parties.AsNoTracking()
            .Where(p => p.IsB2B && p.IsActive != false)
            .Select(p => p.PartyId)
            .ToListAsync();
        companyIds.UnionWith(flaggedIds);

        return companyIds.ToList();
    }

    // ═══════════════════════════════════════════════════════════
    //  قائمة الشركات + الإحصاءات
    // ═══════════════════════════════════════════════════════════

    public async Task<B2bCompaniesSummaryDto> GetCompaniesSummaryAsync(IReadOnlyCollection<int> scopeIds)
    {
        if (scopeIds == null || scopeIds.Count == 0)
            return new B2bCompaniesSummaryDto();

        await using var db = await _dbFactory.CreateDbContextAsync();

        var openOpps = await db.SalesOpportunities.AsNoTracking()
            .Where(o => o.IsActive && scopeIds.Contains(o.PartyId))
            .Select(o => new { o.ExpectedValue })
            .ToListAsync();

        var contactsCount = await db.PartyContacts.AsNoTracking()
            .CountAsync(c => c.IsActive && scopeIds.Contains(c.PartyId));

        return new B2bCompaniesSummaryDto
        {
            CompanyCount = scopeIds.Count,
            OpenOpportunitiesCount = openOpps.Count,
            PipelineValue = openOpps.Sum(o => o.ExpectedValue ?? 0),
            ContactsCount = contactsCount
        };
    }


    public async Task<PagedResult<B2bCompanyRowDto>> GetCompaniesAsync(
        IReadOnlyCollection<int> scopeIds, string? search, int page = 1, int pageSize = 12)
    {
        var empty = new PagedResult<B2bCompanyRowDto>
        {
            Items = new List<B2bCompanyRowDto>(),
            TotalCount = 0,
            PageNumber = page,
            PageSize = pageSize
        };

        if (scopeIds == null || scopeIds.Count == 0) return empty;

        await using var db = await _dbFactory.CreateDbContextAsync();

        var q = db.Parties.AsNoTracking()
            .Where(p => p.IsActive != false && scopeIds.Contains(p.PartyId));

        var s = search?.Trim();
        if (!string.IsNullOrWhiteSpace(s))
        {
            q = q.Where(p => p.PartyName.Contains(s)
                || (p.Phone != null && p.Phone.Contains(s))
                || (p.Phone2 != null && p.Phone2.Contains(s))
                || (p.Email != null && p.Email.Contains(s))
                || (p.TaxNumber != null && p.TaxNumber.Contains(s)));
        }

        var total = await q.CountAsync();

        var rows = await q.OrderBy(p => p.PartyName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.PartyId,
                p.PartyName,
                p.City,
                p.Area,
                p.Phone,
                p.Phone2,
                p.Email,
                p.TaxNumber,
                p.LastContactDate
            })
            .ToListAsync();

        if (rows.Count == 0)
            return new PagedResult<B2bCompanyRowDto>
            {
                Items = new List<B2bCompanyRowDto>(),
                TotalCount = total,
                PageNumber = page,
                PageSize = pageSize
            };

        var ids = rows.Select(r => r.PartyId).ToList();

        // جهات الاتصال النشطة لكل شركة (على دفعة الصفحة فقط)
        var contactNamesByParty = new Dictionary<int, List<string>>();
        var contactCountByParty = new Dictionary<int, int>();
        foreach (var pid in ids) { contactNamesByParty[pid] = new List<string>(); contactCountByParty[pid] = 0; }
        var crows = await db.PartyContacts.AsNoTracking()
            .Where(c => ids.Contains(c.PartyId) && c.IsActive)
            .Select(c => new { c.PartyId, c.ContactName })
            .ToListAsync();
        foreach (var c in crows)
        {
            contactCountByParty[c.PartyId] = contactCountByParty.GetValueOrDefault(c.PartyId) + 1;
            if (contactNamesByParty.TryGetValue(c.PartyId, out var names) && names.Count < 3)
                names.Add(c.ContactName);
        }

        // الفرص النشطة لكل شركة (على دفعة الصفحة فقط)
        var oppByParty = new Dictionary<int, List<decimal?>>();
        foreach (var pid in ids) oppByParty[pid] = new List<decimal?>();
        var orows = await db.SalesOpportunities.AsNoTracking()
            .Where(o => ids.Contains(o.PartyId) && o.IsActive)
            .Select(o => new { o.PartyId, o.ExpectedValue })
            .ToListAsync();
        foreach (var o in orows)
        {
            if (oppByParty.TryGetValue(o.PartyId, out var vals)) vals.Add(o.ExpectedValue);
        }

        var items = rows.Select(r =>
        {
            oppByParty.TryGetValue(r.PartyId, out var os);

            return new B2bCompanyRowDto
            {
                PartyId = r.PartyId,
                PartyName = r.PartyName,
                City = r.City,
                Area = r.Area,
                Phone = r.Phone,
                Phone2 = r.Phone2,
                Email = r.Email,
                TaxNumber = r.TaxNumber,
                LastContactDate = r.LastContactDate,
                ContactCount = contactCountByParty.GetValueOrDefault(r.PartyId),
                ContactNames = contactNamesByParty.TryGetValue(r.PartyId, out var cs) ? cs : new List<string>(),
                OpenCount = os?.Count ?? 0,
                PipelineValue = os?.Sum(v => v ?? 0) ?? 0
            };
        }).ToList();

        return new PagedResult<B2bCompanyRowDto>
        {
            Items = items,
            TotalCount = total,
            PageNumber = page,
            PageSize = pageSize
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  بطاقة شركة (جهات اتصال + فرص)
    // ═══════════════════════════════════════════════════════════

    public async Task<B2bCompanyCardDto?> GetCompanyCardAsync(int partyId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var party = await db.Parties.AsNoTracking()
            .Where(p => p.PartyId == partyId && p.IsActive != false)
            .Select(p => new
            {
                p.PartyId,
                p.PartyName,
                TypeName = p.PartyTypeNavigation.PartyTypeName,
                p.Phone,
                p.Phone2,
                p.Email,
                p.Address,
                p.City,
                p.Area,
                p.TaxNumber,
                p.Notes,
                p.LastContactDate
            })
            .FirstOrDefaultAsync();

        if (party == null) return null;

        var contacts = await db.PartyContacts.AsNoTracking()
            .Where(c => c.PartyId == partyId && c.IsActive)
            .OrderByDescending(c => c.IsPrimary)
            .ThenBy(c => c.ContactName)
            .Select(c => new B2bContactDto
            {
                ContactId = c.ContactId,
                ContactName = c.ContactName,
                JobTitle = c.JobTitle,
                Phone = c.Phone,
                Email = c.Email,
                Notes = c.Notes,
                IsPrimary = c.IsPrimary
            })
            .ToListAsync();

        var oppRows = await (from o in db.SalesOpportunities.AsNoTracking()
                             where o.PartyId == partyId
                             join s in db.SalesStages.AsNoTracking() on o.StageId equals s.StageId
                             select new
                             {
                                 o.OpportunityId,
                                 o.EmployeeId,
                                 o.ExpectedValue,
                                 o.NextFollowUpDate,
                                 o.IsActive,
                                 o.CreatedAt,
                                 o.ClosedAt,
                                 StageNameAr = s.StageNameAr ?? s.StageName ?? "",
                                 StageColor = s.StageColor
                             })
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        var empIds = oppRows.Where(x => x.EmployeeId.HasValue)
            .Select(x => x.EmployeeId!.Value).Distinct().ToList();
        var empNames = empIds.Count > 0
            ? await db.Employees.AsNoTracking()
                .Where(e => empIds.Contains(e.EmployeeId))
                .Select(e => new { e.EmployeeId, e.FullName })
                .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName ?? "—")
            : new Dictionary<int, string>();

        var today = DateTime.Today;
        var opportunities = oppRows.Select(x => new B2bOpportunityMiniDto
        {
            OpportunityId = x.OpportunityId,
            StageNameAr = x.StageNameAr,
            StageColor = x.StageColor,
            ExpectedValue = x.ExpectedValue,
            EmployeeId = x.EmployeeId,
            EmployeeName = x.EmployeeId.HasValue && empNames.TryGetValue(x.EmployeeId.Value, out var n) ? n : null,
            NextFollowUpDate = x.NextFollowUpDate,
            IsOverdue = x.NextFollowUpDate.HasValue && x.NextFollowUpDate.Value.Date < today,
            IsActive = x.IsActive,
            CreatedAt = x.CreatedAt,
            ClosedAt = x.ClosedAt
        }).ToList();

        var active = opportunities.Where(o => o.IsActive).ToList();

        return new B2bCompanyCardDto
        {
            PartyId = party.PartyId,
            PartyName = party.PartyName,
            TypeName = party.TypeName,
            Phone = party.Phone,
            Phone2 = party.Phone2,
            Email = party.Email,
            Address = party.Address,
            City = party.City,
            Area = party.Area,
            TaxNumber = party.TaxNumber,
            Notes = party.Notes,
            LastContactDate = party.LastContactDate,
            Contacts = contacts,
            Opportunities = opportunities,
            ActiveOpportunities = active.Count,
            ActivePipeline = active.Sum(o => o.ExpectedValue ?? 0)
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  إدارة جهات الاتصال (محمية بالنطاق scopeIds)
    // ═══════════════════════════════════════════════════════════

    public async Task<(bool Success, string Message)> AddContactAsync(
        IReadOnlyCollection<int> scopeIds, int partyId, B2bContactDto dto, string userName)
    {
        if (scopeIds == null || !scopeIds.Contains(partyId))
            return (false, "غير مصرح لك بهذه الشركة.");

        var name = dto.ContactName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return (false, "اسم جهة الاتصال مطلوب.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        var now = DateTime.Now;

        if (dto.IsPrimary)
        {
            // ⚠️ بدون AsNoTracking: نغيّرها وهي متتبّعة حتى تُحفظ التعديلات
            var others = await db.PartyContacts
                .Where(c => c.PartyId == partyId && c.IsPrimary && c.IsActive)
                .ToListAsync();
            foreach (var o in others) o.IsPrimary = false;
        }

        db.PartyContacts.Add(new PartyContact
        {
            PartyId = partyId,
            ContactName = name,
            JobTitle = string.IsNullOrWhiteSpace(dto.JobTitle) ? null : dto.JobTitle.Trim(),
            Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim(),
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
            IsPrimary = dto.IsPrimary,
            IsActive = true,
            CreatedBy = userName,
            CreatedAt = now
        });

        await db.SaveChangesAsync();
        return (true, "تمت إضافة جهة الاتصال.");
    }

    public async Task<(bool Success, string Message)> RemoveContactAsync(
        IReadOnlyCollection<int> scopeIds, int partyId, int contactId, string userName)
    {
        if (scopeIds == null || !scopeIds.Contains(partyId))
            return (false, "غير مصرح لك بهذه الشركة.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        var contact = await db.PartyContacts
            .FirstOrDefaultAsync(c => c.ContactId == contactId && c.PartyId == partyId && c.IsActive);

        if (contact == null) return (false, "جهة الاتصال غير موجودة.");

        contact.IsActive = false;
        await db.SaveChangesAsync();
        return (true, "تم حذف جهة الاتصال.");
    }

    /// <summary>
    /// الشركات B2B (خيارات اختيار) مرتبة بالاسم.
    /// onlyIds = null → كل شركات B2B. onlyIds = نطاق معيّن → الشركات ضمنه فقط.
    /// </summary>
    public async Task<List<B2bCompanyOptionDto>> GetCompanyOptionsAsync(IReadOnlyCollection<int>? onlyIds = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var ids = onlyIds is { Count: > 0 }
            ? onlyIds.ToList()
            : await ResolveCompanyPartyIdsCoreAsync(db);
        if (ids.Count == 0) return new List<B2bCompanyOptionDto>();

        return await db.Parties.AsNoTracking()
            .Where(p => p.IsActive != false && ids.Contains(p.PartyId))
            .OrderBy(p => p.PartyName)
            .Select(p => new B2bCompanyOptionDto
            {
                PartyId = p.PartyId,
                PartyName = p.PartyName,
                City = p.City
            })
            .ToListAsync();
    }


    // ═══════════════════════════════════════════════════════════
    //  سجل التواصل + المهام (شاشات /crm/b2b/interactions و /crm/b2b/tasks)
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// الفرص للشركات ضمن نطاق معيّن (خيارات للتواصل/المهام).
    /// onlyEmployeeId = معيّن → فرص هذا الموظف فقط (للمختص: شغله).
    /// null → كل الفرص ضمن النطاق (للمدراء).
    /// </summary>
    public async Task<List<B2bOppSelectDto>> GetActiveOpportunitiesAsync(IReadOnlyCollection<int> partyIds, int? onlyEmployeeId = null)
    {
        if (partyIds == null || partyIds.Count == 0) return new List<B2bOppSelectDto>();

        await using var db = await _dbFactory.CreateDbContextAsync();

        var rows = await (from o in db.SalesOpportunities.AsNoTracking()
                          join s in db.SalesStages.AsNoTracking() on o.StageId equals s.StageId
                          where o.IsActive
                              && partyIds.Contains(o.PartyId)
                              && (!onlyEmployeeId.HasValue || o.EmployeeId == onlyEmployeeId.Value)
                          select new B2bOppSelectDto
                          {
                              OpportunityId = o.OpportunityId,
                              PartyId = o.PartyId,
                              PartyName = "",
                              StageNameAr = s.StageNameAr ?? s.StageName ?? "",
                              StageColor = s.StageColor
                          })
            .ToListAsync();

        if (rows.Count == 0) return rows;

        var partyIds2 = rows.Select(r => r.PartyId).Distinct().ToList();
        var names = await db.Parties.AsNoTracking()
            .Where(p => partyIds2.Contains(p.PartyId))
            .Select(p => new { p.PartyId, p.PartyName })
            .ToDictionaryAsync(p => p.PartyId, p => p.PartyName);

        foreach (var r in rows)
            r.PartyName = names.TryGetValue(r.PartyId, out var n) ? n : "";

        return rows.OrderBy(r => r.PartyName).ThenBy(r => r.StageNameAr).ToList();
    }

    /// <summary>إحصاءات سريعة لشاشات التواصل والمهام ضمن النطاق.</summary>
    public async Task<B2bLogSummaryDto> GetLogSummaryAsync(IReadOnlyCollection<int> scopeIds)
    {
        var dto = new B2bLogSummaryDto();
        if (scopeIds == null || scopeIds.Count == 0) return dto;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var today = DateTime.Today;

        dto.InteractionsTotal = await db.CustomerInteractions.AsNoTracking()
            .CountAsync(i => scopeIds.Contains(i.PartyId));
        dto.InteractionsThisMonth = await db.CustomerInteractions.AsNoTracking()
            .CountAsync(i => i.InteractionDate >= monthStart && scopeIds.Contains(i.PartyId));

        var openTaskQuery = db.CrmTasks.AsNoTracking().Where(t =>
            (t.Status == "Pending" || t.Status == "In Progress")
            && ((t.PartyId.HasValue && scopeIds.Contains(t.PartyId.Value))
                || (t.OpportunityId.HasValue
                    && db.SalesOpportunities.Any(o => o.OpportunityId == t.OpportunityId.Value && scopeIds.Contains(o.PartyId)))));

        dto.OpenTasks = await openTaskQuery.CountAsync();
        dto.OverdueTasks = await openTaskQuery.CountAsync(t => t.DueDate.Date < today);
        dto.TasksThisMonth = await db.CrmTasks.AsNoTracking()
            .CountAsync(t => t.CreatedAt >= monthStart
                && ((t.PartyId.HasValue && scopeIds.Contains(t.PartyId.Value))
                    || (t.OpportunityId.HasValue
                        && db.SalesOpportunities.Any(o => o.OpportunityId == t.OpportunityId.Value && scopeIds.Contains(o.PartyId)))));

        return dto;
    }

    private IQueryable<CustomerInteraction> ScopedInteractionsQuery(db24804Context db, IReadOnlyCollection<int> scopeIds, string? search)
    {
        var q = db.CustomerInteractions.AsNoTracking()
            .Where(i => scopeIds.Contains(i.PartyId));

        var s = search?.Trim();
        if (!string.IsNullOrWhiteSpace(s))
        {
            q = q.Where(i => i.Party.PartyName.Contains(s)
                || (i.Summary != null && i.Summary.Contains(s)));
        }
        return q;
    }

    /// <summary>صفحة سجل تواصلات B2B (مرتبة الأحدث أولاً).</summary>
    public async Task<PagedResult<B2bInteractionRowDto>> GetInteractionsPageAsync(
        IReadOnlyCollection<int> scopeIds, string? search, int page = 1, int pageSize = 25)
    {
        var empty = new PagedResult<B2bInteractionRowDto>
        {
            Items = new List<B2bInteractionRowDto>(), TotalCount = 0, PageNumber = page, PageSize = pageSize
        };
        if (scopeIds == null || scopeIds.Count == 0) return empty;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var q = ScopedInteractionsQuery(db, scopeIds, search);
        var total = await q.CountAsync();

        var rows = await q.OrderByDescending(i => i.InteractionDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(i => new
            {
                i.InteractionId,
                i.InteractionDate,
                i.PartyId,
                i.OpportunityId,
                i.Summary,
                i.EmployeeId,
                i.CreatedBy,
                i.StageAfterId,
                i.StageBeforeId
            })
            .ToListAsync();

        if (rows.Count == 0)
            return new PagedResult<B2bInteractionRowDto> { Items = new(), TotalCount = total, PageNumber = page, PageSize = pageSize };

        var partyIds = rows.Select(r => r.PartyId).Distinct().ToList();
        var partyNames = partyIds.Count > 0
            ? await db.Parties.AsNoTracking().Where(p => partyIds.Contains(p.PartyId))
                .Select(p => new { p.PartyId, p.PartyName }).ToDictionaryAsync(p => p.PartyId, p => p.PartyName)
            : new Dictionary<int, string>();

        var empIds = rows.Where(r => r.EmployeeId.HasValue).Select(r => r.EmployeeId!.Value).Distinct().ToList();
        var empNames = empIds.Count > 0
            ? await db.Employees.AsNoTracking().Where(e => empIds.Contains(e.EmployeeId))
                .Select(e => new { e.EmployeeId, e.FullName }).ToDictionaryAsync(e => e.EmployeeId, e => e.FullName ?? "—")
            : new Dictionary<int, string>();

        var stageIds = rows.Where(r => r.StageAfterId.HasValue).Select(r => r.StageAfterId!.Value)
            .Concat(rows.Where(r => r.StageBeforeId.HasValue).Select(r => r.StageBeforeId!.Value))
            .Distinct().ToList();
        var stages = new Dictionary<int, (string Name, string? Color)>();
        if (stageIds.Count > 0)
        {
            var stageRows = await db.SalesStages.AsNoTracking()
                .Where(s => stageIds.Contains(s.StageId))
                .Select(s => new { s.StageId, Name = s.StageNameAr ?? s.StageName ?? "", s.StageColor })
                .ToListAsync();
            foreach (var srow in stageRows)
                stages[srow.StageId] = (srow.Name, srow.StageColor);
        }

        var items = rows.Select(r =>
        {
            partyNames.TryGetValue(r.PartyId, out var pn);
            var en = r.EmployeeId.HasValue && empNames.TryGetValue(r.EmployeeId.Value, out var e1) ? e1
                : (string.IsNullOrWhiteSpace(r.CreatedBy) ? null : r.CreatedBy);

            string? stName = null; string? stColor = null;
            if (r.StageAfterId.HasValue && stages.TryGetValue(r.StageAfterId.Value, out var st))
            {
                stName = st.Name;
                stColor = st.Color;
            }

            return new B2bInteractionRowDto
            {
                InteractionId = r.InteractionId,
                InteractionDate = r.InteractionDate,
                PartyId = r.PartyId,
                OpportunityId = r.OpportunityId,
                ClientName = pn ?? "—",
                Summary = r.Summary,
                EmployeeName = en,
                StageAfterNameAr = stName,
                StageAfterColor = stColor
            };
        }).ToList();

        return new PagedResult<B2bInteractionRowDto> { Items = items, TotalCount = total, PageNumber = page, PageSize = pageSize };
    }

    /// <summary>صفحة مهام B2B ضمن النطاق.</summary>
    public async Task<PagedResult<B2bTaskRowDto>> GetTasksPageAsync(
        IReadOnlyCollection<int> scopeIds, string? statusFilter, string? search, int page = 1, int pageSize = 25)
    {
        var empty = new PagedResult<B2bTaskRowDto>
        {
            Items = new List<B2bTaskRowDto>(), TotalCount = 0, PageNumber = page, PageSize = pageSize
        };
        if (scopeIds == null || scopeIds.Count == 0) return empty;

        await using var db = await _dbFactory.CreateDbContextAsync();

        var q = db.CrmTasks.AsNoTracking().Where(t =>
            (t.PartyId.HasValue && scopeIds.Contains(t.PartyId.Value))
            || (t.OpportunityId.HasValue
                && db.SalesOpportunities.Any(o => o.OpportunityId == t.OpportunityId.Value && scopeIds.Contains(o.PartyId))));

        var s = search?.Trim();
        if (!string.IsNullOrWhiteSpace(s))
        {
            q = q.Where(t => (t.TaskDescription != null && t.TaskDescription.Contains(s))
                || t.Party!.PartyName.Contains(s));
        }
        if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "All")
        {
            if (statusFilter == "Open")
                q = q.Where(t => t.Status == "Pending" || t.Status == "In Progress");
            else if (statusFilter == "Overdue")
                q = q.Where(t => (t.Status == "Pending" || t.Status == "In Progress")
                    && t.DueDate.Date < DateTime.Today);
            else
                q = q.Where(t => t.Status == statusFilter);
        }

        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(t => t.DueDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(t => new
            {
                t.TaskId,
                t.OpportunityId,
                t.PartyId,
                t.AssignedTo,
                t.TaskTypeId,
                t.TaskDescription,
                t.DueDate,
                t.Priority,
                t.Status
            })
            .ToListAsync();

        if (rows.Count == 0)
            return new PagedResult<B2bTaskRowDto> { Items = new(), TotalCount = total, PageNumber = page, PageSize = pageSize };

        var partyIds = rows.Where(r => r.PartyId.HasValue).Select(r => r.PartyId!.Value).Distinct().ToList();
        var partyNames = partyIds.Count > 0
            ? await db.Parties.AsNoTracking().Where(p => partyIds.Contains(p.PartyId))
                .Select(p => new { p.PartyId, p.PartyName }).ToDictionaryAsync(p => p.PartyId, p => p.PartyName)
            : new Dictionary<int, string>();

        var empIds = rows.Select(r => r.AssignedTo).Distinct().ToList();
        var empNames = empIds.Count > 0
            ? await db.Employees.AsNoTracking().Where(e => empIds.Contains(e.EmployeeId))
                .Select(e => new { e.EmployeeId, e.FullName }).ToDictionaryAsync(e => e.EmployeeId, e => e.FullName ?? "—")
            : new Dictionary<int, string>();

        var typeIds = rows.Where(r => r.TaskTypeId.HasValue).Select(r => r.TaskTypeId!.Value).Distinct().ToList();
        var typeNames = typeIds.Count > 0
            ? await db.TaskTypes.AsNoTracking().Where(tt => typeIds.Contains(tt.TaskTypeId))
                .Select(tt => new { tt.TaskTypeId, Name = tt.TaskTypeNameAr ?? tt.TaskTypeName ?? "" }).ToDictionaryAsync(tt => tt.TaskTypeId, tt => tt.Name)
            : new Dictionary<int, string>();

        var items = rows.Select(r =>
        {
            partyNames.TryGetValue(r.PartyId ?? -1, out var pn);
            empNames.TryGetValue(r.AssignedTo, out var en);
            typeNames.TryGetValue(r.TaskTypeId ?? -1, out var tn);
            return new B2bTaskRowDto
            {
                TaskId = r.TaskId,
                OpportunityId = r.OpportunityId,
                PartyId = r.PartyId,
                ClientName = pn ?? "—",
                TaskDescription = string.IsNullOrWhiteSpace(r.TaskDescription) ? "مهمة بدون وصف" : r.TaskDescription,
                TaskTypeNameAr = tn,
                AssignedToName = en ?? "—",
                DueDate = r.DueDate,
                Priority = r.Priority,
                Status = r.Status
            };
        }).ToList();

        return new PagedResult<B2bTaskRowDto> { Items = items, TotalCount = total, PageNumber = page, PageSize = pageSize };
    }


    // ═══════════════════════════════════════════════════════════
    //  تقارير B2B (شاشة /crm/b2b/reports)
    // ═══════════════════════════════════════════════════════════

    /// <summary>تجميع كل إحصاءات تقرير B2B ضمن النطاق (التجميع في الذاكرة — متوافق مع كل قواعد البيانات).</summary>
    public async Task<B2bReportsDto> GetB2bReportsAsync(IReadOnlyCollection<int> scopeIds)
    {
        var dto = new B2bReportsDto();
        if (scopeIds == null || scopeIds.Count == 0) return dto;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var scope = scopeIds.ToList();
        var today = DateTime.Today;

        // ── الشركات ──
        var parties = await db.Parties.AsNoTracking()
            .Where(p => p.IsActive != false && scope.Contains(p.PartyId))
            .Select(p => new { p.PartyId, p.PartyName, p.City, p.LastContactDate })
            .ToListAsync();

        dto.CompaniesCount = parties.Count;
        if (parties.Count == 0) return dto;

        var partyIds = parties.Select(p => p.PartyId).ToList();

        // ── جهات الاتصال ──
        var contactsRows = await db.PartyContacts.AsNoTracking()
            .Where(c => c.IsActive && partyIds.Contains(c.PartyId))
            .Select(c => new { c.PartyId })
            .ToListAsync();
        var contactsByParty = contactsRows.GroupBy(c => c.PartyId)
            .ToDictionary(g => g.Key, g => g.Count());
        dto.ContactsCount = contactsRows.Count;

        // ── الفرص ──
        var opps = await db.SalesOpportunities.AsNoTracking()
            .Where(o => partyIds.Contains(o.PartyId))
            .Select(o => new { o.OpportunityId, o.PartyId, o.StageId, o.IsActive, o.ExpectedValue })
            .ToListAsync();
        var oppPartyByOppId = opps.ToDictionary(o => o.OpportunityId, o => o.PartyId);

        // توزيع المراحل (كل الفرص المسجلة)
        var stageRows = opps.GroupBy(o => o.StageId)
            .Select(g => new { StageId = g.Key, Cnt = g.Count(), Val = g.Sum(o => o.ExpectedValue ?? 0) })
            .ToList();

        // مراحل الخروج (خسارة/غير مهتم)
        dto.ClosedLost = stageRows.FirstOrDefault(s => s.StageId == CrmStages.LostStageId)?.Cnt ?? 0;
        dto.ClosedNotInterested = stageRows.FirstOrDefault(s => s.StageId == CrmStages.NotInterestedStageId)?.Cnt ?? 0;

        // قيد العمل = كل ما ليس مرحلة خروج
        var working = opps.Where(o => !CrmStages.IsExitStage(o.StageId)).ToList();
        dto.WorkingOpps = working.Count;
        dto.PipelineValue = working.Sum(o => o.ExpectedValue ?? 0);

        var workingByParty = working.GroupBy(o => o.PartyId)
            .ToDictionary(g => g.Key, g => g.Sum(o => o.ExpectedValue ?? 0));

        // أسماء/ألوان المراحل الفعلية
        var stageMeta = await db.SalesStages.AsNoTracking()
            .Where(s => s.IsActive)
            .Select(s => new { s.StageId, Name = s.StageNameAr ?? s.StageName ?? "", s.StageOrder, s.StageColor })
            .ToListAsync();
        var stageMetaBy = stageMeta.ToDictionary(s => s.StageId, s => s);

        var stageOrder = stageMeta
            .Where(m => stageRows.Any(sr => sr.StageId == m.StageId))
            .OrderBy(m => m.StageOrder)
            .Select(m => m.StageId)
            .Concat(stageRows.Where(sr => !stageMetaBy.ContainsKey(sr.StageId)).Select(sr => sr.StageId))
            .Distinct().ToList();

        foreach (var sid in stageOrder)
        {
            var sr = stageRows.FirstOrDefault(x => x.StageId == sid);
            if (sr == null) continue;
            stageMetaBy.TryGetValue(sid, out var meta);
            dto.Stages.Add(new B2bReportStageDto
            {
                StageId = sid,
                StageNameAr = meta?.Name ?? $"مرحلة {sid}",
                StageColor = meta?.StageColor,
                OpportunitiesCount = sr.Cnt,
                PipelineValue = sr.Val
            });
        }

        // ── التواصلات (كلي + هذا الشهر + آخر ٦ شهور) ──
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var sixAgoStart = monthStart.AddMonths(-5);

        var interactions = await db.CustomerInteractions.AsNoTracking()
            .Where(i => partyIds.Contains(i.PartyId))
            .Select(i => new { i.PartyId, i.InteractionDate })
            .ToListAsync();

        dto.InteractionsTotal = interactions.Count;
        dto.InteractionsThisMonth = interactions.Count(i => i.InteractionDate >= monthStart);

        // سلسلة آخر ٦ شهور
        var monthNames = new[] { "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };
        for (var m = 0; m < 6; m++)
        {
            var d = monthStart.AddMonths(-(5 - m));
            dto.Months.Add(new B2bReportMonthDto
            {
                Year = d.Year,
                Month = d.Month,
                Label = monthNames[d.Month - 1],
                InteractionsCount = 0
            });
        }
        var byMonth = interactions
            .GroupBy(i => new { i.InteractionDate.Year, i.InteractionDate.Month })
            .ToDictionary(g => (g.Key.Year, g.Key.Month), g => g.Count());
        foreach (var mth in dto.Months)
        {
            if (byMonth.TryGetValue((mth.Year, mth.Month), out var cnt))
                mth.InteractionsCount = cnt;
        }

        var interactionsByParty = interactions.GroupBy(i => i.PartyId)
            .ToDictionary(g => g.Key, g => g.Count());

        // ── المهام المفتوحة ──
        var openTasks = await db.CrmTasks.AsNoTracking()
            .Where(t => (t.Status == "Pending" || t.Status == "In Progress")
                && ((t.PartyId.HasValue && partyIds.Contains(t.PartyId.Value))
                    || (t.OpportunityId.HasValue
                        && db.SalesOpportunities.Any(o => o.OpportunityId == t.OpportunityId.Value && partyIds.Contains(o.PartyId)))))
            .Select(t => new { t.PartyId, t.OpportunityId, t.DueDate })
            .ToListAsync();

        dto.OpenTasks = openTasks.Count;
        dto.OverdueTasks = openTasks.Count(t => t.DueDate.Date < today);

        var openByCompany = new Dictionary<int, int>();
        foreach (var task in openTasks)
        {
            var pid = task.PartyId
                ?? (task.OpportunityId.HasValue && oppPartyByOppId.TryGetValue(task.OpportunityId.Value, out var op) ? op : (int?)null);
            if (pid.HasValue)
                openByCompany[pid.Value] = openByCompany.GetValueOrDefault(pid.Value) + 1;
        }

        // ── تجميع سطور الشركات ──
        foreach (var p in parties)
        {
            var workingCnt = working.Count(o => o.PartyId == p.PartyId);
            dto.Companies.Add(new B2bReportCompanyDto
            {
                PartyId = p.PartyId,
                PartyName = p.PartyName,
                City = p.City,
                WorkingOpps = workingCnt,
                PipelineValue = workingByParty.GetValueOrDefault(p.PartyId),
                ContactsCount = contactsByParty.GetValueOrDefault(p.PartyId),
                InteractionsCount = interactionsByParty.GetValueOrDefault(p.PartyId),
                OpenTasksCount = openByCompany.GetValueOrDefault(p.PartyId),
                LastContactDate = p.LastContactDate
            });
        }

        dto.Companies = dto.Companies
            .OrderByDescending(c => c.PipelineValue)
            .ThenByDescending(c => c.WorkingOpps)
            .ThenBy(c => c.PartyName)
            .ToList();

        return dto;
    }

    /// <summary>
    /// ترجمة اسم مستخدم → EmployeeId (يرجع 0 إن لم يكن مربوطاً بموظف).
    /// نفس منطق RecoveryService.ResolveEmployeeByUsernameAsync.
    /// </summary>
    public async Task<int> ResolveEmployeeIdAsync(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return 0;
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Users.AsNoTracking()
            .Where(u => u.Username == username && u.EmployeeId.HasValue)
            .Select(u => u.EmployeeId!.Value)
            .FirstOrDefaultAsync();
    }
}
