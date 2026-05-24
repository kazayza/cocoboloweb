using System.Security.Claims;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════╗
/// ║  Quotation Service - v5 FINAL                                ║
/// ║  Generated: 2026-05-22 21:35                                 ║
/// ║  🎯 الإصلاح النهائي: شلنا كل الـ JOINs                       ║
/// ╚══════════════════════════════════════════════════════════════╝
/// 
/// 🆕 إصلاحات v5 (الأهم):
///   ✅ شلنا كل الـ JOINs المعقدة (دي كانت سبب Data is Null)
///   ✅ نستخدم Batch Queries - استعلام بسيط منفصل لكل جدول
///   ✅ كل جدول له dictionary lookup في الذاكرة
///   ✅ مفيش Entity materialization على جداول فيها أعمدة null
///   ✅ Try/Catch مفصل لو حصلت مشكلة، يقولك بالظبط في أي خطوة
/// 
/// كل التحسينات السابقة محفوظة (Transactions, Permissions, إلخ).
/// </summary>
public class QuotationService : IQuotationService
{
    private readonly db24804Context _db;
    private readonly IAuditService _audit;
    private readonly NotificationService _notify;
    private readonly IInvoiceService _invoiceService;
    private readonly IHttpContextAccessor _httpContext;
    private readonly ILogger<QuotationService> _logger;

    private static readonly Dictionary<string, string[]> AllowedTransitions = new()
    {
        [QuotationStatuses.Draft] = new[] { QuotationStatuses.Sent, QuotationStatuses.Accepted, QuotationStatuses.Rejected, QuotationStatuses.Expired },
        [QuotationStatuses.Sent] = new[] { QuotationStatuses.Accepted, QuotationStatuses.Rejected, QuotationStatuses.Expired },
        [QuotationStatuses.Accepted] = new[] { QuotationStatuses.Converted, QuotationStatuses.Rejected, QuotationStatuses.Expired },
        [QuotationStatuses.Rejected] = new[] { QuotationStatuses.Draft },
        [QuotationStatuses.Expired] = new[] { QuotationStatuses.Draft },
        [QuotationStatuses.Converted] = Array.Empty<string>()
    };

    public QuotationService(
        db24804Context db, IAuditService audit, NotificationService notify,
        IInvoiceService invoiceService, IHttpContextAccessor httpContext,
        ILogger<QuotationService> logger)
    {
        _db = db; _audit = audit; _notify = notify;
        _invoiceService = invoiceService; _httpContext = httpContext; _logger = logger;
    }

    private ClaimsPrincipal? CurrentUser => _httpContext.HttpContext?.User;
    private bool HasPermission(string permission)
    {
        var user = CurrentUser;
        if (user?.Identity?.IsAuthenticated != true) return false;
        if (user.IsInRole("Admin")) return true;
        return user.HasClaim("Permission", $"frmQuotations:{permission}");
    }

