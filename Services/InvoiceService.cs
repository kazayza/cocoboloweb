using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class InvoiceService : IInvoiceService
{
    private readonly db24804Context _db;
    private readonly IAuditService _audit;
    private readonly NotificationService _notify;
    private readonly IHttpContextAccessor _http;

    public InvoiceService(db24804Context db, IAuditService audit, NotificationService notify, IHttpContextAccessor http)
    {
        _db = db;
        _audit = audit;
        _notify = notify;
        _http = http;
    }

    // ============================================================
    //  قائمة الفواتير
    // ============================================================
    public async Task<PagedResult<InvoiceListDto>> GetInvoicesAsync(InvoiceFilterDto filter)
    {
        // ⭐ سكوب تاريخ الاطلاع — من Users.CrmAccessFromDate عبر الـ Claim (Admin معفي تلقائياً)
        var accessFrom = _http.GetCrmAccessFrom();

        // ⭐ حماية فواتير مديري الحسابات: لا تظهر إلا لـ Admin/AccountManager/Account
        var protectedCreators = SalesInvoiceAccess.CanViewAccountManagerInvoices(_http.HttpContext?.User)
            ? new List<string>()
            : await SalesInvoiceAccess.GetProtectedCreatorUsernamesAsync(_db);

        var query = _db.Transactions
            .AsNoTracking()
            .Where(t => t.TransactionType == filter.TransactionType)
            .AsQueryable();

        if (accessFrom.HasValue)
            query = query.Where(t => t.TransactionDate >= accessFrom.Value);

        query = query.ExcludeProtectedSales(protectedCreators);

                 if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();

            // ⭐ جلب كل العملاء وبعدين فلترة بالعربي
            var allParties = await _db.Parties
                .AsNoTracking()
                .Select(p => new { p.PartyId, p.PartyName, p.Phone })
                .ToListAsync();

            var matchingPartyIds = allParties
                .Where(p => (p.PartyName ?? "").ContainsArabic(s) ||
                            (p.Phone ?? "").ContainsArabic(s))
                .Select(p => p.PartyId)
                .ToList();

            if (filter.TransactionType == TransactionTypes.Purchase)
            {
                query = query.Where(t =>
                    (t.ReferenceNumber != null && t.ReferenceNumber.Contains(s)) ||
                    matchingPartyIds.Contains(t.EmpId ?? 0));
            }
            else
            {
                query = query.Where(t =>
                    (t.ReferenceNumber != null && t.ReferenceNumber.Contains(s)) ||
                    matchingPartyIds.Contains(t.PartyId));
            }
        }

        if (filter.PartyId.HasValue)
            query = query.Where(t => t.PartyId == filter.PartyId.Value);

        if (filter.WarehouseId.HasValue)
            query = query.Where(t => t.WarehouseId == filter.WarehouseId.Value);

        if (filter.DateFrom.HasValue)
            query = query.Where(t => t.TransactionDate >= filter.DateFrom.Value.Date);

        if (filter.DateTo.HasValue)
            query = query.Where(t => t.TransactionDate <= filter.DateTo.Value.Date.AddDays(1).AddTicks(-1));

        if (!string.IsNullOrWhiteSpace(filter.InvoiceStatus))
            query = query.Where(t => t.InvoiceStatus == filter.InvoiceStatus);

        if (!string.IsNullOrWhiteSpace(filter.PaymentMethod))
            query = query.Where(t => t.PaymentMethod == filter.PaymentMethod);

        if (filter.IsDelivered.HasValue)
            query = query.Where(t => t.IsDelivered == filter.IsDelivered.Value);

        if (filter.HasRemaining.HasValue)
        {
            if (filter.HasRemaining.Value)
                query = query.Where(t => t.GrandTotal > t.PaidAmount);
            else
                query = query.Where(t => t.GrandTotal <= t.PaidAmount);
        }

        if (filter.IsOverdue.HasValue && filter.IsOverdue.Value)
        {
            var todayDate = DateTime.Today;
            query = query.Where(t => t.GrandTotal > t.PaidAmount && t.DueDate.HasValue && t.DueDate.Value < todayDate);
        }

        var totalCount = await query.CountAsync();

        query = filter.SortBy switch
        {
            "GrandTotal" => filter.SortDescending
                ? query.OrderByDescending(t => t.GrandTotal)
                : query.OrderBy(t => t.GrandTotal),
            "PaidAmount" => filter.SortDescending
                ? query.OrderByDescending(t => t.PaidAmount)
                : query.OrderBy(t => t.PaidAmount),
            "ReferenceNumber" => filter.SortDescending
                ? query.OrderByDescending(t => t.ReferenceNumber)
                : query.OrderBy(t => t.ReferenceNumber),
            _ => filter.SortDescending
                ? query.OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.TransactionId)
                : query.OrderBy(t => t.TransactionDate).ThenBy(t => t.TransactionId)
        };

        var items = await query
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(t => new InvoiceListDto
            {
                TransactionId = t.TransactionId,
                ReferenceNumber = t.ReferenceNumber,
                TransactionDate = t.TransactionDate,
                PartyId = t.PartyId,
                PartyName = _db.Parties.Where(p => p.PartyId == t.PartyId)
                    .Select(p => p.PartyName).FirstOrDefault() ?? "",
                PartyPhone = _db.Parties.Where(p => p.PartyId == t.PartyId)
                    .Select(p => p.Phone).FirstOrDefault(),
                WarehouseId = t.WarehouseId,
                WarehouseName = _db.Warehouses.Where(w => w.WarehouseId == t.WarehouseId)
                    .Select(w => w.WarehouseName).FirstOrDefault(),
                                EmpId = t.EmpId,
                EmpName = t.TransactionType == TransactionTypes.Purchase
                    ? (t.EmpId == null ? null :
                        _db.Parties.Where(p => p.PartyId == t.EmpId)
                            .Select(p => p.PartyName).FirstOrDefault())
                    : (t.EmpId == null ? null :
                        _db.Employees.Where(e => e.EmployeeId == t.EmpId)
                            .Select(e => e.FullName).FirstOrDefault()),
                TotalAmount = t.TotalAmount,
                DiscountAmount = t.DiscountAmount,
                NetTotalAmount = t.NetTotalAmount,
                TotalChargesAmount = t.TotalChargesAmount,
                GrandTotal = t.GrandTotal,
                PaidAmount = t.PaidAmount,
                PaymentMethod = t.PaymentMethod,
                InvoiceStatus = t.InvoiceStatus,
                IsDelivered = t.IsDelivered,
                DueDate = t.DueDate,
                ItemsCount = _db.TransactionDetails.Count(d => d.TransactionId == t.TransactionId),
                CreatedBy = t.CreatedBy,
                CreatedAt = t.CreatedAt,
                EditReason = t.EditReason,
                EditBy = t.EditBy,
                EditAt = t.EditAt,
                EditReviewedBy = t.EditReviewedBy,
                EditReviewedAt = t.EditReviewedAt,
                EditReviewNotes = t.EditReviewNotes,
                EditStatus = t.EditStatus,
                EditRequestDate = t.EditRequestDate,
                EditDone = t.EditDone
            })
            .ToListAsync();

        return new PagedResult<InvoiceListDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize
        };
    }

    // ============================================================
    //  تفاصيل الفاتورة
    // ============================================================
    public async Task<InvoiceDetailsDto?> GetInvoiceDetailsAsync(int transactionId)
    {
        var form = await GetInvoiceForEditAsync(transactionId);
        if (form == null) return null;

        var grandTotal = form.GrandTotal;

        var payments = await (from p in _db.Payments.AsNoTracking()
                              where p.TransactionId == transactionId
                              orderby p.PaymentDate
                              select new PaymentHistoryDto
                              {
                                  PaymentId = p.PaymentId,
                                  PaymentDate = p.PaymentDate,
                                  Amount = p.Amount,
                                  PaymentMethod = p.PaymentMethod,
                                  Notes = p.Notes,
                                  CreatedBy = p.CreatedBy,
                                  CashBoxName = (from ct in _db.CashboxTransactions
                                                 join c in _db.CashBoxes on ct.CashBoxId equals c.CashBoxId
                                                 where ct.PaymentId == p.PaymentId
                                                 select c.CashBoxName).FirstOrDefault()
                              }).ToListAsync();

        // احسب نسبة كل دفعة
        foreach (var p in payments)
            p.Percentage = grandTotal == 0 ? 0 : Math.Round((p.Amount / grandTotal) * 100, 1);

        return new InvoiceDetailsDto { Invoice = form, Payments = payments };
    }

    public async Task<InvoiceFormDto?> GetInvoiceForEditAsync(int transactionId)
    {
        var t = await _db.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TransactionId == transactionId);

        if (t == null) return null;

        // ⭐ حماية الفتح المباشر: فواتير مديري الحسابات لا تفتح إلا لـ Admin/AccountManager/Account
        // (تغطي التفاصيل والطباعة والتعديل — كلها تمر من هنا)
        if (await SalesInvoiceAccess.IsProtectedSaleAsync(_db, t, _http.HttpContext?.User))
            return null;

                var partyName = await _db.Parties
            .Where(p => p.PartyId == t.PartyId)
            .Select(p => p.PartyName).FirstOrDefaultAsync();

        string? empName;
        if (t.TransactionType == TransactionTypes.Purchase)
        {
            empName = t.EmpId == null ? null
                : await _db.Parties.Where(p => p.PartyId == t.EmpId)
                    .Select(p => p.PartyName).FirstOrDefaultAsync();
        }
        else
        {
            empName = t.EmpId == null ? null
                : await _db.Employees.Where(e => e.EmployeeId == t.EmpId)
                    .Select(e => e.FullName).FirstOrDefaultAsync();
        }

        var rawItems = await (from d in _db.TransactionDetails.AsNoTracking()
                              join p in _db.Products.AsNoTracking() on d.ProductId equals p.ProductId
                              where d.TransactionId == transactionId
                              select new
                              {
                                  d.DetailId,
                                  d.ProductId,
                                  ProductName = p.ProductName,
                                  ProductDescription = p.ProductDescription,
                                  d.Quantity,
                                  d.UnitPrice,
                                  d.Notes,
                                  d.PricingTier,
                                  d.SelectedAlternativeId,
                                  p.SuggestedSalePriceCClass,
                                  p.SuggestedSalePrice,
                                  p.SuggestedSalePriceElite,
                                  p.PurchasePriceCClass,
                                  p.PurchasePrice,
                                  p.PurchasePriceElite,
                                  p.Period,
                                  ProductCustomer = p.Customer
                              }).ToListAsync();

        var itemAlternativeIds = rawItems.Where(x => x.SelectedAlternativeId.HasValue).Select(x => x.SelectedAlternativeId!.Value).Distinct().ToList();
        var itemAlternatives = itemAlternativeIds.Any()
            ? await _db.ProductFactoryAlternatives.AsNoTracking()
                .Where(a => itemAlternativeIds.Contains(a.AlternativeId))
                .ToDictionaryAsync(a => a.AlternativeId)
            : new Dictionary<int, ProductFactoryAlternative>();

        var items = rawItems.Select(d =>
        {
            itemAlternatives.TryGetValue(d.SelectedAlternativeId ?? 0, out var alt);
            return new InvoiceItemDto
            {
                DetailId = d.DetailId,
                ProductId = d.ProductId,
                ProductName = d.ProductName,
                ProductDescription = alt?.SpecificationSummary ?? d.ProductDescription,
                ProductImagePath = null,
                Quantity = d.Quantity,
                UnitPrice = d.UnitPrice,
                Notes = d.Notes,
                SelectedAlternativeId = d.SelectedAlternativeId,
                SelectedAlternativeName = alt?.AlternativeName,
                SelectedAlternativeSummary = alt?.SpecificationSummary,
                PricingTier = d.PricingTier,
                SalePriceCClass = d.SuggestedSalePriceCClass,
                SalePricePremium = d.SuggestedSalePrice,
                SalePriceElite = d.SuggestedSalePriceElite,
                PurchasePriceCClass = d.PurchasePriceCClass,
                PurchasePricePremium = d.PurchasePrice,
                PurchasePriceElite = d.PurchasePriceElite,
                Period = d.Period,
                AlternativeSalePriceCClass = alt?.SuggestedSalePriceCClass,
                AlternativeSalePricePremium = alt?.SuggestedSalePricePremium,
                AlternativeSalePriceElite = alt?.SuggestedSalePriceElite,
                AlternativePurchasePriceCClass = alt?.PurchasePriceCClass,
                AlternativePurchasePricePremium = alt?.PurchasePricePremium,
                AlternativePurchasePriceElite = alt?.PurchasePriceElite,
                AlternativePeriod = alt?.Period,
                IsShowroomProduct = !d.ProductCustomer.HasValue
            };
        }).ToList();

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.PricingTier))
                item.PricingTier = ExtractPricingTierFromNotes(item.Notes);
        }

        var charges = await _db.AdditionalCharges
            .AsNoTracking()
            .Where(c => c.TransactionId == transactionId)
            .Select(c => new InvoiceChargeDto
            {
                ChargeId = c.ChargeId,
                ChargeDescription = c.ChargeDescription,
                ChargeAmount = c.ChargeAmount ?? 0,
                Notes = c.Notes
            })
            .ToListAsync();

        // البحث عن الفاتورة المرآة
        int? mirrorId = null;
        string? mirrorRef = null;
        if (t.TransactionType == TransactionTypes.Sale)
        {
            var mirror = await _db.Transactions
                .Where(x => x.TransactionType == TransactionTypes.Purchase &&
                            x.ReferenceType == "MirrorOf:" + t.TransactionId)
                .Select(x => new { x.TransactionId, x.ReferenceNumber })
                .FirstOrDefaultAsync();
            if (mirror != null)
            {
                mirrorId = mirror.TransactionId;
                mirrorRef = mirror.ReferenceNumber;
            }
        }
        else if (t.TransactionType == TransactionTypes.Purchase && t.ReferenceType != null && t.ReferenceType.StartsWith("MirrorOf:"))
        {
            if (int.TryParse(t.ReferenceType.Substring("MirrorOf:".Length), out var saleId))
            {
                var saleInv = await _db.Transactions
                    .Where(x => x.TransactionId == saleId)
                    .Select(x => new { x.TransactionId, x.ReferenceNumber })
                    .FirstOrDefaultAsync();
                if (saleInv != null)
                {
                    mirrorId = saleInv.TransactionId;
                    mirrorRef = saleInv.ReferenceNumber;
                }
            }
        }

        return new InvoiceFormDto
        {
            TransactionId = t.TransactionId,
            ReferenceNumber = t.ReferenceNumber,
            TransactionDate = t.TransactionDate,
            PartyId = t.PartyId,
            PartyName = partyName,
            WarehouseId = t.WarehouseId,
            OpportunityId = t.OpportunityId,
            EmpId = t.EmpId,
            EmpName = empName,
            DueDate = t.DueDate,
            TransactionType = t.TransactionType,
            TotalAmount = t.TotalAmount,
            DiscountPercentage = t.DiscountPercentage,
            DiscountAmount = t.DiscountAmount,
            NetTotalAmount = t.NetTotalAmount,
            TotalChargesAmount = t.TotalChargesAmount,
            GrandTotal = t.GrandTotal,
            PaidAmount = t.PaidAmount,
            PaymentMethod = t.PaymentMethod,
            Notes = t.Notes,
            InvoiceStatus = t.InvoiceStatus,
            IsDelivered = t.IsDelivered,
            CreatedBy = t.CreatedBy,
            CreatedAt = t.CreatedAt,
            EditReason = t.EditReason,
            EditBy = t.EditBy,
            EditAt = t.EditAt,
            EditReviewedBy = t.EditReviewedBy,
            EditReviewedAt = t.EditReviewedAt,
            EditReviewNotes = t.EditReviewNotes,
            EditStatus = t.EditStatus,
            EditRequestDate = t.EditRequestDate,
            EditDone = t.EditDone,
            Items = items,
            Charges = charges,
            MirrorPurchaseTransactionId = mirrorId,
            MirrorPurchaseReferenceNumber = mirrorRef
        };
    }

    // ============================================================
    //  للطباعة - مع بيانات الشركة والعميل
    // ============================================================
    public async Task<InvoicePrintDto?> GetInvoiceForPrintAsync(int transactionId)
    {
        var details = await GetInvoiceDetailsAsync(transactionId);
        if (details == null) return null;

        var party = await _db.Parties.AsNoTracking()
            .FirstOrDefaultAsync(p => p.PartyId == details.Invoice.PartyId);

        var company = await _db.CompanyInfos.AsNoTracking().FirstOrDefaultAsync();

        var dto = new InvoicePrintDto
        {
            Invoice = details.Invoice,
            Payments = details.Payments,
            CustomerAddress = party?.Address,
            CustomerEmail = party?.Email,
            CustomerPhone = party?.Phone,
            CustomerCity = party?.City
        };

        // محاولة قراءة بيانات الشركة (الحقول الشائعة)
        if (company != null)
        {
            var t = company.GetType();
            dto.CompanyName = t.GetProperty("CompanyName")?.GetValue(company)?.ToString()
                              ?? t.GetProperty("Name")?.GetValue(company)?.ToString()
                              ?? "COCOBOLO";
            dto.CompanyPhone = t.GetProperty("Phone")?.GetValue(company)?.ToString()
                               ?? t.GetProperty("PhoneNumber")?.GetValue(company)?.ToString();
            dto.CompanyAddress = t.GetProperty("Address")?.GetValue(company)?.ToString();
            dto.CompanyTaxNumber = t.GetProperty("TaxNumber")?.GetValue(company)?.ToString();
            dto.CompanyLogo = t.GetProperty("LogoPath")?.GetValue(company)?.ToString()
                              ?? t.GetProperty("Logo")?.GetValue(company)?.ToString();
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
        public async Task<InvoiceStatsDto> GetStatsAsync(DateTime? from = null, DateTime? to = null, string transactionType = "Sale")
    {
        // ⭐ نفس قيود القائمة: سكوب التاريخ + حماية فواتير مديري الحسابات
        var accessFrom = _http.GetCrmAccessFrom();
        var protectedCreators = SalesInvoiceAccess.CanViewAccountManagerInvoices(_http.HttpContext?.User)
            ? new List<string>()
            : await SalesInvoiceAccess.GetProtectedCreatorUsernamesAsync(_db);

        var query = _db.Transactions
            .AsNoTracking()
            .Where(t => t.TransactionType == transactionType && t.InvoiceStatus != "Cancelled");

        if (accessFrom.HasValue) query = query.Where(t => t.TransactionDate >= accessFrom.Value);
        if (from.HasValue) query = query.Where(t => t.TransactionDate >= from.Value.Date);
        if (to.HasValue) query = query.Where(t => t.TransactionDate <= to.Value.Date.AddDays(1).AddTicks(-1));
        query = query.ExcludeProtectedSales(protectedCreators);

        var today = DateTime.Today;
        var stats = new InvoiceStatsDto
        {
            TotalCount = await query.CountAsync(),
            TotalSales = await query.SumAsync(t => (decimal?)t.GrandTotal) ?? 0,
            TotalPaid = await query.SumAsync(t => (decimal?)t.PaidAmount) ?? 0,
            TodayCount = await query.CountAsync(t => t.TransactionDate.Date == today),
            TodaySales = await query.Where(t => t.TransactionDate.Date == today)
                .SumAsync(t => (decimal?)t.GrandTotal) ?? 0,
            OpenCount = await query.CountAsync(t => t.GrandTotal > t.PaidAmount),
            OverdueCount = await query.CountAsync(t =>
                t.GrandTotal > t.PaidAmount &&
                t.DueDate.HasValue && t.DueDate.Value < today)
        };

        stats.TotalRemaining = stats.TotalSales - stats.TotalPaid;
        return stats;
    }

    // ============================================================
    //  توليد رقم فاتورة
    // ============================================================
    public async Task<string> GenerateNextInvoiceNumberAsync(string transactionType = "Sale")
    {
        var year = DateTime.Now.Year;
        var prefix = transactionType == TransactionTypes.Purchase
            ? $"PRC-{year}-"
            : $"INV-{year}-";

        var lastNumber = await _db.Transactions
            .Where(t => t.TransactionType == transactionType &&
                        t.ReferenceNumber != null &&
                        t.ReferenceNumber.StartsWith(prefix))
            .OrderByDescending(t => t.TransactionId)
            .Select(t => t.ReferenceNumber)
            .FirstOrDefaultAsync();

        int next = 1;
        if (!string.IsNullOrEmpty(lastNumber))
        {
            var parts = lastNumber.Split('-');
            if (parts.Length == 3 && int.TryParse(parts[2], out var n))
                next = n + 1;
        }

        return $"{prefix}{next:D5}";
    }

    // ============================================================
    //  إنشاء فاتورة + المرآة + الإشعارات
    // ============================================================
    public async Task<(bool Success, string Message, int? TransactionId, int? MirrorTransactionId)>
        CreateInvoiceAsync(InvoiceFormDto dto, string currentUserName)
    {
        var validation = ValidateInvoice(dto);
        if (!validation.IsValid) return (false, validation.Message, null, null);

        if (dto.TransactionType == TransactionTypes.Purchase)
            return await CreateShowroomPurchaseInvoiceAsync(dto, currentUserName);

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            CalculateTotals(dto);

            // ⭐ حدد EmpId تلقائي من اليوزر الحالي لو مش محدد
            if (dto.EmpId == null || dto.EmpId == 0)
            {
                dto.EmpId = await GetEmployeeIdByUserNameAsync(currentUserName);
            }

            // رقم الفاتورة
            if (string.IsNullOrWhiteSpace(dto.ReferenceNumber))
                dto.ReferenceNumber = await GenerateNextInvoiceNumberAsync(TransactionTypes.Sale);
            else
            {
                var exists = await _db.Transactions.AnyAsync(t =>
                    t.ReferenceNumber == dto.ReferenceNumber &&
                    t.TransactionType == TransactionTypes.Sale);
                if (exists) return (false, $"رقم الفاتورة '{dto.ReferenceNumber}' مستخدم.", null, null);
            }

            var productIds = dto.Items.Select(i => i.ProductId).Distinct().ToList();
            var productMap = await _db.Products
                .AsNoTracking()
                .Where(p => productIds.Contains(p.ProductId))
                .Select(p => new { p.ProductId, p.ProductName, p.Customer })
                .ToDictionaryAsync(p => p.ProductId);

            if (productMap.Count != productIds.Count)
                return (false, "يوجد صنف غير موجود أو تم حذفه من قاعدة البيانات.", null, null);

            var showroomItemGroups = dto.Items
                .Where(item => !productMap[item.ProductId].Customer.HasValue)
                .GroupBy(item => item.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    ProductName = productMap[g.Key].ProductName ?? $"#{g.Key}",
                    RequiredQuantity = (int)Math.Round(g.Sum(x => x.Quantity))
                })
                .Where(x => x.RequiredQuantity > 0)
                .ToList();

            if (showroomItemGroups.Any())
            {
                var showroomProductIds = showroomItemGroups.Select(x => x.ProductId).ToList();
                var stockMap = await _db.StockLevels
                    .AsNoTracking()
                    .Where(s => s.WarehouseId == dto.WarehouseId.Value && showroomProductIds.Contains(s.ProductId))
                    .ToDictionaryAsync(s => s.ProductId, s => s.Quantity);

                var shortages = showroomItemGroups
                    .Select(x => new
                    {
                        x.ProductName,
                        x.RequiredQuantity,
                        AvailableQuantity = stockMap.TryGetValue(x.ProductId, out var qty) ? qty : 0
                    })
                    .Where(x => x.AvailableQuantity < x.RequiredQuantity)
                    .ToList();

                if (shortages.Any())
                {
                    var shortageMessage = string.Join(" | ", shortages.Select(x =>
                        $"رصيد المنتج '{x.ProductName}' في المخزن المختار غير كافٍ. المتاح {x.AvailableQuantity} والمطلوب {x.RequiredQuantity}."));
                    return (false, shortageMessage, null, null);
                }
            }

            var customerLinkedItems = dto.Items
                .Where(item => productMap[item.ProductId].Customer.HasValue)
                .ToList();

            // فاتورة المبيعات
            var saleTransaction = new Transaction
            {
                TransactionDate = dto.TransactionDate,
                PartyId = dto.PartyId!.Value,
                TransactionType = TransactionTypes.Sale,
                WarehouseId = dto.WarehouseId!.Value,
                ReferenceNumber = dto.ReferenceNumber,
                ReferenceType = "Invoice",
                OpportunityId = dto.OpportunityId,
                EmpId = dto.EmpId, // ⭐ موظف الفاتورة
                DueDate = dto.DueDate,
                TotalAmount = dto.TotalAmount,
                DiscountPercentage = dto.DiscountPercentage,
                DiscountAmount = dto.DiscountAmount,
                NetTotalAmount = dto.NetTotalAmount,
                TotalChargesAmount = dto.TotalChargesAmount,
                GrandTotal = dto.GrandTotal,
                PaidAmount = 0,
                PaymentMethod = dto.PaymentMethod,
                Notes = dto.Notes,
                InvoiceStatus = InvoiceStatuses.Open,
                IsDelivered = dto.IsDelivered ?? false,
                CreatedBy = currentUserName,
                CreatedAt = DateTime.Now
            };

            _db.Transactions.Add(saleTransaction);
            await _db.SaveChangesAsync();

            Transaction? mirrorPurchase = null;
            string? mirrorRefNumber = null;

            if (customerLinkedItems.Any())
            {
                mirrorRefNumber = await GenerateNextInvoiceNumberAsync(TransactionTypes.Purchase);
                mirrorPurchase = new Transaction
                {
                    TransactionDate = dto.TransactionDate,
                    PartyId = SystemConstants.DefaultSupplierId,
                    TransactionType = TransactionTypes.Purchase,
                    WarehouseId = dto.WarehouseId.Value,
                    ReferenceNumber = mirrorRefNumber,
                    ReferenceType = "MirrorOf:" + saleTransaction.TransactionId,
                    EmpId = dto.PartyId,
                    Notes = $"فاتورة شراء تلقائية مقابل البيع رقم {dto.ReferenceNumber}",
                    CreatedBy = currentUserName,
                    CreatedAt = DateTime.Now,
                    InvoiceStatus = InvoiceStatuses.Open,
                    IsDelivered = true,
                    PaymentMethod = PaymentMethods.Credit,
                    PaidAmount = 0,
                    DiscountPercentage = 0,
                    DiscountAmount = 0,
                    TotalChargesAmount = 0
                };

                decimal mirrorTotal = 0;
                _db.Transactions.Add(mirrorPurchase);
                await _db.SaveChangesAsync();

                foreach (var item in customerLinkedItems)
                {
                    var effectiveTier = NormalizePricingTier(item.PricingTier);
                    var purchasePrice = GetPurchasePriceByTier(item, effectiveTier);

                    var purchaseDetail = new TransactionDetail
                    {
                        TransactionId = mirrorPurchase.TransactionId,
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        UnitPrice = purchasePrice,
                        TotalAmount = Math.Round(item.Quantity * purchasePrice, 2),
                        SelectedAlternativeId = item.SelectedAlternativeId,
                        PricingTier = effectiveTier,
                        Notes = $"[{effectiveTier}] - مقابل بيع {dto.ReferenceNumber}"
                    };
                    _db.TransactionDetails.Add(purchaseDetail);
                    mirrorTotal += purchaseDetail.TotalAmount ?? 0;

                    await UpdateStockAsync(item.ProductId, mirrorPurchase.WarehouseId,
                        +(int)Math.Round(item.Quantity), mirrorPurchase.TransactionId,
                        purchasePrice, currentUserName, "PurchaseInvoice");
                }

                mirrorPurchase.TotalAmount = mirrorTotal;
                mirrorPurchase.NetTotalAmount = mirrorTotal;
                mirrorPurchase.GrandTotal = mirrorTotal;
            }

            // أصناف فاتورة البيع (يخصم المخزون)
            foreach (var item in dto.Items)
            {
                var effectiveTier = NormalizePricingTier(item.PricingTier);

                var detail = new TransactionDetail
                {
                    TransactionId = saleTransaction.TransactionId,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    TotalAmount = item.TotalAmount,
                    SelectedAlternativeId = item.SelectedAlternativeId,
                    PricingTier = effectiveTier,
                    Notes = string.IsNullOrEmpty(item.Notes)
                        ? $"[{effectiveTier}]"
                        : $"[{effectiveTier}] {item.Notes}"
                };
                _db.TransactionDetails.Add(detail);

                await UpdateStockAsync(item.ProductId, saleTransaction.WarehouseId,
                    -(int)Math.Round(item.Quantity), saleTransaction.TransactionId,
                    item.UnitPrice, currentUserName, "SaleInvoice");
            }

            if (customerLinkedItems.Any())
            {
                var customerProductIds = customerLinkedItems.Select(i => i.ProductId).Distinct().ToList();
                var soldCustomerProducts = await _db.Products
                    .Where(p => customerProductIds.Contains(p.ProductId))
                    .ToListAsync();

                foreach (var product in soldCustomerProducts)
                {
                    product.IsSelected = true;
                }
            }
            await _db.SaveChangesAsync();

            // الرسوم الإضافية
            foreach (var ch in dto.Charges)
            {
                _db.AdditionalCharges.Add(new AdditionalCharge
                {
                    TransactionId = saleTransaction.TransactionId,
                    PartyId = saleTransaction.PartyId,
                    ChargeDescription = ch.ChargeDescription,
                    ChargeAmount = ch.ChargeAmount,
                    Notes = ch.Notes,
                    CreatedBy = currentUserName,
                    CreatedAt = DateTime.Now
                });
            }

            // الدفعات المقدمة
            decimal advanceTotal = 0;
            if (dto.SelectedAdvanceChargeIds.Any())
            {
                var advances = await _db.AdditionalCharges
                    .Where(c => dto.SelectedAdvanceChargeIds.Contains(c.ChargeId)
                                && c.PartyId == dto.PartyId
                                && c.TransactionId == null)
                    .ToListAsync();

                foreach (var adv in advances)
                {
                    adv.TransactionId = saleTransaction.TransactionId;

                    var advPayment = new Payment
                    {
                        TransactionId = saleTransaction.TransactionId,
                        PaymentDate = DateTime.Now,
                        Amount = adv.ChargeAmount ?? 0,
                        PaymentMethod = "Advance",
                        Notes = $"تطبيق دفعة مقدمة: {adv.ChargeDescription}",
                        CreatedBy = currentUserName,
                        CreatedAt = DateTime.Now
                    };
                    _db.Payments.Add(advPayment);
                    advanceTotal += adv.ChargeAmount ?? 0;

                    // ⭐ Audit
                    await _audit.LogAsync("Payments", "Insert",
                        "Advance:" + adv.ChargeId, null,
                        new { advPayment.Amount, advPayment.PaymentMethod, advPayment.Notes },
                        currentUserName);
                }
            }

            // الدفعة الفورية
            decimal newPaymentTotal = 0;
            if (dto.PaidAmount > 0)
            {
                if (dto.CashBoxId == null)
                    return (false, "يرجى اختيار الخزينة عند تسجيل دفعة.", null, null);

                var payment = new Payment
                {
                    TransactionId = saleTransaction.TransactionId,
                    PaymentDate = DateTime.Now,
                    Amount = dto.PaidAmount,
                    PaymentMethod = dto.PaymentMethod ?? PaymentMethods.Cash,
                    Notes = "دفعة عند إنشاء الفاتورة",
                    CreatedBy = currentUserName,
                    CreatedAt = DateTime.Now
                };
                _db.Payments.Add(payment);
                await _db.SaveChangesAsync();

                _db.CashboxTransactions.Add(new CashboxTransaction
                {
                    CashBoxId = dto.CashBoxId.Value,
                    PaymentId = payment.PaymentId,
                    ReferenceId = saleTransaction.TransactionId,
                    ReferenceType = "SaleInvoice",
                    TransactionType = "قبض",
                    Amount = dto.PaidAmount,
                    TransactionDate = DateTime.Now,
                    Notes = $"تحصيل فاتورة {saleTransaction.ReferenceNumber}",
                    CreatedBy = currentUserName,
                    CreatedAt = DateTime.Now
                });

                newPaymentTotal = dto.PaidAmount;

                // Audit
                await _audit.LogAsync("Payments", "Insert",
                    payment.PaymentId.ToString(), null, payment, currentUserName);
            }

            saleTransaction.PaidAmount = advanceTotal + newPaymentTotal;
            saleTransaction.InvoiceStatus = ComputeStatus(saleTransaction.GrandTotal, saleTransaction.PaidAmount);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            // ⭐ Audit للفواتير
            await _audit.LogAsync("Transactions", "Insert",
                saleTransaction.TransactionId.ToString(), null, saleTransaction, currentUserName);

            if (mirrorPurchase != null)
            {
                await _audit.LogAsync("Transactions", "Insert",
                    mirrorPurchase.TransactionId.ToString(), null, mirrorPurchase, currentUserName);
            }

            // ⭐ إشعارات الإدارة + الإنتاج
            await SendInvoiceNotificationsAsync(saleTransaction, currentUserName, "تم إنشاء فاتورة جديدة");
            if (customerLinkedItems.Any())
                await SendProductionStartNotificationAsync(saleTransaction, currentUserName);

            var successMessage = mirrorPurchase == null
                ? $"تم إنشاء الفاتورة {saleTransaction.ReferenceNumber} وخصم منتجات المعرض من المخزون بدون إنشاء فاتورة شراء مرآة."
                : showroomItemGroups.Any()
                    ? $"تم إنشاء الفاتورة {saleTransaction.ReferenceNumber}، وتم إنشاء فاتورة الشراء المرآة {mirrorRefNumber} للأصناف المخصصة للعميل فقط، مع خصم منتجات المعرض من المخزون مباشرة."
                    : $"تم إنشاء الفاتورة {saleTransaction.ReferenceNumber} مع فاتورة الشراء المرآة {mirrorRefNumber}.";

            return (true,
                successMessage,
                saleTransaction.TransactionId,
                mirrorPurchase?.TransactionId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return (false, $"حدث خطأ: {ex.Message}", null, null);
        }
    }

    private async Task<(bool Success, string Message, int? TransactionId, int? MirrorTransactionId)> CreateShowroomPurchaseInvoiceAsync(InvoiceFormDto dto, string currentUserName)
    {
        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            dto.PartyId = SystemConstants.DefaultSupplierId;
            dto.PartyName = "المصنع";

            CalculateTotals(dto);

            if (string.IsNullOrWhiteSpace(dto.ReferenceNumber))
                dto.ReferenceNumber = await GenerateNextInvoiceNumberAsync(TransactionTypes.Purchase);
            else
            {
                var exists = await _db.Transactions.AnyAsync(t =>
                    t.ReferenceNumber == dto.ReferenceNumber &&
                    t.TransactionType == TransactionTypes.Purchase);
                if (exists) return (false, $"رقم فاتورة الشراء '{dto.ReferenceNumber}' مستخدم.", null, null);
            }

            var purchaseTransaction = new Transaction
            {
                TransactionDate = dto.TransactionDate,
                PartyId = dto.PartyId!.Value,
                TransactionType = TransactionTypes.Purchase,
                WarehouseId = dto.WarehouseId!.Value,
                ReferenceNumber = dto.ReferenceNumber,
                ReferenceType = "PurchaseInvoice",
                OpportunityId = dto.OpportunityId,
                EmpId = dto.EmpId,
                DueDate = dto.DueDate,
                TotalAmount = dto.TotalAmount,
                DiscountPercentage = dto.DiscountPercentage,
                DiscountAmount = dto.DiscountAmount,
                NetTotalAmount = dto.NetTotalAmount,
                TotalChargesAmount = dto.TotalChargesAmount,
                GrandTotal = dto.GrandTotal,
                PaidAmount = 0,
                PaymentMethod = dto.PaymentMethod,
                Notes = dto.Notes,
                InvoiceStatus = InvoiceStatuses.Open,
                IsDelivered = false,
                CreatedBy = currentUserName,
                CreatedAt = DateTime.Now
            };

            _db.Transactions.Add(purchaseTransaction);
            await _db.SaveChangesAsync();

            var purchaseProductIds = dto.Items.Select(i => i.ProductId).Distinct().ToList();
            var customerLinkedProducts = await _db.Products
                .Where(p => purchaseProductIds.Contains(p.ProductId) && p.Customer.HasValue)
                .Select(p => p.ProductName)
                .ToListAsync();

            if (customerLinkedProducts.Any())
                return (false, "هذه الشاشة مخصصة لشراء منتجات المعرض فقط، ولا تسمح بشراء منتجات مرتبطة بعميل.", null, null);

            foreach (var item in dto.Items)
            {
                var detail = new TransactionDetail
                {
                    TransactionId = purchaseTransaction.TransactionId,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    TotalAmount = item.TotalAmount,
                    SelectedAlternativeId = item.SelectedAlternativeId,
                    PricingTier = NormalizePricingTier(item.PricingTier),
                    Notes = item.Notes
                };
                _db.TransactionDetails.Add(detail);
            }

            foreach (var ch in dto.Charges)
            {
                _db.AdditionalCharges.Add(new AdditionalCharge
                {
                    TransactionId = purchaseTransaction.TransactionId,
                    PartyId = purchaseTransaction.PartyId,
                    ChargeDescription = ch.ChargeDescription,
                    ChargeAmount = ch.ChargeAmount,
                    Notes = ch.Notes,
                    CreatedBy = currentUserName,
                    CreatedAt = DateTime.Now
                });
            }

            decimal newPaymentTotal = 0;
            if (dto.PaidAmount > 0)
            {
                if (dto.CashBoxId == null)
                    return (false, "يرجى اختيار الخزينة عند تسجيل دفعة شراء.", null, null);

                var payment = new Payment
                {
                    TransactionId = purchaseTransaction.TransactionId,
                    PaymentDate = DateTime.Now,
                    Amount = dto.PaidAmount,
                    PaymentMethod = dto.PaymentMethod ?? PaymentMethods.Cash,
                    Notes = "دفعة عند إنشاء فاتورة الشراء",
                    CreatedBy = currentUserName,
                    CreatedAt = DateTime.Now
                };
                _db.Payments.Add(payment);
                await _db.SaveChangesAsync();

                _db.CashboxTransactions.Add(new CashboxTransaction
                {
                    CashBoxId = dto.CashBoxId.Value,
                    PaymentId = payment.PaymentId,
                    ReferenceId = purchaseTransaction.TransactionId,
                    ReferenceType = "PurchaseInvoice",
                    TransactionType = "صرف",
                    Amount = dto.PaidAmount,
                    TransactionDate = DateTime.Now,
                    Notes = $"سداد فاتورة شراء {purchaseTransaction.ReferenceNumber}",
                    CreatedBy = currentUserName,
                    CreatedAt = DateTime.Now
                });

                newPaymentTotal = dto.PaidAmount;

                await _audit.LogAsync("Payments", "Insert",
                    payment.PaymentId.ToString(), null, payment, currentUserName);
            }

            purchaseTransaction.PaidAmount = newPaymentTotal;
            purchaseTransaction.InvoiceStatus = ComputeStatus(purchaseTransaction.GrandTotal, purchaseTransaction.PaidAmount);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            await _audit.LogAsync("Transactions", "Insert",
                purchaseTransaction.TransactionId.ToString(), null, purchaseTransaction, currentUserName);

            await SendInvoiceNotificationsAsync(purchaseTransaction, currentUserName, "تم إنشاء فاتورة شراء للمعرض");
            await SendPurchaseSupplyNotificationAsync(purchaseTransaction, currentUserName);

            return (true,
                $"تم إنشاء فاتورة الشراء {purchaseTransaction.ReferenceNumber} وسيتم إدخال المخزون عند الاستلام الفعلي.",
                purchaseTransaction.TransactionId,
                null);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return (false, $"حدث خطأ: {ex.Message}", null, null);
        }
    }

    // ============================================================
    //  تعديل (محدود)
    // ============================================================
    public async Task<(bool Success, string Message)> UpdateInvoiceAsync(
    InvoiceFormDto dto, string currentUserName)
{
    var transaction = await _db.Transactions
        .FirstOrDefaultAsync(t => t.TransactionId == dto.TransactionId);

    if (transaction == null) return (false, "الفاتورة غير موجودة.");
    if (transaction.InvoiceStatus == InvoiceStatuses.Cancelled)
        return (false, "لا يمكن تعديل فاتورة ملغية.");

    var oldSnapshot = new
    {
        transaction.Notes,
        transaction.DueDate,
        transaction.IsDelivered,
        transaction.PaymentMethod
    };

    transaction.Notes = dto.Notes;
    transaction.DueDate = dto.DueDate;
    transaction.IsDelivered = dto.IsDelivered;
    transaction.PaymentMethod = dto.PaymentMethod;
    transaction.OpportunityId = dto.OpportunityId;
    transaction.EditBy = currentUserName;
    transaction.EditAt = DateTime.Now;
    if (transaction.EditStatus == InvoiceEditStatuses.Approved || transaction.EditStatus == InvoiceEditStatuses.Pending)
    {
        transaction.EditStatus = InvoiceEditStatuses.Edited;
        transaction.EditDone = $"تم التعديل بواسطة {currentUserName} بتاريخ {DateTime.Now:yyyy/MM/dd HH:mm}";
    }

    await _db.SaveChangesAsync();

    var newSnapshot = new
    {
        transaction.Notes,
        transaction.DueDate,
        transaction.IsDelivered,
        transaction.PaymentMethod
    };

    await _audit.LogAsync(
        "Transactions",
        "Update",
        transaction.TransactionId.ToString(),
        oldSnapshot,
        newSnapshot,
        currentUserName
    );

    await SendInvoiceNotificationsAsync(transaction, currentUserName, "تم تعديل فاتورة");

    return (true, "تم تحديث الفاتورة.");
}

    // ============================================================
    //  طلب تعديل الفاتورة وإدارته (Workflow)
    // ============================================================
    public async Task<(bool Success, string Message)> RequestInvoiceEditAsync(
        int transactionId, string reason, string currentUserName)
    {
        var transaction = await _db.Transactions
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId);
        if (transaction == null) return (false, "الفاتورة غير موجودة.");
        if (transaction.InvoiceStatus == InvoiceStatuses.Cancelled)
            return (false, "لا يمكن طلب تعديل لفاتورة ملغية.");

        transaction.EditStatus = InvoiceEditStatuses.Pending;
        transaction.EditReason = reason;
        transaction.EditBy = currentUserName;
        transaction.EditRequestDate = DateTime.Now;
        transaction.EditReviewedBy = null;
        transaction.EditReviewedAt = null;
        transaction.EditReviewNotes = null;
        transaction.EditDone = null;

        await _db.SaveChangesAsync();

        await _audit.LogAsync("Transactions", "EditRequest",
            transactionId.ToString(), null, new { Reason = reason, RequestedBy = currentUserName }, currentUserName);

        // ⭐ إشعار طلب التعديل يوجَّه لمدير الإنتاج (مالك تدفق التسليم) — يعدّل التاريخ من أوامر التشغيل
        await SendInvoiceEditRequestToProductionAsync(transaction, currentUserName, reason);

        return (true, "تم إرسال طلب التعديل وتسجيله في النظام بنجاح.");
    }

    public async Task<(bool Success, string Message)> ProcessInvoiceEditRequestAsync(
        int transactionId, bool approve, string? notes, string currentUserName)
    {
        var transaction = await _db.Transactions
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId);
        if (transaction == null) return (false, "الفاتورة غير موجودة.");

        var now = DateTime.Now;
        transaction.EditReviewedBy = currentUserName;
        transaction.EditReviewedAt = now;
        transaction.EditReviewNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();


        if (approve)
        {
            transaction.EditStatus = InvoiceEditStatuses.Approved;
            transaction.EditDone = $"تمت الموافقة بواسطة {currentUserName} بتاريخ {now:yyyy/MM/dd hh:mm tt}" + (string.IsNullOrWhiteSpace(notes) ? "" : $" ({notes})");

            await _audit.LogAsync("Transactions", "EditApprove",
                transactionId.ToString(), null, new { ApprovedBy = currentUserName, ApprovedAt = now, Notes = notes }, currentUserName);
        }
        else
        {
            transaction.EditStatus = InvoiceEditStatuses.Rejected;
            transaction.EditDone = $"تم الرفض بواسطة {currentUserName} بتاريخ {now:yyyy/MM/dd hh:mm tt}" + (string.IsNullOrWhiteSpace(notes) ? "" : $" - السبب: {notes}");

            await _audit.LogAsync("Transactions", "EditReject",
                transactionId.ToString(), null, new { RejectedBy = currentUserName, RejectedAt = now, Reason = notes }, currentUserName);
        }

        await _db.SaveChangesAsync();

        // ⭐ إشعار القرار لصاحب الطلب شخصياً فقط (بدون أدمن/مدير حسابات)
        await SendInvoiceEditDecisionNotificationToRequesterAsync(transaction, approve, currentUserName, notes);

        return (true, approve ? "تمت الموافقة على فتح الفاتورة للتعديل." : "تم رفض طلب التعديل.");
    }

    // ============================================================
    //  إلغاء فاتورة
    // ============================================================
    public async Task<(bool Success, string Message)> ReceivePurchaseInvoiceAsync(
        int transactionId, string currentUserName, string? notes = null)
    {
        var transaction = await _db.Transactions
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

        if (transaction == null) return (false, "فاتورة الشراء غير موجودة.");
        if (transaction.TransactionType != TransactionTypes.Purchase)
            return (false, "هذا الإجراء متاح لفواتير الشراء فقط.");
        if (!string.IsNullOrWhiteSpace(transaction.ReferenceType) && transaction.ReferenceType.StartsWith("MirrorOf:"))
            return (false, "فاتورة الشراء المرآة لا تستقبل يدويًا.");
        if (transaction.InvoiceStatus == InvoiceStatuses.Cancelled)
            return (false, "لا يمكن استلام فاتورة شراء ملغية.");
        if (transaction.IsDelivered == true)
            return (false, "تم استلام هذه الفاتورة بالفعل.");

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var details = await _db.TransactionDetails
                .Where(d => d.TransactionId == transactionId)
                .ToListAsync();

            foreach (var d in details)
            {
                await UpdateStockAsync(d.ProductId, transaction.WarehouseId,
                    +(int)Math.Round(d.Quantity), transaction.TransactionId,
                    d.UnitPrice, currentUserName, "PurchaseInvoiceReceipt");
            }

            transaction.IsDelivered = true;
            transaction.DeliveredAt = DateTime.Now;
            transaction.DeliveredNotes = string.IsNullOrWhiteSpace(notes)
                ? "تم الاستلام وإدخال المخزون"
                : notes.Trim();

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            await _audit.LogAsync<object>(
                "Transactions",
                "ReceivePurchaseInvoice",
                transaction.TransactionId.ToString(),
                null,
                new
                {
                    transaction.TransactionId,
                    transaction.ReferenceNumber,
                    transaction.WarehouseId,
                    transaction.DeliveredAt,
                    transaction.DeliveredNotes
                },
                currentUserName);

            return (true, "تم استلام فاتورة الشراء وإدخال الأصناف إلى المخزن بنجاح.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return (false, $"حدث خطأ: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> CancelInvoiceAsync(
        int transactionId, string reason, string currentUserName)
    {
        var transaction = await _db.Transactions
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId);
        if (transaction == null) return (false, "الفاتورة غير موجودة.");
        if (transaction.InvoiceStatus == InvoiceStatuses.Cancelled)
            return (false, "الفاتورة ملغية بالفعل.");

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var mirrorWasCancelled = false;
            var invoiceDetails = await _db.TransactionDetails
                .Where(d => d.TransactionId == transactionId).ToListAsync();

            if (transaction.TransactionType == TransactionTypes.Sale)
            {
                foreach (var d in invoiceDetails)
                {
                    await UpdateStockAsync(d.ProductId, transaction.WarehouseId,
                        +(int)Math.Round(d.Quantity), transactionId,
                        d.UnitPrice, currentUserName, "SaleInvoiceCancel");
                }

                // ⭐ إرجاع IsSelected = false للمنتجات الخاصة بالعملاء فقط
                var cancelledProductIds = invoiceDetails.Select(d => d.ProductId).ToList();
                var cancelledProducts = await _db.Products
                    .Where(p => cancelledProductIds.Contains(p.ProductId) && p.Customer.HasValue)
                    .ToListAsync();
                foreach (var p in cancelledProducts)
                {
                    p.IsSelected = false;
                }

                // إلغاء المرآة
                var mirrorTag = "MirrorOf:" + transactionId;
                var mirror = await _db.Transactions
                    .FirstOrDefaultAsync(t => t.ReferenceType == mirrorTag);

                if (mirror != null && mirror.InvoiceStatus != InvoiceStatuses.Cancelled)
                {
                    var mirrorDetails = await _db.TransactionDetails
                        .Where(d => d.TransactionId == mirror.TransactionId).ToListAsync();
                    foreach (var d in mirrorDetails)
                    {
                        await UpdateStockAsync(d.ProductId, mirror.WarehouseId,
                            -(int)Math.Round(d.Quantity), mirror.TransactionId,
                            d.UnitPrice, currentUserName, "PurchaseInvoiceCancel");
                    }
                    mirror.InvoiceStatus = InvoiceStatuses.Cancelled;
                    mirror.EditReason = "إلغاء تلقائي مع فاتورة البيع";
                    mirror.EditBy = currentUserName;
                    mirror.EditAt = DateTime.Now;

                    await _audit.LogAsync("Transactions", "Cancel",
                        mirror.TransactionId.ToString(), null,
                        new { mirror.InvoiceStatus, mirror.EditReason, mirror.EditBy, mirror.EditAt }, currentUserName);

                    mirrorWasCancelled = true;
                }
            }
            else if (transaction.TransactionType == TransactionTypes.Purchase)
            {
                if (transaction.IsDelivered == true && (transaction.ReferenceType == "PurchaseInvoice" || string.IsNullOrWhiteSpace(transaction.ReferenceType)))
                {
                    foreach (var d in invoiceDetails)
                    {
                        await UpdateStockAsync(d.ProductId, transaction.WarehouseId,
                            -(int)Math.Round(d.Quantity), transactionId,
                            d.UnitPrice, currentUserName, "PurchaseInvoiceCancel");
                    }
                }
            }

            // فك الدفعات المقدمة
            var advancePayments = await _db.Payments
                .Where(p => p.TransactionId == transactionId && p.PaymentMethod == "Advance")
                .ToListAsync();

            foreach (var advPay in advancePayments)
            {
                var matchingAdvance = await _db.AdditionalCharges
                    .FirstOrDefaultAsync(c => c.TransactionId == transactionId
                        && c.PartyId == transaction.PartyId
                        && c.ChargeAmount == advPay.Amount);
                if (matchingAdvance != null)
                {
                    matchingAdvance.TransactionId = null;
                }
                _db.Payments.Remove(advPay);
            }

            // ⭐ فك ارتباط عروض الأسعار المرتبطة بفاتورة بيع ملغاة:
            // العرض يعود لحالة «مقبول» — العميل قبله فعلاً وما أُلغي هو التنفيذ فقط —
            // فيصبح جاهزاً لإعادة التحويل من جديد دون أي عمل يدوي.
            var quotationUnlinkNote = string.Empty;
            if (transaction.TransactionType == TransactionTypes.Sale)
            {
                var linkedQuotations = await _db.Quotations
                    .Where(q => q.InvoiceId == transactionId)
                    .ToListAsync();

                foreach (var q in linkedQuotations)
                {
                    q.InvoiceId = null;
                    q.Status = QuotationStatuses.Accepted;

                    await _audit.LogAsync("Quotations", "CancelUnlink",
                        q.QuotationId.ToString(), null,
                        new
                        {
                            q.QuotationId,
                            q.ReferenceNumber,
                            UnlinkedInvoiceId = transactionId,
                            NewStatus = q.Status,
                            Reason = $"فك ارتباط تلقائي بسبب إلغاء الفاتورة {transaction.ReferenceNumber ?? transactionId.ToString()}",
                            By = currentUserName,
                            At = DateTime.Now
                        }, currentUserName);

                    quotationUnlinkNote += $"، وفُك ارتباط عرض السعر {q.ReferenceNumber ?? q.QuotationId.ToString()} وأُعيد لحالة «مقبول»";
                }
            }

            transaction.InvoiceStatus = InvoiceStatuses.Cancelled;
            transaction.EditReason = reason;
            transaction.EditBy = currentUserName;
            transaction.EditAt = DateTime.Now;

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            await _audit.LogAsync("Transactions", "Cancel",
                transactionId.ToString(), null, new { Reason = reason }, currentUserName);

            await SendInvoiceNotificationsAsync(transaction, currentUserName,
                $"تم إلغاء فاتورة - السبب: {reason}");

            var cancelMessage = transaction.TransactionType == TransactionTypes.Sale
                ? (mirrorWasCancelled
                    ? "تم إلغاء الفاتورة وفاتورة الشراء المرآة وإعادة المخزون."
                    : "تم إلغاء الفاتورة وإعادة المخزون بدون وجود فاتورة شراء مرآة مرتبطة.")
                : "تم إلغاء فاتورة الشراء ومعالجة المخزون حسب حالة الاستلام.";

            if (!string.IsNullOrEmpty(quotationUnlinkNote))
                cancelMessage += quotationUnlinkNote + ".";

            return (true, cancelMessage);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return (false, $"حدث خطأ: {ex.Message}");
        }
    }

    // ============================================================
    //  مسح الفاتورة الملغية نهائياً من النظام (لصلاحية الأدمن)
    // ============================================================
    public async Task<(bool Success, string Message)> PermanentlyDeleteInvoiceAsync(
        int transactionId, string currentUserName)
    {
        var transaction = await _db.Transactions
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId);
        if (transaction == null) return (false, "الفاتورة غير موجودة.");
        if (transaction.InvoiceStatus != InvoiceStatuses.Cancelled && transaction.InvoiceStatus != "ملغية")
            return (false, "لا يمكن مسح فاتورة غير ملغية نهائياً من السيستيم.");

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // إذا كانت هناك فاتورة مرآة (شراء/بيع) نحذفها هي وسجلاتها أولاً
            var mirrorTag = "MirrorOf:" + transactionId;
            var mirrors = await _db.Transactions.Where(t => t.ReferenceType == mirrorTag).ToListAsync();
            foreach (var mirror in mirrors)
            {
                await CleanTransactionDependenciesAsync(mirror.TransactionId);
                _db.Transactions.Remove(mirror);
            }

            await CleanTransactionDependenciesAsync(transactionId);
            _db.Transactions.Remove(transaction);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            await _audit.LogAsync("Transactions", "PermanentDelete",
                transactionId.ToString(), transaction, null, currentUserName);

            return (true, "تم مسح الفاتورة الملغية وسجلاتها نهائياً من السيستيم.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return (false, $"حدث خطأ أثناء المسح النهائي: {ex.Message}");
        }
    }

    private async Task CleanTransactionDependenciesAsync(int id)
    {
        var opps = await _db.SalesOpportunities.Where(o => o.TransactionId == id).ToListAsync();
        foreach (var o in opps) o.TransactionId = null;

        var charges = await _db.AdditionalCharges.Where(c => c.TransactionId == id || c.AppliedToTransactionId == id).ToListAsync();
        foreach (var c in charges)
        {
            if (c.TransactionId == id) _db.AdditionalCharges.Remove(c);
            if (c.AppliedToTransactionId == id) c.AppliedToTransactionId = null;
        }

        var complaints = await _db.Complaints.Where(c => c.TransactionId == id).ToListAsync();
        foreach (var comp in complaints) comp.TransactionId = null;

        var tempPrints = await _db.TempInvoicePrintDetails.Where(t => t.TransactionId == id).ToListAsync();
        _db.TempInvoicePrintDetails.RemoveRange(tempPrints);

        var stockTrans = await _db.StockTransactions
            .Where(st => st.ReferenceId == id &&
                (st.ReferenceType == "SaleInvoice" || st.ReferenceType == "SaleInvoiceCancel" ||
                 st.ReferenceType == "PurchaseInvoice" || st.ReferenceType == "PurchaseInvoiceCancel" ||
                 st.ReferenceType == "SaleInvoiceEdit"))
            .ToListAsync();
        _db.StockTransactions.RemoveRange(stockTrans);

        var commissions = await _db.CommissionAssignments.Where(c => c.TransactionId == id).ToListAsync();
        _db.CommissionAssignments.RemoveRange(commissions);

        var payments = await _db.Payments.Where(p => p.TransactionId == id).ToListAsync();
        foreach (var p in payments)
        {
            var cashboxTrans = await _db.CashboxTransactions.Where(ct => ct.PaymentId == p.PaymentId).ToListAsync();
            _db.CashboxTransactions.RemoveRange(cashboxTrans);
        }
        _db.Payments.RemoveRange(payments);

        var details = await _db.TransactionDetails.Where(d => d.TransactionId == id).ToListAsync();
        _db.TransactionDetails.RemoveRange(details);
    }

    // ============================================================
    //  إضافة دفعة
    // ============================================================
    public async Task<(bool Success, string Message)> AddPaymentAsync(
        int transactionId, decimal amount, string method, int? cashBoxId,
        string? notes, string currentUserName)
    {
        if (amount <= 0) return (false, "المبلغ يجب أن يكون أكبر من صفر.");
        if (cashBoxId == null) return (false, "يرجى اختيار الخزينة.");

        var transaction = await _db.Transactions
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId);
        if (transaction == null) return (false, "الفاتورة غير موجودة.");
        if (transaction.InvoiceStatus == InvoiceStatuses.Cancelled)
            return (false, "لا يمكن إضافة دفعة لفاتورة ملغية.");

        var remaining = transaction.GrandTotal - transaction.PaidAmount;
        if (amount > remaining)
            return (false, $"المبلغ أكبر من المتبقي ({remaining:N2}).");

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var payment = new Payment
            {
                TransactionId = transactionId,
                PaymentDate = DateTime.Now,
                Amount = amount,
                PaymentMethod = method,
                Notes = notes,
                CreatedBy = currentUserName,
                CreatedAt = DateTime.Now
            };
            _db.Payments.Add(payment);
            await _db.SaveChangesAsync();

            _db.CashboxTransactions.Add(new CashboxTransaction
            {
                CashBoxId = cashBoxId.Value,
                PaymentId = payment.PaymentId,
                ReferenceId = transactionId,
                ReferenceType = "SaleInvoice",
                TransactionType = "قبض",
                Amount = amount,
                TransactionDate = DateTime.Now,
                Notes = $"تحصيل فاتورة {transaction.ReferenceNumber}",
                CreatedBy = currentUserName,
                CreatedAt = DateTime.Now
            });

            transaction.PaidAmount += amount;
            transaction.InvoiceStatus = ComputeStatus(transaction.GrandTotal, transaction.PaidAmount);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            await _audit.LogAsync("Payments", "Insert",
                payment.PaymentId.ToString(), null, payment, currentUserName);

            // إشعار للدفعة
            var pct = transaction.GrandTotal == 0 ? 0 : Math.Round((amount / transaction.GrandTotal) * 100, 1);
            await SendInvoiceNotificationsAsync(transaction, currentUserName,
                $"تحصيل دفعة {amount:N2} ج ({pct}%) على فاتورة");

            return (true, "تم تسجيل الدفعة بنجاح.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return (false, $"حدث خطأ: {ex.Message}");
        }
    }

    // ============================================================
    //  Lookups
    // ============================================================
        public async Task<List<PartyLookupDto>> SearchPartiesAsync(string? search, int max = 20)
    {
        var query = _db.Parties.AsNoTracking().Where(p => p.IsActive == true);

        // جلب البيانات من الداتابيز أولاً
        var list = await query
            .OrderBy(p => p.PartyName)
            .Select(p => new PartyLookupDto
            {
                PartyId = p.PartyId,
                PartyName = p.PartyName,
                Phone = p.Phone,
                Phone2 = p.Phone2,
                City = p.City
            })
            .ToListAsync();

        // ⭐ تطبيق البحث بالعربي في الذاكرة
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            list = list.Where(p =>
                (p.PartyName ?? "").ContainsArabic(s) ||
                (p.Phone ?? "").ContainsArabic(s) ||
                (p.Phone2 ?? "").ContainsArabic(s))
                .ToList();
        }

        list = list.Take(max).ToList();

        var ids = list.Select(x => x.PartyId).ToList();
        var balances = await _db.AdditionalCharges
            .AsNoTracking()
            .Where(c => ids.Contains(c.PartyId ?? 0) && c.TransactionId == null)
            .GroupBy(c => c.PartyId)
            .Select(g => new { PartyId = g.Key, Total = g.Sum(x => x.ChargeAmount ?? 0) })
            .ToListAsync();

        foreach (var item in list)
        {
            item.AdvanceBalance = balances
                .FirstOrDefault(b => b.PartyId == item.PartyId)?.Total ?? 0;
        }

        return list;
    }

    // ⭐⭐⭐ منتجات العميل المختار فقط
        public async Task<List<ProductLookupDto>> SearchProductsForPartyAsync(
        int partyId, string? search, int max = 50)
    {
        var query = _db.Products.AsNoTracking()
            .Where(p => p.Customer == partyId && (p.IsSelected == false || p.IsSelected == null));

        // جلب البيانات من الداتابيز أولاً
        var products = await query
            .OrderBy(p => p.ProductName)
            .Select(p => new ProductLookupDto
            {
                ProductId = p.ProductId,
                ProductName = p.ProductName,
                ProductDescription = p.ProductDescription,
                ImagePath = null,
                SuggestedSalePriceCClass = p.SuggestedSalePriceCClass,
                SuggestedSalePrice = p.SuggestedSalePrice,
                SuggestedSalePriceElite = p.SuggestedSalePriceElite,
                PurchasePriceCClass = p.PurchasePriceCClass,
                PurchasePrice = p.PurchasePrice,
                PurchasePriceElite = p.PurchasePriceElite,
                AvailableStock = _db.StockLevels
                    .Where(s => s.ProductId == p.ProductId)
                    .Sum(s => (int?)s.Quantity) ?? 0,
                IsShowroomProduct = false,
                Period = p.Period,
                PricingType = p.PricingType
            })
            .ToListAsync();

        // ⭐ تطبيق البحث بالعربي في الذاكرة
        if (!string.IsNullOrWhiteSpace(search))
        {
            products = products
                .Where(p => (p.ProductName ?? "").ContainsArabic(search) ||
                            (p.ProductDescription ?? "").ContainsArabic(search))
                .ToList();
        }

        return products.Take(max).ToList();
    }

    public async Task<List<ProductLookupDto>> SearchAvailableSaleProductsAsync(int partyId, int warehouseId, string? search, int max = 200)
    {
        var customerProducts = await SearchProductsForPartyAsync(partyId, search, int.MaxValue);

        var showroomProducts = await _db.Products.AsNoTracking()
            .Where(p => !p.Customer.HasValue)
            .OrderBy(p => p.ProductName)
            .Select(p => new ProductLookupDto
            {
                ProductId = p.ProductId,
                ProductName = p.ProductName,
                ProductDescription = p.ProductDescription,
                ImagePath = null,
                SuggestedSalePriceCClass = p.SuggestedSalePriceCClass,
                SuggestedSalePrice = p.SuggestedSalePrice,
                SuggestedSalePriceElite = p.SuggestedSalePriceElite,
                PurchasePriceCClass = p.PurchasePriceCClass,
                PurchasePrice = p.PurchasePrice,
                PurchasePriceElite = p.PurchasePriceElite,
                AvailableStock = _db.StockLevels
                    .Where(s => s.ProductId == p.ProductId && s.WarehouseId == warehouseId)
                    .Sum(s => (int?)s.Quantity) ?? 0,
                IsShowroomProduct = true,
                Period = p.Period,
                PricingType = p.PricingType
            })
            .ToListAsync();

        showroomProducts = showroomProducts
            .Where(p => p.AvailableStock > 0)
            .ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            showroomProducts = showroomProducts
                .Where(p => (p.ProductName ?? "").ContainsArabic(s) ||
                            (p.ProductDescription ?? "").ContainsArabic(s))
                .ToList();
        }

        return customerProducts
            .Concat(showroomProducts)
            .OrderBy(p => p.IsShowroomProduct)
            .ThenBy(p => p.ProductName)
            .Take(max)
            .ToList();
    }

    public async Task<List<ProductLookupDto>> SearchShowroomProductsAsync(string? search, int max = 200)
    {
        var query = _db.Products.AsNoTracking()
            .Where(p => !p.Customer.HasValue);

        var products = await query
            .OrderBy(p => p.ProductName)
            .Select(p => new ProductLookupDto
            {
                ProductId = p.ProductId,
                ProductName = p.ProductName,
                ProductDescription = p.ProductDescription,
                ImagePath = null,
                SuggestedSalePriceCClass = p.SuggestedSalePriceCClass,
                SuggestedSalePrice = p.SuggestedSalePrice,
                SuggestedSalePriceElite = p.SuggestedSalePriceElite,
                PurchasePriceCClass = p.PurchasePriceCClass,
                PurchasePrice = p.PurchasePrice,
                PurchasePriceElite = p.PurchasePriceElite,
                AvailableStock = _db.StockLevels
                    .Where(s => s.ProductId == p.ProductId)
                    .Sum(s => (int?)s.Quantity) ?? 0,
                IsShowroomProduct = true,
                Period = p.Period,
                PricingType = p.PricingType
            })
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(search))
        {
            products = products
                .Where(p => (p.ProductName ?? "").ContainsArabic(search) ||
                            (p.ProductDescription ?? "").ContainsArabic(search))
                .ToList();
        }

        return products.Take(max).ToList();
    }

    public async Task<List<Warehouse>> GetWarehousesAsync()
    {
        return await _db.Warehouses
            .AsNoTracking()
            .Include(w => w.Branch)
            .Where(w => w.IsActive == true)
            .OrderBy(w => w.Branch != null ? w.Branch.BranchNameAr : "")
            .ThenBy(w => w.WarehouseName)
            .ToListAsync();
    }

    public async Task<List<Warehouse>> GetWarehousesForUserAsync(string userName)
    {
        var preferredBranchId = await _db.Users
            .AsNoTracking()
            .Where(u => u.Username == userName)
            .Select(u => u.DefaultBranchId)
            .FirstOrDefaultAsync();

        if (!preferredBranchId.HasValue)
        {
            preferredBranchId = await _db.Users
                .AsNoTracking()
                .Where(u => u.Username == userName && u.EmployeeId.HasValue)
                .Join(_db.Employees.AsNoTracking(),
                    u => u.EmployeeId,
                    e => (int?)e.EmployeeId,
                    (u, e) => e.BranchId)
                .FirstOrDefaultAsync();
        }

        var warehouses = await _db.Warehouses
            .AsNoTracking()
            .Include(w => w.Branch)
            .Where(w => w.IsActive == true)
            .ToListAsync();

        return warehouses
            .OrderByDescending(w => preferredBranchId.HasValue && w.BranchId == preferredBranchId.Value)
            .ThenBy(w => w.Branch != null ? w.Branch.BranchNameAr : "")
            .ThenBy(w => w.WarehouseName)
            .ToList();
    }

    public async Task<List<CashBox>> GetCashBoxesAsync()
    {
        return await _db.CashBoxes
            .AsNoTracking()
            .OrderBy(c => c.CashBoxName)
            .ToListAsync();
    }

    // ============================================================
    //  جلب الموظف من اليوزر
    // ============================================================
    public async Task<int?> GetEmployeeIdByUserNameAsync(string userName)
    {
        return await _db.Users
            .AsNoTracking()
            .Where(u => u.Username == userName && u.EmployeeId.HasValue)
            .Select(u => u.EmployeeId)
            .FirstOrDefaultAsync();
    }

    public async Task<string?> GetEmployeeNameByIdAsync(int employeeId)
    {
        return await _db.Employees
            .AsNoTracking()
            .Where(e => e.EmployeeId == employeeId)
            .Select(e => e.FullName)
            .FirstOrDefaultAsync();
    }

    // ============================================================
    //  الدفعات المقدمة
    // ============================================================
    public async Task<List<CustomerAdvanceDto>> GetCustomerAdvancesAsync(int partyId, bool unappliedOnly = true)
    {
        var query = _db.AdditionalCharges
            .AsNoTracking()
            .Where(c => c.PartyId == partyId);

        if (unappliedOnly)
            query = query.Where(c => c.TransactionId == null);

        return await query
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new CustomerAdvanceDto
            {
                ChargeId = c.ChargeId,
                PartyId = c.PartyId ?? 0,
                ChargeType = c.ChargeType,
                ChargeDescription = c.ChargeDescription,
                ChargeAmount = c.ChargeAmount ?? 0,
                Notes = c.Notes,
                CreatedBy = c.CreatedBy,
                CreatedAt = c.CreatedAt,
                IsApplied = c.TransactionId != null,
                AppliedToTransactionId = c.TransactionId,
                AppliedToReferenceNumber = c.TransactionId == null ? null :
                    _db.Transactions.Where(t => t.TransactionId == c.TransactionId)
                        .Select(t => t.ReferenceNumber).FirstOrDefault()
            })
            .ToListAsync();
    }

    public async Task<decimal> GetCustomerAdvanceBalanceAsync(int partyId)
    {
        return await _db.AdditionalCharges
            .AsNoTracking()
            .Where(c => c.PartyId == partyId && c.TransactionId == null)
            .SumAsync(c => (decimal?)(c.ChargeAmount ?? 0)) ?? 0;
    }

    public async Task<(bool Success, string Message, int? ChargeId)> AddCustomerAdvanceAsync(
        int partyId, decimal amount, string description, string? notes, string currentUserName)
    {
        if (amount <= 0) return (false, "المبلغ يجب أن يكون أكبر من صفر.", null);

        var party = await _db.Parties.AsNoTracking().FirstOrDefaultAsync(p => p.PartyId == partyId);
        if (party == null) return (false, "العميل غير موجود.", null);

        // ⭐ الدفعات الجديدة تُسجل بالنوع الصريح Advance (ما عدا رسوم المعاينة)
        var chargeType = description == AdvanceChargeTypes.Inspection
            ? ChargeTypes.Inspection
            : ChargeTypes.Advance;

        var charge = new AdditionalCharge
        {
            PartyId = partyId,
            TransactionId = null,
            ChargeType = chargeType,
            ChargeDescription = description,
            ChargeAmount = amount,
            Status = ChargeStatuses.Paid,
            Notes = notes,
            CreatedBy = currentUserName,
            CreatedAt = DateTime.Now
        };

        _db.AdditionalCharges.Add(charge);
        await _db.SaveChangesAsync();

        var cashBoxId = await GetDefaultCashBoxIdAsync();
        _db.CashboxTransactions.Add(new CashboxTransaction
        {
            CashBoxId = cashBoxId,
            ReferenceId = charge.ChargeId,
            ReferenceType = "Charge",
            TransactionType = "قبض",
            Amount = amount,
            TransactionDate = DateTime.Now,
            Notes = $"تحصيل {amount:N2} ج - {description} - {party.PartyName}",
            CreatedBy = currentUserName,
            CreatedAt = DateTime.Now
        });
        await _db.SaveChangesAsync();

        await _audit.LogAsync("AdditionalCharges", "Insert",
            charge.ChargeId.ToString(), null, charge, currentUserName);

        return (true, "تم تسجيل الدفعة المقدمة بنجاح.", charge.ChargeId);
    }

    public async Task<(bool Success, string Message)> DeleteCustomerAdvanceAsync(
        int chargeId, string currentUserName)
    {
        var charge = await _db.AdditionalCharges.FirstOrDefaultAsync(c => c.ChargeId == chargeId);
        if (charge == null) return (false, "الدفعة غير موجودة.");
        if (charge.TransactionId != null)
            return (false, "لا يمكن حذف دفعة تم تطبيقها على فاتورة.");

        _db.AdditionalCharges.Remove(charge);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("AdditionalCharges", "Delete",
            chargeId.ToString(), charge, null, currentUserName);

        return (true, "تم حذف الدفعة المقدمة.");
    }

    // ============================================================
    //  Helpers
    // ============================================================
    private async Task<int> GetDefaultCashBoxIdAsync()
    {
        var cashBox = await _db.CashBoxes.AsNoTracking().FirstOrDefaultAsync();
        return cashBox?.CashBoxId ?? 1;
    }

    private async Task SendInvoiceNotificationsAsync(
        Transaction transaction, string actor, string action)
    {
        try
        {
            var partyName = await _db.Parties
                .Where(p => p.PartyId == transaction.PartyId)
                .Select(p => p.PartyName).FirstOrDefaultAsync() ?? "غير محدد";

            var title = "🧾 إشعار فاتورة";
            var message = $"{action}: {transaction.ReferenceNumber} للعميل {partyName} " +
                          $"بقيمة {transaction.GrandTotal:N2} ج بواسطة {actor}";

            await _notify.NotifyRoleAsync(title, message, SystemRoles.Admin, actor,
                "sales/invoices", "Transactions", transaction.TransactionId);

            await _notify.NotifyRoleAsync(title, message, SystemRoles.AccountManager, actor,
                "sales/invoices", "Transactions", transaction.TransactionId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[InvoiceService.Notify] {ex.Message}");
        }
    }

    // ⭐ إشعار طلب تعديل الفاتورة يوجَّه لمدير الإنتاج (مالك تدفق التسليم) — يعدّل التاريخ من شاشة أوامر التشغيل
    private async Task SendInvoiceEditRequestToProductionAsync(Transaction transaction, string requester, string reason)
    {
        try
        {
            var partyName = await _db.Parties
                .Where(p => p.PartyId == transaction.PartyId)
                .Select(p => p.PartyName)
                .FirstOrDefaultAsync() ?? "غير محدد";

            var title = "🧾 طلب تعديل فاتورة";
            var message = $"طلب تعديل الفاتورة {transaction.ReferenceNumber} للعميل {partyName} بواسطة {requester}." +
                          $"\nالسبب: {reason}" +
                          "\nبرجاء تعديل تاريخ التسليم من شاشة أوامر التشغيل (زر 📅 تعديل التاريخ).";

            await _notify.NotifyRoleAsync(title, message, SystemRoles.ProductionManager, requester,
                "production/job-orders", "Transactions", transaction.TransactionId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[InvoiceService.EditRequestNotify] {ex.Message}");
        }
    }

    private async Task SendProductionStartNotificationAsync(Transaction transaction, string actor)
    {
        try
        {
            if (transaction.TransactionType != TransactionTypes.Sale)
                return;

            var partyName = await _db.Parties
                .Where(p => p.PartyId == transaction.PartyId)
                .Select(p => p.PartyName)
                .FirstOrDefaultAsync() ?? "غير محدد";

            var title = "🏭 أمر تصنيع جديد";
            var message = $"تم إنشاء فاتورة بيع {transaction.ReferenceNumber} للعميل {partyName}. برجاء بدء التصنيع أو تحديد تاريخ الاستلام.";

            if (transaction.DueDate.HasValue)
                message += $"\nتاريخ الاستحقاق/الاستلام المتوقع: {transaction.DueDate.Value:yyyy/MM/dd}";

            await _notify.NotifyRoleAsync(
                title: title,
                message: message,
                role: SystemRoles.ProductionManager,
                createdBy: actor,
                formName: $"sales/invoices/{transaction.TransactionId}/job-order",
                relatedTable: "Transactions");

            await _notify.NotifyRoleAsync(
                title: title,
                message: message,
                role: "factory",
                createdBy: actor,
                formName: $"sales/invoices/{transaction.TransactionId}/job-order",
                relatedTable: "Transactions");

            await _notify.NotifyRoleAsync(
                title: title,
                message: message,
                role: SystemRoles.FactoryManager,
                createdBy: actor,
                formName: $"sales/invoices/{transaction.TransactionId}/job-order",
                relatedTable: "Transactions");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[InvoiceService.ProductionNotify] {ex.Message}");
        }
    }

    // ⭐ إشعار "أمر توريد جديد" عند إنشاء فاتورة شراء مباشرة للمعرض — يُوجَّه لأدوار المصنع/الإنتاج الثلاثة
    private async Task SendPurchaseSupplyNotificationAsync(Transaction transaction, string actor)
    {
        try
        {
            var itemsCount = await _db.TransactionDetails
                .CountAsync(d => d.TransactionId == transaction.TransactionId);

            var title = "🏭 أمر توريد جديد";
            var message = $"تم إنشاء فاتورة شراء {transaction.ReferenceNumber} من المورد (المصنع) بواسطة {actor}."
                          + $"\nعدد الأصناف: {itemsCount}"
                          + (transaction.DueDate.HasValue
                              ? $"\nتاريخ الاستلام المتوقع: {transaction.DueDate.Value:yyyy/MM/dd}"
                              : "")
                          + "\nبرجاء متابعة تجهيز المنتجات والشحن والاستلام من شاشة استلامات الشراء.";

            await _notify.NotifyRoleAsync(title, message, SystemRoles.ProductionManager, actor,
                "purchase-receipt-status", "Transactions", transaction.TransactionId);
            await _notify.NotifyRoleAsync(title, message, "factory", actor,
                "purchase-receipt-status", "Transactions", transaction.TransactionId);
            await _notify.NotifyRoleAsync(title, message, SystemRoles.FactoryManager, actor,
                "purchase-receipt-status", "Transactions", transaction.TransactionId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[InvoiceService.PurchaseSupplyNotify] {ex.Message}");
        }
    }

    private async Task SendInvoiceEditDecisionNotificationToRequesterAsync(Transaction transaction, bool approved, string reviewer, string? reviewNotes)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(transaction.EditBy))
                return;

            if (string.Equals(transaction.EditBy, reviewer, StringComparison.OrdinalIgnoreCase))
                return;

            var partyName = await _db.Parties
                .Where(p => p.PartyId == transaction.PartyId)
                .Select(p => p.PartyName)
                .FirstOrDefaultAsync() ?? "غير محدد";

            var title = approved ? "✅ تمت الموافقة على طلب تعديل الفاتورة" : "❌ تم رفض طلب تعديل الفاتورة";
            var message = approved
                ? $"تمت الموافقة بواسطة {reviewer} على طلب تعديل الفاتورة {transaction.ReferenceNumber} للعميل {partyName}. يمكنك الآن فتح الفاتورة واستكمال التعديل."
                : $"تم رفض طلب تعديل الفاتورة {transaction.ReferenceNumber} للعميل {partyName} بواسطة {reviewer}.";

            if (!string.IsNullOrWhiteSpace(reviewNotes))
                message += approved
                    ? $"\nملاحظات المراجعة: {reviewNotes}"
                    : $"\nسبب / ملاحظات الرفض: {reviewNotes}";

            await _notify.AddAsync(
                title: title,
                message: message,
                recipientUser: transaction.EditBy,
                createdBy: reviewer,
                formName: "sales/invoices",
                relatedTable: "Transactions",
                relatedId: transaction.TransactionId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[InvoiceService.EditDecisionNotify] {ex.Message}");
        }
    }

    private static string NormalizePricingTier(string? tier)
    {
        if (string.Equals(tier, PricingTiers.CClass, StringComparison.OrdinalIgnoreCase))
            return PricingTiers.CClass;

        if (string.Equals(tier, PricingTiers.Elite, StringComparison.OrdinalIgnoreCase))
            return PricingTiers.Elite;

        return PricingTiers.Premium;
    }

    private static string ExtractPricingTierFromNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return PricingTiers.Premium;

        if (notes.StartsWith($"[{PricingTiers.CClass}]", StringComparison.OrdinalIgnoreCase))
            return PricingTiers.CClass;

        if (notes.StartsWith($"[{PricingTiers.Elite}]", StringComparison.OrdinalIgnoreCase))
            return PricingTiers.Elite;

        return PricingTiers.Premium;
    }

    private static decimal GetPurchasePriceByTier(InvoiceItemDto item, string tier)
    {
        if (item.SelectedAlternativeId.HasValue)
        {
            return tier switch
            {
                var t when t == PricingTiers.CClass => item.AlternativePurchasePriceCClass ?? 0m,
                var t when t == PricingTiers.Elite => item.AlternativePurchasePriceElite ?? item.AlternativePurchasePricePremium ?? 0m,
                _ => item.AlternativePurchasePricePremium ?? 0m
            };
        }

        return tier switch
        {
            var t when t == PricingTiers.CClass => item.PurchasePriceCClass ?? 0m,
            var t when t == PricingTiers.Elite => item.PurchasePriceElite ?? item.PurchasePricePremium ?? 0m,
            _ => item.PurchasePricePremium ?? 0m
        };
    }

    private (bool IsValid, string Message) ValidateInvoice(InvoiceFormDto dto)
    {
        if (dto.PartyId == null || dto.PartyId == 0)
            return (false, "يرجى اختيار العميل.");
        if (dto.WarehouseId == null || dto.WarehouseId == 0)
            return (false, "يرجى اختيار المخزن.");
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

    private void CalculateTotals(InvoiceFormDto dto)
    {
        dto.TotalAmount = Math.Round(dto.Items.Sum(i => i.TotalAmount), 2);

        // ✅ الأولوية لقيمة الخصم الفعلية لو موجودة
        // لأن إعادة اشتقاق الخصم من نسبة مقربة قد يغيّر الرقم الأصلي.
        if (dto.DiscountAmount.HasValue && dto.DiscountAmount.Value > 0)
        {
            if (dto.DiscountAmount.Value > dto.TotalAmount)
                dto.DiscountAmount = dto.TotalAmount;

            dto.DiscountPercentage = dto.TotalAmount > 0
                ? Math.Round((dto.DiscountAmount.Value / dto.TotalAmount) * 100m, 2)
                : 0;
        }
        else if (dto.DiscountPercentage.HasValue && dto.DiscountPercentage.Value > 0)
        {
            dto.DiscountAmount = Math.Round(
                dto.TotalAmount * (dto.DiscountPercentage.Value / 100m), 2);
        }
        else
        {
            dto.DiscountAmount = 0;
            dto.DiscountPercentage = 0;
        }

        dto.NetTotalAmount = dto.TotalAmount - (dto.DiscountAmount ?? 0);
        dto.TotalChargesAmount = Math.Round(dto.Charges.Sum(c => c.ChargeAmount), 2);
        dto.GrandTotal = (dto.NetTotalAmount ?? 0) + dto.TotalChargesAmount;
    }

    private string ComputeStatus(decimal grandTotal, decimal paid)
    {
        if (paid >= grandTotal && grandTotal > 0) return InvoiceStatuses.Paid;
        if (paid > 0) return InvoiceStatuses.PartiallyPaid;
        return InvoiceStatuses.Open;
    }

    private async Task UpdateStockAsync(int productId, int warehouseId, int qtyChange,
        int referenceId, decimal unitPrice, string user, string referenceType)
    {
        var stock = await _db.StockLevels
            .FirstOrDefaultAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId);

        if (stock == null)
        {
            stock = new StockLevel
            {
                ProductId = productId,
                WarehouseId = warehouseId,
                Quantity = qtyChange,
                CreatedBy = user,
                CreatedAt = DateTime.Now,
                LastUpdatedAt = DateTime.Now
            };
            _db.StockLevels.Add(stock);
        }
        else
        {
            stock.Quantity += qtyChange;
            stock.LastUpdatedAt = DateTime.Now;
        }

        _db.StockTransactions.Add(new StockTransaction
        {
            ProductId = productId,
            WarehouseId = warehouseId,
            TransactionType = qtyChange < 0 ? "Out" : "In",
            Quantity = Math.Abs(qtyChange),
            TransactionDate = DateTime.Now,
            ReferenceId = referenceId,
            ReferenceType = referenceType,
            UnitPrice = unitPrice,
            TotalAmount = unitPrice * Math.Abs(qtyChange),
            CreatedBy = user,
            CreatedAt = DateTime.Now
        });

        await _db.SaveChangesAsync();
    }
}
