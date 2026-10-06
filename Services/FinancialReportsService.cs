using System.Globalization;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class FinancialReportsService : IFinancialReportsService
{
    private readonly db24804Context _db;
    private readonly IHttpContextAccessor _http;
    private readonly IAuditService _audit;

    public FinancialReportsService(db24804Context db, IHttpContextAccessor http, IAuditService audit)
    {
        _audit = audit;
        _db = db;
        _http = http;
    }

    // ⭐ منشئو الفواتير المحمية (فواتير مديري الحسابات) — قائمة فارغة لو المستخدم مخوّل
    // ⭐ (12-ح7 قرار المستخدم): قائمة الدخل والمركز المالي يشملان كل الفواتير —
    //    لا استبعاد لفواتير مدير الحسابات من الأرقام المالية (الحماية مكانها شاشة الفواتير فقط).

    // ============================================================
    //  ⭐ قائمة الدخل الكاملة (الـ Method الرئيسية)
    // ============================================================
    public async Task<IncomeStatementDto> GetIncomeStatementAsync(IncomeStatementFilterDto filter)
    {
        var dto = new IncomeStatementDto
        {
            FromDate = filter.FromDate.Date,
            ToDate = filter.ToDate.Date.AddDays(1).AddTicks(-1),
            PeriodLabel = BuildPeriodLabel(filter)
        };

        // ⭐ نطاق الفرع (يُحسب مرة واحدة ويُمرَّر لكل الحسابات)
        var scope = await GetBranchScopeAsync(filter.BranchId);

        // ────────────── 1. حساب الإيرادات ──────────────
        await CalculateRevenueAsync(dto, scope);

        // ────────────── 2. الإيرادات الأخرى المكتسبة من الرسوم المستقلة ──────────────
        await CalculateOtherRevenueAsync(dto, scope);

        // ────────────── 3. حساب تكلفة المبيعات (COGS) ──────────────
        await CalculateCogsAsync(dto, scope);

        // ────────────── 3. الربح الإجمالي ──────────────
        dto.GrossProfit = dto.NetRevenue - dto.CostOfGoodsSold;
        dto.GrossProfitMargin = dto.NetRevenue == 0 ? 0
            : Math.Round((dto.GrossProfit / dto.NetRevenue) * 100, 2);

        // ────────────── 4. الأجور والرواتب وسلف الموظفين ──────────────
        await CalculatePayrollAndLoansAsync(dto, scope);

        // ────────────── 5. المصروفات التشغيلية ──────────────
        await CalculateExpensesAsync(dto, scope);

        // ────────────── 6. صافي الربح ──────────────
        dto.NetProfit = dto.GrossProfit - dto.TotalOperatingExpenses;
        dto.NetProfitMargin = dto.NetRevenue == 0 ? 0
            : Math.Round((dto.NetProfit / dto.NetRevenue) * 100, 2);

        dto.ProfitStatus = dto.NetProfit > 0 ? "ربح"
                         : dto.NetProfit < 0 ? "خسارة" : "تعادل";

        // ────────────── 6. نقطة التعادل ──────────────
        CalculateBreakEven(dto);

        // ────────────── 7. الهدف الديناميكي ──────────────
        CalculateProfitTarget(dto);

        // ────────────── 8. أعلى المصروفات ──────────────
        await GetTopExpensesAsync(dto, scope);

        // ────────────── 9. المقارنات (اختياري) ──────────────
        if (filter.IncludeComparison)
        {
            await GetPreviousPeriodComparisonAsync(dto, filter, scope);
        }

        // ────────────── 10. الترند الشهري (اختياري) ──────────────
        if (filter.IncludeMonthlyTrend)
        {
            await GetMonthlyTrendAsync(dto, scope);
        }

        // ────────────── 11. التوصيات الذكية ──────────────
        GenerateSmartRecommendations(dto);

        return dto;
    }

    public async Task<decimal> GetNetProfitMarginAsync(DateTime from, DateTime to)
    {
        var filter = new IncomeStatementFilterDto
        {
            FromDate = from, ToDate = to,
            IncludeComparison = false,
            IncludeMonthlyTrend = false
        };
        var stmt = await GetIncomeStatementAsync(filter);
        return stmt.NetProfitMargin;
    }

    // ============================================================
    //  ⭐ نطاق الفرع (Branch Scope)
    //  إسناد الفاتورة للفرع: فرع الموظف المنشئ (EmpId) أولاً،
    //  ثم فرع المخزن (WarehouseId) احتياطياً؛ غير متاح = "غير محدد".
    //  Null = كل الفروع (نفس السلوك القديم تماماً).
    // ============================================================
    private sealed record BranchScope(int? BranchId, List<int> EmployeeIds, List<int> WarehouseIds)
    {
        public static readonly BranchScope All = new(null, new List<int>(), new List<int>());
        public bool IsScoped => BranchId.HasValue;
    }

    private async Task<BranchScope> GetBranchScopeAsync(int? branchId)
    {
        if (!branchId.HasValue) return BranchScope.All;

        var employeeIds = await _db.Employees.AsNoTracking()
            .Where(e => e.BranchId == branchId.Value)
            .Select(e => e.EmployeeId)
            .ToListAsync();

        var warehouseIds = await _db.Warehouses.AsNoTracking()
            .Where(w => w.BranchId == branchId.Value)
            .Select(w => w.WarehouseId)
            .ToListAsync();

        return new BranchScope(branchId, employeeIds, warehouseIds);
    }

    // فلترة فواتير البيع بنطاق الفرع (تُطبَّق بعد فلاتر النوع/التاريخ/الحماية)
    private IQueryable<Transaction> ApplySaleBranchScope(IQueryable<Transaction> query, BranchScope scope)
    {
        if (!scope.IsScoped) return query;

        // ⭐ (12-ح7) موظف الفرع أولًا · احتياطي: الموظف بلا فرع (أو بلا سجل) ← فرع المخزن
        //    (قبل كده: فاتورة موظف BranchId=NULL كانت بتختفي من كل الفروع)
        return query.Where(t =>
            (t.EmpId != null && scope.EmployeeIds.Contains(t.EmpId.Value))
            || ((t.EmpId == null || !_db.Employees.Any(e => e.EmployeeId == t.EmpId.Value && e.BranchId != null))
                && scope.WarehouseIds.Contains(t.WarehouseId)));
    }

    // ============================================================
    //  حسابات الإيرادات
    // ============================================================
    private async Task CalculateRevenueAsync(IncomeStatementDto dto, BranchScope scope)
    {
        var query = _db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Sale
                && t.InvoiceStatus != "Cancelled"
                && t.TransactionDate >= dto.FromDate
                && t.TransactionDate < dto.ToDate.Date.AddDays(1));

        // ⭐ فلترة بنطاق الفرع (موظف المنشئ ← مخزن احتياطي)
        query = ApplySaleBranchScope(query, scope);

        var sales = await query
            .Select(t => new
            {
                t.GrandTotal,
                t.NetTotalAmount,
                t.DiscountAmount
            })
            .ToListAsync();

        dto.TotalRevenue = sales.Sum(s => s.GrandTotal);
        dto.NetRevenue = sales.Sum(s => s.NetTotalAmount ?? s.GrandTotal);
        dto.InvoicesCount = sales.Count;
        dto.AverageInvoiceValue = dto.InvoicesCount == 0 ? 0
            : Math.Round(dto.TotalRevenue / dto.InvoicesCount, 2);
    }

    // ============================================================
    //  الإيرادات الأخرى المكتسبة من الرسوم غير المرتبطة بفواتير
    // ============================================================
    private async Task CalculateOtherRevenueAsync(IncomeStatementDto dto, BranchScope scope)
    {
        // الرسوم المرتبطة بفواتير لا تدخل هنا حتى لا يتم احتسابها مرتين؛
        // فهي موجودة بالفعل داخل GrandTotal/TotalChargesAmount للفواتير.
        // الدفعات المقدمة غير المكتسبة لا تدخل قائمة الدخل.
        // ⭐ (12-ح7/ج) الرسوم المستقلة (بلا فاتورة) تُسند لفرع الموظف المنشئ —
        //    نفس منطق الفواتير: CreatedBy ← Users ← Employee ← Branch.
        //    رسوم منشئها غير مربوط بموظف/فرع تُحسب مع "كل الفروع" فقط (لا تضيع).
        var query = _db.AdditionalCharges.AsNoTracking()
            .Where(c => c.TransactionId == null
                     && c.AppliedToTransactionId == null
                     && (c.Status == ChargeStatuses.Paid
                         || c.Status == ChargeStatuses.NonRefundable)
                     // ⭐ دفعة مقدمة مدفوعة ولم تُطبق = التزام (إيراد غير مكتسب) — تُستبعد من الإيراد
                     && !(c.ChargeType == ChargeTypes.Advance && c.Status == ChargeStatuses.Paid)
                     && c.CreatedAt.HasValue
                     && c.CreatedAt.Value >= dto.FromDate
                     && c.CreatedAt.Value < dto.ToDate.Date.AddDays(1));

        if (scope.IsScoped)
            query = query.Where(c => c.CreatedBy != null
                && _db.Users.Any(u => u.Username == c.CreatedBy
                    && u.EmployeeId != null
                    && _db.Employees.Any(e => e.EmployeeId == u.EmployeeId.Value
                        && e.BranchId == scope.BranchId.Value)));

        var charges = await query
            .Select(c => new
            {
                c.ChargeType,
                Amount = c.ChargeAmount ?? 0m
            })
            .ToListAsync();

        dto.OtherRevenue = charges.Sum(c => c.Amount);
        dto.OtherRevenueCount = charges.Count;
        dto.OtherRevenueByType = charges
            .GroupBy(c => c.ChargeType ?? "Other")
            .Select(g => new OtherRevenueBreakdownDto
            {
                ChargeType = g.Key,
                ChargeTypeName = ChargeTypes.All.GetValueOrDefault(g.Key, "إيرادات رسوم أخرى"),
                Amount = g.Sum(x => x.Amount),
                Count = g.Count()
            })
            .OrderByDescending(x => x.Amount)
            .ToList();

        var total = dto.OtherRevenue == 0 ? 1m : dto.OtherRevenue;
        foreach (var item in dto.OtherRevenueByType)
            item.Percentage = Math.Round(item.Amount / total * 100m, 1);

        dto.InvoiceRevenue = dto.TotalRevenue;
        dto.TotalRevenue += dto.OtherRevenue;
        dto.NetRevenue += dto.OtherRevenue;
    }

    // ============================================================
    //  حساب تكلفة المبيعات (COGS)
    // ============================================================
    private async Task CalculateCogsAsync(IncomeStatementDto dto, BranchScope scope)
    {
        dto.CostOfGoodsSold = await GetCogsFromMirrorPurchasesAsync(
            dto.FromDate,
            dto.ToDate,
            scope);
    }

    /// <summary>
    /// تكلفة البضاعة المباعة من فاتورة الشراء المرآة المرتبطة بكل فاتورة بيع.
    /// لا نستخدم Product.PurchasePrice الحالي، لأن المنتج قد يكون له أكثر من
    /// باقة/تسعير، ولأن سعره قد يتغير بعد إنشاء الفاتورة.
    /// </summary>
    private async Task<decimal> GetCogsFromMirrorPurchasesAsync(
        DateTime fromDate,
        DateTime toDate,
        BranchScope scope)
    {
        var salesQuery = _db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Sale
                        && t.InvoiceStatus != InvoiceStatuses.Cancelled
                        && t.TransactionDate >= fromDate
                        && t.TransactionDate < toDate.Date.AddDays(1));

        // ⭐ فلترة بنطاق الفرع — COGS يتبع نفس إسناد الفاتورة (اتساق الهامش)
        salesQuery = ApplySaleBranchScope(salesQuery, scope);

        var saleIds = await salesQuery
            .Select(t => t.TransactionId)
            .ToListAsync();

        if (saleIds.Count == 0)
            return 0m;

        // InvoiceService ينشئ فاتورة شراء مرآة بالشكل:
        // ReferenceType = "MirrorOf:" + SaleTransactionId
        var mirrorReferences = saleIds
            .Select(id => "MirrorOf:" + id)
            .ToList();

        // نأخذ إجمالي فاتورة الشراء المرآة نفسها، مرة واحدة لكل فاتورة،
        // وليس سعر المنتج الحالي ولا مجموع كل فواتير المشتريات العامة.
        var mirrorCogs = await _db.Transactions.AsNoTracking()
            .Where(purchase => purchase.TransactionType == TransactionTypes.Purchase
                && purchase.InvoiceStatus != InvoiceStatuses.Cancelled
                && purchase.ReferenceType != null
                && mirrorReferences.Contains(purchase.ReferenceType))
            .SumAsync(purchase => (decimal?)purchase.GrandTotal) ?? 0m;

        // ⭐ (قرار المستخدم — الخيار أ) + تكلفة أصناف المعرض المباعة (بلا مرآة)
        //    = الكمية × سعر الشراء بالباقة/البديل — نفس منطق ComputeSalesCostByTransactionAsync حرفيًا
        var showroomCogs = await ComputeShowroomItemsCostAsync(saleIds);

        return mirrorCogs + showroomCogs;
    }

    // ⭐ (12-ح7) تكلفة أصناف المعرض — إصلاحان جوهريان:
    //    1) التصنيف لكل فاتورة: أي بند تغطيه مرآة الفاتورة (فاتورة شراء المصنع) يُستبعد،
    //       بصرف النظر عن حالة Customer الحالية للمنتج — يمنع الازدواج لو الارتباط اتشال
    //       بعد البيع، ويمنع اختفاء التكلفة لو الارتباط أُضيف بعد البيع.
    //    2) سعر الشراء وقت الفاتورة من PriceHistory (نوع {tier}_Cost) — fallback: السعر الحالي.
    //       الشهور التاريخية لم تعد تتغير مع كل تحديث سعر شراء.
    private async Task<decimal> ComputeShowroomItemsCostAsync(List<int> saleIds)
    {
        if (saleIds.Count == 0) return 0m;

        // 1) البنود التي غطّتها مرآة كل فاتورة بيع (فاتورة شراء المصنع المرتبطة بها)
        var mirrorReferences = saleIds.Select(id => "MirrorOf:" + id).ToList();
        var mirrorRows = await (from p in _db.Transactions.AsNoTracking()
                                join d in _db.TransactionDetails.AsNoTracking()
                                    on p.TransactionId equals d.TransactionId
                                where p.TransactionType == TransactionTypes.Purchase
                                   && p.InvoiceStatus != InvoiceStatuses.Cancelled
                                   && p.ReferenceType != null
                                   && mirrorReferences.Contains(p.ReferenceType)
                                select new { p.ReferenceType, d.ProductId }).ToListAsync();

        var coveredBySale = mirrorRows
            .GroupBy(x => x.ReferenceType!)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ProductId).ToHashSet());

        // 2) بنود البيع كلها — التصنيف بالمرآة مش بحالة Customer الحالية
        var rows = await (from d in _db.TransactionDetails.AsNoTracking()
                          join tx in _db.Transactions.AsNoTracking()
                              on d.TransactionId equals tx.TransactionId
                          join pr in _db.Products.AsNoTracking() on d.ProductId equals pr.ProductId
                          where saleIds.Contains(d.TransactionId)
                          select new
                          {
                              d.TransactionId,
                              d.ProductId,
                              d.Quantity,
                              d.PricingTier,
                              d.Notes,
                              d.SelectedAlternativeId,
                              tx.TransactionDate,
                              pr.PurchasePrice,
                              pr.PurchasePriceCClass,
                              pr.PurchasePriceElite,
                              AltPurchasePriceCClass = d.SelectedAlternativeId != null
                                  ? _db.ProductFactoryAlternatives
                                      .Where(a => a.AlternativeId == d.SelectedAlternativeId)
                                      .Select(a => a.PurchasePriceCClass).FirstOrDefault()
                                  : (decimal?)null,
                              AltPurchasePricePremium = d.SelectedAlternativeId != null
                                  ? _db.ProductFactoryAlternatives
                                      .Where(a => a.AlternativeId == d.SelectedAlternativeId)
                                      .Select(a => a.PurchasePricePremium).FirstOrDefault()
                                  : (decimal?)null,
                              AltPurchasePriceElite = d.SelectedAlternativeId != null
                                  ? _db.ProductFactoryAlternatives
                                      .Where(a => a.AlternativeId == d.SelectedAlternativeId)
                                      .Select(a => a.PurchasePriceElite).FirstOrDefault()
                                  : (decimal?)null
                          }).ToListAsync();

        // 3) تاريخ أسعار الشراء لهذه المنتجات (PriceType = {tier}_Cost)
        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var historyRaw = await _db.PriceHistories.AsNoTracking()
            .Where(h => productIds.Contains(h.ProductId) && h.PriceType.EndsWith("_Cost"))
            .OrderBy(h => h.ChangedAt)
            .Select(h => new { h.ProductId, h.PriceType, h.NewPrice, h.ChangedAt })
            .ToListAsync();
        var historyByProduct = historyRaw
            .GroupBy(h => h.ProductId)
            .ToDictionary(g => g.Key,
                g => g.Select(x => (x.ProductId, x.PriceType, x.NewPrice, x.ChangedAt)).ToList());

        decimal total = 0m;
        foreach (var r in rows)
        {
            // بند مغطى بمرآة هذه الفاتورة → تكلفته محسوبة في إجمالي المرآة
            if (coveredBySale.TryGetValue("MirrorOf:" + r.TransactionId, out var covered)
                && covered.Contains(r.ProductId))
                continue;

            var tier = string.IsNullOrWhiteSpace(r.PricingTier)
                ? ExtractTierFromNotes(r.Notes)
                : r.PricingTier;

            decimal unitCost;
            if (r.SelectedAlternativeId.HasValue)
            {
                // أسعار البدائل غير مسجلة في PriceHistory — السعر الحالي (أفضل المتاح)
                unitCost = tier switch
                {
                    var t when t == PricingTiers.CClass => r.AltPurchasePriceCClass ?? 0m,
                    var t when t == PricingTiers.Elite => r.AltPurchasePriceElite ?? r.AltPurchasePricePremium ?? 0m,
                    _ => r.AltPurchasePricePremium ?? 0m
                };
            }
            else
            {
                var currentCost = tier switch
                {
                    var t when t == PricingTiers.CClass => r.PurchasePriceCClass ?? 0m,
                    var t when t == PricingTiers.Elite => r.PurchasePriceElite ?? r.PurchasePrice ?? 0m,
                    _ => r.PurchasePrice ?? 0m
                };

                // التاريخي: آخر تغيير {tier}_Cost في أو قبل يوم الفاتورة — وإلا الحالي
                var tierKey = tier switch
                {
                    var t when t == PricingTiers.CClass => PricingTiers.CClass,
                    var t when t == PricingTiers.Elite => PricingTiers.Elite,
                    _ => PricingTiers.Premium
                };
                unitCost = HistoricalPurchaseCost(
                    historyByProduct, r.ProductId, tierKey + "_Cost", r.TransactionDate)
                    ?? currentCost;
            }
            total += Math.Round(r.Quantity * unitCost, 2);
        }
        return total;
    }

    // سعر الشراء الساري في تاريخ محدد: آخر تغيير مسجل قبله — null لو مفيش تاريخ
    private static decimal? HistoricalPurchaseCost(
        Dictionary<int, List<(int ProductId, string PriceType, decimal NewPrice, DateTime ChangedAt)>> historyByProduct,
        int productId, string costType, DateTime at)
    {
        if (!historyByProduct.TryGetValue(productId, out var points)) return null;
        decimal? price = null;
        foreach (var pt in points)   // مرتبة تصاعديًا حسب ChangedAt
        {
            if (pt.PriceType != costType) continue;
            if (pt.ChangedAt > at) break;
            price = pt.NewPrice;
        }
        return price;
    }

    // استخراج الباقة من الملاحظات — نفس دالة InvoiceService
    private static string ExtractTierFromNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return PricingTiers.Premium;
        if (notes.StartsWith($"[{PricingTiers.CClass}]", StringComparison.OrdinalIgnoreCase))
            return PricingTiers.CClass;
        if (notes.StartsWith($"[{PricingTiers.Elite}]", StringComparison.OrdinalIgnoreCase))
            return PricingTiers.Elite;
        return PricingTiers.Premium;
    }

    // ============================================================
    //  الأجور والرواتب + ملخص سلف الموظفين
    // ============================================================
    private async Task CalculatePayrollAndLoansAsync(IncomeStatementDto dto, BranchScope scope)
    {
        var monthFrom = new DateTime(dto.FromDate.Year, dto.FromDate.Month, 1)
            .ToString("yyyy-MM");
        var monthTo = new DateTime(dto.ToDate.Year, dto.ToDate.Month, 1)
            .ToString("yyyy-MM");

        var recognizedStatuses = new[]
        {
            PayrollPaymentStatuses.PendingReview,
            PayrollPaymentStatuses.Approved,
            PayrollPaymentStatuses.Unpaid,
            PayrollPaymentStatuses.Paid
        };

        var payrollsQuery = _db.Payrolls.AsNoTracking()
            .Where(p => string.Compare(p.PayrollMonth, monthFrom) >= 0
                     && string.Compare(p.PayrollMonth, monthTo) <= 0
                     && recognizedStatuses.Contains(p.PaymentStatus));

        // ⭐ فلترة بالفرع عبر فرع الموظف
        if (scope.IsScoped)
            payrollsQuery = payrollsQuery.Where(p => p.Employee.BranchId == scope.BranchId.Value);

        var payrolls = await payrollsQuery
            .Select(p => new
            {
                p.PayrollMonth,
                p.BasicSalary,
                p.Allowances,
                p.BonusInPayroll,
                p.NetSalary,
                p.PaymentStatus
            })
            .ToListAsync();

        // تكلفة الأجور = الإجمالي المستحق، وليس صافي ما تم تحويله للموظف.
        // Allowances و BonusInPayroll متداخلان في بعض السجلات القديمة، لذلك نستخدم
        // البدلات أولاً ثم المكافأة كبديل حتى لا يتم احتسابها مرتين.
        // ⭐ استحقاق بالتناسب: الفترات الجزئية (اليوم/أسبوع/مخصصة) تحسب نسبة أيام الفترة من كل شهر
        //    (الشهر الكامل = 100% — نفس الأرقام القديمة تمامًا للفترات الكاملة)
        dto.PayrollExpense = Math.Round(SumPayrollProRated(
            payrolls.Select(p => (p.PayrollMonth, p.BasicSalary + (p.Allowances ?? p.BonusInPayroll ?? 0m))),
            dto.FromDate, dto.ToDate), 2);
        dto.PayrollPaidAmount = payrolls
            .Where(p => p.PaymentStatus == PayrollPaymentStatuses.Paid)
            .Sum(p => p.NetSalary ?? p.BasicSalary);
        dto.PayrollOutstandingAmount = payrolls
            .Where(p => p.PaymentStatus != PayrollPaymentStatuses.Paid)
            .Sum(p => p.NetSalary ?? p.BasicSalary);

        var loansQuery = _db.EmployeeLoans.AsNoTracking()
            .Where(l => l.LoanDate >= dto.FromDate && l.LoanDate < dto.ToDate.Date.AddDays(1));

        // ⭐ فلترة بالفرع عبر فرع الموظف
        if (scope.IsScoped)
            loansQuery = loansQuery.Where(l => l.Employee.BranchId == scope.BranchId.Value);

        var loans = await loansQuery
            .Select(l => new { l.LoanAmount })
            .ToListAsync();

        var installmentsQuery = _db.LoanInstallments.AsNoTracking()
            .Where(i => string.Compare(i.DeductionMonth, monthFrom) >= 0
                     && string.Compare(i.DeductionMonth, monthTo) <= 0
                     && i.Status != "Skipped");

        // ⭐ فلترة بالفرع عبر فرع الموظف
        if (scope.IsScoped)
            installmentsQuery = installmentsQuery.Where(i => i.Employee.BranchId == scope.BranchId.Value);

        var installmentRows = await installmentsQuery
            .Select(i => new { i.Amount, i.Status })
            .ToListAsync();

        // ⭐ القروض النشطة والرصيد القائم — فلترة بالفرع
        var activeLoansQuery = _db.EmployeeLoans.AsNoTracking().Where(l => l.Status == "Active");
        if (scope.IsScoped)
            activeLoansQuery = activeLoansQuery.Where(l => l.Employee.BranchId == scope.BranchId.Value);

        dto.EmployeeLoans = new EmployeeLoansSummaryDto
        {
            LoansDisbursed = loans.Sum(x => x.LoanAmount),
            InstallmentsDue = installmentRows.Sum(x => x.Amount),
            InstallmentsDeducted = installmentRows
                .Where(x => x.Status == "Deducted")
                .Sum(x => x.Amount),
            OutstandingBalance = await activeLoansQuery
                .SumAsync(l => (decimal?)l.RemainingAmount) ?? 0m,
            ActiveLoansCount = await activeLoansQuery.CountAsync(),
            EmployeesWithLoansCount = await activeLoansQuery
                .Select(l => l.EmployeeId)
                .Distinct()
                .CountAsync()
        };
    }

    // ============================================================
    //  حساب المصروفات التشغيلية
    // ============================================================
    private async Task CalculateExpensesAsync(IncomeStatementDto dto, BranchScope scope)
    {
        // ⭐ ملاحظة مهمة: للمصروفات المقدمة، نحسب كل شهر فرعي على حدة
        // (الأشهر الفرعية تمثل المصروف الشهري الفعلي)

        var expensesQuery = _db.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDate >= dto.FromDate
                && e.ExpenseDate < dto.ToDate.Date.AddDays(1)
                // نأخذ:
                // - المصروفات العادية (مش مقدمة)
                // - الأشهر الفرعية للمصروف المقدم (هي اللي بتمثل المصروف الفعلي)
                && (
                    (e.IsAdvance != true)
                    || (e.AdvanceParentExpenseId.HasValue) // الأشهر الفرعية
                ));

        // ⭐ فلترة بالفرع (العمود موجود مباشرة على المصروف)
        if (scope.IsScoped)
            expensesQuery = expensesQuery.Where(e => e.BranchId == scope.BranchId.Value);

        var expenses = await expensesQuery
            .Select(e => new
            {
                e.ExpenseId,
                e.Amount,
                e.ExpenseGroupId
            })
            .ToListAsync();

        dto.TotalOperatingExpenses = expenses.Sum(e => e.Amount);

        // تجميع بالـ ExpenseGroup
        var groupTotals = expenses
            .GroupBy(e => e.ExpenseGroupId)
            .Select(g => new { GroupId = g.Key, Total = g.Sum(x => x.Amount), Count = g.Count() })
            .ToList();

        if (groupTotals.Any())
        {
            var groupIds = groupTotals.Select(g => g.GroupId).ToList();
            var groups = await _db.ExpenseGroups.AsNoTracking()
                .Where(g => groupIds.Contains(g.ExpenseGroupId))
                .ToDictionaryAsync(g => g.ExpenseGroupId, g => g.ExpenseGroupName);

            var totalForPct = dto.TotalOperatingExpenses == 0 ? 1 : dto.TotalOperatingExpenses;

            dto.ExpensesByGroup = groupTotals
                .Select((g, idx) => new ExpenseGroupBreakdownDto
                {
                    GroupId = g.GroupId,
                    GroupName = groups.GetValueOrDefault(g.GroupId, "غير محدد"),
                    Amount = g.Total,
                    Percentage = Math.Round((g.Total / totalForPct) * 100, 1),
                    Count = g.Count,
                    Color = GetColorForIndex(idx)
                })
                .OrderByDescending(g => g.Amount)
                .ToList();
        }

        if (dto.PayrollExpense > 0)
        {
            dto.ExpensesByGroup.Add(new ExpenseGroupBreakdownDto
            {
                GroupId = -1,
                GroupName = "الأجور والرواتب",
                Amount = dto.PayrollExpense,
                Count = 0,
                Color = "#7c3aed"
            });
        }

        dto.TotalOperatingExpenses = dto.ExpensesByGroup.Sum(g => g.Amount);
        var operatingTotal = dto.TotalOperatingExpenses == 0 ? 1m : dto.TotalOperatingExpenses;
        foreach (var group in dto.ExpensesByGroup)
            group.Percentage = Math.Round(group.Amount / operatingTotal * 100m, 1);
    }

    // ============================================================
    //  أعلى المصروفات
    // ============================================================
    private async Task GetTopExpensesAsync(IncomeStatementDto dto, BranchScope scope)
    {
        var topQuery = (
            from e in _db.Expenses.AsNoTracking()
            where e.ExpenseDate >= dto.FromDate
                && e.ExpenseDate < dto.ToDate.Date.AddDays(1)
                && ((e.IsAdvance != true) || (e.AdvanceParentExpenseId.HasValue))
            orderby e.Amount descending
            select new { e.ExpenseId, e.ExpenseName, e.Amount, e.ExpenseDate, e.ExpenseGroupId, e.BranchId });

        // ⭐ فلترة بالفرع
        if (scope.IsScoped)
            topQuery = topQuery.Where(x => x.BranchId == scope.BranchId.Value);

        var topExpenses = await topQuery.Take(10).ToListAsync();

        var groupIds = topExpenses.Select(e => e.ExpenseGroupId).Distinct().ToList();
        var groups = await _db.ExpenseGroups.AsNoTracking()
            .Where(g => groupIds.Contains(g.ExpenseGroupId))
            .ToDictionaryAsync(g => g.ExpenseGroupId, g => g.ExpenseGroupName);

        var totalForPct = dto.TotalOperatingExpenses == 0 ? 1 : dto.TotalOperatingExpenses;

        dto.TopExpenses = topExpenses.Select(e => new TopExpenseDto
        {
            ExpenseId = e.ExpenseId,
            ExpenseName = e.ExpenseName,
            GroupName = groups.GetValueOrDefault(e.ExpenseGroupId),
            Amount = e.Amount,
            PercentageOfTotal = Math.Round((e.Amount / totalForPct) * 100, 1),
            Date = e.ExpenseDate
        }).ToList();
    }

    // ============================================================
    //  ⭐ نقطة التعادل (Break-Even)
    // ============================================================
    private void CalculateBreakEven(IncomeStatementDto dto)
    {
        var be = dto.BreakEvenAnalysis;
        be.FixedExpenses = dto.TotalOperatingExpenses;
        be.GrossProfitMargin = dto.GrossProfitMargin;
        be.CurrentRevenue = dto.NetRevenue;

        if (dto.GrossProfitMargin > 0)
        {
            // نقطة التعادل = المصروفات الثابتة / نسبة الربح الإجمالي
            be.BreakEvenRevenue = Math.Round(be.FixedExpenses / (be.GrossProfitMargin / 100m), 2);
        }
        else
        {
            be.BreakEvenRevenue = be.FixedExpenses; // غير قابل للحساب الدقيق
        }

        be.RevenueGap = be.CurrentRevenue - be.BreakEvenRevenue;

        if (be.BreakEvenRevenue > 0)
        {
            be.SafetyMargin = Math.Round(((be.CurrentRevenue - be.BreakEvenRevenue) / be.BreakEvenRevenue) * 100, 1);
        }

        // تحديد الحالة
        if (be.CurrentRevenue >= be.BreakEvenRevenue * 1.5m)
        {
            be.Status = "آمن جداً";
            be.Description = $"إيراداتك أعلى من نقطة التعادل بمقدار {Math.Abs(be.RevenueGap):N2} ج (+{be.SafetyMargin}%) — وضع ممتاز!";
        }
        else if (be.CurrentRevenue >= be.BreakEvenRevenue)
        {
            be.Status = "آمن";
            be.Description = $"تجاوزت نقطة التعادل بـ {Math.Abs(be.RevenueGap):N2} ج. هامش الأمان: {be.SafetyMargin}%";
        }
        else if (be.CurrentRevenue >= be.BreakEvenRevenue * 0.8m)
        {
            be.Status = "حرج";
            be.Description = $"تحتاج {Math.Abs(be.RevenueGap):N2} ج إيراد إضافي للوصول لنقطة التعادل";
        }
        else
        {
            be.Status = "تحت التعادل";
            be.Description = $"خطر! إيراداتك أقل من نقطة التعادل بمقدار {Math.Abs(be.RevenueGap):N2} ج";
        }
    }

    // ============================================================
    //  ⭐ الهدف الديناميكي (10% → 20% → 30%)
    // ============================================================
    private void CalculateProfitTarget(IncomeStatementDto dto)
    {
        var t = dto.ProfitTarget;
        t.CurrentMargin = dto.NetProfitMargin;

        // تحديد الهدف الحالي والقادم
        // النظام: لو حقق 10% → الهدف الحالي 10، القادم 20
        // لو حقق 23% → الهدف الحالي 20، القادم 30
        // لو حقق 0% أو سالب → الهدف الحالي 10، القادم 20
        if (t.CurrentMargin <= 0)
        {
            t.CurrentTargetPercentage = 10;
            t.NextTargetPercentage = 20;
            t.Status = "تحت الهدف";
            t.Message = "العمل بخسارة - أولوية: الوصول لنقطة التعادل ثم تحقيق ربح 10%";
        }
        else
        {
            // اوجد أعلى هدف محقق
            var achievedTier = (int)Math.Floor(t.CurrentMargin / 10) * 10;
            if (achievedTier == 0)
            {
                t.CurrentTargetPercentage = 10;
                t.NextTargetPercentage = 20;
                t.Status = "قريب من الهدف";
                t.Message = $"اقتربت من تحقيق ربح 10%. الفجوة: {(10 - t.CurrentMargin):N1}%";
            }
            else
            {
                t.CurrentTargetPercentage = achievedTier;
                t.NextTargetPercentage = achievedTier + 10;

                if (t.CurrentMargin >= achievedTier + 5)
                {
                    t.Status = "متجاوز الهدف";
                    t.Message = $"رائع! حققت {t.CurrentMargin:N1}% — أعلى من هدف {achievedTier}%. اطمح للوصول لـ {achievedTier + 10}%";
                }
                else
                {
                    t.Status = "محقق الهدف";
                    t.Message = $"ممتاز! حققت {t.CurrentMargin:N1}% (هدف {achievedTier}%). اطمح للوصول لـ {achievedTier + 10}%";
                }
            }
        }

        // حساب المبالغ المستهدفة
        t.TargetAmount = Math.Round(dto.NetRevenue * (t.CurrentTargetPercentage / 100m), 2);
        t.NextTargetAmount = Math.Round(dto.NetRevenue * (t.NextTargetPercentage / 100m), 2);

        // % التحقيق
        if (t.TargetAmount > 0)
        {
            t.AchievementPercentage = Math.Round((dto.NetProfit / t.TargetAmount) * 100, 1);
            if (t.AchievementPercentage < 0) t.AchievementPercentage = 0;
            if (t.AchievementPercentage > 100) t.AchievementPercentage = 100;
        }

        // الفجوات للأهداف
        t.GapToTarget = t.TargetAmount - dto.NetProfit;
        t.GapToNextTarget = t.NextTargetAmount - dto.NetProfit;

        // اقتراحات للوصول للهدف القادم
        if (t.GapToNextTarget > 0)
        {
            // كم إيراد إضافي مع نفس نسبة الربح الإجمالي
            if (dto.GrossProfitMargin > 0)
            {
                t.RevenueIncreaseNeeded = Math.Round(t.GapToNextTarget / (dto.GrossProfitMargin / 100m), 2);
            }
            t.ExpensesReductionNeeded = t.GapToNextTarget;
        }
    }

    // ============================================================
    //  المقارنات (الفترة السابقة + السنة السابقة)
    // ============================================================
    private async Task GetPreviousPeriodComparisonAsync(IncomeStatementDto dto, IncomeStatementFilterDto filter, BranchScope scope)
    {
        var periodLength = (dto.ToDate.Date - dto.FromDate.Date).Days + 1;

        // الفترة السابقة (نفس المدة قبل بداية الفترة الحالية)
        var prevFrom = dto.FromDate.AddDays(-periodLength);
        var prevTo = dto.FromDate.AddDays(-1).AddDays(1).AddTicks(-1);

        var prev = await GetSummaryAsync(prevFrom, prevTo, scope);
        if (prev != null)
        {
            dto.PreviousPeriod = BuildComparison("الفترة السابقة", dto, prev);
        }

        // نفس الفترة من السنة السابقة
        var prevYearFrom = dto.FromDate.AddYears(-1);
        var prevYearTo = dto.ToDate.AddYears(-1);
        var prevYear = await GetSummaryAsync(prevYearFrom, prevYearTo, scope);
    if (prevYear.HasValue && prevYear.Value.Revenue > 0)
    {
        dto.PreviousYear = BuildComparison("نفس الفترة العام السابق", dto, prevYear);
    }
    }

    private async Task<(decimal Revenue, decimal Cogs, decimal Expenses, decimal Payroll, decimal OtherRevenue)?> GetSummaryAsync(
    DateTime fromDate, DateTime toDate, BranchScope scope)
{
    // إيرادات
    var revQuery = _db.Transactions.AsNoTracking()
        .Where(t => t.TransactionType == TransactionTypes.Sale
            && t.InvoiceStatus != "Cancelled"
            && t.TransactionDate >= fromDate
            && t.TransactionDate < toDate.Date.AddDays(1));

    // ⭐ فلترة بنطاق الفرع
    revQuery = ApplySaleBranchScope(revQuery, scope);

    var rev = await revQuery
        .SumAsync(t => (decimal?)(t.NetTotalAmount ?? t.GrandTotal)) ?? 0;

    // COGS من إجمالي فواتير الشراء المرآة المرتبطة بفواتير البيع
    var cogs = await GetCogsFromMirrorPurchasesAsync(fromDate, toDate, scope);

    // المصروفات
    var expQuery = _db.Expenses.AsNoTracking()
        .Where(e => e.ExpenseDate >= fromDate
            && e.ExpenseDate < toDate.Date.AddDays(1)
            && ((e.IsAdvance != true) || (e.AdvanceParentExpenseId.HasValue)));

    // ⭐ فلترة بالفرع
    if (scope.IsScoped)
        expQuery = expQuery.Where(e => e.BranchId == scope.BranchId.Value);

    var exp = await expQuery
        .SumAsync(e => (decimal?)e.Amount) ?? 0;

    var payroll = await GetPayrollExpenseForRangeAsync(fromDate, toDate, scope);
    var otherRevenue = await GetOtherRevenueForRangeAsync(fromDate, toDate, scope);
    return (rev + otherRevenue, cogs, exp, payroll, otherRevenue);
}

    private async Task<decimal> GetOtherRevenueForRangeAsync(DateTime fromDate, DateTime toDate, BranchScope scope)
    {
        // ⭐ (12-ح7/ج) الرسوم المستقلة تُسند لفرع الموظف المنشئ (اتساقًا مع القائمة)
        var query = _db.AdditionalCharges.AsNoTracking()
            .Where(c => c.TransactionId == null
                     && c.AppliedToTransactionId == null
                     && (c.Status == ChargeStatuses.Paid
                         || c.Status == ChargeStatuses.NonRefundable)
                     && !(c.ChargeType == ChargeTypes.Advance && c.Status == ChargeStatuses.Paid)
                     && c.CreatedAt.HasValue
                     && c.CreatedAt.Value >= fromDate
                     && c.CreatedAt.Value < toDate.Date.AddDays(1));

        if (scope.IsScoped)
            query = query.Where(c => c.CreatedBy != null
                && _db.Users.Any(u => u.Username == c.CreatedBy
                    && u.EmployeeId != null
                    && _db.Employees.Any(e => e.EmployeeId == u.EmployeeId.Value
                        && e.BranchId == scope.BranchId.Value)));

        return await query.SumAsync(c => (decimal?)(c.ChargeAmount ?? 0m)) ?? 0m;
    }

    private async Task<decimal> GetPayrollExpenseForRangeAsync(DateTime fromDate, DateTime toDate, BranchScope scope)
    {
        var monthFrom = new DateTime(fromDate.Year, fromDate.Month, 1).ToString("yyyy-MM");
        var monthTo = new DateTime(toDate.Year, toDate.Month, 1).ToString("yyyy-MM");
        var statuses = new[]
        {
            PayrollPaymentStatuses.PendingReview,
            PayrollPaymentStatuses.Approved,
            PayrollPaymentStatuses.Unpaid,
            PayrollPaymentStatuses.Paid
        };

        var payrollQuery = _db.Payrolls.AsNoTracking()
            .Where(p => string.Compare(p.PayrollMonth, monthFrom) >= 0
                     && string.Compare(p.PayrollMonth, monthTo) <= 0
                     && statuses.Contains(p.PaymentStatus));

        // ⭐ فلترة بالفرع عبر فرع الموظف
        if (scope.IsScoped)
            payrollQuery = payrollQuery.Where(p => p.Employee.BranchId == scope.BranchId.Value);

        var rows = await payrollQuery
            .Select(p => new { p.PayrollMonth, p.BasicSalary, p.Allowances, p.BonusInPayroll })
            .ToListAsync();

        // ⭐ نفس التناسب — اتساق المقارنات والترند مع القائمة الرئيسية
        return Math.Round(SumPayrollProRated(
            rows.Select(p => (p.PayrollMonth, p.BasicSalary + (p.Allowances ?? p.BonusInPayroll ?? 0m))),
            fromDate, toDate), 2);
    }

    // ⭐ توزيع رواتب الشهور على الفترة بالأيام: كل شهر يُحسب بنسبة أيام الفترة المتداخلة معه
    private static decimal SumPayrollProRated(
        IEnumerable<(string Month, decimal Amount)> rows, DateTime from, DateTime to)
    {
        var fromD = from.Date;
        var toD = to.Date;
        decimal total = 0m;
        foreach (var g in rows.GroupBy(r => r.Month))
        {
            if (!DateTime.TryParseExact(g.Key, "yyyy-MM",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var monthStart))
                continue;
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var overlapFrom = monthStart > fromD ? monthStart : fromD;
            var overlapTo = monthEnd < toD ? monthEnd : toD;
            var days = (overlapTo - overlapFrom).Days + 1;
            if (days <= 0) continue;
            total += g.Sum(r => r.Amount) * days
                     / DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
        }
        return total;
    }

    private IncomeComparisonDto BuildComparison(string label, IncomeStatementDto current,
        (decimal Revenue, decimal Cogs, decimal Expenses, decimal Payroll, decimal OtherRevenue)? prev)
    {
        if (prev == null) return new() { Label = label };

        var previousTotalExpenses = prev.Value.Expenses + prev.Value.Payroll;
        var prevNetProfit = prev.Value.Revenue - prev.Value.Cogs - previousTotalExpenses;
        var prevMargin = prev.Value.Revenue == 0 ? 0
            : Math.Round((prevNetProfit / prev.Value.Revenue) * 100, 2);

        var revChange = prev.Value.Revenue == 0 ? 0
            : Math.Round(((current.NetRevenue - prev.Value.Revenue) / prev.Value.Revenue) * 100, 1);
        var expChange = previousTotalExpenses == 0 ? 0
            : Math.Round(((current.TotalOperatingExpenses - previousTotalExpenses) / previousTotalExpenses) * 100, 1);
        var profitChange = prevNetProfit == 0 ? 0
            : Math.Round(((current.NetProfit - prevNetProfit) / Math.Abs(prevNetProfit)) * 100, 1);

        var trend = profitChange > 5 ? "تحسن"
                  : profitChange < -5 ? "تراجع" : "ثابت";

        return new IncomeComparisonDto
        {
            Label = label,
            Revenue = prev.Value.Revenue,
            Expenses = prev.Value.Expenses,
            NetProfit = prevNetProfit,
            NetProfitMargin = prevMargin,
            RevenueChange = revChange,
            ExpensesChange = expChange,
            NetProfitChange = profitChange,
            Trend = trend
        };
    }

    // ============================================================
    //  الترند الشهري (آخر 12 شهر)
    // ============================================================
    private async Task GetMonthlyTrendAsync(IncomeStatementDto dto, BranchScope scope)
    {
        var endDate = DateTime.Today;
        var startDate = endDate.AddMonths(-11);
        startDate = new DateTime(startDate.Year, startDate.Month, 1);

        var months = new List<MonthlyTrendDto>();
        var current = startDate;

        while (current <= endDate)
        {
            var monthEnd = current.AddMonths(1).AddDays(-1);

            var summary = await GetSummaryAsync(current, monthEnd.Date.AddDays(1).AddTicks(-1), scope);
            if (summary != null)
            {
                var totalExpenses = summary.Value.Expenses + summary.Value.Payroll;
                var profit = summary.Value.Revenue - summary.Value.Cogs - totalExpenses;
                var margin = summary.Value.Revenue == 0 ? 0
                    : Math.Round((profit / summary.Value.Revenue) * 100, 1);

                months.Add(new MonthlyTrendDto
                {
                    Month = current,
                    MonthLabel = current.ToString("yyyy/MM"),
                    Revenue = summary.Value.Revenue,
                    Cogs = summary.Value.Cogs,
                    Expenses = totalExpenses,
                    NetProfit = profit,
                    NetProfitMargin = margin
                });
            }

            current = current.AddMonths(1);
        }

        dto.MonthlyTrend = months;
    }

    // ============================================================
    //  ⭐ التوصيات الذكية
    // ============================================================
    private void GenerateSmartRecommendations(IncomeStatementDto dto)
    {
        var recs = new List<SmartRecommendationDto>();

        // 1. تحليل الخسارة
        if (dto.NetProfit < 0)
        {
            recs.Add(new SmartRecommendationDto
            {
                Type = RecommendationTypes.Critical,
                Title = "العمل بخسارة!",
                Description = $"خسارة {Math.Abs(dto.NetProfit):N2} ج هذه الفترة. " +
                              $"يجب اتخاذ إجراءات فورية لتقليل المصروفات أو زيادة المبيعات.",
                Priority = 1,
                Color = "#dc2626"
            });
        }

        // 2. تحليل نقطة التعادل
        if (!dto.BreakEvenAnalysis.AboveBreakEven)
        {
            recs.Add(new SmartRecommendationDto
            {
                Type = RecommendationTypes.Critical,
                Title = "تحت نقطة التعادل",
                Description = $"تحتاج {Math.Abs(dto.BreakEvenAnalysis.RevenueGap):N2} ج إيراد إضافي للوصول لنقطة التعادل.",
                Priority = 2,
                Color = "#dc2626"
            });
        }
        else if (dto.BreakEvenAnalysis.SafetyMargin < 20)
        {
            recs.Add(new SmartRecommendationDto
            {
                Type = RecommendationTypes.Warning,
                Title = "هامش الأمان منخفض",
                Description = $"هامش الأمان فقط {dto.BreakEvenAnalysis.SafetyMargin}%. حاول زيادته لـ 30% على الأقل.",
                Priority = 3,
                Color = "#f59e0b"
            });
        }

        // 3. توصيات الهدف
        if (dto.NetProfitMargin < 10 && dto.NetProfit > 0)
        {
            recs.Add(new SmartRecommendationDto
            {
                Type = RecommendationTypes.Warning,
                Title = "نسبة الربح أقل من 10%",
                Description = $"حالياً: {dto.NetProfitMargin:N1}%. " +
                              $"للوصول لـ 10% تحتاج إما زيادة الإيراد بـ {dto.ProfitTarget.RevenueIncreaseNeeded:N2} ج " +
                              $"أو تقليل المصروفات بـ {dto.ProfitTarget.GapToTarget:N2} ج",
                Priority = 4,
                Color = "#f59e0b"
            });
        }
        else if (dto.NetProfitMargin >= 10 && dto.NetProfitMargin < 20)
        {
            recs.Add(new SmartRecommendationDto
            {
                Type = RecommendationTypes.Success,
                Title = "نسبة ربح ممتازة - اطمح للأعلى",
                Description = $"حققت {dto.NetProfitMargin:N1}%. " +
                              $"للوصول لـ 20% تحتاج زيادة إيراد بـ {dto.ProfitTarget.RevenueIncreaseNeeded:N2} ج",
                Priority = 5,
                Color = "#10b981"
            });
        }
        else if (dto.NetProfitMargin >= 20)
        {
            recs.Add(new SmartRecommendationDto
            {
                Type = RecommendationTypes.Success,
                Title = "أداء استثنائي!",
                Description = $"نسبة ربح {dto.NetProfitMargin:N1}% — وضع ممتاز! " +
                              $"الهدف القادم: {dto.ProfitTarget.NextTargetPercentage}%. " +
                              $"فكر في توسيع النشاط أو الاستثمار في النمو.",
                Priority = 6,
                Color = "#10b981"
            });
        }

        // 4. تحليل المصروفات الكبيرة
        if (dto.ExpensesByGroup.Any())
        {
            var biggestGroup = dto.ExpensesByGroup.First();
            if (biggestGroup.Percentage > 40)
            {
                recs.Add(new SmartRecommendationDto
                {
                    Type = RecommendationTypes.Warning,
                    Title = $"مصروفات {biggestGroup.GroupName} مرتفعة",
                    Description = $"تمثل {biggestGroup.Percentage}% من إجمالي المصروفات " +
                                  $"({biggestGroup.Amount:N2} ج). راجع إمكانية التخفيض.",
                    Priority = 7,
                    Color = "#f59e0b"
                });
            }
        }

        // 5. تحليل المقارنة
        if (dto.PreviousPeriod != null)
        {
            if (dto.PreviousPeriod.RevenueChange < -10)
            {
                recs.Add(new SmartRecommendationDto
                {
                    Type = RecommendationTypes.Critical,
                    Title = "تراجع في الإيرادات",
                    Description = $"الإيرادات تراجعت بـ {Math.Abs(dto.PreviousPeriod.RevenueChange):N1}% " +
                                  $"مقارنة بالفترة السابقة. ابحث عن الأسباب.",
                    Priority = 8,
                    Color = "#dc2626"
                });
            }
            else if (dto.PreviousPeriod.RevenueChange > 20)
            {
                recs.Add(new SmartRecommendationDto
                {
                    Type = RecommendationTypes.Success,
                    Title = "نمو رائع في الإيرادات",
                    Description = $"الإيرادات نمت بـ +{dto.PreviousPeriod.RevenueChange:N1}% " +
                                  $"مقارنة بالفترة السابقة. استمر!",
                    Priority = 9,
                    Color = "#10b981"
                });
            }

            if (dto.PreviousPeriod.ExpensesChange > 20)
            {
                recs.Add(new SmartRecommendationDto
                {
                    Type = RecommendationTypes.Warning,
                    Title = "زيادة في المصروفات",
                    Description = $"المصروفات زادت بـ +{dto.PreviousPeriod.ExpensesChange:N1}% " +
                                  $"عن الفترة السابقة. راجع المصروفات الجديدة.",
                    Priority = 10,
                    Color = "#f59e0b"
                });
            }
        }

        // 6. هامش الربح الإجمالي
        if (dto.GrossProfitMargin < 30 && dto.NetRevenue > 0)
        {
            recs.Add(new SmartRecommendationDto
            {
                Type = RecommendationTypes.Warning,
                Title = "هامش الربح الإجمالي منخفض",
                Description = $"الربح الإجمالي {dto.GrossProfitMargin:N1}% — تكلفة المبيعات مرتفعة. " +
                              $"فكر في رفع الأسعار أو تقليل تكلفة الشراء.",
                Priority = 11,
                Color = "#f59e0b"
            });
        }

        // 7. لو أداء ممتاز عام
        if (dto.NetProfit > 0 && dto.NetProfitMargin >= 15 && dto.BreakEvenAnalysis.SafetyMargin > 30)
        {
            recs.Add(new SmartRecommendationDto
            {
                Type = RecommendationTypes.Info,
                Title = "نصيحة: استثمر في النمو",
                Description = "أداؤك المالي قوي. فكر في الاستثمار في التسويق، التوسع، " +
                              "أو إضافة منتجات جديدة لزيادة الإيرادات.",
                Priority = 12,
                Color = "#3b82f6"
            });
        }

        dto.Recommendations = recs.OrderBy(r => r.Priority).ToList();
    }

    // ============================================================
    //  Helpers
    // ============================================================
    private string BuildPeriodLabel(IncomeStatementFilterDto filter)
    {
        return filter.PeriodType switch
        {
            "Today" => $"اليوم ({DateTime.Today:yyyy/MM/dd})",
            "Week" => $"هذا الأسبوع ({filter.FromDate:yyyy/MM/dd} - {filter.ToDate:yyyy/MM/dd})",
            "Month" => $"شهر {filter.FromDate:yyyy/MM}",
            "Quarter" => $"الربع ({filter.FromDate:yyyy/MM/dd} - {filter.ToDate:yyyy/MM/dd})",
            "Year" => $"سنة {filter.FromDate.Year}",
            _ => $"من {filter.FromDate:yyyy/MM/dd} إلى {filter.ToDate:yyyy/MM/dd}"
        };
    }

    private string GetColorForIndex(int index)
    {
        var colors = new[]
        {
            "#3b82f6", "#10b981", "#f59e0b", "#ef4444", "#8b5cf6",
            "#ec4899", "#06b6d4", "#84cc16", "#d4af37", "#6366f1"
        };
        return colors[index % colors.Length];
    }

    // ============================================================
    // 💼 قائمة المركز المالي (الميزانية)
    // ============================================================
    public async Task<BalanceSheetDto> GetBalanceSheetAsync(int? branchId = null)
    {
        var dto = new BalanceSheetDto
        {
            AsOfDate = DateTime.Today,
            AsOfLabel = DateTime.Today.ToString("dddd dd MMMM yyyy", new System.Globalization.CultureInfo("ar-EG")),
            BranchId = branchId
        };

        if (branchId.HasValue)
        {
            dto.BranchName = await _db.Branches.AsNoTracking()
                .Where(b => b.BranchId == branchId.Value)
                .Select(b => b.BranchNameAr)
                .FirstOrDefaultAsync();
        }

        // ⭐ نفس آلية إسناد الفواتير للفرع المستخدمة في قائمة الدخل بالحرف
        var scope = await GetBranchScopeAsync(branchId);

        // ───── 1) النقدية: رصيد الخزائن (افتتاحي + قبض - صرف) — بنطاق فرع الخزينة (نفس CashFlow)
        var boxesQuery = _db.CashBoxes.AsNoTracking();
        if (scope.IsScoped)
            boxesQuery = boxesQuery.Where(b => b.BranchId == scope.BranchId.Value);

        var boxes = await boxesQuery
            .Select(b => new
            {
                b.CashBoxId,
                b.OpeningBalance,
                TotalIn = _db.CashboxTransactions
                    .Where(t => t.CashBoxId == b.CashBoxId && t.TransactionType == "قبض")
                    .Sum(t => (decimal?)t.Amount) ?? 0m,
                TotalOut = _db.CashboxTransactions
                    .Where(t => t.CashBoxId == b.CashBoxId && t.TransactionType == "صرف")
                    .Sum(t => (decimal?)t.Amount) ?? 0m
            }).ToListAsync();

        dto.CashBoxesCount = boxes.Count;
        dto.CashBalance = boxes.Sum(b => b.OpeningBalance + b.TotalIn - b.TotalOut);

        // ───── 2) المدينون: متبقي فواتير البيع (GrandTotal - Paid) بإسناد الفرع (موظف ← مخزن)
        var receivableQuery = _db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Sale
                        && t.InvoiceStatus != InvoiceStatuses.Cancelled
                        && t.GrandTotal > t.PaidAmount);
        // ⭐ (12-ح7) اتساقًا مع قائمة الدخل: كل الفواتير تُحتسب — لا استبعاد لفواتير مدير الحسابات
        receivableQuery = ApplySaleBranchScope(receivableQuery, scope);

        var salesReceivable = await receivableQuery
            .GroupBy(_ => 1)
            .Select(g => new { Sum = g.Sum(t => (decimal?)(t.GrandTotal - t.PaidAmount)) ?? 0m, Count = g.Count() })
            .FirstOrDefaultAsync();
        dto.AccountsReceivable = salesReceivable?.Sum ?? 0m;
        dto.ReceivableInvoicesCount = salesReceivable?.Count ?? 0;

        // ───── 3) المخزون: الأرصدة × تكلفة باقة المنتج نفسه — قيمة واحدة (بدون باقات بديلة)
        var invWarehousesQuery = _db.Warehouses.AsNoTracking().Where(w => w.IsActive == true);
        if (scope.IsScoped)
            invWarehousesQuery = invWarehousesQuery.Where(w => w.BranchId == scope.BranchId.Value);

        var invRaw = await (from sl in _db.StockLevels.AsNoTracking()
                            join w in invWarehousesQuery on sl.WarehouseId equals w.WarehouseId
                            join p in _db.Products.AsNoTracking() on sl.ProductId equals p.ProductId
                            where sl.Quantity > 0
                            select new
                            {
                                sl.Quantity,
                                sl.ProductId,
                                p.PurchasePrice,
                                p.PurchasePriceCClass,
                                p.PurchasePriceElite,
                                p.PricingType
                            }).ToListAsync();

        decimal inv = 0m;
        foreach (var x in invRaw)
        {
            var ownTier = string.IsNullOrWhiteSpace(x.PricingType) ? "Premium" : x.PricingType!.Trim();
            decimal costOfOwn = ownTier switch
            {
                "CClass" => x.PurchasePriceCClass ?? x.PurchasePrice ?? 0m,
                "Elite"  => x.PurchasePriceElite ?? x.PurchasePrice ?? 0m,
                _        => x.PurchasePrice ?? x.PurchasePriceCClass ?? x.PurchasePriceElite ?? 0m
            };
            inv += costOfOwn * x.Quantity;
        }
        dto.InventoryValue = inv;
        dto.InventoryItemsCount = invRaw.Select(x => x.ProductId).Distinct().Count();

        // ───── 4) سلف الموظفين القائمة (أصل) — بنطاق فرع الموظف (نفس قائمة الدخل)
        var loansQ = _db.EmployeeLoans.AsNoTracking().Where(l => l.Status == "Active");
        if (scope.IsScoped)
            loansQ = loansQ.Where(l => l.Employee.BranchId == scope.BranchId.Value);
        dto.ActiveLoansCount = await loansQ.CountAsync();
        dto.EmployeeLoansOutstanding = await loansQ
            .SumAsync(l => (decimal?)l.RemainingAmount) ?? 0m;

        // ───── 4ب) مصروفات مدفوعة مقدمًا (أصل): الأشهر الفرعية المستقبلية للمصروف المقدم ─────
        //     (الأصل نفسه Amount=0 والأشهر الفرعية تحمل القيم — الشهر الحالي يُحسب مصروفًا في الدخل)
        var prepaidQ = _db.Expenses.AsNoTracking()
            .Where(e => e.AdvanceParentExpenseId != null && e.ExpenseDate > dto.AsOfDate);
        if (scope.IsScoped)
            prepaidQ = prepaidQ.Where(e => e.BranchId == scope.BranchId.Value);
        dto.PrepaidExpensesAsset = await prepaidQ.SumAsync(e => (decimal?)e.Amount) ?? 0m;

        dto.TotalAssets = dto.CashBalance + dto.AccountsReceivable + dto.InventoryValue
                        + dto.EmployeeLoansOutstanding + dto.PrepaidExpensesAsset;

        // ───── 5) الالتزامات: رواتب غير مدفوعة (عدا المرفوض) + متبقي مشتريات بنفس إسناد الفرع
        var payrollQ = _db.Payrolls.AsNoTracking()
            .Where(p => p.PaymentStatus != PayrollPaymentStatuses.Paid
                     && p.PaymentStatus != PayrollPaymentStatuses.Rejected
                     && p.PaymentStatus != PayrollPaymentStatuses.Cancelled);
        if (scope.IsScoped)
            payrollQ = payrollQ.Where(p => p.Employee.BranchId == scope.BranchId.Value);
        dto.PayrollPendingCount = await payrollQ.CountAsync();
        dto.PayrollOutstanding = await payrollQ
            .SumAsync(p => (decimal?)(p.NetSalary ?? p.BasicSalary)) ?? 0m;

        var purchaseQuery = _db.Transactions.AsNoTracking()
            .Where(t => t.TransactionType == TransactionTypes.Purchase
                        && (t.InvoiceStatus == null || t.InvoiceStatus != InvoiceStatuses.Cancelled)
                        // ⚠️ بلا استبعاد للمرآة — المرآة فاتورة شراء حقيقية من المورد الوحيد (المصنع) وتُدفع فعلًا
                        //    (قرار المستخدم 12-ح22: المتبقي على المرايا = ديون حقيقية للمصنع)
                        && t.GrandTotal > t.PaidAmount);
        purchaseQuery = ApplySaleBranchScope(purchaseQuery, scope);

        var purchasePayable = await purchaseQuery
            .GroupBy(_ => 1)
            .Select(g => new { Sum = g.Sum(t => (decimal?)(t.GrandTotal - t.PaidAmount)) ?? 0m, Count = g.Count() })
            .FirstOrDefaultAsync();
        dto.SupplierPayables = purchasePayable?.Sum ?? 0m;
        dto.PayablePurchasesCount = purchasePayable?.Count ?? 0;

        // ───── 5ب) دفعات عملاء مقدمة (التزام): رسوم «دفعة مقدمة» مقبوضة ولم تُطبق على فاتورة ─────
        //     (12-ح7/ج: إسناد بالفرع عبر الموظف المنشئ — نفس معاملة الرسوم المستقلة في قائمة الدخل)
        var advancesQ = _db.AdditionalCharges.AsNoTracking()
            .Where(c => c.ChargeType == ChargeTypes.Advance
                     && c.Status == ChargeStatuses.Paid
                     && c.TransactionId == null
                     && c.AppliedToTransactionId == null);
        if (scope.IsScoped)
            advancesQ = advancesQ.Where(c => c.CreatedBy != null
                && _db.Users.Any(u => u.Username == c.CreatedBy
                    && u.EmployeeId != null
                    && _db.Employees.Any(e => e.EmployeeId == u.EmployeeId.Value
                        && e.BranchId == scope.BranchId.Value)));
        dto.CustomerAdvances = await advancesQ.SumAsync(c => (decimal?)(c.ChargeAmount ?? 0m)) ?? 0m;

        dto.TotalLiabilities = dto.PayrollOutstanding + dto.SupplierPayables + dto.CustomerAdvances;

        // ───── 6) حقوق الملكية: رأس مال افتتاحي + أرباح محتجزة (صافي ربح قائمة الدخل التراكمي)
        var capital = await GetOpeningCapitalAsync(branchId);
        dto.OpeningCapital = capital.Value;
        dto.CapitalNotes = capital.Notes;
        dto.CapitalUpdatedAt = capital.UpdatedAt;
        dto.CapitalUpdatedBy = capital.UpdatedBy;

        var cumulative = await GetIncomeStatementAsync(new IncomeStatementFilterDto
        {
            FromDate = new DateTime(2000, 1, 1),
            ToDate = DateTime.Today,
            PeriodType = "Custom",
            IncludeComparison = false,
            IncludeMonthlyTrend = false,
            BranchId = branchId
        });
        dto.RetainedEarnings = cumulative.NetProfit;
        dto.CumulativeRevenue = cumulative.NetRevenue;
        dto.CumulativeCogs = cumulative.CostOfGoodsSold;
        dto.CumulativeOperatingExpenses = cumulative.TotalOperatingExpenses;
        dto.CumulativeFrom = cumulative.FromDate;

        dto.TotalEquity = dto.OpeningCapital + dto.RetainedEarnings;
        dto.LiabilitiesAndEquity = dto.TotalLiabilities + dto.TotalEquity;
        dto.BalanceDifference = dto.TotalAssets - dto.LiabilitiesAndEquity;

        return dto;
    }

    // ⭐ رأس المال لكل فرع (قرار المستخدم 12-خ23): مفتاح "OpeningCapital:{branchId}"
    //    العام (null) = مجموع رؤوس أموال الفروع + أي رقم قديم عام غير موزع
    private static string CapitalKey(int branchId) => $"OpeningCapital:{branchId}";

    public async Task<(decimal Value, string? Notes, DateTime? UpdatedAt, string? UpdatedBy)> GetOpeningCapitalAsync(int? branchId = null)
    {
        var rows = await _db.FinancialSettings.AsNoTracking()
            .Where(f => f.SettingKey == "OpeningCapital" || f.SettingKey.StartsWith("OpeningCapital:"))
            .ToListAsync();

        if (branchId.HasValue)
        {
            var key = CapitalKey(branchId.Value);
            var row = rows.FirstOrDefault(f => f.SettingKey == key);
            return (row?.SettingValue ?? 0m, row?.Notes, row?.UpdatedAt, row?.UpdatedBy);
        }

        var latest = rows.OrderByDescending(f => f.UpdatedAt).FirstOrDefault();
        return (rows.Sum(f => f.SettingValue ?? 0m), latest?.Notes, latest?.UpdatedAt, latest?.UpdatedBy);
    }

    // ⭐ اقتراح رأس المال من بضاعة المعرض الافتتاحية (حركات OpeningBalance × سعرها أو تكلفة الباقة الحالية)
    public async Task<decimal> GetOpeningStockValueAsync(int? branchId = null)
    {
        var rows = await (from st in _db.StockTransactions.AsNoTracking()
                          join pr in _db.Products.AsNoTracking() on st.ProductId equals pr.ProductId
                          where st.TransactionType == "In" && st.ReferenceType == "OpeningBalance"
                          select new
                          {
                              st.Quantity,
                              st.UnitPrice,
                              st.WarehouseId,
                              pr.PurchasePrice,
                              pr.PurchasePriceCClass,
                              pr.PurchasePriceElite,
                              pr.PricingType
                          }).ToListAsync();

        if (branchId.HasValue)
        {
            var whIds = await _db.Warehouses.AsNoTracking()
                .Where(w => w.BranchId == branchId.Value)
                .Select(w => w.WarehouseId)
                .ToListAsync();
            rows = rows.Where(r => whIds.Contains(r.WarehouseId)).ToList();
        }

        decimal total = 0m;
        foreach (var r in rows)
        {
            var unit = r.UnitPrice;
            if (unit == null || unit == 0m)
            {
                var ownTier = string.IsNullOrWhiteSpace(r.PricingType) ? "Premium" : r.PricingType!.Trim();
                unit = ownTier switch
                {
                    "CClass" => r.PurchasePriceCClass ?? r.PurchasePrice ?? 0m,
                    "Elite" => r.PurchasePriceElite ?? r.PurchasePrice ?? 0m,
                    _ => r.PurchasePrice ?? r.PurchasePriceCClass ?? r.PurchasePriceElite ?? 0m
                };
            }
            total += r.Quantity * unit.Value;
        }
        return Math.Round(total, 2);
    }

    public async Task SaveOpeningCapitalAsync(int? branchId, decimal value, string? notes, string userName)
    {
        var key = branchId.HasValue ? CapitalKey(branchId.Value) : "OpeningCapital";
        var row = await _db.FinancialSettings
            .FirstOrDefaultAsync(f => f.SettingKey == key);

        if (row == null)
        {
            row = new FinancialSetting
            {
                SettingKey = key,
                SettingValue = value,
                Notes = notes,
                UpdatedBy = userName,
                UpdatedAt = DateTime.Now
            };
            _db.FinancialSettings.Add(row);
        }
        else
        {
            row.SettingValue = value;
            row.Notes = notes;
            row.UpdatedBy = userName;
            row.UpdatedAt = DateTime.Now;
        }

        await _db.SaveChangesAsync();

        await _audit.LogAsync<object>(
            "FinancialSettings",
            "OpeningCapitalUpdate",
            row.FinancialSettingId.ToString(),
            null,
            new { row.SettingKey, row.SettingValue, row.Notes, row.UpdatedBy, row.UpdatedAt },
            userName);
    }
}