    // ============================================================
    //  ⭐ قائمة عروض الأسعار - بدون JOINs (الحل النهائي v5)
    // ============================================================
    public async Task<PagedResult<QuotationListDto>> GetQuotationsAsync(QuotationFilterDto filter)
    {
        if (!HasPermission("View"))
            return new PagedResult<QuotationListDto> { Items = new(), TotalCount = 0 };

        // ✅ Step 1: استعلام أساسي على Quotations فقط، بـ nullable casts
        var step = "Step 1: Build base query";
        try
        {
            var baseQuery = _db.Quotations.AsNoTracking().AsQueryable();

            // البحث
            if (!string.IsNullOrWhiteSpace(filter.SearchText))
            {
                step = "Step 1.1: Search parties";
                var s = filter.SearchText.Trim();

                var prefiltered = await _db.Parties.AsNoTracking()
                    .Where(p => EF.Functions.Like(p.PartyName ?? "", $"%{s}%")
                             || EF.Functions.Like(p.Phone ?? "", $"%{s}%"))
                    .Select(p => new { p.PartyId, p.PartyName, p.Phone })
                    .Take(500)
                    .ToListAsync();

                var matchingPartyIds = prefiltered
                    .Where(p => (p.PartyName ?? "").ContainsArabic(s) ||
                                (p.Phone ?? "").ContainsArabic(s))
                    .Select(p => p.PartyId)
                    .ToList();

                baseQuery = baseQuery.Where(q =>
                    matchingPartyIds.Contains(q.PartyId) ||
                    (q.Notes != null && q.Notes.Contains(s)));
            }

            // الفلاتر
            if (filter.PartyId.HasValue)
                baseQuery = baseQuery.Where(q => q.PartyId == filter.PartyId.Value);

            if (filter.DateFrom.HasValue)
                baseQuery = baseQuery.Where(q => q.QuotationDate >= filter.DateFrom.Value.Date);

            if (filter.DateTo.HasValue)
            {
                var endOfDay = filter.DateTo.Value.Date.AddDays(1).AddTicks(-1);
                baseQuery = baseQuery.Where(q => q.QuotationDate <= endOfDay);
            }

            if (!string.IsNullOrWhiteSpace(filter.Status))
                baseQuery = baseQuery.Where(q => q.Status == filter.Status);

            if (filter.IsConverted.HasValue)
            {
                baseQuery = filter.IsConverted.Value
                    ? baseQuery.Where(q => q.InvoiceId != null)
                    : baseQuery.Where(q => q.InvoiceId == null);
            }

            if (filter.IsExpired.GetValueOrDefault())
            {
                var today = DateTime.Today;
                baseQuery = baseQuery.Where(q => q.ValidUntil.HasValue
                                              && q.ValidUntil.Value < today
                                              && q.InvoiceId == null);
            }

            step = "Step 2: Count total";
            var totalCount = await baseQuery.CountAsync();

            // ترتيب بسيط آمن
            step = "Step 3: Apply sorting";
            baseQuery = filter.SortBy switch
            {
                "GrandTotal" => filter.SortDescending
                    ? baseQuery.OrderByDescending(q => q.GrandTotal ?? q.TotalAmount)
                    : baseQuery.OrderBy(q => q.GrandTotal ?? q.TotalAmount),
                _ => filter.SortDescending
                    ? baseQuery.OrderByDescending(q => q.QuotationDate).ThenByDescending(q => q.QuotationId)
                    : baseQuery.OrderBy(q => q.QuotationDate).ThenBy(q => q.QuotationId)
            };

            // ✅ Step 4: نجيب بيانات Quotations فقط بـ FULL NULLABLE CASTS
            step = "Step 4: Fetch quotations (nullable projection)";
            var rawQuotes = await baseQuery
                .Skip((filter.PageNumber - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .Select(q => new
                {
                    QId = (int?)q.QuotationId,
                    QDate = (DateTime?)q.QuotationDate,
                    PId = (int?)q.PartyId,
                    WId = q.WarehouseId,
                    EId = q.EmpId,
                    PType = q.PricingType,
                    Total = (decimal?)q.TotalAmount,
                    Discount = q.DiscountAmount,
                    Grand = q.GrandTotal,
                    Stat = q.Status,
                    InvId = q.InvoiceId,
                    Valid = q.ValidUntil,
                    CBy = q.CreatedBy,
                    CAt = (DateTime?)q.CreatedAt
                })
                .ToListAsync();

            if (!rawQuotes.Any())
            {
                return new PagedResult<QuotationListDto>
                {
                    Items = new(),
                    TotalCount = totalCount,
                    PageNumber = filter.PageNumber,
                    PageSize = filter.PageSize
                };
            }

            // ✅ Step 5: نجمع الـ IDs اللي محتاجين نجيب بياناتهم
            step = "Step 5: Collect related IDs";
            var partyIds = rawQuotes.Where(q => q.PId.HasValue).Select(q => q.PId!.Value).Distinct().ToList();
            var warehouseIds = rawQuotes.Where(q => q.WId.HasValue).Select(q => q.WId!.Value).Distinct().ToList();
            var empIds = rawQuotes.Where(q => q.EId.HasValue).Select(q => q.EId!.Value).Distinct().ToList();
            var invoiceIds = rawQuotes.Where(q => q.InvId.HasValue).Select(q => q.InvId!.Value).Distinct().ToList();
            var quoteIds = rawQuotes.Where(q => q.QId.HasValue).Select(q => q.QId!.Value).Distinct().ToList();

            // ✅ Step 6: نجيب بيانات كل جدول لوحده بـ projection آمن
            step = "Step 6a: Fetch parties";
            var partiesDict = new Dictionary<int, (string Name, string? Phone)>();
            if (partyIds.Any())
            {
                var partyRows = await _db.Parties.AsNoTracking()
                    .Where(p => partyIds.Contains(p.PartyId))
                    .Select(p => new { p.PartyId, Name = p.PartyName, p.Phone })
                    .ToListAsync();
                foreach (var p in partyRows)
                    partiesDict[p.PartyId] = (p.Name ?? "", p.Phone);
            }

            step = "Step 6b: Fetch warehouses";
            var warehousesDict = new Dictionary<int, string>();
            if (warehouseIds.Any())
            {
                var whRows = await _db.Warehouses.AsNoTracking()
                    .Where(w => warehouseIds.Contains(w.WarehouseId))
                    .Select(w => new { w.WarehouseId, Name = w.WarehouseName })
                    .ToListAsync();
                foreach (var w in whRows)
                    warehousesDict[w.WarehouseId] = w.Name ?? "";
            }

            step = "Step 6c: Fetch employees";
            var empsDict = new Dictionary<int, string>();
            if (empIds.Any())
            {
                var empRows = await _db.Employees.AsNoTracking()
                    .Where(e => empIds.Contains(e.EmployeeId))
                    .Select(e => new { e.EmployeeId, Name = e.FullName })
                    .ToListAsync();
                foreach (var e in empRows)
                    empsDict[e.EmployeeId] = e.Name ?? "";
            }

            step = "Step 6d: Fetch invoices";
            var invoicesDict = new Dictionary<int, string>();
            if (invoiceIds.Any())
            {
                var invRows = await _db.Transactions.AsNoTracking()
                    .Where(t => invoiceIds.Contains(t.TransactionId))
                    .Select(t => new { t.TransactionId, Ref = t.ReferenceNumber })
                    .ToListAsync();
                foreach (var i in invRows)
                    invoicesDict[i.TransactionId] = i.Ref ?? "";
            }

            step = "Step 6e: Fetch items count";
            var itemsCountDict = new Dictionary<int, int>();
            if (quoteIds.Any())
            {
                var counts = await _db.QuotationDetails.AsNoTracking()
                    .Where(d => quoteIds.Contains(d.QuotationId))
                    .GroupBy(d => d.QuotationId)
                    .Select(g => new { Id = g.Key, Cnt = g.Count() })
                    .ToListAsync();
                foreach (var c in counts)
                    itemsCountDict[c.Id] = c.Cnt;
            }

            // ✅ Step 7: نركّب الـ DTO النهائي في الذاكرة
            step = "Step 7: Compose final DTOs";
            var items = rawQuotes.Select(r =>
            {
                var qid = r.QId ?? 0;
                var date = r.QDate ?? DateTime.Today;
                var total = r.Total ?? 0m;
                var pid = r.PId ?? 0;

                partiesDict.TryGetValue(pid, out var partyInfo);
                string? wname = r.WId.HasValue && warehousesDict.TryGetValue(r.WId.Value, out var w) ? w : null;
                string? ename = r.EId.HasValue && empsDict.TryGetValue(r.EId.Value, out var e) ? e : null;
                string? invRef = r.InvId.HasValue && invoicesDict.TryGetValue(r.InvId.Value, out var iv) ? iv : null;
                int icnt = itemsCountDict.TryGetValue(qid, out var ic) ? ic : 0;

                return new QuotationListDto
                {
                    QuotationId = qid,
                    ReferenceNumber = $"QT-{date.Year}-{qid:D5}",
                    QuotationDate = date,
                    PartyId = pid,
                    PartyName = partyInfo.Name ?? "",
                    PartyPhone = partyInfo.Phone,
                    WarehouseId = r.WId,
                    WarehouseName = wname,
                    EmpId = r.EId,
                    EmpName = ename,
                    PricingType = r.PType ?? PricingTiers.Premium,
                    TotalAmount = total,
                    DiscountAmount = r.Discount,
                    GrandTotal = r.Grand ?? total,
                    ItemsCount = icnt,
                    Status = string.IsNullOrWhiteSpace(r.Stat) ? QuotationStatuses.Draft : r.Stat,
                    InvoiceId = r.InvId,
                    InvoiceReference = invRef,
                    ValidUntil = r.Valid,
                    CreatedBy = r.CBy ?? "",
                    CreatedAt = r.CAt ?? DateTime.Today
                };
            }).ToList();

            return new PagedResult<QuotationListDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize
            };
        }
        catch (Exception ex)
        {
            // ✅ Logging مفصل يقولنا بالظبط أي خطوة فشلت
            _logger.LogError(ex,
                "❌ GetQuotationsAsync FAILED at [{Step}]. Inner: {Inner}",
                step, ex.InnerException?.Message ?? "none");

            // نرمي الخطأ بـ context واضح
            throw new InvalidOperationException(
                $"فشل تحميل القائمة في الخطوة: {step}. التفاصيل: {ex.Message}",
                ex);
        }
    }

    // ============================================================
    //  جلب عرض السعر للتعديل (نفس الطريقة - بدون JOINs)
    // ============================================================
    public async Task<QuotationFormDto?> GetQuotationForEditAsync(int quotationId)
    {
        if (!HasPermission("View")) return null;

        // Step 1: Quotation الأساسي
        var rawData = await _db.Quotations.AsNoTracking()
            .Where(x => x.QuotationId == quotationId)
            .Select(x => new
            {
                QId = (int?)x.QuotationId,
                QDate = (DateTime?)x.QuotationDate,
                x.ValidUntil,
                PId = (int?)x.PartyId,
                x.WarehouseId,
                x.EmpId,
                x.PricingType,
                Total = (decimal?)x.TotalAmount,
                x.DiscountAmount,
                x.GrandTotal,
                x.InvoiceId,
                x.Notes,
                x.Status,
                x.CreatedBy,
                CAt = (DateTime?)x.CreatedAt
            })
            .FirstOrDefaultAsync();

        if (rawData == null) return null;

        var qid = rawData.QId ?? 0;
        var pid = rawData.PId ?? 0;
        var qdate = rawData.QDate ?? DateTime.Today;
        var total = rawData.Total ?? 0m;

        // Step 2: العميل
        var party = await _db.Parties.AsNoTracking()
            .Where(p => p.PartyId == pid)
            .Select(p => new { p.PartyName, p.Phone })
            .FirstOrDefaultAsync();

        // Step 3: الموظف
        string? empName = null;
        if (rawData.EmpId.HasValue)
        {
            empName = await _db.Employees.AsNoTracking()
                .Where(e => e.EmployeeId == rawData.EmpId.Value)
                .Select(e => e.FullName)
                .FirstOrDefaultAsync();
        }

        // Step 4: الأصناف (بدون JOIN مع Products - نجيب Products منفصلة)
        var detailRows = await _db.QuotationDetails.AsNoTracking()
            .Where(d => d.QuotationId == qid)
            .Select(d => new
            {
                DId = (int?)d.QuotationDetailId,
                PrId = (int?)d.ProductId,
                Qty = (decimal?)d.Quantity,
                Price = (decimal?)d.UnitPrice,
                d.Notes
            })
            .ToListAsync();

        var productIds = detailRows.Where(d => d.PrId.HasValue).Select(d => d.PrId!.Value).Distinct().ToList();
        var productsDict = new Dictionary<int, (string? Name, string? Desc, decimal? Sale, decimal? SaleE, decimal? Purch, decimal? PurchE, int? Period)>();
        if (productIds.Any())
        {
            var prods = await _db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.ProductId))
                .Select(p => new
                {
                    p.ProductId,
                    p.ProductName,
                    p.ProductDescription,
                    p.SuggestedSalePrice,
                    p.SuggestedSalePriceElite,
                    p.PurchasePrice,
                    p.PurchasePriceElite,
                    p.Period
                })
                .ToListAsync();
            foreach (var p in prods)
                productsDict[p.ProductId] = (p.ProductName, p.ProductDescription,
                    p.SuggestedSalePrice, p.SuggestedSalePriceElite,
                    p.PurchasePrice, p.PurchasePriceElite, p.Period);
        }

