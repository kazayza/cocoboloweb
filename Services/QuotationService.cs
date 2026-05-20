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

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();
            query = query.Where(q =>
                _db.Parties.Any(p => p.PartyId == q.PartyId && p.PartyName.Contains(s)) ||
                (q.Notes != null && q.Notes.Contains(s)));
        }

        if (filter.PartyId.HasValue)
            query = query.Where(q => q.PartyId == filter.PartyId.Value);

        if (filter.DateFrom.HasValue)
            query = query.Where(q => q.QuotationDate >= filter.DateFrom.Value.Date);

        if (filter.DateTo.HasValue)
            query = query.Where(q => q.QuotationDate <= filter.DateTo.Value.Date.AddDays(1).AddTicks(-1));

        if (filter.IsConverted.HasValue)
        {
            if (filter.IsConverted.Value)
                query = query.Where(q => q.InvoiceId != null);
            else
                query = query.Where(q => q.InvoiceId == null);
        }

        var totalCount = await query.CountAsync();

        query = filter.SortDescending
            ? query.OrderByDescending(q => q.QuotationDate).ThenByDescending(q => q.QuotationId)
            : query.OrderBy(q => q.QuotationDate).ThenBy(q => q.QuotationId);

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
                PricingType = q.PricingType,
                TotalAmount = q.TotalAmount,
                GrandTotal = q.GrandTotal ?? q.TotalAmount,
                ItemsCount = _db.QuotationDetails.Count(d => d.QuotationId == q.QuotationId),
                Status = q.InvoiceId != null ? QuotationStatuses.Converted : QuotationStatuses.Draft,
                InvoiceId = q.InvoiceId,
                InvoiceReference = q.InvoiceId == null ? null :
                    _db.Transactions.Where(t => t.TransactionId == q.InvoiceId)
                        .Select(t => t.ReferenceNumber).FirstOrDefault(),
                CreatedBy = q.CreatedBy ?? "",
                CreatedAt = q.CreatedAt
            })
            .ToListAsync();

        // فلترة بالحالة بعد الجلب (الحالة محسوبة)
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            items = items.Where(i => i.Status == filter.Status).ToList();
        }

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

        var partyName = await _db.Parties
            .Where(p => p.PartyId == q.PartyId)
            .Select(p => p.PartyName).FirstOrDefaultAsync();

        var items = await (from d in _db.QuotationDetails.AsNoTracking()
                           join p in _db.Products.AsNoTracking() on d.ProductId equals p.ProductId
                           where d.QuotationId == quotationId
                           select new QuotationItemDto
                           {
                               QuotationDetailId = d.QuotationDetailId,
                               ProductId = d.ProductId,
                               ProductName = p.ProductName,
                               ProductDescription = p.ProductDescription,
                               ProductImagePath = _db.ProductImages
                                   .Where(im => im.ProductId == p.ProductId)
                                   .Select(im => im.ImagePath).FirstOrDefault(),
                               Quantity = d.Quantity,
                               UnitPrice = d.UnitPrice,
                               Notes = d.Notes,
                               PricingTier = d.Notes != null && d.Notes.Contains("[Elite]")
                                   ? PricingTiers.Elite : PricingTiers.Premium,
                               SalePricePremium = p.SuggestedSalePrice,
                               SalePriceElite = p.SuggestedSalePriceElite
                           }).ToListAsync();

        var invoiceRef = q.InvoiceId == null ? null
            : await _db.Transactions.Where(t => t.TransactionId == q.InvoiceId)
                .Select(t => t.ReferenceNumber).FirstOrDefaultAsync();

        return new QuotationFormDto
        {
            QuotationId = q.QuotationId,
            ReferenceNumber = $"QT-{q.QuotationDate.Year}-{q.QuotationId:D5}",
            QuotationDate = q.QuotationDate,
            PartyId = q.PartyId,
            PartyName = partyName,
            WarehouseId = q.WarehouseId,
            PricingType = q.PricingType ?? PricingTiers.Premium,
            TotalAmount = q.TotalAmount,
            GrandTotal = q.GrandTotal ?? q.TotalAmount,
            Notes = q.Notes,
            InvoiceId = q.InvoiceId,
            InvoiceReference = invoiceRef,
            Status = q.InvoiceId != null ? QuotationStatuses.Converted : QuotationStatuses.Draft,
            Items = items
        };
    }

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
                              ?? "COCOBOLO";
            dto.CompanyPhone = t.GetProperty("Phone")?.GetValue(company)?.ToString();
            dto.CompanyAddress = t.GetProperty("Address")?.GetValue(company)?.ToString();
            dto.CompanyTaxNumber = t.GetProperty("TaxNumber")?.GetValue(company)?.ToString();
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

        var total = await query.CountAsync();
        var converted = await query.CountAsync(q => q.InvoiceId != null);
        var convertedValue = await query.Where(q => q.InvoiceId != null)
            .SumAsync(q => (decimal?)(q.GrandTotal ?? q.TotalAmount)) ?? 0;

        return new QuotationStatsDto
        {
            TotalCount = total,
            TotalValue = await query.SumAsync(q => (decimal?)(q.GrandTotal ?? q.TotalAmount)) ?? 0,
            ConvertedCount = converted,
            ConvertedValue = convertedValue,
            PendingCount = total - converted,
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

            var quotation = new Quotation
            {
                QuotationDate = dto.QuotationDate,
                PartyId = dto.PartyId!.Value,
                WarehouseId = dto.WarehouseId,
                PricingType = dto.PricingType,
                TotalAmount = dto.TotalAmount,
                GrandTotal = dto.GrandTotal,
                Notes = dto.Notes,
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
            try
            {
                var partyName = await _db.Parties.Where(p => p.PartyId == dto.PartyId)
                    .Select(p => p.PartyName).FirstOrDefaultAsync() ?? "غير محدد";
                var msg = $"تم إنشاء عرض سعر QT-{quotation.QuotationDate.Year}-{quotation.QuotationId:D5} " +
                          $"للعميل {partyName} بقيمة {quotation.GrandTotal:N2} ج بواسطة {currentUserName}";
                await _notify.NotifyRoleAsync("📋 إشعار عرض سعر", msg, SystemRoles.Admin, currentUserName,
                    "frmQuotations", "Quotations", quotation.QuotationId);
                await _notify.NotifyRoleAsync("📋 إشعار عرض سعر", msg, SystemRoles.SalesManager, currentUserName,
                    "frmQuotations", "Quotations", quotation.QuotationId);
            }
            catch { }

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

            quotation.QuotationDate = dto.QuotationDate;
            quotation.PartyId = dto.PartyId!.Value;
            quotation.WarehouseId = dto.WarehouseId;
            quotation.PricingType = dto.PricingType;
            quotation.TotalAmount = dto.TotalAmount;
            quotation.GrandTotal = dto.GrandTotal;
            quotation.Notes = dto.Notes;

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

            await _audit.LogAsync("Quotations", "Update",
                quotation.QuotationId.ToString(), null, quotation, currentUserName);

            return (true, "تم تحديث عرض السعر.");
        }
        catch (Exception ex)
        {
            return (false, $"حدث خطأ: {ex.Message}");
        }
    }

    // ============================================================
    //  تغيير الحالة (مؤقت لحد ما يتعمل عمود Status)
    // ============================================================
    public async Task<(bool Success, string Message)> ChangeStatusAsync(
        int quotationId, string newStatus, string currentUserName)
    {
        // ملاحظة: الحالة محسوبة من InvoiceId حالياً
        // لو عايز Status فعلي في الداتابيز، محتاج Migration
        await _audit.LogAsync("Quotations", "ChangeStatus",
            quotationId.ToString(), null, new { Status = newStatus }, currentUserName);
        return (true, "تم تحديث الحالة (في سجل المراجعة فقط).");
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
    //  ⭐⭐⭐ تحويل عرض السعر لفاتورة
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

        var details = await _db.QuotationDetails
            .Where(d => d.QuotationId == quotationId).ToListAsync();

        if (!details.Any())
            return (false, "لا توجد أصناف في عرض السعر.", null);

        // حضّر InvoiceFormDto وابعتها لـ InvoiceService
        var invoiceForm = new InvoiceFormDto
        {
            TransactionDate = DateTime.Now,
            PartyId = quotation.PartyId,
            WarehouseId = quotation.WarehouseId,
            DueDate = null,
            Notes = $"تحويل من عرض السعر QT-{quotation.QuotationDate.Year}-{quotation.QuotationId:D5}" +
                    (string.IsNullOrEmpty(quotation.Notes) ? "" : $"\n{quotation.Notes}"),
            PaidAmount = initialPaidAmount,
            CashBoxId = cashBoxId,
            PaymentMethod = paymentMethod,
            Items = details.Select(d => new InvoiceItemDto
            {
                ProductId = d.ProductId,
                Quantity = d.Quantity,
                UnitPrice = d.UnitPrice,
                Notes = d.Notes,
                PricingTier = d.Notes != null && d.Notes.Contains("[Elite]")
                    ? PricingTiers.Elite : PricingTiers.Premium
            }).ToList()
        };

        // الـ InvoiceService هياخد المهمة كاملة
        var (ok, msg, invoiceId, _) = await _invoiceService.CreateInvoiceAsync(invoiceForm, currentUserName);

        if (!ok || invoiceId == null) return (false, msg, null);

        // اربط عرض السعر بالفاتورة
        quotation.InvoiceId = invoiceId.Value;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("Quotations", "ConvertToInvoice",
            quotationId.ToString(), null,
            new { InvoiceId = invoiceId.Value }, currentUserName);

        return (true, $"تم تحويل عرض السعر إلى الفاتورة بنجاح.", invoiceId);
    }

    // ============================================================
    //  Helpers
    // ============================================================
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
