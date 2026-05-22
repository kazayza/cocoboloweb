using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class QuotationService : IQuotationService
{
    private readonly db24804Context _db;
    private readonly IAuditService _audit;
    private readonly NotificationService _notify;
    private readonly IInvoiceService _invoiceService;

    public QuotationService(
        db24804Context db,
        IAuditService audit,
        NotificationService notify,
        IInvoiceService invoiceService)
    {
        _db = db;
        _audit = audit;
        _notify = notify;
        _invoiceService = invoiceService;
    }

    // ============================================================
    //  قائمة عروض الأسعار
    // ============================================================
    public async Task<PagedResult<QuotationListDto>> GetQuotationsAsync(QuotationFilterDto filter)
    {
        var query = _db.Quotations.AsNoTracking().AsQueryable();

        // البحث بالعربي (جلب العملاء أولاً)
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();

            var allParties = await _db.Parties
                .AsNoTracking()
                .Select(p => new { p.PartyId, p.PartyName, p.Phone })
                .ToListAsync();

            var matchingPartyIds = allParties
                .Where(p => (p.PartyName ?? "").ContainsArabic(s) ||
                            (p.Phone ?? "").ContainsArabic(s))
                .Select(p => p.PartyId)
                .ToList();

            query = query.Where(q =>
                matchingPartyIds.Contains(q.PartyId) ||
                (q.Notes != null && q.Notes.Contains(s)));
        }

        if (filter.PartyId.HasValue)
            query = query.Where(q => q.PartyId == filter.PartyId.Value);

        if (filter.DateFrom.HasValue)
            query = query.Where(q => q.QuotationDate >= filter.DateFrom.Value.Date);

        if (filter.DateTo.HasValue)
            query = query.Where(q => q.QuotationDate <= filter.DateTo.Value.Date.AddDays(1).AddTicks(-1));

        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(q => q.Status == filter.Status);

        if (filter.IsConverted.HasValue)
        {
            if (filter.IsConverted.Value)
                query = query.Where(q => q.InvoiceId != null);
            else
                query = query.Where(q => q.InvoiceId == null);
        }

        if (filter.IsExpired.HasValue && filter.IsExpired.Value)
        {
            var today = DateTime.Today;
            query = query.Where(q => q.ValidUntil.HasValue 
                                  && q.ValidUntil.Value < today 
                                  && q.InvoiceId == null);
        }

        var totalCount = await query.CountAsync();

        // الترتيب
        query = filter.SortBy switch
        {
            "GrandTotal" => filter.SortDescending
                ? query.OrderByDescending(q => q.GrandTotal)
                : query.OrderBy(q => q.GrandTotal),
            "PartyName" => filter.SortDescending
                ? query.OrderByDescending(q => q.PartyId)
                : query.OrderBy(q => q.PartyId),
            _ => filter.SortDescending
                ? query.OrderByDescending(q => q.QuotationDate).ThenByDescending(q => q.QuotationId)
                : query.OrderBy(q => q.QuotationDate).ThenBy(q => q.QuotationId)
        };

        var items = await query
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(q => new QuotationListDto
            {
                QuotationId = q.QuotationId,
                ReferenceNumber = $"QT-{q.QuotationDate.Year}-{q.QuotationId:D5}",
                QuotationDate = q.QuotationDate,
                PartyId = q.PartyId,
                PartyName = _db.Parties.Where(p => p.PartyId == q.PartyId)
                    .Select(p => p.PartyName).FirstOrDefault() ?? "",
                PartyPhone = _db.Parties.Where(p => p.PartyId == q.PartyId)
                    .Select(p => p.Phone).FirstOrDefault(),
                WarehouseId = q.WarehouseId,
                WarehouseName = q.WarehouseId == null ? null :
                    _db.Warehouses.Where(w => w.WarehouseId == q.WarehouseId)
                        .Select(w => w.WarehouseName).FirstOrDefault(),
                EmpId = q.EmpId,
                EmpName = q.EmpId == null ? null :
                    _db.Employees.Where(e => e.EmployeeId == q.EmpId)
                        .Select(e => e.FullName).FirstOrDefault(),
                PricingType = q.PricingType,
                TotalAmount = q.TotalAmount,
                DiscountAmount = q.DiscountAmount,
                GrandTotal = q.GrandTotal ?? q.TotalAmount,
                ItemsCount = _db.QuotationDetails.Count(d => d.QuotationId == q.QuotationId),
                Status = q.Status,
                InvoiceId = q.InvoiceId,
                InvoiceReference = q.InvoiceId == null ? null :
                    _db.Transactions.Where(t => t.TransactionId == q.InvoiceId)
                        .Select(t => t.ReferenceNumber).FirstOrDefault(),
                ValidUntil = q.ValidUntil,
                CreatedBy = q.CreatedBy ?? "",
                CreatedAt = q.CreatedAt
            })
            .ToListAsync();

        return new PagedResult<QuotationListDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize
        };
    }

    // ============================================================
    //  جلب عرض السعر للتعديل / العرض
    // ============================================================
    public async Task<QuotationFormDto?> GetQuotationForEditAsync(int quotationId)
    {
        var q = await _db.Quotations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.QuotationId == quotationId);

        if (q == null) return null;

        var party = await _db.Parties
            .AsNoTracking()
            .Where(p => p.PartyId == q.PartyId)
            .Select(p => new { p.PartyName, p.Phone })
            .FirstOrDefaultAsync();

        var empName = q.EmpId == null ? null
            : await _db.Employees.Where(e => e.EmployeeId == q.EmpId)
                .Select(e => e.FullName).FirstOrDefaultAsync();

        var items = await (from d in _db.QuotationDetails.AsNoTracking()
                           join p in _db.Products.AsNoTracking() on d.ProductId equals p.ProductId
                           where d.QuotationId == quotationId
                           select new QuotationItemDto
                           {
                               QuotationDetailId = d.QuotationDetailId,
                               ProductId = d.ProductId,
                               ProductName = p.ProductName,
                               ProductDescription = p.ProductDescription,
                               Quantity = d.Quantity,
                               UnitPrice = d.UnitPrice,
                               Notes = d.Notes,
                               PricingTier = d.Notes != null && d.Notes.Contains("[Elite]")
                                   ? PricingTiers.Elite : PricingTiers.Premium,
                               SalePricePremium = p.SuggestedSalePrice,
                               SalePriceElite = p.SuggestedSalePriceElite,
                               PurchasePricePremium = p.PurchasePrice,
                               PurchasePriceElite = p.PurchasePriceElite,
                               Period = p.Period
                           }).ToListAsync();

        var invoiceRef = q.InvoiceId == null ? null
            : await _db.Transactions.Where(t => t.TransactionId == q.InvoiceId)
                .Select(t => t.ReferenceNumber).FirstOrDefaultAsync();

        // حساب الخصم %
        decimal? discountPct = null;
        if (q.DiscountAmount.HasValue && q.DiscountAmount.Value > 0 && q.TotalAmount > 0)
        {
            discountPct = Math.Round((q.DiscountAmount.Value / q.TotalAmount) * 100, 2);
        }

        var netTotal = q.TotalAmount - (q.DiscountAmount ?? 0);

        return new QuotationFormDto
        {
            QuotationId = q.QuotationId,
            ReferenceNumber = $"QT-{q.QuotationDate.Year}-{q.QuotationId:D5}",
            QuotationDate = q.QuotationDate,
            ValidUntil = q.ValidUntil,
            PartyId = q.PartyId,
            PartyName = party?.PartyName,
            PartyPhone = party?.Phone,
            WarehouseId = q.WarehouseId,
            EmpId = q.EmpId,
            EmpName = empName,
            PricingType = q.PricingType ?? PricingTiers.Premium,
            TotalAmount = q.TotalAmount,
            DiscountAmount = q.DiscountAmount,
            DiscountPercentage = discountPct,
            NetTotalAmount = netTotal,
            GrandTotal = q.GrandTotal ?? q.TotalAmount,
            Notes = q.Notes,
            Status = q.Status ?? QuotationStatuses.Draft,
            InvoiceId = q.InvoiceId,
            InvoiceReference = invoiceRef,
            CreatedBy = q.CreatedBy,
            CreatedAt = q.CreatedAt,
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
            .FirstOrDefaultAsync(p => p.PartyId == form.PartyId);

        var company = await _db.CompanyInfos.AsNoTracking().FirstOrDefaultAsync();

        var dto = new QuotationPrintDto
        {
            Quotation = form,
            CustomerAddress = party?.Address,
            CustomerEmail = party?.Email,
            CustomerPhone = party?.Phone,
            CustomerCity = party?.City
        };

        if (company != null)
        {
            var t = company.GetType();
            dto.CompanyName = t.GetProperty("CompanyName")?.GetValue(company)?.ToString()
                              ?? t.GetProperty("Name")?.GetValue(company)?.ToString()
                              ?? "COCOBOLO";
            dto.CompanyPhone = t.GetProperty("Phone")?.GetValue(company)?.ToString();
            dto.CompanyAddress = t.GetProperty("Address")?.GetValue(company)?.ToString();
            dto.CompanyTaxNumber = t.GetProperty("TaxNumber")?.GetValue(company)?.ToString();
            dto.CompanyLogo = t.GetProperty("LogoPath")?.GetValue(company)?.ToString();
        }
        else
        {
            dto.CompanyName = "COCOBOLO";
        }

        return dto;
    }

    // ============================================================
    //  الإحصائيات
    // ============================================================
    public async Task<QuotationStatsDto> GetStatsAsync(DateTime? from = null, DateTime? to = null)
    {
        var query = _db.Quotations.AsNoTracking().AsQueryable();
        if (from.HasValue) query = query.Where(q => q.QuotationDate >= from.Value.Date);
        if (to.HasValue) query = query.Where(q => q.QuotationDate <= to.Value.Date.AddDays(1).AddTicks(-1));

        var today = DateTime.Today;
        var total = await query.CountAsync();
        var converted = await query.CountAsync(q => q.InvoiceId != null);
        var convertedValue = await query.Where(q => q.InvoiceId != null)
            .SumAsync(q => (decimal?)(q.GrandTotal ?? q.TotalAmount)) ?? 0;

        return new QuotationStatsDto
        {
            TotalCount = total,
            TotalValue = await query.SumAsync(q => (decimal?)(q.GrandTotal ?? q.TotalAmount)) ?? 0,
            DraftCount = await query.CountAsync(q => q.Status == QuotationStatuses.Draft),
            SentCount = await query.CountAsync(q => q.Status == QuotationStatuses.Sent),
            AcceptedCount = await query.CountAsync(q => q.Status == QuotationStatuses.Accepted),
            RejectedCount = await query.CountAsync(q => q.Status == QuotationStatuses.Rejected),
            ConvertedCount = converted,
            ExpiredCount = await query.CountAsync(q => q.ValidUntil.HasValue 
                                                    && q.ValidUntil.Value < today 
                                                    && q.InvoiceId == null),
            ConvertedValue = convertedValue,
            ConversionRate = total == 0 ? 0 : Math.Round(((decimal)converted / total) * 100, 1)
        };
    }

    public async Task<string> GenerateNextQuotationNumberAsync()
    {
        var year = DateTime.Now.Year;
        var maxId = await _db.Quotations
            .Where(q => q.QuotationDate.Year == year)
            .MaxAsync(q => (int?)q.QuotationId) ?? 0;
        return $"QT-{year}-{(maxId + 1):D5}";
    }

    // ============================================================
    //  إنشاء عرض سعر
    // ============================================================
    public async Task<(bool Success, string Message, int? QuotationId)> CreateQuotationAsync(
        QuotationFormDto dto, string currentUserName)
    {
        var validation = ValidateQuotation(dto);
        if (!validation.IsValid) return (false, validation.Message, null);

        try
        {
            CalculateTotals(dto);

            // جلب EmpId تلقائياً من اليوزر لو مش محدد
            if (dto.EmpId == null || dto.EmpId == 0)
            {
                dto.EmpId = await _invoiceService.GetEmployeeIdByUserNameAsync(currentUserName);
            }

            var quotation = new Quotation
            {
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
                    Notes = string.IsNullOrEmpty(item.Notes)
                        ? $"[{item.PricingTier}]"
                        : $"[{item.PricingTier}] {item.Notes}"
                });
            }

            await _db.SaveChangesAsync();

            await _audit.LogAsync("Quotations", "Insert",
                quotation.QuotationId.ToString(), null, quotation, currentUserName);

            // إشعارات
            await SendQuotationNotificationAsync(quotation, currentUserName, "تم إنشاء عرض سعر جديد");

            return (true, "تم إنشاء عرض السعر بنجاح.", quotation.QuotationId);
        }
        catch (Exception ex)
        {
            return (false, $"حدث خطأ: {ex.Message}", null);
        }
    }

    // ============================================================
    //  تعديل
    // ============================================================
    public async Task<(bool Success, string Message)> UpdateQuotationAsync(
        QuotationFormDto dto, string currentUserName)
    {
        var quotation = await _db.Quotations
            .FirstOrDefaultAsync(q => q.QuotationId == dto.QuotationId);

        if (quotation == null) return (false, "عرض السعر غير موجود.");
        if (quotation.InvoiceId != null)
            return (false, "لا يمكن تعديل عرض سعر تم تحويله لفاتورة.");

        var validation = ValidateQuotation(dto);
        if (!validation.IsValid) return (false, validation.Message);

        try
        {
            CalculateTotals(dto);

            var oldSnapshot = new
            {
                quotation.TotalAmount,
                quotation.DiscountAmount,
                quotation.GrandTotal,
                quotation.Notes,
                quotation.Status
            };

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
            quotation.Status = dto.Status;

            // امسح القديم وضيف الجديد
            var oldDetails = await _db.QuotationDetails
                .Where(d => d.QuotationId == quotation.QuotationId).ToListAsync();
            _db.QuotationDetails.RemoveRange(oldDetails);

            foreach (var item in dto.Items)
            {
                _db.QuotationDetails.Add(new QuotationDetail
                {
                    QuotationId = quotation.QuotationId,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    TotalAmount = item.TotalAmount,
                    Notes = string.IsNullOrEmpty(item.Notes)
                        ? $"[{item.PricingTier}]"
                        : $"[{item.PricingTier}] {item.Notes}"
                });
            }

            await _db.SaveChangesAsync();

            await _audit.LogAsync<object>("Quotations", "Update",
                quotation.QuotationId.ToString(), oldSnapshot, quotation, currentUserName);

            return (true, "تم تحديث عرض السعر بنجاح.");
        }
        catch (Exception ex)
        {
            return (false, $"حدث خطأ: {ex.Message}");
        }
    }

    // ============================================================
    //  تغيير الحالة
    // ============================================================
    public async Task<(bool Success, string Message)> ChangeStatusAsync(
        int quotationId, string newStatus, string currentUserName)
    {
        var quotation = await _db.Quotations
            .FirstOrDefaultAsync(q => q.QuotationId == quotationId);

        if (quotation == null) return (false, "عرض السعر غير موجود.");
        if (quotation.InvoiceId != null)
            return (false, "لا يمكن تغيير حالة عرض سعر تم تحويله لفاتورة.");

        if (!QuotationStatuses.All.ContainsKey(newStatus))
            return (false, "حالة غير صحيحة.");

        var oldStatus = quotation.Status;
        quotation.Status = newStatus;
        await _db.SaveChangesAsync();

        await _audit.LogAsync<object>("Quotations", "ChangeStatus",
            quotationId.ToString(),
            new { OldStatus = oldStatus },
            new { NewStatus = newStatus },
            currentUserName);

        return (true, $"تم تغيير الحالة إلى: {QuotationStatuses.All[newStatus]}");
    }

    // ============================================================
    //  حذف
    // ============================================================
    public async Task<(bool Success, string Message)> DeleteQuotationAsync(
        int quotationId, string currentUserName)
    {
        var quotation = await _db.Quotations.FirstOrDefaultAsync(q => q.QuotationId == quotationId);
        if (quotation == null) return (false, "عرض السعر غير موجود.");
        if (quotation.InvoiceId != null)
            return (false, "لا يمكن حذف عرض سعر تم تحويله لفاتورة.");

        var details = await _db.QuotationDetails
            .Where(d => d.QuotationId == quotationId).ToListAsync();
        _db.QuotationDetails.RemoveRange(details);
        _db.Quotations.Remove(quotation);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("Quotations", "Delete",
            quotationId.ToString(), quotation, null, currentUserName);

        return (true, "تم حذف عرض السعر.");
    }

    // ============================================================
    //  ⭐⭐⭐ تحويل عرض السعر لفاتورة (مع المرآة)
    // ============================================================
    public async Task<(bool Success, string Message, int? InvoiceId)> ConvertToInvoiceAsync(
        int quotationId, decimal initialPaidAmount, int? cashBoxId,
        string paymentMethod, string currentUserName)
    {
        var quotation = await _db.Quotations
            .FirstOrDefaultAsync(q => q.QuotationId == quotationId);

        if (quotation == null) return (false, "عرض السعر غير موجود.", null);
        if (quotation.InvoiceId != null)
            return (false, $"تم تحويل هذا العرض من قبل للفاتورة #{quotation.InvoiceId}.", quotation.InvoiceId);

        // جلب الأصناف مع بيانات المنتج (عشان نجيب الـ Purchase Prices)
        var details = await (from d in _db.QuotationDetails
                             join p in _db.Products on d.ProductId equals p.ProductId
                             where d.QuotationId == quotationId
                             select new
                             {
                                 Detail = d,
                                 Product = p
                             }).ToListAsync();

        if (!details.Any())
            return (false, "لا توجد أصناف في عرض السعر.", null);

        // التأكد من وجود WarehouseId
        var warehouseId = quotation.WarehouseId;
        if (!warehouseId.HasValue)
        {
            var defaultWarehouse = await _db.Warehouses
                .Where(w => w.IsActive == true)
                .OrderBy(w => w.WarehouseId)
                .FirstOrDefaultAsync();
            warehouseId = defaultWarehouse?.WarehouseId;
        }

        if (!warehouseId.HasValue)
            return (false, "يرجى اختيار المخزن قبل التحويل.", null);

        // ⭐⭐⭐ تجهيز الـ InvoiceFormDto بكل البيانات المطلوبة للمرآة
        var invoiceForm = new InvoiceFormDto
        {
            TransactionDate = DateTime.Now,
            PartyId = quotation.PartyId,
            WarehouseId = warehouseId,
            EmpId = quotation.EmpId,
            DueDate = quotation.ValidUntil,
            Notes = $"تحويل من عرض السعر QT-{quotation.QuotationDate.Year}-{quotation.QuotationId:D5}" +
                    (string.IsNullOrEmpty(quotation.Notes) ? "" : $"\n{quotation.Notes}"),

            // الإجماليات والخصم
            DiscountAmount = quotation.DiscountAmount,
            DiscountPercentage = quotation.DiscountAmount.HasValue && quotation.TotalAmount > 0
                ? Math.Round((quotation.DiscountAmount.Value / quotation.TotalAmount) * 100, 2)
                : null,

            // الدفعة
            PaidAmount = initialPaidAmount,
            CashBoxId = cashBoxId,
            PaymentMethod = paymentMethod,

            // ⭐ الأصناف بكل البيانات (مهم جداً للمرآة)
            Items = details.Select(x => new InvoiceItemDto
            {
                ProductId = x.Detail.ProductId,
                ProductName = x.Product.ProductName,
                ProductDescription = x.Product.ProductDescription,
                Quantity = x.Detail.Quantity,
                UnitPrice = x.Detail.UnitPrice,
                Notes = x.Detail.Notes,
                PricingTier = x.Detail.Notes != null && x.Detail.Notes.Contains("[Elite]")
                    ? PricingTiers.Elite : PricingTiers.Premium,

                // ⭐⭐⭐ الأسعار للمرآة - بدون دول مفيش فاتورة مشتريات
                SalePricePremium = x.Product.SuggestedSalePrice,
                SalePriceElite = x.Product.SuggestedSalePriceElite,
                PurchasePricePremium = x.Product.PurchasePrice,
                PurchasePriceElite = x.Product.PurchasePriceElite,
                Period = x.Product.Period
            }).ToList()
        };

        // إرسال لـ InvoiceService (هيعمل البيع + المشتريات المرآة + المخزون)
        var (ok, msg, invoiceId, mirrorId) = await _invoiceService.CreateInvoiceAsync(invoiceForm, currentUserName);

        if (!ok || invoiceId == null) return (false, msg, null);

        // ربط عرض السعر بالفاتورة + تحديث الحالة
        quotation.InvoiceId = invoiceId.Value;
        quotation.Status = QuotationStatuses.Converted;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("Quotations", "ConvertToInvoice",
            quotationId.ToString(), null,
            new { InvoiceId = invoiceId.Value, MirrorId = mirrorId }, currentUserName);

        return (true, $"تم تحويل عرض السعر إلى فاتورة بنجاح. ✅", invoiceId);
    }

    // ============================================================
    //  Helpers
    // ============================================================
    private async Task SendQuotationNotificationAsync(
        Quotation quotation, string actor, string action)
    {
        try
        {
            var partyName = await _db.Parties
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
            Console.WriteLine($"[QuotationService.Notify] {ex.Message}");
        }
    }

    private (bool IsValid, string Message) ValidateQuotation(QuotationFormDto dto)
    {
        if (dto.PartyId == null || dto.PartyId == 0)
            return (false, "يرجى اختيار العميل.");
        if (dto.Items == null || !dto.Items.Any())
            return (false, "يجب إضافة صنف واحد على الأقل.");
        if (dto.Items.Any(i => i.ProductId == 0))
            return (false, "هناك صنف بدون منتج محدد.");
        if (dto.Items.Any(i => i.Quantity <= 0))
            return (false, "يجب أن تكون الكمية أكبر من صفر لكل صنف.");
        if (dto.Items.Any(i => i.UnitPrice < 0))
            return (false, "السعر لا يمكن أن يكون سالباً.");

        return (true, "");
    }

    private void CalculateTotals(QuotationFormDto dto)
    {
        dto.TotalAmount = Math.Round(dto.Items.Sum(i => i.TotalAmount), 2);

        if (dto.DiscountPercentage.HasValue && dto.DiscountPercentage.Value > 0)
            dto.DiscountAmount = Math.Round(dto.TotalAmount * (dto.DiscountPercentage.Value / 100m), 2);

        dto.DiscountAmount ??= 0;

        dto.NetTotalAmount = dto.TotalAmount - (dto.DiscountAmount ?? 0);
        dto.GrandTotal = dto.NetTotalAmount ?? 0;
    }
}