        var items = detailRows.Select(d =>
        {
            productsDict.TryGetValue(d.PrId ?? 0, out var p);
            return new QuotationItemDto
            {
                QuotationDetailId = d.DId ?? 0,
                ProductId = d.PrId ?? 0,
                ProductName = p.Name,
                ProductDescription = p.Desc,
                Quantity = d.Qty ?? 0,
                UnitPrice = d.Price ?? 0,
                Notes = StripTierTag(d.Notes),
                PricingTier = ExtractTier(d.Notes),
                SalePricePremium = p.Sale,
                SalePriceElite = p.SaleE,
                PurchasePricePremium = p.Purch,
                PurchasePriceElite = p.PurchE,
                Period = p.Period
            };
        }).ToList();

        // Step 5: مرجع الفاتورة
        string? invoiceRef = null;
        if (rawData.InvoiceId.HasValue)
        {
            invoiceRef = await _db.Transactions.AsNoTracking()
                .Where(t => t.TransactionId == rawData.InvoiceId.Value)
                .Select(t => t.ReferenceNumber)
                .FirstOrDefaultAsync();
        }

        // حساب الخصم %
        decimal? discountPct = null;
        if (rawData.DiscountAmount.HasValue && rawData.DiscountAmount.Value > 0 && total > 0)
            discountPct = Math.Round((rawData.DiscountAmount.Value / total) * 100, 2);

