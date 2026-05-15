using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class PaymentService : IPaymentService
{
    private readonly db24804Context _db;
    private readonly IAuditService _audit;
    private readonly NotificationService _notify;

    public PaymentService(db24804Context db, IAuditService audit, NotificationService notify)
    {
        _db = db;
        _audit = audit;
        _notify = notify;
    }

    // ============================================================
    //  قائمة المدفوعات
    // ============================================================
    public async Task<PagedResult<PaymentListDto>> GetPaymentsAsync(PaymentFilterDto filter)
{
    var query = from p in _db.Payments.AsNoTracking()
                join t in _db.Transactions.AsNoTracking() on p.TransactionId equals t.TransactionId
                where t.TransactionType == filter.TransactionType
                select new { p, t };

    // استبعد فواتير المرآة لو مش مطلوبة
    ///if (filter.IncludeMirrorPurchases != true && filter.TransactionType == TransactionTypes.Purchase)
    ///{
        ///query = query.Where(x => x.t.PartyId != SystemConstants.DefaultSupplierId
                               ///  || x.t.ReferenceType == null
                               ///  || !x.t.ReferenceType.StartsWith("MirrorOf:"));
   /// }

    // ⭐ فلتر بحث بالعربي + EmpID
    if (!string.IsNullOrWhiteSpace(filter.SearchText))
{
    var s = filter.SearchText.Trim();

    var allParties = await _db.Parties.AsNoTracking()
        .Select(p => new { p.PartyId, p.PartyName, p.Phone, p.Phone2 })
        .ToListAsync();

    var matchingPartyIds = allParties
        .Where(p => (p.PartyName ?? "").Contains(s, StringComparison.OrdinalIgnoreCase)
                 || (p.Phone ?? "").Contains(s, StringComparison.OrdinalIgnoreCase)
                 || (p.Phone2 ?? "").Contains(s, StringComparison.OrdinalIgnoreCase))
        .Select(p => p.PartyId)
        .ToList();

    List<int>? empPartyIds = null;
    if (filter.TransactionType == TransactionTypes.Purchase)
    {
        empPartyIds = allParties
            .Where(p => (p.PartyName ?? "").Contains(s, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.PartyId)
            .ToList();
    }

    query = query.Where(x =>
        (x.t.ReferenceNumber != null && x.t.ReferenceNumber.Contains(s)) ||
        matchingPartyIds.Contains(x.t.PartyId) ||
        (filter.TransactionType == TransactionTypes.Purchase
            && x.t.EmpId != null
            && empPartyIds != null
            && empPartyIds.Contains(x.t.EmpId.Value))
    );
}

    // فلاتر تاريخ
    if (filter.DateFrom.HasValue)
        query = query.Where(x => x.p.PaymentDate >= filter.DateFrom.Value.Date);
    if (filter.DateTo.HasValue)
        query = query.Where(x => x.p.PaymentDate <= filter.DateTo.Value.Date.AddDays(1).AddTicks(-1));

    // فلاتر إضافية
    if (filter.PartyId.HasValue)
        query = query.Where(x => x.t.PartyId == filter.PartyId.Value);

    if (filter.EmpId.HasValue && filter.TransactionType == TransactionTypes.Purchase)
        query = query.Where(x => x.t.EmpId == filter.EmpId.Value);

    if (!string.IsNullOrWhiteSpace(filter.PaymentMethod))
        query = query.Where(x => x.p.PaymentMethod == filter.PaymentMethod);

    if (filter.CashBoxId.HasValue)
    {
        query = query.Where(x =>
            _db.CashboxTransactions.Any(ct => ct.PaymentId == x.p.PaymentId
                                              && ct.CashBoxId == filter.CashBoxId.Value));
    }

    // استثناء الـ Advance payments
    query = query.Where(x => x.p.PaymentMethod != "Advance");

    var totalCount = await query.CountAsync();

    // Sorting
    query = filter.SortBy switch
    {
        "Amount" => filter.SortDescending
            ? query.OrderByDescending(x => x.p.Amount)
            : query.OrderBy(x => x.p.Amount),
        _ => filter.SortDescending
            ? query.OrderByDescending(x => x.p.PaymentDate).ThenByDescending(x => x.p.PaymentId)
            : query.OrderBy(x => x.p.PaymentDate).ThenBy(x => x.p.PaymentId)
    };

    var rawData = await query
        .Skip((filter.PageNumber - 1) * filter.PageSize)
        .Take(filter.PageSize)
        .Select(x => new
        {
            x.p.PaymentId,
            x.p.PaymentDate,
            x.p.Amount,
            x.p.PaymentMethod,
            x.p.Notes,
            x.p.CreatedBy,
            x.p.CreatedAt,
            x.t.TransactionId,
            InvoiceRef = x.t.ReferenceNumber,
            InvoiceType = x.t.TransactionType,
            InvoiceTotal = x.t.GrandTotal,
            InvoicePaid = x.t.PaidAmount,
            x.t.PartyId,
            x.t.EmpId,
            PartyName = _db.Parties.Where(party => party.PartyId == x.t.PartyId)
                .Select(party => party.PartyName).FirstOrDefault() ?? "",
            PartyPhone = _db.Parties.Where(party => party.PartyId == x.t.PartyId)
                .Select(party => party.Phone).FirstOrDefault(),
            EmpName = x.t.EmpId == null ? null :
                (x.t.TransactionType == TransactionTypes.Purchase
                    ? _db.Parties.Where(p2 => p2.PartyId == x.t.EmpId)
                        .Select(p2 => p2.PartyName).FirstOrDefault()
                    : _db.Employees.Where(e => e.EmployeeId == x.t.EmpId)
                        .Select(e => e.FullName).FirstOrDefault()),
            CashBoxId = _db.CashboxTransactions
                .Where(ct => ct.PaymentId == x.p.PaymentId)
                .Select(ct => (int?)ct.CashBoxId).FirstOrDefault(),
            CashBoxName = (from ct in _db.CashboxTransactions
                           join cb in _db.CashBoxes on ct.CashBoxId equals cb.CashBoxId
                           where ct.PaymentId == x.p.PaymentId
                           select cb.CashBoxName).FirstOrDefault()
        })
        .ToListAsync();

    // ⭐ حساب النسبة التراكمية
var transactionPayments = rawData
    .GroupBy(x => x.TransactionId)
    .ToDictionary(
        g => g.Key,
        g => g.OrderBy(x => x.PaymentDate).ThenBy(x => x.PaymentId).ToList()
    );

var cumulativePaid = new Dictionary<(int TransactionId, int PaymentId), decimal>();
foreach (var kvp in transactionPayments)
{
    var running = 0m;
    foreach (var payment in kvp.Value)
    {
        running += payment.Amount;
        cumulativePaid[(kvp.Key, payment.PaymentId)] = running;
    }
}

var items = rawData.Select(x =>
{
    var cumPaid = cumulativePaid.GetValueOrDefault((x.TransactionId, x.PaymentId), x.Amount);
    return new PaymentListDto
    {
        PaymentId = x.PaymentId,
        ReceiptNumber = GenerateReceiptNumber(x.PaymentId, x.InvoiceType, x.PaymentDate),
        PaymentDate = x.PaymentDate,
        Amount = x.Amount,
        Percentage = x.InvoiceTotal == 0 ? 0
            : Math.Round((cumPaid / x.InvoiceTotal) * 100, 1),
        TransactionId = x.TransactionId,
        InvoiceReferenceNumber = x.InvoiceRef,
        TransactionType = x.InvoiceType,
        InvoiceGrandTotal = x.InvoiceTotal,
        InvoicePaidAmount = x.InvoicePaid,
        PartyId = x.PartyId,
        PartyName = x.PartyName,
        PartyPhone = x.PartyPhone,
        EmpId = x.EmpId,
        EmpName = x.EmpName,
        PaymentMethod = x.PaymentMethod,
        CashBoxId = x.CashBoxId,
        CashBoxName = x.CashBoxName,
        Notes = x.Notes,
        CreatedBy = x.CreatedBy,
        CreatedAt = x.CreatedAt,
        IsCancelled = false
    };
}).ToList();

    return new PagedResult<PaymentListDto>
    {
        Items = items,
        TotalCount = totalCount,
        PageNumber = filter.PageNumber,
        PageSize = filter.PageSize
    };
}

    // ============================================================
    //  دفعة واحدة
    // ============================================================
    public async Task<PaymentListDto?> GetPaymentByIdAsync(int paymentId)
    {
        var data = await (from p in _db.Payments.AsNoTracking()
                          join t in _db.Transactions.AsNoTracking() on p.TransactionId equals t.TransactionId
                          where p.PaymentId == paymentId
                          select new { p, t }).FirstOrDefaultAsync();

        if (data == null) return null;

        var partyName = await _db.Parties.Where(p => p.PartyId == data.t.PartyId)
            .Select(p => p.PartyName).FirstOrDefaultAsync() ?? "";
        var partyPhone = await _db.Parties.Where(p => p.PartyId == data.t.PartyId)
            .Select(p => p.Phone).FirstOrDefaultAsync();

        string? empName = null;
        if (data.t.EmpId.HasValue)
        {
            if (data.t.TransactionType == TransactionTypes.Purchase)
                empName = await _db.Parties.Where(p => p.PartyId == data.t.EmpId.Value)
                    .Select(p => p.PartyName).FirstOrDefaultAsync();
            else
                empName = await _db.Employees.Where(e => e.EmployeeId == data.t.EmpId.Value)
                    .Select(e => e.FullName).FirstOrDefaultAsync();
        }

        var cashBox = await (from ct in _db.CashboxTransactions
                             join cb in _db.CashBoxes on ct.CashBoxId equals cb.CashBoxId
                             where ct.PaymentId == paymentId
                             select new { ct.CashBoxId, cb.CashBoxName }).FirstOrDefaultAsync();

        return new PaymentListDto
        {
            PaymentId = data.p.PaymentId,
            ReceiptNumber = GenerateReceiptNumber(data.p.PaymentId, data.t.TransactionType, data.p.PaymentDate),
            PaymentDate = data.p.PaymentDate,
            Amount = data.p.Amount,
            Percentage = data.t.GrandTotal == 0 ? 0
                : Math.Round((data.p.Amount / data.t.GrandTotal) * 100, 1),
            TransactionId = data.t.TransactionId,
            InvoiceReferenceNumber = data.t.ReferenceNumber,
            TransactionType = data.t.TransactionType,
            InvoiceGrandTotal = data.t.GrandTotal,
            InvoicePaidAmount = data.t.PaidAmount,
            PartyId = data.t.PartyId,
            PartyName = partyName,
            PartyPhone = partyPhone,
            EmpId = data.t.EmpId,
            EmpName = empName,
            PaymentMethod = data.p.PaymentMethod,
            CashBoxId = cashBox?.CashBoxId,
            CashBoxName = cashBox?.CashBoxName,
            Notes = data.p.Notes,
            CreatedBy = data.p.CreatedBy,
            CreatedAt = data.p.CreatedAt
        };
    }

    // ============================================================
    //  سند الطباعة
    // ============================================================
    public async Task<PaymentReceiptDto?> GetPaymentForReceiptAsync(int paymentId)
    {
        var payment = await GetPaymentByIdAsync(paymentId);
        if (payment == null) return null;

        var party = await _db.Parties.AsNoTracking()
            .FirstOrDefaultAsync(p => p.PartyId == payment.PartyId);

        var company = await _db.CompanyInfos.AsNoTracking().FirstOrDefaultAsync();

        var dto = new PaymentReceiptDto
        {
            Payment = payment,
            CustomerAddress = party?.Address,
            CustomerCity = party?.City,
            CustomerEmail = party?.Email,
            CustomerNationalId = party?.NationalId,
            AmountInWords = ConvertNumberToArabicWords(payment.Amount),
            ReceiptType = payment.TransactionType == TransactionTypes.Sale ? "Receive" : "Pay"
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
    public async Task<PaymentStatsDto> GetStatsAsync(string transactionType, DateTime? from = null, DateTime? to = null)
    {
        var query = from p in _db.Payments.AsNoTracking()
                    join t in _db.Transactions.AsNoTracking() on p.TransactionId equals t.TransactionId
                    where t.TransactionType == transactionType
                          && p.PaymentMethod != "Advance"
                    select new { p, t };

        if (from.HasValue)
            query = query.Where(x => x.p.PaymentDate >= from.Value.Date);
        if (to.HasValue)
            query = query.Where(x => x.p.PaymentDate <= to.Value.Date.AddDays(1).AddTicks(-1));

        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);

        var stats = new PaymentStatsDto
        {
            TotalCount = await query.CountAsync(),
            TotalAmount = await query.SumAsync(x => (decimal?)x.p.Amount) ?? 0,
            TodayCount = await query.CountAsync(x => x.p.PaymentDate.Date == today),
            TodayAmount = await query.Where(x => x.p.PaymentDate.Date == today)
                .SumAsync(x => (decimal?)x.p.Amount) ?? 0,
            MonthCount = await query.CountAsync(x => x.p.PaymentDate >= monthStart),
            MonthAmount = await query.Where(x => x.p.PaymentDate >= monthStart)
                .SumAsync(x => (decimal?)x.p.Amount) ?? 0
        };

        // تفصيل الخزن
        var paymentIds = await query.Select(x => x.p.PaymentId).ToListAsync();
        stats.CashBoxBreakdown = await (from ct in _db.CashboxTransactions.AsNoTracking()
                                        join cb in _db.CashBoxes.AsNoTracking() on ct.CashBoxId equals cb.CashBoxId
                                        where ct.PaymentId != null && paymentIds.Contains(ct.PaymentId.Value)
                                        group new { ct, cb } by new { ct.CashBoxId, cb.CashBoxName } into g
                                        select new CashBoxSummaryDto
                                        {
                                            CashBoxId = g.Key.CashBoxId,
                                            CashBoxName = g.Key.CashBoxName,
                                            Count = g.Count(),
                                            Total = g.Sum(x => x.ct.Amount)
                                        }).ToListAsync();

        stats.CashBoxesCount = stats.CashBoxBreakdown.Count;

        // تفصيل طرق الدفع
        var methodGroups = await query
            .GroupBy(x => x.p.PaymentMethod ?? "Other")
            .Select(g => new { Method = g.Key, Count = g.Count(), Total = g.Sum(x => x.p.Amount) })
            .ToListAsync();

        stats.MethodBreakdown = methodGroups.Select(m => new PaymentMethodSummaryDto
        {
            PaymentMethod = m.Method,
            PaymentMethodAr = PaymentMethods.All.TryGetValue(m.Method, out var ar) ? ar : m.Method,
            Count = m.Count,
            Total = m.Total
        }).ToList();

        return stats;
    }

    // ============================================================
    //  ⭐ تحليل النسب لفاتورة معينة
    // ============================================================
    public async Task<PaymentAnalysisDto?> GetPaymentAnalysisAsync(int transactionId, decimal? proposedAmount = null)
    {
        var t = await _db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TransactionId == transactionId);
        if (t == null) return null;

        var grand = t.GrandTotal;
        var paidBefore = t.PaidAmount;
        var remaining = grand - paidBefore;

        var dto = new PaymentAnalysisDto
        {
            TransactionId = t.TransactionId,
            ReferenceNumber = t.ReferenceNumber,
            GrandTotal = grand,
            PaidBefore = paidBefore,
            Remaining = remaining,
            PaidPercentage = grand == 0 ? 0 : Math.Round((paidBefore / grand) * 100, 1)
        };

        // بناء الـ Tiers (70/20/10)
        decimal cumulativePaid = 0;
        decimal cumulativeAmount = 0;

        for (int i = 0; i < PaymentTiers.DefaultPercentages.Length; i++)
        {
            var pct = PaymentTiers.DefaultPercentages[i];
            var tierAmount = Math.Round(grand * (pct / 100m), 2);
            cumulativeAmount += tierAmount;

            // كم اتدفع من هذه الـ Tier
            var tierPaid = Math.Max(0, Math.Min(tierAmount, paidBefore - cumulativePaid));
            cumulativePaid += tierPaid;

            var tier = new PaymentTierDto
            {
                TierNumber = i + 1,
                TierName = PaymentTiers.TierNames.GetValueOrDefault(i + 1, $"الدفعة {i + 1}"),
                Percentage = pct,
                Amount = tierAmount,
                AmountPaid = tierPaid,
                Status = tierPaid >= tierAmount ? "Paid"
                         : tierPaid > 0 ? "Partial" : "Pending"
            };

            dto.Tiers.Add(tier);
        }

        // تحليل الدفعة المقترحة
        if (proposedAmount.HasValue && proposedAmount.Value > 0)
        {
            var amount = proposedAmount.Value;
            var analysis = new PaymentTierAnalysisDto
            {
                EnteredAmount = amount,
                PercentageOfInvoice = grand == 0 ? 0 : Math.Round((amount / grand) * 100, 1)
            };

            // حسب الدفعة على الـ Tiers
            decimal remainingToAllocate = amount;
            decimal accumPaid = paidBefore;

            for (int i = 0; i < dto.Tiers.Count && remainingToAllocate > 0; i++)
            {
                var tier = dto.Tiers[i];
                var tierRemaining = tier.AmountRemaining;
                if (tierRemaining <= 0) continue;

                var allocated = Math.Min(tierRemaining, remainingToAllocate);
                var newRemaining = tierRemaining - allocated;

                analysis.Allocations.Add(new TierAllocation
                {
                    TierNumber = tier.TierNumber,
                    TierName = tier.TierName,
                    AmountAllocated = allocated,
                    RemainingInTier = newRemaining,
                    Note = newRemaining == 0
                        ? "✅ ستكتمل هذه الدفعة"
                        : $"⏳ سيتبقى {newRemaining:N2} ج من هذه الدفعة"
                });

                remainingToAllocate -= allocated;
            }

            // وصف نصي
            if (amount > remaining)
            {
                analysis.Description = $"⚠️ المبلغ ({amount:N2}) أكبر من المتبقي ({remaining:N2})";
            }
            else if (amount == remaining)
            {
                analysis.Description = " ستسدد الفاتورة بالكامل بهذه الدفعة";
            }
            else
            {
                var newPaid = paidBefore + amount;
                var newRemain = grand - newPaid;
                var newPct = grand == 0 ? 0 : Math.Round((newPaid / grand) * 100, 1);
                analysis.Description = $"بعد الدفعة: المسدد {newPaid:N2} ج ({newPct}%) | المتبقي {newRemain:N2} ج";
            }

            dto.CurrentPaymentAnalysis = analysis;
        }

        return dto;
    }

    // ============================================================
    //  بحث عن فاتورة (للـ Autocomplete)
    // ============================================================
    public async Task<List<InvoiceLookupDto>> SearchInvoicesAsync(
    string transactionType, string? search, bool onlyWithRemaining = true, int max = 20)
{
    var query = _db.Transactions.AsNoTracking()
        .Where(t => t.TransactionType == transactionType
                    && t.InvoiceStatus != "Cancelled");

    if (onlyWithRemaining)
        query = query.Where(t => t.GrandTotal > t.PaidAmount);

    if (!string.IsNullOrWhiteSpace(search))
{
    var s = search.Trim();

    var allParties = await _db.Parties.AsNoTracking()
        .Select(p => new { p.PartyId, p.PartyName, p.Phone, p.Phone2 })
        .ToListAsync();

    var matchingPartyIds = allParties
        .Where(p => (p.PartyName ?? "").Contains(s, StringComparison.OrdinalIgnoreCase)
                 || (p.Phone ?? "").Contains(s, StringComparison.OrdinalIgnoreCase)
                 || (p.Phone2 ?? "").Contains(s, StringComparison.OrdinalIgnoreCase))
        .Select(p => p.PartyId)
        .ToList();

    // ⭐ في المشتريات نبحث في EmpId كمان (العميل المرتبط)
    if (transactionType == TransactionTypes.Purchase)
    {
        query = query.Where(t =>
            (t.ReferenceNumber != null && t.ReferenceNumber.Contains(s)) ||
            matchingPartyIds.Contains(t.PartyId) ||
            (t.EmpId != null && matchingPartyIds.Contains(t.EmpId.Value)));
    }
    else
    {
        query = query.Where(t =>
            (t.ReferenceNumber != null && t.ReferenceNumber.Contains(s)) ||
            matchingPartyIds.Contains(t.PartyId));
    }
}

    return await query
        .OrderByDescending(t => t.TransactionDate)
        .Take(max)
        .Select(t => new InvoiceLookupDto
        {
            TransactionId = t.TransactionId,
            ReferenceNumber = t.ReferenceNumber,
            TransactionDate = t.TransactionDate,
            PartyId = t.PartyId,
            PartyName = _db.Parties.Where(p => p.PartyId == t.PartyId)
                .Select(p => p.PartyName).FirstOrDefault() ?? "",
            PartyPhone = _db.Parties.Where(p => p.PartyId == t.PartyId)
                .Select(p => p.Phone).FirstOrDefault(),
                EmpName = t.EmpId != null
        ? _db.Parties.Where(p => p.PartyId == t.EmpId.Value)
              .Select(p => p.PartyName).FirstOrDefault()
        : null,
            GrandTotal = t.GrandTotal,
            PaidAmount = t.PaidAmount,
            TransactionType = t.TransactionType,
            Status = t.InvoiceStatus
        }).ToListAsync();
}
public async Task<List<PaymentHistoryDto>> GetPaymentHistoryAsync(int transactionId)
{
    var transaction = await _db.Transactions.AsNoTracking()
        .Where(t => t.TransactionId == transactionId)
        .Select(t => new { t.GrandTotal })
        .FirstOrDefaultAsync();

    var grandTotal = transaction?.GrandTotal ?? 0;

    return await (from p in _db.Payments.AsNoTracking()
                  where p.TransactionId == transactionId
                         && p.PaymentMethod != "Advance"
                  orderby p.PaymentDate descending
                  select new PaymentHistoryDto
                  {
                      PaymentId = p.PaymentId,
                      Amount = p.Amount,
                      PaymentDate = p.PaymentDate,
                      PaymentMethod = p.PaymentMethod ?? "Cash",
                      CashBoxName = (from ct in _db.CashboxTransactions
                                     join cb in _db.CashBoxes on ct.CashBoxId equals cb.CashBoxId
                                     where ct.PaymentId == p.PaymentId
                                     select cb.CashBoxName).FirstOrDefault(),
                      Notes = p.Notes,
                      CreatedBy = p.CreatedBy,
                      Percentage = grandTotal == 0 ? 0
                          : Math.Round((p.Amount / grandTotal) * 100, 1)
                  }).ToListAsync();
}
public async Task<PartyBalanceDto> GetPartyBalanceAsync(int partyId, string transactionType)
{
    var invoices = await _db.Transactions.AsNoTracking()
        .Where(t => t.PartyId == partyId
                    && t.TransactionType == transactionType
                    && t.InvoiceStatus != "Cancelled")
        .ToListAsync();

    return new PartyBalanceDto
    {
        TotalInvoices = invoices.Count,
        GrandTotal = invoices.Sum(t => t.GrandTotal),
        PaidAmount = invoices.Sum(t => t.PaidAmount),
        Remaining = invoices.Sum(t => t.GrandTotal - t.PaidAmount),
        PaidInvoices = invoices.Count(t => t.InvoiceStatus == "Paid"),
        PartialInvoices = invoices.Count(t => t.InvoiceStatus == "PartiallyPaid"),
        OpenInvoices = invoices.Count(t => t.InvoiceStatus == "Open" || t.InvoiceStatus == null)
    };
}
public async Task<IEnumerable<PartyLookupDto>> SearchPartiesAsync(string search)
{
    if (string.IsNullOrWhiteSpace(search)) return Enumerable.Empty<PartyLookupDto>();

    var s = search.Trim().ToLower();
    return await _db.Parties.AsNoTracking()
        .Where(p => (p.PartyName ?? "").ToLower().Contains(s)
                 || (p.Phone ?? "").ToLower().Contains(s))
        .Select(p => new PartyLookupDto
        {
            PartyId = p.PartyId,
            PartyName = p.PartyName,
            Phone = p.Phone
        })
        .Take(15)
        .ToListAsync();
}

    // ============================================================
    //  ⭐ إنشاء دفعة جديدة (الأهم)
    // ============================================================
    public async Task<(bool Success, string Message, int? PaymentId)> CreatePaymentAsync(
    PaymentFormDto dto, string currentUserName)
{
    if (dto.TransactionId == null || dto.TransactionId == 0)
        return (false, "يرجى اختيار الفاتورة.", null);
    if (dto.Amount <= 0)
        return (false, "المبلغ يجب أن يكون أكبر من صفر.", null);
    if (dto.CashBoxId == null || dto.CashBoxId == 0)
        return (false, "يرجى اختيار الخزينة.", null);

    var transaction = await _db.Transactions
        .FirstOrDefaultAsync(t => t.TransactionId == dto.TransactionId.Value);
    if (transaction == null)
        return (false, "الفاتورة غير موجودة.", null);
    if (transaction.InvoiceStatus == "Cancelled")
        return (false, "لا يمكن إضافة دفعة لفاتورة ملغية.", null);

    var remaining = transaction.GrandTotal - transaction.PaidAmount;
    if (dto.Amount > remaining)
        return (false, $"المبلغ ({dto.Amount:N2}) أكبر من المتبقي ({remaining:N2}).", null);

    // ⭐ جلب بيانات للملخص التلقائي
    var partyName = await _db.Parties.Where(p => p.PartyId == transaction.PartyId)
        .Select(p => p.PartyName).FirstOrDefaultAsync() ?? "غير محدد";
    var refNumber = transaction.ReferenceNumber ?? $"#{transaction.TransactionId}";
    var methodAr = PaymentMethods.All.TryGetValue(dto.PaymentMethod ?? "Cash", out var m) ? m : dto.PaymentMethod ?? "نقدي";
    var pct = transaction.GrandTotal == 0 ? 0 : Math.Round((dto.Amount / transaction.GrandTotal) * 100, 1);

    // ⭐ معرفة من أي نسبة (Tier)
    var tierInfo = "";
    var analysis = await GetPaymentAnalysisAsync(transaction.TransactionId, dto.Amount);
    if (analysis?.CurrentPaymentAnalysis?.Allocations.Any() == true)
    {
        var firstAlloc = analysis.CurrentPaymentAnalysis.Allocations.First();
        tierInfo = $" - {firstAlloc.TierName}";
    }

    // ⭐ تفريق تحصيل/سداد حسب النوع
    var isSale = transaction.TransactionType == TransactionTypes.Sale;
    var actionWord = isSale ? "تحصيل من" : "سداد إلى";
    var cashActionWord = isSale ? "تحصيل" : "صرف";
    var directionWord = isSale ? "من" : "إلى";

    var autoNote = $"{actionWord} {partyName} على فاتورة {refNumber}{tierInfo} - {methodAr} ({pct}%)";
    var finalNote = string.IsNullOrWhiteSpace(dto.Notes)
        ? autoNote
        : $"{autoNote} | {dto.Notes}";

    var cashBoxName = await _db.CashBoxes.Where(cb => cb.CashBoxId == dto.CashBoxId.Value)
        .Select(cb => cb.CashBoxName).FirstOrDefaultAsync() ?? "";
    var cashNote = $"{cashActionWord} {dto.Amount:N2} ج {directionWord} {partyName} - فاتورة {refNumber}{tierInfo} - {pct}% - {cashBoxName}";

    using var tx = await _db.Database.BeginTransactionAsync();
    try
    {
        var payment = new Payment
        {
            TransactionId = transaction.TransactionId,
            PaymentDate = dto.PaymentDate,
            Amount = dto.Amount,
            PaymentMethod = dto.PaymentMethod,
            Notes = finalNote,
            CreatedBy = currentUserName,
            CreatedAt = DateTime.Now
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();

        // نوع الحركة في الخزينة (قبض للمبيعات / صرف للمشتريات)
        var cashboxType = isSale ? "قبض" : "صرف";
        var refType = isSale ? "SaleInvoice" : "PurchaseInvoice";

        var cashTrans = new CashboxTransaction
        {
            CashBoxId = dto.CashBoxId.Value,
            PaymentId = payment.PaymentId,
            ReferenceId = transaction.TransactionId,
            ReferenceType = refType,
            TransactionType = cashboxType,
            Amount = dto.Amount,
            TransactionDate = DateTime.Now,
            Notes = cashNote,
            CreatedBy = currentUserName,
            CreatedAt = DateTime.Now
        };
        _db.CashboxTransactions.Add(cashTrans);

        // تحديث الفاتورة
        transaction.PaidAmount += dto.Amount;
        if (transaction.PaidAmount >= transaction.GrandTotal && transaction.GrandTotal > 0)
            transaction.InvoiceStatus = "Paid";
        else if (transaction.PaidAmount > 0)
            transaction.InvoiceStatus = "PartiallyPaid";

        await _db.SaveChangesAsync();

        // ⭐ نربط الدفعة بحركة الخزينة
        payment.CashboxTransactionId = cashTrans.CashboxTransactionId;
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        // Audit
        await _audit.LogAsync("Payments", "Insert",
            payment.PaymentId.ToString(), null, payment, currentUserName);

        // إشعارات
        try
        {
            var actionAr = isSale ? "تحصيل دفعة من" : "سداد دفعة إلى";
            var msg = $"{actionAr} {partyName} بمبلغ {dto.Amount:N2} ج ({pct}%) " +
                      $"على فاتورة {transaction.ReferenceNumber} بواسطة {currentUserName}";

            await _notify.NotifyRoleAsync("💰 إشعار دفعة", msg, SystemRoles.Admin,
                currentUserName, "frmPayments", "Payments", payment.PaymentId);
            await _notify.NotifyRoleAsync("💰 إشعار دفعة", msg, SystemRoles.SalesManager,
                currentUserName, "frmPayments", "Payments", payment.PaymentId);
        }
        catch { }

        return (true, "تم تسجيل الدفعة بنجاح.", payment.PaymentId);
    }
    catch (Exception ex)
    {
        await tx.RollbackAsync();
        return (false, $"حدث خطأ: {ex.Message}", null);
    }
}

    // ============================================================
    //  إلغاء دفعة (Hard delete + رد الخزينة)
    // ============================================================
    public async Task<(bool Success, string Message)> CancelPaymentAsync(
        int paymentId, string reason, string currentUserName)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.PaymentId == paymentId);
        if (payment == null) return (false, "الدفعة غير موجودة.");

        var transaction = await _db.Transactions
            .FirstOrDefaultAsync(t => t.TransactionId == payment.TransactionId);
        if (transaction == null) return (false, "الفاتورة المرتبطة غير موجودة.");

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // اسحب من الفاتورة
            transaction.PaidAmount -= payment.Amount;
            if (transaction.PaidAmount < 0) transaction.PaidAmount = 0;

            if (transaction.PaidAmount >= transaction.GrandTotal && transaction.GrandTotal > 0)
                transaction.InvoiceStatus = "Paid";
            else if (transaction.PaidAmount > 0)
                transaction.InvoiceStatus = "PartiallyPaid";
            else
                transaction.InvoiceStatus = "Open";

            // احذف الـ CashboxTransactions المرتبطة (يرد الخزينة)
            var cashboxTrans = await _db.CashboxTransactions
                .Where(ct => ct.PaymentId == paymentId).ToListAsync();
            _db.CashboxTransactions.RemoveRange(cashboxTrans);

            // احذف الدفعة
            _db.Payments.Remove(payment);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            await _audit.LogAsync<object>("Payments", "Cancel",
                paymentId.ToString(), payment, new { Reason = reason }, currentUserName);

            // إشعار
            try
            {
                var partyName = await _db.Parties.Where(p => p.PartyId == transaction.PartyId)
                    .Select(p => p.PartyName).FirstOrDefaultAsync() ?? "غير محدد";
                var msg = $"تم إلغاء دفعة بمبلغ {payment.Amount:N2} ج من {partyName} " +
                          $"على فاتورة {transaction.ReferenceNumber} بواسطة {currentUserName}. السبب: {reason}";

                await _notify.NotifyRoleAsync("⚠️ إلغاء دفعة", msg, SystemRoles.Admin,
                    currentUserName, "frmPayments", "Payments", paymentId);
            }
            catch { }

            return (true, "تم إلغاء الدفعة وردت قيمتها للخزينة.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return (false, $"حدث خطأ: {ex.Message}");
        }
    }
    // ============================================================
//  ⭐ جلب بيانات الدفعة للتعديل
// ============================================================
public async Task<PaymentFormDto?> GetPaymentForEditAsync(int paymentId)
{
    var payment = await _db.Payments.AsNoTracking()
        .FirstOrDefaultAsync(p => p.PaymentId == paymentId);
    if (payment == null) return null;

    var transaction = await _db.Transactions.AsNoTracking()
        .FirstOrDefaultAsync(t => t.TransactionId == payment.TransactionId);
    if (transaction == null) return null;

    var party = await _db.Parties.AsNoTracking()
        .Where(p => p.PartyId == transaction.PartyId)
        .Select(p => new { p.PartyName, p.Phone }).FirstOrDefaultAsync();

    var cashBox = await _db.CashboxTransactions.AsNoTracking()
        .Where(ct => ct.PaymentId == paymentId)
        .Select(ct => (int?)ct.CashBoxId)
        .FirstOrDefaultAsync();

    // المتبقي = المتبقي الحالي + مبلغ الدفعة القديمة (لأننا هنشيلها لو عدّلنا)
    var currentRemaining = transaction.GrandTotal - transaction.PaidAmount;

    return new PaymentFormDto
{
    TransactionId = transaction.TransactionId,
    InvoiceReferenceNumber = transaction.ReferenceNumber,
    TransactionType = transaction.TransactionType,
    PartyId = transaction.PartyId,
    PartyName = party?.PartyName ?? "",
    PartyPhone = party?.Phone,
    InvoiceGrandTotal = transaction.GrandTotal,
    InvoicePaidBefore = transaction.PaidAmount - payment.Amount,
    InvoiceRemaining = currentRemaining + payment.Amount,
    Amount = payment.Amount,
    PaymentMethod = payment.PaymentMethod ?? "Cash",
    CashBoxId = cashBox,
    Notes = payment.Notes,
    PaymentDate = payment.PaymentDate,  // ⭐ تاريخ الدفعة الأصلي
    LastModifiedBy = payment.LastModifiedBy,
    LastModifiedAt = payment.LastModifiedAt
};
}

// ============================================================
//  ⭐ تعديل دفعة
// ============================================================
public async Task<(bool Success, string Message)> UpdatePaymentAsync(
    int paymentId, PaymentFormDto dto, string currentUserName)
{
    if (dto.Amount <= 0)
        return (false, "المبلغ يجب أن يكون أكبر من صفر.");
    if (dto.CashBoxId == null || dto.CashBoxId == 0)
        return (false, "يرجى اختيار الخزينة.");

    var payment = await _db.Payments.FirstOrDefaultAsync(p => p.PaymentId == paymentId);
    if (payment == null) return (false, "الدفعة غير موجودة.");

    var transaction = await _db.Transactions
        .FirstOrDefaultAsync(t => t.TransactionId == payment.TransactionId);
    if (transaction == null) return (false, "الفاتورة المرتبطة غير موجودة.");
    if (transaction.InvoiceStatus == "Cancelled")
        return (false, "لا يمكن تعديل دفعة لفاتورة ملغية.");

    // المبلغ الأقصى = المتبقي الحالي + مبلغ الدفعة القديمة
    var maxAmount = (transaction.GrandTotal - transaction.PaidAmount) + payment.Amount;
    if (dto.Amount > maxAmount)
        return (false, $"المبلغ ({dto.Amount:N2}) أكبر من المسموح ({maxAmount:N2}).");

    // ⭐ جلب بيانات للملخص (قبل الـ Transaction)
    var partyName = await _db.Parties.Where(p => p.PartyId == transaction.PartyId)
        .Select(p => p.PartyName).FirstOrDefaultAsync() ?? "غير محدد";
    var refNumber = transaction.ReferenceNumber ?? $"#{transaction.TransactionId}";
    var methodAr = PaymentMethods.All.TryGetValue(dto.PaymentMethod ?? "Cash", out var m) ? m : dto.PaymentMethod ?? "نقدي";
    var pct = transaction.GrandTotal == 0 ? 0 : Math.Round((dto.Amount / transaction.GrandTotal) * 100, 1);

    var tierInfo = "";
    var analysis = await GetPaymentAnalysisAsync(transaction.TransactionId, dto.Amount);
    if (analysis?.CurrentPaymentAnalysis?.Allocations.Any() == true)
    {
        var firstAlloc = analysis.CurrentPaymentAnalysis.Allocations.First();
        tierInfo = $" - {firstAlloc.TierName}";
    }

    var cashBoxName = await _db.CashBoxes.Where(cb => cb.CashBoxId == dto.CashBoxId.Value)
        .Select(cb => cb.CashBoxName).FirstOrDefaultAsync() ?? "";

    // ⭐ تفريق تحصيل/سداد حسب النوع
    var isSale = transaction.TransactionType == TransactionTypes.Sale;
    var actionWord = isSale ? "تحصيل من" : "سداد إلى";
    var cashActionWord = isSale ? "تحصيل" : "صرف";

    var autoNote = $"{actionWord} {partyName} على فاتورة {refNumber}{tierInfo} - {methodAr} ({pct}%)";
    var finalNote = string.IsNullOrWhiteSpace(dto.Notes)
        ? autoNote
        : $"{autoNote} | {dto.Notes}";

    var cashNote = $"{cashActionWord} {dto.Amount:N2} ج {(isSale ? "من" : "إلى")} {partyName} - فاتورة {refNumber}{tierInfo} - {pct}% - {cashBoxName}";

    using var tx = await _db.Database.BeginTransactionAsync();
    try
    {
        // حفظ القديم للـ Audit
        var oldPayment = new { payment.Amount, payment.PaymentMethod, payment.Notes };

        // 1) نشيل أثر الدفعة القديمة من الفاتورة
        transaction.PaidAmount -= payment.Amount;

        // 2) ⭐ نبحث عن حركة الخزينة الموجودة ونعدّلها
        var existingCashTrans = await _db.CashboxTransactions
            .FirstOrDefaultAsync(ct => ct.PaymentId == paymentId);

        if (existingCashTrans == null)
        {
            // fallback: نبحث بـ ReferenceId
            var fallbackRefType = isSale ? "SaleInvoice" : "PurchaseInvoice";
            existingCashTrans = await _db.CashboxTransactions
                .FirstOrDefaultAsync(ct => ct.ReferenceId == transaction.TransactionId
                                           && ct.ReferenceType == fallbackRefType);
        }

        if (existingCashTrans != null)
        {
            // ⭐ نعدّل الحركة الموجودة (بدل مسح وإضافة)
            existingCashTrans.CashBoxId = dto.CashBoxId.Value;
            existingCashTrans.Amount = dto.Amount;
            existingCashTrans.Notes = cashNote;
            existingCashTrans.CreatedBy = currentUserName;
            existingCashTrans.CreatedAt = DateTime.Now;
        }

        // 3) نحدّث الدفعة
        payment.Amount = dto.Amount;
        payment.PaymentMethod = dto.PaymentMethod;
        payment.Notes = finalNote;
        payment.LastModifiedBy = currentUserName;
        payment.LastModifiedAt = DateTime.Now;

        // ⭐ نربط الدفعة بحركة الخزينة
        if (existingCashTrans != null)
        {
            payment.CashboxTransactionId = existingCashTrans.CashboxTransactionId;
        }

        // 4) نحدّث الفاتورة بالمبلغ الجديد
        transaction.PaidAmount += dto.Amount;
        if (transaction.PaidAmount >= transaction.GrandTotal && transaction.GrandTotal > 0)
            transaction.InvoiceStatus = "Paid";
        else if (transaction.PaidAmount > 0)
            transaction.InvoiceStatus = "PartiallyPaid";
        else
            transaction.InvoiceStatus = "Open";

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        // Audit
        await _audit.LogAsync("Payments", "Update",
            paymentId.ToString(), oldPayment, new { dto.Amount, dto.PaymentMethod, dto.Notes }, currentUserName);

        return (true, "تم تعديل الدفعة بنجاح.");
    }
    catch (Exception ex)
    {
        await tx.RollbackAsync();
        return (false, $"حدث خطأ: {ex.Message}");
    }
    
}

    // ============================================================
    //  Helpers
    // ============================================================
    public string GenerateReceiptNumber(int paymentId, string transactionType, DateTime date)
    {
        var prefix = transactionType == TransactionTypes.Sale ? "RCV" : "PMT";
        return $"{prefix}-{date.Year}-{paymentId:D5}";
    }

    // ============================================================
    //  تحويل الرقم لكلمات عربية
    // ============================================================
    public string ConvertNumberToArabicWords(decimal number)
    {
        if (number == 0) return "صفر جنيه فقط لا غير";

        var integerPart = (long)Math.Floor(number);
        var decimalPart = (int)Math.Round((number - integerPart) * 100);

        var integerWords = ConvertIntegerToWords(integerPart);
        var result = $"{integerWords} جنيه";

        if (decimalPart > 0)
        {
            var decimalWords = ConvertIntegerToWords(decimalPart);
            result += $" و {decimalWords} قرشاً";
        }

        result += " فقط لا غير";
        return result;
    }

    private string ConvertIntegerToWords(long number)
    {
        if (number == 0) return "صفر";
        if (number < 0) return "ناقص " + ConvertIntegerToWords(-number);

        if (number >= 1_000_000_000)
        {
            var billions = number / 1_000_000_000;
            var rem = number % 1_000_000_000;
            var word = billions == 1 ? "مليار" : (billions == 2 ? "ملياران" : ConvertIntegerToWords(billions) + " مليار");
            return rem == 0 ? word : word + " و " + ConvertIntegerToWords(rem);
        }
        if (number >= 1_000_000)
        {
            var millions = number / 1_000_000;
            var rem = number % 1_000_000;
            var word = millions == 1 ? "مليون" : (millions == 2 ? "مليونان" : ConvertIntegerToWords(millions) + " مليون");
            return rem == 0 ? word : word + " و " + ConvertIntegerToWords(rem);
        }
        if (number >= 1000)
        {
            var thousands = number / 1000;
            var rem = number % 1000;
            string word;
            if (thousands == 1) word = "ألف";
            else if (thousands == 2) word = "ألفان";
            else if (thousands >= 3 && thousands <= 10) word = ConvertIntegerToWords(thousands) + " آلاف";
            else word = ConvertIntegerToWords(thousands) + " ألف";
            return rem == 0 ? word : word + " و " + ConvertIntegerToWords(rem);
        }
        if (number >= 100)
        {
            var hundreds = number / 100;
            var rem = number % 100;
            var hundredsWord = hundreds switch
            {
                1 => "مائة",
                2 => "مائتان",
                3 => "ثلاثمائة",
                4 => "أربعمائة",
                5 => "خمسمائة",
                6 => "ستمائة",
                7 => "سبعمائة",
                8 => "ثمانمائة",
                9 => "تسعمائة",
                _ => ""
            };
            return rem == 0 ? hundredsWord : hundredsWord + " و " + ConvertIntegerToWords(rem);
        }
        if (number >= 20)
        {
            var tens = number / 10;
            var ones = number % 10;
            var tensWord = tens switch
            {
                2 => "عشرون",
                3 => "ثلاثون",
                4 => "أربعون",
                5 => "خمسون",
                6 => "ستون",
                7 => "سبعون",
                8 => "ثمانون",
                9 => "تسعون",
                _ => ""
            };
            return ones == 0 ? tensWord : ConvertIntegerToWords(ones) + " و " + tensWord;
        }
        return number switch
        {
            1 => "واحد",
            2 => "اثنان",
            3 => "ثلاثة",
            4 => "أربعة",
            5 => "خمسة",
            6 => "ستة",
            7 => "سبعة",
            8 => "ثمانية",
            9 => "تسعة",
            10 => "عشرة",
            11 => "أحد عشر",
            12 => "اثنا عشر",
            13 => "ثلاثة عشر",
            14 => "أربعة عشر",
            15 => "خمسة عشر",
            16 => "ستة عشر",
            17 => "سبعة عشر",
            18 => "ثمانية عشر",
            19 => "تسعة عشر",
            _ => number.ToString()
        };
    }
}