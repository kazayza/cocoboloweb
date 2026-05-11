using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class InvoiceService : IInvoiceService
{
    private readonly db24804Context _db;
    private readonly IAuditService _audit;
    private readonly NotificationService _notify;

    public InvoiceService(db24804Context db, IAuditService audit, NotificationService notify)
    {
        _db = db;
        _audit = audit;
        _notify = notify;
    }

    // ============================================================
    //  قائمة الفواتير
    // ============================================================
    public async Task<PagedResult<InvoiceListDto>> GetInvoicesAsync(InvoiceFilterDto filter)
    {
        var query = _db.Transactions
            .AsNoTracking()
            .Where(t => t.TransactionType == filter.TransactionType)
            .AsQueryable();

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
                CreatedAt = t.CreatedAt
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

        var items = await (from d in _db.TransactionDetails.AsNoTracking()
                           join p in _db.Products.AsNoTracking() on d.ProductId equals p.ProductId
                           where d.TransactionId == transactionId
                           select new InvoiceItemDto
                           {
                               DetailId = d.DetailId,
                               ProductId = d.ProductId,
                               ProductName = p.ProductName,
                               ProductDescription = p.ProductDescription,
                               ProductImagePath = _db.ProductImages
                                   .Where(im => im.ProductId == p.ProductId)
                                   .Select(im => im.ImagePath)
                                   .FirstOrDefault(),
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

        return new InvoiceFormDto
        {
            TransactionId = t.TransactionId,
            ReferenceNumber = t.ReferenceNumber,
            TransactionDate = t.TransactionDate,
            PartyId = t.PartyId,
            PartyName = partyName,
            WarehouseId = t.WarehouseId,
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
        var query = _db.Transactions
            .AsNoTracking()
            .Where(t => t.TransactionType == transactionType);

        if (from.HasValue) query = query.Where(t => t.TransactionDate >= from.Value.Date);
        if (to.HasValue) query = query.Where(t => t.TransactionDate <= to.Value.Date.AddDays(1).AddTicks(-1));

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

            // فاتورة المبيعات
            var saleTransaction = new Transaction
            {
                TransactionDate = dto.TransactionDate,
                PartyId = dto.PartyId!.Value,
                TransactionType = TransactionTypes.Sale,
                WarehouseId = dto.WarehouseId!.Value,
                ReferenceNumber = dto.ReferenceNumber,
                ReferenceType = "Invoice",
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

            // فاتورة الشراء المرآة
            var mirrorRefNumber = await GenerateNextInvoiceNumberAsync(TransactionTypes.Purchase);
            var mirrorPurchase = new Transaction
            {
                TransactionDate = dto.TransactionDate,
                PartyId = SystemConstants.DefaultSupplierId,
                TransactionType = TransactionTypes.Purchase,
                WarehouseId = dto.WarehouseId.Value,
                ReferenceNumber = mirrorRefNumber,
                ReferenceType = "MirrorOf:" + saleTransaction.TransactionId,
                EmpId = dto.PartyId, // ⭐ كود العميل
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

            // أصناف المرآة (يزود المخزون)
            foreach (var item in dto.Items)
            {
                var purchasePrice = item.PricingTier == PricingTiers.Elite
                    ? (item.PurchasePriceElite ?? item.PurchasePricePremium ?? 0)
                    : (item.PurchasePricePremium ?? 0);

                var purchaseDetail = new TransactionDetail
                {
                    TransactionId = mirrorPurchase.TransactionId,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = purchasePrice,
                    TotalAmount = Math.Round(item.Quantity * purchasePrice, 2),
                    Notes = $"[{item.PricingTier}] - مقابل بيع {dto.ReferenceNumber}"
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

            // أصناف فاتورة البيع (يخصم المخزون)
            foreach (var item in dto.Items)
            {
                var detail = new TransactionDetail
                {
                    TransactionId = saleTransaction.TransactionId,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    TotalAmount = item.TotalAmount,
                    Notes = string.IsNullOrEmpty(item.Notes)
                        ? $"[{item.PricingTier}]"
                        : $"[{item.PricingTier}] {item.Notes}"
                };
                _db.TransactionDetails.Add(detail);
                


                await UpdateStockAsync(item.ProductId, saleTransaction.WarehouseId,
                    -(int)Math.Round(item.Quantity), saleTransaction.TransactionId,
                    item.UnitPrice, currentUserName, "SaleInvoice");
            }

            // ⭐ تحديث IsSelected للمنتجات المباعة
            foreach (var item in dto.Items)
            {
                var product = await _db.Products.FirstOrDefaultAsync(p => p.ProductId == item.ProductId);
                if (product != null)
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
                    TransactionType = "In",
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
            await _audit.LogAsync("Transactions", "Insert",
                mirrorPurchase.TransactionId.ToString(), null, mirrorPurchase, currentUserName);

            // ⭐ إشعارات للأدمن ومدير المبيعات
            await SendInvoiceNotificationsAsync(saleTransaction, currentUserName, "تم إنشاء فاتورة جديدة");

            return (true,
                $"تم إنشاء الفاتورة {saleTransaction.ReferenceNumber} مع فاتورة الشراء المرآة {mirrorRefNumber}.",
                saleTransaction.TransactionId,
                mirrorPurchase.TransactionId);
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
    transaction.EditBy = currentUserName;
    transaction.EditAt = DateTime.Now;

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
    //  إلغاء فاتورة
    // ============================================================
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
            var saleDetails = await _db.TransactionDetails
                .Where(d => d.TransactionId == transactionId).ToListAsync();
            foreach (var d in saleDetails)
            {
                await UpdateStockAsync(d.ProductId, transaction.WarehouseId,
                    +(int)Math.Round(d.Quantity), transactionId,
                    d.UnitPrice, currentUserName, "SaleInvoiceCancel");
            }
                        // ⭐ إرجاع IsSelected = false للمنتجات الملغاة
            var cancelledProductIds = saleDetails.Select(d => d.ProductId).ToList();
            var cancelledProducts = await _db.Products
                .Where(p => cancelledProductIds.Contains(p.ProductId))
                .ToListAsync();
            foreach (var p in cancelledProducts)
            {
                p.IsSelected = false;
            }

            // إلغاء المرآة
            if (transaction.TransactionType == TransactionTypes.Sale)
            {
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
                        new { Reason = "Mirror cancellation" }, currentUserName);
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

            return (true, "تم إلغاء الفاتورة وفاتورة الشراء المرآة وإعادة المخزون.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return (false, $"حدث خطأ: {ex.Message}");
        }
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
                TransactionType = "In",
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
                ImagePath = _db.ProductImages
                    .Where(im => im.ProductId == p.ProductId)
                    .Select(im => im.ImagePath)
                    .FirstOrDefault(),
                SuggestedSalePrice = p.SuggestedSalePrice,
                SuggestedSalePriceElite = p.SuggestedSalePriceElite,
                PurchasePrice = p.PurchasePrice,
                PurchasePriceElite = p.PurchasePriceElite,
                AvailableStock = _db.StockLevels
                    .Where(s => s.ProductId == p.ProductId)
                    .Sum(s => (int?)s.Quantity) ?? 0,
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

    public async Task<List<Warehouse>> GetWarehousesAsync()
    {
        return await _db.Warehouses
            .AsNoTracking()
            .Where(w => w.IsActive == true)
            .OrderBy(w => w.WarehouseName)
            .ToListAsync();
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

        var partyExists = await _db.Parties.AnyAsync(p => p.PartyId == partyId);
        if (!partyExists) return (false, "العميل غير موجود.", null);

        var charge = new AdditionalCharge
        {
            PartyId = partyId,
            TransactionId = null,
            ChargeDescription = description,
            ChargeAmount = amount,
            Notes = notes,
            CreatedBy = currentUserName,
            CreatedAt = DateTime.Now
        };

        _db.AdditionalCharges.Add(charge);
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

            // إشعار للأدمن
await _notify.NotifyRoleAsync(title, message, SystemRoles.Admin, actor,
    "frmInvoices", "Transactions", transaction.TransactionId);

// إشعار لمدير الحسابات
await _notify.NotifyRoleAsync(title, message, SystemRoles.AccountManager, actor,
    "frmInvoices", "Transactions", transaction.TransactionId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[InvoiceService.Notify] {ex.Message}");
        }
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

        if (dto.DiscountPercentage.HasValue && dto.DiscountPercentage.Value > 0)
        {
            dto.DiscountAmount = Math.Round(
                dto.TotalAmount * (dto.DiscountPercentage.Value / 100m), 2);
        }
        dto.DiscountAmount ??= 0;

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