        var netTotal = total - (rawData.DiscountAmount ?? 0);

        return new QuotationFormDto
        {
            QuotationId = qid,
            ReferenceNumber = $"QT-{qdate.Year}-{qid:D5}",
            QuotationDate = qdate,
            ValidUntil = rawData.ValidUntil,
            PartyId = pid,
            PartyName = party?.PartyName,
            PartyPhone = party?.Phone,
            WarehouseId = rawData.WarehouseId,
            EmpId = rawData.EmpId,
            EmpName = empName,
            PricingType = rawData.PricingType ?? PricingTiers.Premium,
            TotalAmount = total,
            DiscountAmount = rawData.DiscountAmount,
            DiscountPercentage = discountPct,
            NetTotalAmount = netTotal,
            GrandTotal = rawData.GrandTotal ?? total,
            Notes = rawData.Notes,
            Status = string.IsNullOrWhiteSpace(rawData.Status) ? QuotationStatuses.Draft : rawData.Status,
            InvoiceId = rawData.InvoiceId,
            InvoiceReference = invoiceRef,
            CreatedBy = rawData.CreatedBy,
            CreatedAt = rawData.CAt ?? DateTime.Today,
            Items = items
        };
    }

    // ============================================================
    //  للطباعة
    // ============================================================
    public async Task<QuotationPrintDto?> GetQuotationForPrintAsync(int quotationId)
    {
        var form = await GetQuotationForEditAsync(quotationId);
        if (form == null) return null;

        var party = await _db.Parties.AsNoTracking()
            .Where(p => p.PartyId == form.PartyId)
            .Select(p => new { p.Address, p.Email, p.Phone, p.City })
            .FirstOrDefaultAsync();

        var dto = new QuotationPrintDto
        {
            Quotation = form,
            CustomerAddress = party?.Address,
            CustomerEmail = party?.Email,
            CustomerPhone = party?.Phone,
            CustomerCity = party?.City,
            CompanyName = "COCOBOLO"
        };

        try
        {
            var company = await _db.CompanyInfos.AsNoTracking().FirstOrDefaultAsync();
            if (company != null)
            {
                dto.CompanyName = GetStringProperty(company, "CompanyName")
                    ?? GetStringProperty(company, "Name") ?? "COCOBOLO";
                dto.CompanyPhone = GetStringProperty(company, "Phone");
                dto.CompanyAddress = GetStringProperty(company, "Address");
                dto.CompanyTaxNumber = GetStringProperty(company, "TaxNumber");
                dto.CompanyLogo = GetStringProperty(company, "LogoPath");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CompanyInfo read failed - using defaults");
        }

        return dto;
    }

    // ============================================================
    //  الإحصائيات (نفس الفكرة - بدون JOINs)
    // ============================================================
    public async Task<QuotationStatsDto> GetStatsAsync(DateTime? from = null, DateTime? to = null)
    {
        if (!HasPermission("View")) return new QuotationStatsDto();

        try
        {
            var query = _db.Quotations.AsNoTracking().AsQueryable();

            if (from.HasValue) query = query.Where(q => q.QuotationDate >= from.Value.Date);
            if (to.HasValue)
            {
                var endOfDay = to.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(q => q.QuotationDate <= endOfDay);
            }

            var today = DateTime.Today;

            // ✅ نجيب البيانات الخام بـ nullable casts
            var raw = await query.Select(q => new
            {
                Total = (decimal?)q.TotalAmount,
                Grand = q.GrandTotal,
                Stat = q.Status,
                InvId = q.InvoiceId,
                Valid = q.ValidUntil
            }).ToListAsync();

            // ✅ نحسب الإحصائيات في الذاكرة - مفيش أي مشكلة nulls
            var total = raw.Count;
            var converted = raw.Count(x => x.InvId != null);
            var totalValue = raw.Sum(x => x.Grand ?? x.Total ?? 0m);
            var convertedValue = raw.Where(x => x.InvId != null).Sum(x => x.Grand ?? x.Total ?? 0m);

            return new QuotationStatsDto
            {
                TotalCount = total,
                TotalValue = totalValue,
                DraftCount = raw.Count(x => string.IsNullOrEmpty(x.Stat) || x.Stat == QuotationStatuses.Draft),
                SentCount = raw.Count(x => x.Stat == QuotationStatuses.Sent),
                AcceptedCount = raw.Count(x => x.Stat == QuotationStatuses.Accepted),
                RejectedCount = raw.Count(x => x.Stat == QuotationStatuses.Rejected),
                ConvertedCount = converted,
                ConvertedValue = convertedValue,
                ExpiredCount = raw.Count(x => x.Valid.HasValue && x.Valid.Value < today && x.InvId == null),
                ConversionRate = total == 0 ? 0 : Math.Round(((decimal)converted / total) * 100, 1)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetStatsAsync failed");
            return new QuotationStatsDto();
        }
    }

    // ============================================================
    //  رقم العرض التالي
    // ============================================================
    public async Task<string> GenerateNextQuotationNumberAsync()
    {
        var year = DateTime.Now.Year;
        var countThisYear = await _db.Quotations.CountAsync(q => q.QuotationDate.Year == year);
        return $"QT-{year}-{(countThisYear + 1):D5}";
    }

    // ============================================================
    //  إنشاء عرض سعر
    // ============================================================
    public async Task<(bool Success, string Message, int? QuotationId)> CreateQuotationAsync(
    QuotationFormDto dto, string currentUserName)
{
    if (!HasPermission("Add"))
        return (false, "ليس لديك صلاحية إنشاء عروض الأسعار.", null);

    var validation = ValidateQuotation(dto);
    if (!validation.IsValid) return (false, validation.Message, null);

    await using var tx = await _db.Database.BeginTransactionAsync();
    try
    {
        CalculateTotals(dto);

        if (dto.EmpId is null or 0)
            dto.EmpId = await _invoiceService.GetEmployeeIdByUserNameAsync(currentUserName);

        // ✅ توليد الرقم وحفظه في قاعدة البيانات بدلاً من الـ DTO
        var refNumber = await GenerateNextQuotationNumberAsync();

        var quotation = new Quotation
        {
            ReferenceNumber = refNumber, // تأكد من إضافة هذا الحقل في موديل Quotation
            QuotationDate = dto.QuotationDate,
            ValidUntil = dto.ValidUntil,
            PartyId = dto.PartyId!.Value,
            WarehouseId = dto.WarehouseId,
            EmpId = dto.EmpId,
            PricingType = dto.PricingType,
            TotalAmount = dto.TotalAmount,
            DiscountAmount = dto.DiscountAmount ?? 0,
            GrandTotal = dto.GrandTotal,
            Notes = dto.Notes,
            Status = string.IsNullOrEmpty(dto.Status) ? QuotationStatuses.Draft : dto.Status,
            CreatedBy = currentUserName,
            CreatedAt = DateTime.Now
        };

        _db.Quotations.Add(quotation);
        await _db.SaveChangesAsync();

        foreach (var item in dto.Items)
        {
            _db.QuotationDetails.Add(new QuotationDetail
            {
                QuotationId = quotation.QuotationId,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                TotalAmount = item.TotalAmount,
                Notes = BuildDetailNotes(item.PricingTier, item.Notes)
            });
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        await _audit.LogAsync("Quotations", "Insert",
            quotation.QuotationId.ToString(), null, quotation, currentUserName);

        await SendQuotationNotificationAsync(quotation, currentUserName, "تم إنشاء عرض سعر جديد");

        return (true, "تم إنشاء عرض السعر بنجاح.", quotation.QuotationId);
    }
    catch (Exception ex)
    {
        await tx.RollbackAsync();
        _logger.LogError(ex, "Error creating quotation");
        return (false, "تعذّر حفظ عرض السعر.", null);
    }
}

    // ============================================================
    //  تعديل
    // ============================================================
    public async Task<(bool Success, string Message)> UpdateQuotationAsync(
        QuotationFormDto dto, string currentUserName)
    {
        if (!HasPermission("Edit"))
            return (false, "ليس لديك صلاحية تعديل عروض الأسعار.");

        var quotation = await _db.Quotations
            .FirstOrDefaultAsync(q => q.QuotationId == dto.QuotationId);

        if (quotation == null) return (false, "عرض السعر غير موجود.");
        if (quotation.InvoiceId != null)
            return (false, "لا يمكن تعديل عرض سعر تم تحويله لفاتورة.");

        var validation = ValidateQuotation(dto);
        if (!validation.IsValid) return (false, validation.Message);

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            CalculateTotals(dto);

            quotation.QuotationDate = dto.QuotationDate;
            quotation.ValidUntil = dto.ValidUntil;
            quotation.PartyId = dto.PartyId!.Value;
            quotation.WarehouseId = dto.WarehouseId;
            quotation.EmpId = dto.EmpId;
            quotation.PricingType = dto.PricingType;
            quotation.TotalAmount = dto.TotalAmount;
            quotation.DiscountAmount = dto.DiscountAmount ?? 0;
            quotation.GrandTotal = dto.GrandTotal;
            quotation.Notes = dto.Notes;
            quotation.Status = string.IsNullOrWhiteSpace(dto.Status) ? QuotationStatuses.Draft : dto.Status;

            var existing = await _db.QuotationDetails
                .Where(d => d.QuotationId == quotation.QuotationId)
                .ToListAsync();

            var incomingIds = dto.Items.Where(i => i.QuotationDetailId > 0)
                .Select(i => i.QuotationDetailId).ToHashSet();

            var toRemove = existing.Where(d => !incomingIds.Contains(d.QuotationDetailId)).ToList();
            if (toRemove.Any()) _db.QuotationDetails.RemoveRange(toRemove);

            foreach (var item in dto.Items)
            {
                if (item.QuotationDetailId > 0)
                {
                    var row = existing.FirstOrDefault(d => d.QuotationDetailId == item.QuotationDetailId);
                    if (row != null)
                    {
                        row.ProductId = item.ProductId;
                        row.Quantity = item.Quantity;
                        row.UnitPrice = item.UnitPrice;
                        row.TotalAmount = item.TotalAmount;
                        row.Notes = BuildDetailNotes(item.PricingTier, item.Notes);
                        continue;
                    }
                }

                _db.QuotationDetails.Add(new QuotationDetail
                {
                    QuotationId = quotation.QuotationId,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    TotalAmount = item.TotalAmount,
                    Notes = BuildDetailNotes(item.PricingTier, item.Notes)
                });
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            await _audit.LogAsync<object>("Quotations", "Update",
                quotation.QuotationId.ToString(), null, quotation, currentUserName);

            return (true, "تم تحديث عرض السعر بنجاح.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Error updating quotation {Id}", dto.QuotationId);
            return (false, "تعذّر تحديث عرض السعر.");
        }
    }

    // ============================================================
    //  تغيير الحالة
    // ============================================================
    public async Task<(bool Success, string Message)> ChangeStatusAsync(
        int quotationId, string newStatus, string currentUserName)
    {
        if (!HasPermission("Edit"))
            return (false, "ليس لديك صلاحية تغيير حالة عرض السعر.");

        var quotation = await _db.Quotations
            .FirstOrDefaultAsync(q => q.QuotationId == quotationId);

        if (quotation == null) return (false, "عرض السعر غير موجود.");
        if (quotation.InvoiceId != null)
            return (false, "لا يمكن تغيير حالة عرض سعر تم تحويله لفاتورة.");

        if (!QuotationStatuses.All.ContainsKey(newStatus))
            return (false, "حالة غير صحيحة.");

        var oldStatus = string.IsNullOrWhiteSpace(quotation.Status)
            ? QuotationStatuses.Draft : quotation.Status;

        if (oldStatus == newStatus)
            return (true, "الحالة مطابقة، لم يتم إجراء أي تغيير.");

        if (!AllowedTransitions.TryGetValue(oldStatus, out var allowed) || !allowed.Contains(newStatus))
        {
            var fromText = QuotationStatuses.All.TryGetValue(oldStatus, out var ft) ? ft : oldStatus;
            var toText = QuotationStatuses.All.TryGetValue(newStatus, out var tt) ? tt : newStatus;
            return (false, $"لا يمكن تغيير الحالة من '{fromText}' إلى '{toText}'.");
        }

        try
        {
            quotation.Status = newStatus;
            await _db.SaveChangesAsync();
            await _audit.LogAsync<object>("Quotations", "ChangeStatus",
                quotationId.ToString(), new { OldStatus = oldStatus },
                new { NewStatus = newStatus }, currentUserName);
            return (true, $"تم تغيير الحالة إلى: {QuotationStatuses.All[newStatus]}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing status of quotation {Id}", quotationId);
            return (false, "تعذّر تغيير حالة عرض السعر.");
        }
    }

    // ============================================================
    //  حذف
    // ============================================================
    public async Task<(bool Success, string Message)> DeleteQuotationAsync(
        int quotationId, string currentUserName)
    {
        if (!HasPermission("Delete"))
            return (false, "ليس لديك صلاحية حذف عروض الأسعار.");

        var quotation = await _db.Quotations.FirstOrDefaultAsync(q => q.QuotationId == quotationId);
        if (quotation == null) return (false, "عرض السعر غير موجود.");
        if (quotation.InvoiceId != null)
            return (false, "لا يمكن حذف عرض سعر تم تحويله لفاتورة.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var details = await _db.QuotationDetails
                .Where(d => d.QuotationId == quotationId).ToListAsync();
            if (details.Any()) _db.QuotationDetails.RemoveRange(details);
            _db.Quotations.Remove(quotation);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            await _audit.LogAsync("Quotations", "Delete",
                quotationId.ToString(), quotation, null, currentUserName);

            return (true, "تم حذف عرض السعر.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Error deleting quotation {Id}", quotationId);
            return (false, "تعذّر حذف عرض السعر.");
        }
    }

    // ============================================================
    //  تحويل لفاتورة
    // ============================================================
    public async Task<(bool Success, string Message, int? InvoiceId)> ConvertToInvoiceAsync(
    int quotationId, decimal initialPaidAmount, int? cashBoxId,
    string paymentMethod, string currentUserName)
{
    if (!HasPermission("Edit"))
        return (false, "ليس لديك صلاحية تحويل عرض السعر لفاتورة.", null);

    // ✅ تغيير Serializable إلى ReadCommitted لمنع الـ Deadlocks
    await using var tx = await _db.Database.BeginTransactionAsync(
        System.Data.IsolationLevel.ReadCommitted); 

    try 
    {
            var quotation = await _db.Quotations.FirstOrDefaultAsync(q => q.QuotationId == quotationId);
            if (quotation == null) { await tx.RollbackAsync(); return (false, "عرض السعر غير موجود.", null); }
            if (quotation.InvoiceId != null)
            {
                await tx.RollbackAsync();
                return (false, $"تم تحويل هذا العرض من قبل للفاتورة #{quotation.InvoiceId}.", quotation.InvoiceId);
            }

            // جلب الأصناف منفصلة عن المنتجات
            var dRows = await _db.QuotationDetails.AsNoTracking()
                .Where(d => d.QuotationId == quotationId)
                .Select(d => new { d.ProductId, d.Quantity, d.UnitPrice, d.Notes })
                .ToListAsync();

            if (!dRows.Any()) { await tx.RollbackAsync(); return (false, "لا توجد أصناف في عرض السعر.", null); }

            var productIds = dRows.Select(d => d.ProductId).Distinct().ToList();
            var products = await _db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.ProductId))
                .Select(p => new {
                    p.ProductId, p.ProductName, p.ProductDescription,
                    p.SuggestedSalePrice, p.SuggestedSalePriceElite,
                    p.PurchasePrice, p.PurchasePriceElite, p.Period
                })
                .ToListAsync();
            var prodDict = products.ToDictionary(p => p.ProductId);

            var warehouseId = quotation.WarehouseId;
            if (!warehouseId.HasValue)
            {
                warehouseId = await _db.Warehouses
                    .Where(w => w.IsActive == true)
                    .OrderBy(w => w.WarehouseId)
                    .Select(w => (int?)w.WarehouseId)
                    .FirstOrDefaultAsync();
            }
            if (!warehouseId.HasValue)
            {
                await tx.RollbackAsync();
                return (false, "يرجى اختيار المخزن قبل التحويل.", null);
            }

            var invoiceForm = new InvoiceFormDto
            {
                TransactionDate = DateTime.Now,
                PartyId = quotation.PartyId,
                WarehouseId = warehouseId,
                EmpId = quotation.EmpId,
                DueDate = quotation.ValidUntil,
                Notes = $"تحويل من عرض السعر QT-{quotation.QuotationDate.Year}-{quotation.QuotationId:D5}" +
                        (string.IsNullOrEmpty(quotation.Notes) ? "" : $"\n{quotation.Notes}"),
                DiscountAmount = quotation.DiscountAmount,
                DiscountPercentage = quotation.DiscountAmount.HasValue && quotation.TotalAmount > 0
                    ? Math.Round((quotation.DiscountAmount.Value / quotation.TotalAmount) * 100, 2)
                    : null,
                PaidAmount = initialPaidAmount,
                CashBoxId = cashBoxId,
                PaymentMethod = paymentMethod,
                Items = dRows.Select(d =>
                {
                    prodDict.TryGetValue(d.ProductId, out var p);
                    return new InvoiceItemDto
                    {
                        ProductId = d.ProductId,
                        ProductName = p?.ProductName,
                        ProductDescription = p?.ProductDescription,
                        Quantity = d.Quantity,
                        UnitPrice = d.UnitPrice,
                        Notes = StripTierTag(d.Notes),
                        PricingTier = ExtractTier(d.Notes),
                        SalePricePremium = p?.SuggestedSalePrice,
                        SalePriceElite = p?.SuggestedSalePriceElite,
                        PurchasePricePremium = p?.PurchasePrice,
                        PurchasePriceElite = p?.PurchasePriceElite,
                        Period = p?.Period
                    };
                }).ToList()
            };

            var (ok, msg, invoiceId, mirrorId) = await _invoiceService.CreateInvoiceAsync(invoiceForm, currentUserName);
            if (!ok || invoiceId == null) { await tx.RollbackAsync(); return (false, msg, null); }

            quotation.InvoiceId = invoiceId.Value;
            quotation.Status = QuotationStatuses.Converted;
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            await _audit.LogAsync("Quotations", "ConvertToInvoice",
                quotationId.ToString(), null,
                new { InvoiceId = invoiceId.Value, MirrorId = mirrorId }, currentUserName);

            return (true, "تم تحويل عرض السعر إلى فاتورة بنجاح. ✅", invoiceId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Error converting quotation {Id}", quotationId);
            return (false, "تعذّر تحويل عرض السعر إلى فاتورة.", null);
        }
    }

    // ============================================================
    //  Helpers
    // ============================================================
    private async Task SendQuotationNotificationAsync(Quotation quotation, string actor, string action)
    {
        try
        {
            var partyName = await _db.Parties.AsNoTracking()
                .Where(p => p.PartyId == quotation.PartyId)
                .Select(p => p.PartyName).FirstOrDefaultAsync() ?? "غير محدد";

            var title = "📋 إشعار عرض سعر";
            var message = $"{action} - QT-{quotation.QuotationDate.Year}-{quotation.QuotationId:D5} " +
                          $"للعميل {partyName} بقيمة {quotation.GrandTotal:N2} ج بواسطة {actor}";

            await _notify.NotifyRoleAsync(title, message, SystemRoles.Admin, actor,
                "quotations", "Quotations", quotation.QuotationId);
            await _notify.NotifyRoleAsync(title, message, SystemRoles.SalesManager, actor,
                "quotations", "Quotations", quotation.QuotationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send notification for quotation {Id}", quotation.QuotationId);
        }
    }

    private static (bool IsValid, string Message) ValidateQuotation(QuotationFormDto dto)
    {
        if (dto.PartyId is null or 0) return (false, "يرجى اختيار العميل.");
        if (dto.Items == null || !dto.Items.Any()) return (false, "يجب إضافة صنف واحد على الأقل.");
        if (dto.Items.Any(i => i.ProductId == 0)) return (false, "هناك صنف بدون منتج محدد.");
        if (dto.Items.Any(i => i.Quantity <= 0)) return (false, "يجب أن تكون الكمية أكبر من صفر.");
        if (dto.Items.Any(i => i.UnitPrice < 0)) return (false, "السعر لا يمكن أن يكون سالباً.");
        if (dto.QuotationDate == default) return (false, "تاريخ عرض السعر غير صحيح.");
        if (dto.ValidUntil.HasValue && dto.ValidUntil.Value.Date < dto.QuotationDate.Date)
            return (false, "تاريخ انتهاء الصلاحية يجب أن يكون بعد تاريخ العرض.");
        return (true, "");
    }

    private static void CalculateTotals(QuotationFormDto dto)
    {
        dto.TotalAmount = Math.Round(dto.Items.Sum(i => i.TotalAmount), 2);
        if (dto.DiscountPercentage.HasValue && dto.DiscountPercentage.Value > 0)
            dto.DiscountAmount = Math.Round(dto.TotalAmount * (dto.DiscountPercentage.Value / 100m), 2);
        else if (dto.DiscountAmount.HasValue && dto.DiscountAmount.Value > 0)
        {
            if (dto.DiscountAmount.Value > dto.TotalAmount)
                dto.DiscountAmount = dto.TotalAmount;
        }
        else dto.DiscountAmount = 0;
        dto.NetTotalAmount = dto.TotalAmount - (dto.DiscountAmount ?? 0);
        dto.GrandTotal = dto.NetTotalAmount ?? 0;
    }

    private static string BuildDetailNotes(string? tier, string? userNotes)
    {
        var safeTier = string.IsNullOrWhiteSpace(tier) ? PricingTiers.Premium : tier;
        return string.IsNullOrWhiteSpace(userNotes)
            ? $"[{safeTier}]"
            : $"[{safeTier}] {userNotes.Trim()}";
    }

    private static string ExtractTier(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return PricingTiers.Premium;
        if (notes.StartsWith($"[{PricingTiers.Elite}]", StringComparison.Ordinal))
            return PricingTiers.Elite;
        return PricingTiers.Premium;
    }

    private static string? StripTierTag(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return null;
        var idx = notes.IndexOf(']');
        if (notes.StartsWith("[") && idx > 0)
        {
            var rest = notes.Substring(idx + 1).Trim();
            return string.IsNullOrEmpty(rest) ? null : rest;
        }
        return notes;
    }

    private static string? GetStringProperty(object obj, string propertyName)
    {
        var prop = obj.GetType().GetProperty(propertyName);
        return prop?.GetValue(obj)?.ToString();
    }
}
