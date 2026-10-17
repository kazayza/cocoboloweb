using System.Security.Claims;
using System.Text.Json;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using COCOBOLOERPNEW.Services;
using Microsoft.EntityFrameworkCore;
using static COCOBOLOERPNEW.Services.Assistant.ZakiInvoiceQueries;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// أدوات فواتير المبيعات وعروض الأسعار في زكي.
/// الصلاحيات: نفس شروط الشاشات (ZakiInvoiceQueries.CanView*).
/// البيانات: نفس ما تعرضه الشاشات (مع إخفاء فواتير مديري الحسابات زي الشاشة).
/// </summary>
public class ZakiSalesDocumentsService
{
    public const string GetSalesInvoicesSummary = "get_sales_invoices_summary";
    public const string GetOpenSalesInvoices = "get_open_sales_invoices";
    public const string GetUndeliveredSalesInvoices = "get_undelivered_sales_invoices";
    public const string GetLargestSalesInvoices = "get_largest_sales_invoices";
    public const string GetSalesByEmployee = "get_sales_by_employee";
    public const string GetTopCustomers = "get_top_customers";
    public const string GetTopProducts = "get_top_products";
    public const string GetSalesBreakdown = "get_sales_breakdown";
    public const string GetSalesInvoiceDetails = "get_sales_invoice_details";
    public const string GetCustomerInvoices = "get_customer_invoices";
    public const string GetOpenQuotations = "get_open_quotations";
    public const string GetInvoiceEditHistory = "get_invoice_edit_history";
    public const string GetPendingEditRequests = "get_pending_invoice_edit_requests";

    private static readonly string[] InvoiceToolNames =
    {
        GetSalesInvoicesSummary, GetOpenSalesInvoices, GetUndeliveredSalesInvoices, GetLargestSalesInvoices,
        GetSalesByEmployee, GetTopCustomers, GetTopProducts, GetSalesBreakdown,
        GetSalesInvoiceDetails, GetCustomerInvoices, GetInvoiceEditHistory, GetPendingEditRequests
    };

    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly ILogger<ZakiSalesDocumentsService> _logger;

    public ZakiSalesDocumentsService(
        IDbContextFactory<db24804Context> dbFactory,
        ILogger<ZakiSalesDocumentsService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public static bool Handles(string toolName) =>
        toolName == GetOpenQuotations || Array.IndexOf(InvoiceToolNames, toolName) >= 0;

    /// <summary>الأدوات اللي المستخدم مسموح بيها</summary>
    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user)
    {
        var list = new List<AiToolDefinition>();

        if (CanViewSalesInvoices(user))
        {
            list.Add(SummaryDefinition());
            list.Add(OpenInvoicesDefinition());
            list.Add(LargestInvoicesDefinition());
            list.Add(SalesByEmployeeDefinition());
            list.Add(TopCustomersDefinition());
            list.Add(TopProductsDefinition());
            list.Add(BreakdownDefinition());
            list.Add(InvoiceDetailsDefinition());
            list.Add(CustomerInvoicesDefinition());
        }

        if (CanViewDeliveryStatus(user))
            list.Add(UndeliveredDefinition());

        if (CanViewInvoiceHistory(user))
        {
            list.Add(EditHistoryDefinition());
            list.Add(PendingEditRequestsDefinition());
        }

        if (CanViewQuotations(user))
            list.Add(QuotationsDefinition());

        return list;
    }

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string name, string argumentsJson, ZakiUserInfo info, CancellationToken ct = default)
    {
        // كل أداة بتتقفل بنفس صلاحية الشاشة المقابلة
        var allowed = name switch
        {
            GetInvoiceEditHistory or GetPendingEditRequests => CanViewInvoiceHistory(user),
            GetOpenQuotations => CanViewQuotations(user),
            GetUndeliveredSalesInvoices => CanViewDeliveryStatus(user),
            _ => CanViewSalesInvoices(user)
        };

        if (!allowed)
            return Denied();

        var args = ParseArgs(argumentsJson);

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // نفس نطاق الشاشات: تاريخ الاطلاع + فواتير مديري الحسابات المخفية (الكل زي الشاشة)
            var scope = await BuildScopeAsync(db, user, null);

            // فلتر الفرع اختياري: لو مش مكتوب = كل الفروع (نفس شاشة الفواتير)
            var branchSearch = ReadRawString(args, "branch_name");
            if (branchSearch.Length > 0)
            {
                var (branchId, _, branchError) = await ResolveBranchAsync(db, user, branchSearch, ct);
                if (branchError != null)
                    return branchError;

                scope = scope with { BranchId = branchId };
            }

            return name switch
            {
                GetSalesInvoicesSummary => await SummaryAsync(db, scope, args, ct),
                GetOpenSalesInvoices => await OpenInvoicesAsync(db, scope, args, ct),
                GetUndeliveredSalesInvoices => await UndeliveredAsync(db, scope, args, ct),
                GetLargestSalesInvoices => await LargestInvoicesAsync(db, scope, args, ct),
                GetSalesByEmployee => await SalesByEmployeeAsync(db, scope, args, ct),
                GetTopCustomers => await TopCustomersAsync(db, scope, args, ct),
                GetTopProducts => await TopProductsAsync(db, scope, args, ct),
                GetSalesBreakdown => await BreakdownAsync(db, scope, args, ct),
                GetSalesInvoiceDetails => await InvoiceDetailsAsync(db, scope, args, ct),
                GetCustomerInvoices => await CustomerInvoicesAsync(db, scope, args, ct),
                GetOpenQuotations => await OpenQuotationsAsync(db, scope, args, ct),
                GetInvoiceEditHistory => await InvoiceEditHistoryAsync(db, scope, args, ct),
                GetPendingEditRequests => await PendingEditRequestsAsync(db, scope, args, ct),
                _ => Message("أداة غير معروفة.")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki sales documents: query failed ({Tool})", name);
            return Message("ماقدرتش أجيب البيانات دلوقتي، حاول تاني بعد شوية.");
        }
    }

    // ════════════════════════════════════════════════════════════
    // 1) ملخص فواتير المبيعات لفترة
    // ════════════════════════════════════════════════════════════
    private static async Task<ZakiToolOutcome> SummaryAsync(
        db24804Context db, ZakiInvoiceScope scope, JsonElement args, CancellationToken ct)
    {
        var today = DateTime.Today;
        var (start, label) = PeriodRange(ReadString(args, "period", "month"), today);

        var sales = Base(db, scope, TransactionTypes.Sale).FromDate(start);
        var count = await sales.CountAsync(ct);
        var total = await sales.SumAsync(t => (decimal?)t.GrandTotal, ct) ?? 0m;
        var paid = await sales.SumAsync(t => (decimal?)t.PaidAmount, ct) ?? 0m;

        var overdue = sales.Where(t => t.GrandTotal > t.PaidAmount && t.DueDate.HasValue && t.DueDate.Value < today);
        var overdueCount = await overdue.CountAsync(ct);
        var overdueRemaining = await overdue.SumAsync(t => (decimal?)(t.GrandTotal - t.PaidAmount), ct) ?? 0m;

        var cancelled = await Base(db, scope, TransactionTypes.Sale, includeCancelled: true)
            .Where(t => t.InvoiceStatus == CancelledStatus)
            .FromDate(start)
            .CountAsync(ct);

        var returns = Base(db, scope, TransactionTypes.SaleReturn).FromDate(start);
        var returnsCount = await returns.CountAsync(ct);
        var returnsTotal = await returns.SumAsync(t => (decimal?)t.GrandTotal, ct) ?? 0m;

        // طلبات التعديل المعلقة حالياً (مش مرتبطة بالفترة)
        var pendingEdits = await Base(db, scope, TransactionTypes.Sale)
            .Where(t => t.EditStatus == InvoiceEditStatuses.Pending)
            .CountAsync(ct);

        var average = count > 0 ? total / count : 0m;

        var headers = new[] { "المؤشر", "القيمة" };
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "عدد الفواتير", count.ToString(Culture) },
            new[] { "إجمالي المبيعات", Money(total) },
            new[] { "المحصّل من فواتير الفترة", Money(paid) },
            new[] { "المتبقي من فواتير الفترة", Money(total - paid) },
            new[] { "متوسط قيمة الفاتورة", Money(average) },
            new[] { "فواتير متأخرة", overdueCount.ToString(Culture) },
            new[] { "المتأخر المتبقي", Money(overdueRemaining) },
            new[] { "فواتير ملغاة", cancelled.ToString(Culture) },
            new[] { "مرتجعات البيع (عدد)", returnsCount.ToString(Culture) },
            new[] { "مرتجعات البيع (قيمة)", Money(returnsTotal) },
            new[] { "طلبات تعديل فواتير معلقة الآن", pendingEdits.ToString(Culture) }
        };

        // مقارنة بنفس المدة من الفترة اللي قبلها (لو الفترة محددة)
        var prevPeriod = PreviousPeriodRange(ReadString(args, "period", "month"), today);
        if (prevPeriod is { } pp)
        {
            var prevSales = Base(db, scope, TransactionTypes.Sale)
                .Where(t => t.TransactionDate >= pp.Start && t.TransactionDate < pp.End);
            var prevCount = await prevSales.CountAsync(ct);
            var prevTotal = await prevSales.SumAsync(t => (decimal?)t.GrandTotal, ct) ?? 0m;

            rows.Add(new[] { "عدد الفواتير — " + pp.Label, prevCount.ToString(Culture) });
            rows.Add(new[] { "إجمالي المبيعات — " + pp.Label, Money(prevTotal) });
            rows.Add(new[] { "تغيير إجمالي المبيعات", ChangePercent(total, prevTotal) });
        }

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفترة"] = label,
            ["ملخص فواتير المبيعات"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, new ZakiTable("ملخص فواتير المبيعات — " + label, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 2) الفواتير المفتوحة (لسه عليها متبقي) — أو المتأخرة بس
    // ════════════════════════════════════════════════════════════
    private static async Task<ZakiToolOutcome> OpenInvoicesAsync(
        db24804Context db, ZakiInvoiceScope scope, JsonElement args, CancellationToken ct)
    {
        var limit = ReadInt(args, "limit", 15, 1, 20);
        var overdueOnly = ReadBool(args, "overdue_only", false);
        var today = DateTime.Today;

        var query = Base(db, scope, TransactionTypes.Sale).Where(t => t.GrandTotal > t.PaidAmount);
        if (overdueOnly)
            query = query.Where(t => t.DueDate.HasValue && t.DueDate.Value < today);

        var total = await query.CountAsync(ct);
        var remainingSum = await query.SumAsync(t => (decimal?)(t.GrandTotal - t.PaidAmount), ct) ?? 0m;

        var items = await query
            .OrderBy(t => t.DueDate == null)
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.TransactionDate)
            .Take(limit)
            .Select(t => new InvoiceRow(
                t.ReferenceNumber, t.TransactionDate, t.DueDate,
                t.GrandTotal, t.PaidAmount, t.PartyId, t.EmpId))
            .ToListAsync(ct);

        var partyNames = await PartyNamesAsync(db, items.Select(i => i.PartyId), ct);
        var empNames = await EmployeeNamesAsync(db, EmpIds(items.Select(i => i.EmpId)), ct);

        var headers = new[]
        {
            "رقم الفاتورة", "التاريخ", "العميل", "الإجمالي", "المدفوع", "المتبقي",
            "تاريخ الاستحقاق", "الحالة", "البائع"
        };

        var rows = items.Select(i => (IReadOnlyList<string>)new[]
        {
            i.ReferenceNumber ?? "—",
            Date(i.TransactionDate),
            NameOf(i.PartyId, partyNames),
            Money(i.GrandTotal),
            Money(i.PaidAmount),
            Money(i.GrandTotal - i.PaidAmount),
            Date(i.DueDate),
            InvoiceState(i.GrandTotal, i.PaidAmount, i.DueDate, today),
            NameOf(i.EmpId, empNames)
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["عدد الفواتير في نطاقك"] = total,
            ["إجمالي المتبقي عليها"] = Money(remainingSum),
            ["عدد المعروض"] = rows.Count,
            ["الفواتير"] = ToObjects(headers, rows)
        });

        var title = overdueOnly ? "الفواتير المتأخرة" : "الفواتير المفتوحة";
        return new ZakiToolOutcome(json, rows.Count == 0 ? null : new ZakiTable(title, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 3) فواتير لسه ماتسلمتش
    // ════════════════════════════════════════════════════════════
    private static async Task<ZakiToolOutcome> UndeliveredAsync(
        db24804Context db, ZakiInvoiceScope scope, JsonElement args, CancellationToken ct)
    {
        var limit = ReadInt(args, "limit", 15, 1, 20);
        var today = DateTime.Today;

        var query = Base(db, scope, TransactionTypes.Sale)
            .Where(t => t.IsDelivered == null || t.IsDelivered == false);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.TransactionId)
            .Take(limit)
            .Select(t => new InvoiceRow(
                t.ReferenceNumber, t.TransactionDate, t.DueDate,
                t.GrandTotal, t.PaidAmount, t.PartyId, t.EmpId))
            .ToListAsync(ct);

        var partyNames = await PartyNamesAsync(db, items.Select(i => i.PartyId), ct);
        var empNames = await EmployeeNamesAsync(db, EmpIds(items.Select(i => i.EmpId)), ct);

        var headers = new[]
        {
            "رقم الفاتورة", "التاريخ", "العميل", "الإجمالي", "المدفوع", "البائع", "عمر الفاتورة (أيام)"
        };

        var rows = items.Select(i => (IReadOnlyList<string>)new[]
        {
            i.ReferenceNumber ?? "—",
            Date(i.TransactionDate),
            NameOf(i.PartyId, partyNames),
            Money(i.GrandTotal),
            Money(i.PaidAmount),
            NameOf(i.EmpId, empNames),
            (today - i.TransactionDate.Date).Days.ToString(Culture)
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["عدد الفواتير اللي لسه ماتسلمتش"] = total,
            ["عدد المعروض"] = rows.Count,
            ["الفواتير"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json,
            rows.Count == 0 ? null : new ZakiTable("فواتير لم تُسلَّم بعد", headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 4) أعلى الفواتير قيمة لفترة
    // ════════════════════════════════════════════════════════════
    private static async Task<ZakiToolOutcome> LargestInvoicesAsync(
        db24804Context db, ZakiInvoiceScope scope, JsonElement args, CancellationToken ct)
    {
        var limit = ReadInt(args, "limit", 5, 1, 20);
        var today = DateTime.Today;
        var (start, label) = PeriodRange(ReadString(args, "period", PeriodAll), today);

        var query = Base(db, scope, TransactionTypes.Sale).FromDate(start);
        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(t => t.GrandTotal)
            .ThenByDescending(t => t.TransactionDate)
            .Take(limit)
            .Select(t => new InvoiceRow(
                t.ReferenceNumber, t.TransactionDate, t.DueDate,
                t.GrandTotal, t.PaidAmount, t.PartyId, t.EmpId))
            .ToListAsync(ct);

        var partyNames = await PartyNamesAsync(db, items.Select(i => i.PartyId), ct);
        var empNames = await EmployeeNamesAsync(db, EmpIds(items.Select(i => i.EmpId)), ct);

        var headers = new[]
        {
            "الترتيب", "رقم الفاتورة", "التاريخ", "العميل", "الإجمالي", "المدفوع", "المتبقي", "الحالة", "البائع"
        };

        var rows = items.Select((i, idx) => (IReadOnlyList<string>)new[]
        {
            (idx + 1).ToString(Culture),
            i.ReferenceNumber ?? "—",
            Date(i.TransactionDate),
            NameOf(i.PartyId, partyNames),
            Money(i.GrandTotal),
            Money(i.PaidAmount),
            Money(i.GrandTotal - i.PaidAmount),
            InvoiceState(i.GrandTotal, i.PaidAmount, i.DueDate, today),
            NameOf(i.EmpId, empNames)
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفترة"] = label,
            ["عدد الفواتير في الفترة"] = total,
            ["أعلى فاتورة"] = FirstRow(headers, rows),
            ["أعلى الفواتير"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json,
            rows.Count == 0 ? null : new ZakiTable("أعلى الفواتير — " + label, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 5) ترتيب البائعين حسب إجمالي المبيعات لفترة
    // ════════════════════════════════════════════════════════════
    private static async Task<ZakiToolOutcome> SalesByEmployeeAsync(
        db24804Context db, ZakiInvoiceScope scope, JsonElement args, CancellationToken ct)
    {
        var limit = ReadInt(args, "limit", 10, 1, 20);
        var (start, label) = PeriodRange(ReadString(args, "period", PeriodAll), DateTime.Today);

        var query = Base(db, scope, TransactionTypes.Sale).FromDate(start);
        var overallTotal = await query.SumAsync(t => (decimal?)t.GrandTotal, ct) ?? 0m;

        var groups = await query
            .GroupBy(t => t.EmpId)
            .Select(g => new EmpRow(
                g.Key,
                g.Count(),
                g.Sum(t => t.GrandTotal),
                g.Sum(t => t.PaidAmount),
                g.Max(t => t.GrandTotal)))
            .OrderByDescending(x => x.Total)
            .Take(limit)
            .ToListAsync(ct);

        var empNames = await EmployeeNamesAsync(db, EmpIds(groups.Select(g => g.EmpId)), ct);

        var headers = new[]
        {
            "الترتيب", "الموظف", "عدد الفواتير", "إجمالي المبيعات", "المحصّل", "المتبقي", "أكبر فاتورة"
        };

        var rows = groups.Select((g, idx) => (IReadOnlyList<string>)new[]
        {
            (idx + 1).ToString(Culture),
            g.EmpId.HasValue ? NameOf(g.EmpId, empNames) : "بدون بائع",
            g.Count.ToString(Culture),
            Money(g.Total),
            Money(g.Paid),
            Money(g.Total - g.Paid),
            Money(g.Largest)
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفترة"] = label,
            ["إجمالي مبيعات الفترة"] = Money(overallTotal),
            ["أعلى موظف في المبيعات"] = FirstRow(headers, rows),
            ["ترتيب الموظفين"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json,
            rows.Count == 0 ? null : new ZakiTable("ترتيب الموظفين حسب المبيعات — " + label, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 6) أعلى العملاء شراءً لفترة
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> TopCustomersAsync(
        db24804Context db, ZakiInvoiceScope scope, JsonElement args, CancellationToken ct)
    {
        var limit = ReadInt(args, "limit", 10, 1, 20);
        var (start, label) = PeriodRange(ReadString(args, "period", PeriodAll), DateTime.Today);

        // نجيب الأعمدة المطلوبة بس ونجمع في الذاكرة: أبسط وأسهل في التشخيص من GroupBy على الداتابيز
        var invoices = await Base(db, scope, TransactionTypes.Sale)
            .FromDate(start)
            .Select(t => new CustomerInvoiceRow(t.PartyId, t.GrandTotal, t.PaidAmount))
            .ToListAsync(ct);

        var groups = SummarizeCustomers(invoices, limit);
        var scopeInvoices = invoices.Count;

        // لو الفترة فاضية: نعرض كل الفترات بدل ما نقول "مفيش بيانات"
        if (groups.Count == 0 && start.HasValue)
        {
            var all = await Base(db, scope, TransactionTypes.Sale)
                .Select(t => new CustomerInvoiceRow(t.PartyId, t.GrandTotal, t.PaidAmount))
                .ToListAsync(ct);

            groups = SummarizeCustomers(all, limit);
            scopeInvoices = all.Count;
            label = label + " (مفيش فواتير في الفترة دي، دي النتايج لكل الفترات)";
        }

        _logger.LogInformation(
            "Zaki top customers: period={Period} invoicesInScope={Invoices} groups={Groups}",
            label, scopeInvoices, groups.Count);

        var names = await PartyNamesAsync(db, groups.Select(g => g.PartyId), ct);

        var headers = new[]
        {
            "الترتيب", "العميل", "عدد الفواتير", "إجمالي المشتريات", "المدفوع", "المتبقي عليه"
        };

        var rows = groups.Select((g, idx) => (IReadOnlyList<string>)new[]
        {
            (idx + 1).ToString(Culture),
            NameOf(g.PartyId, names),
            g.Count.ToString(Culture),
            Money(g.Total),
            Money(g.Paid),
            Money(g.Total - g.Paid)
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفترة"] = label,
            ["أعلى عميل"] = FirstRow(headers, rows),
            ["أعلى العملاء"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json,
            rows.Count == 0 ? null : new ZakiTable("أعلى العملاء شراءً — " + label, headers, rows));
    }

    private static List<CustomerRow> SummarizeCustomers(List<CustomerInvoiceRow> invoices, int limit) =>
        invoices
            .GroupBy(i => i.PartyId)
            .Select(g => new CustomerRow(g.Key, g.Count(), g.Sum(x => x.GrandTotal), g.Sum(x => x.PaidAmount)))
            .OrderByDescending(x => x.Total)
            .Take(limit)
            .ToList();

    // ════════════════════════════════════════════════════════════
    // 7) أعلى المنتجات مبيعاً لفترة (حسب القيمة)
    // ════════════════════════════════════════════════════════════
    private static async Task<ZakiToolOutcome> TopProductsAsync(
        db24804Context db, ZakiInvoiceScope scope, JsonElement args, CancellationToken ct)
    {
        var limit = ReadInt(args, "limit", 10, 1, 20);
        var (start, label) = PeriodRange(ReadString(args, "period", PeriodAll), DateTime.Today);

        var invoiceIds = Base(db, scope, TransactionTypes.Sale).FromDate(start).Select(t => t.TransactionId);

        var groups = await db.TransactionDetails
            .AsNoTracking()
            .Where(d => invoiceIds.Contains(d.TransactionId))
            .GroupBy(d => d.ProductId)
            .Select(g => new ProductRow(
                g.Key,
                g.Sum(d => d.Quantity),
                g.Sum(d => d.TotalAmount ?? (d.Quantity * d.UnitPrice))))
            .OrderByDescending(x => x.Amount)
            .Take(limit)
            .ToListAsync(ct);

        var names = await ProductNamesAsync(db, groups.Select(g => g.ProductId), ct);

        var headers = new[] { "الترتيب", "الصنف", "الكمية المباعة", "إجمالي المبيعات" };

        var rows = groups.Select((g, idx) => (IReadOnlyList<string>)new[]
        {
            (idx + 1).ToString(Culture),
            NameOf(g.ProductId, names),
            g.Quantity.ToString("#,0.##", Culture),
            Money(g.Amount)
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفترة"] = label,
            ["أعلى منتج"] = FirstRow(headers, rows),
            ["أعلى المنتجات"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json,
            rows.Count == 0 ? null : new ZakiTable("أعلى المنتجات مبيعاً — " + label, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 8) تفصيل المبيعات: حسب المخزن أو طريقة الدفع أو الشهر
    // ════════════════════════════════════════════════════════════
    private static async Task<ZakiToolOutcome> BreakdownAsync(
        db24804Context db, ZakiInvoiceScope scope, JsonElement args, CancellationToken ct)
    {
        var by = ReadString(args, "by", "warehouse");
        var limit = ReadInt(args, "limit", 12, 1, 24);
        var (start, label) = PeriodRange(ReadString(args, "period", PeriodAll), DateTime.Today);

        var query = Base(db, scope, TransactionTypes.Sale).FromDate(start);
        List<Crumb> crumbs;
        string dimension;

        switch (by)
        {
            case "payment_method":
            {
                dimension = "طريقة الدفع";
                var groups = await query
                    .GroupBy(t => t.PaymentMethod)
                    .Select(g => new MethodRow(g.Key, g.Count(), g.Sum(t => t.GrandTotal), g.Sum(t => t.PaidAmount)))
                    .OrderByDescending(x => x.Total)
                    .Take(limit)
                    .ToListAsync(ct);

                crumbs = groups.Select(g => new Crumb(MethodLabel(g.Method), g.Count, g.Total, g.Paid)).ToList();
                break;
            }
            case "month":
            {
                dimension = "الشهر";
                var groups = await query
                    .GroupBy(t => new { t.TransactionDate.Year, t.TransactionDate.Month })
                    .Select(g => new MonthRow(
                        g.Key.Year,
                        g.Key.Month,
                        g.Count(),
                        g.Sum(t => t.GrandTotal),
                        g.Sum(t => t.PaidAmount)))
                    .OrderByDescending(x => x.Year)
                    .ThenByDescending(x => x.Month)
                    .Take(limit)
                    .ToListAsync(ct);

                crumbs = groups.Select(g => new Crumb(
                    g.Year.ToString(Culture) + "-" + g.Month.ToString("00", Culture),
                    g.Count, g.Total, g.Paid)).ToList();
                break;
            }
            case "warehouse":
            {
                dimension = "المخزن";
                var groups = await query
                    .GroupBy(t => t.WarehouseId)
                    .Select(g => new WarehouseRow(g.Key, g.Count(), g.Sum(t => t.GrandTotal), g.Sum(t => t.PaidAmount)))
                    .OrderByDescending(x => x.Total)
                    .Take(limit)
                    .ToListAsync(ct);

                var whIds = groups.Select(g => g.WarehouseId).Distinct().ToList();
                var whNames = whIds.Count == 0
                    ? new Dictionary<int, string>()
                    : await db.Warehouses.AsNoTracking()
                        .Where(w => whIds.Contains(w.WarehouseId))
                        .ToDictionaryAsync(w => w.WarehouseId, w => w.WarehouseName, ct);

                crumbs = groups.Select(g => new Crumb(NameOf(g.WarehouseId, whNames), g.Count, g.Total, g.Paid)).ToList();
                break;
            }
            default:
                return Message("قيمة by لازم تكون: warehouse أو payment_method أو month.");
        }

        var headers = new[] { "التصنيف", "عدد الفواتير", "إجمالي المبيعات", "المحصّل", "المتبقي" };

        var rows = crumbs.Select(c => (IReadOnlyList<string>)new[]
        {
            c.Label,
            c.Count.ToString(Culture),
            Money(c.Total),
            Money(c.Paid),
            Money(c.Total - c.Paid)
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفترة"] = label,
            ["التصنيف حسب"] = dimension,
            ["المبيعات"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json,
            rows.Count == 0 ? null : new ZakiTable("مبيعات حسب " + dimension + " — " + label, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 9) تفاصيل فاتورة برقمها (الأصناف + الدفعات + التسليم)
    // ════════════════════════════════════════════════════════════
    private static async Task<ZakiToolOutcome> InvoiceDetailsAsync(
        db24804Context db, ZakiInvoiceScope scope, JsonElement args, CancellationToken ct)
    {
        var number = ReadRawString(args, "invoice_number");
        if (number.Length == 0)
            return Message("اكتب رقم الفاتورة اللي عايز تفاصيلها.");

        var matches = await Base(db, scope, TransactionTypes.Sale, includeCancelled: true)
            .Where(t => t.ReferenceNumber != null && t.ReferenceNumber.Contains(number))
            .OrderByDescending(t => t.TransactionDate)
            .Take(10)
            .Select(t => new HeaderRow(
                t.TransactionId, t.ReferenceNumber, t.TransactionDate, t.DueDate,
                t.GrandTotal, t.PaidAmount, t.DiscountAmount, t.PaymentMethod, t.InvoiceStatus,
                t.IsDelivered, t.DeliveredAt, t.DeliveryEmployeeName, t.WarehouseId, t.PartyId, t.EmpId))
            .ToListAsync(ct);

        if (matches.Count == 0)
            return Message("مالقيتش فاتورة بالرقم ده في نطاق صلاحياتك.");

        var inv = matches.FirstOrDefault(m => string.Equals(m.ReferenceNumber, number, StringComparison.OrdinalIgnoreCase))
                  ?? matches[0];
        var today = DateTime.Today;

        var items = await db.TransactionDetails
            .AsNoTracking()
            .Where(d => d.TransactionId == inv.TransactionId)
            .OrderBy(d => d.DetailId)
            .Select(d => new ItemRow(
                d.Product.ProductName,
                d.Quantity,
                d.UnitPrice,
                d.TotalAmount ?? (d.Quantity * d.UnitPrice)))
            .ToListAsync(ct);

        var payments = await db.Payments
            .AsNoTracking()
            .Where(p => p.TransactionId == inv.TransactionId)
            .OrderBy(p => p.PaymentDate)
            .Select(p => new PaymentRow(p.PaymentDate, p.Amount, p.PaymentMethod))
            .ToListAsync(ct);

        var partyNames = await PartyNamesAsync(db, new[] { inv.PartyId }, ct);
        var empNames = await EmployeeNamesAsync(db, inv.EmpId.HasValue ? new[] { inv.EmpId.Value } : Array.Empty<int>(), ct);
        var warehouseName = await db.Warehouses.AsNoTracking()
            .Where(w => w.WarehouseId == inv.WarehouseId)
            .Select(w => w.WarehouseName)
            .FirstOrDefaultAsync(ct) ?? "—";

        var remaining = inv.GrandTotal - inv.PaidAmount;
        var state = inv.InvoiceStatus == CancelledStatus
            ? "ملغاة"
            : InvoiceState(inv.GrandTotal, inv.PaidAmount, inv.DueDate, today);

        var delivery = inv.IsDelivered == true
            ? "تم التسليم"
              + (inv.DeliveredAt.HasValue ? " في " + Date(inv.DeliveredAt) : "")
              + (string.IsNullOrWhiteSpace(inv.DeliveryEmployeeName) ? "" : " بواسطة " + inv.DeliveryEmployeeName)
            : "لم يُسلَّم بعد";

        var customerName = NameOf(inv.PartyId, partyNames);
        var refNo = inv.ReferenceNumber ?? "—";

        var headerInfo = new Dictionary<string, object?>
        {
            ["رقم الفاتورة"] = refNo,
            ["التاريخ"] = Date(inv.TransactionDate),
            ["العميل"] = customerName,
            ["البائع"] = NameOf(inv.EmpId, empNames),
            ["المخزن"] = warehouseName,
            ["الإجمالي"] = Money(inv.GrandTotal),
            ["الخصم"] = Money(inv.DiscountAmount ?? 0m),
            ["المدفوع"] = Money(inv.PaidAmount),
            ["المتبقي"] = Money(remaining),
            ["الحالة"] = state,
            ["طريقة الدفع"] = MethodLabel(inv.PaymentMethod),
            ["تاريخ الاستحقاق"] = Date(inv.DueDate),
            ["التسليم"] = delivery,
            ["الدفعات"] = payments.Select(p => new Dictionary<string, object?>
            {
                ["التاريخ"] = Date(p.PaymentDate),
                ["المبلغ"] = Money(p.Amount),
                ["الطريقة"] = MethodLabel(p.PaymentMethod)
            }).ToList()
        };

        var headers = new[] { "الصنف", "الكمية", "سعر الوحدة", "الإجمالي" };
        var rows = items.Select(i => (IReadOnlyList<string>)new[]
        {
            i.ProductName,
            i.Quantity.ToString("#,0.##", Culture),
            Money(i.UnitPrice),
            Money(i.Total)
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفاتورة"] = headerInfo,
            ["عدد الأصناف"] = items.Count,
            ["أرقام مشابهة"] = matches.Select(m => m.ReferenceNumber)
                .Where(r => r != null && r != inv.ReferenceNumber)
                .Take(5)
                .ToList()
        });

        var title = "أصناف الفاتورة " + refNo + " — العميل: " + customerName
                    + " — الإجمالي: " + Money(inv.GrandTotal) + " — المتبقي: " + Money(remaining);

        return new ZakiToolOutcome(json, rows.Count == 0 ? null : new ZakiTable(title, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 10) فواتير عميل معين + رصيده
    // ════════════════════════════════════════════════════════════
    private static async Task<ZakiToolOutcome> CustomerInvoicesAsync(
        db24804Context db, ZakiInvoiceScope scope, JsonElement args, CancellationToken ct)
    {
        var search = ReadRawString(args, "customer_name");
        if (search.Length < 2)
            return Message("اكتب اسم العميل (أو جزء منه).");

        var limit = ReadInt(args, "limit", 15, 1, 20);
        var today = DateTime.Today;

        var parties = await db.Parties
            .AsNoTracking()
            .Select(p => new { p.PartyId, p.PartyName })
            .ToListAsync(ct);

        var matched = parties.Where(p => (p.PartyName ?? "").ContainsArabic(search)).ToList();
        if (matched.Count == 0)
            return Message("مالقيتش عميل بالاسم ده.");

        var exact = matched.Where(p => string.Equals(p.PartyName, search, StringComparison.OrdinalIgnoreCase)).ToList();

        // أكتر من عميل ومفيش تطابق كامل: نطلب الاسم بالكامل بدل ما نجمع أرقام عملاء مختلفين
        if (exact.Count == 0 && matched.Count > 1)
        {
            var names = matched.Take(8).Select(p => p.PartyName).ToList();
            return new ZakiToolOutcome(ToJson(new Dictionary<string, object?>
            {
                ["ملاحظة"] = "في أكتر من عميل بالاسم ده. اطلب من المستخدم يكتب الاسم بالكامل.",
                ["العملاء المطابقين"] = names
            }), null);
        }

        var chosen = exact.Count > 0 ? exact : matched;
        var ids = chosen.Select(p => p.PartyId).ToList();
        var customerName = string.Join("، ", chosen.Select(p => p.PartyName));

        var query = Base(db, scope, TransactionTypes.Sale).Where(t => ids.Contains(t.PartyId));

        var count = await query.CountAsync(ct);
        if (count == 0)
            return Message("مالقيتش فواتير للعميل ده في نطاق صلاحياتك.");

        var total = await query.SumAsync(t => (decimal?)t.GrandTotal, ct) ?? 0m;
        var paid = await query.SumAsync(t => (decimal?)t.PaidAmount, ct) ?? 0m;

        var items = await query
            .OrderByDescending(t => t.TransactionDate)
            .Take(limit)
            .Select(t => new InvoiceRow(
                t.ReferenceNumber, t.TransactionDate, t.DueDate,
                t.GrandTotal, t.PaidAmount, t.PartyId, t.EmpId))
            .ToListAsync(ct);

        var empNames = await EmployeeNamesAsync(db, EmpIds(items.Select(i => i.EmpId)), ct);

        var headers = new[] { "رقم الفاتورة", "التاريخ", "الإجمالي", "المدفوع", "المتبقي", "الحالة", "البائع" };

        var rows = items.Select(i => (IReadOnlyList<string>)new[]
        {
            i.ReferenceNumber ?? "—",
            Date(i.TransactionDate),
            Money(i.GrandTotal),
            Money(i.PaidAmount),
            Money(i.GrandTotal - i.PaidAmount),
            InvoiceState(i.GrandTotal, i.PaidAmount, i.DueDate, today),
            NameOf(i.EmpId, empNames)
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["العميل"] = customerName,
            ["عدد الفواتير"] = count,
            ["إجمالي المشتريات"] = Money(total),
            ["المدفوع"] = Money(paid),
            ["الرصيد المتبقي عليه"] = Money(total - paid),
            ["آخر الفواتير"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json,
            new ZakiTable("فواتير " + customerName + " — الرصيد المتبقي " + Money(total - paid), headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 11) عروض الأسعار المفتوحة (مسودة أو تم الإرسال، ولسه ماتحولتش لفاتورة)
    // ════════════════════════════════════════════════════════════
    private static async Task<ZakiToolOutcome> OpenQuotationsAsync(
        db24804Context db, ZakiInvoiceScope scope, JsonElement args, CancellationToken ct)
    {
        var limit = ReadInt(args, "limit", 15, 1, 20);
        var today = DateTime.Today;

        var query = db.Quotations
            .AsNoTracking()
            .Where(q => q.InvoiceId == null
                        && (q.Status == QuotationStatuses.Draft || q.Status == QuotationStatuses.Sent));

        if (scope.AccessFrom.HasValue)
            query = query.Where(q => q.QuotationDate >= scope.AccessFrom.Value);

        if (scope.EmployeeId.HasValue)
            query = query.Where(q => q.EmpId == scope.EmployeeId.Value);

        query = query.ExcludeProtectedQuotations(scope.ProtectedCreators);

        var total = await query.CountAsync(ct);
        var amount = await query.SumAsync(q => (decimal?)(q.GrandTotal ?? q.TotalAmount), ct) ?? 0m;

        var items = await query
            .OrderByDescending(q => q.QuotationDate)
            .ThenByDescending(q => q.QuotationId)
            .Take(limit)
            .Select(q => new QuotationRow(
                q.ReferenceNumber,
                q.QuotationDate,
                q.PartyId,
                q.GrandTotal ?? q.TotalAmount,
                q.ValidUntil,
                q.Status,
                q.EmpId))
            .ToListAsync(ct);

        var partyNames = await PartyNamesAsync(db, items.Select(i => i.PartyId), ct);
        var empNames = await EmployeeNamesAsync(db, EmpIds(items.Select(i => i.EmpId)), ct);

        var headers = new[] { "رقم العرض", "التاريخ", "العميل", "الإجمالي", "صالح لغاية", "الحالة", "البائع" };

        var rows = items.Select(i =>
        {
            var expired = i.ValidUntil.HasValue && i.ValidUntil.Value.Date < today;
            var state = expired
                ? "منتهي الصلاحية"
                : i.Status == QuotationStatuses.Draft ? "مسودة" : "تم الإرسال";

            return (IReadOnlyList<string>)new[]
            {
                i.ReferenceNumber ?? "—",
                Date(i.QuotationDate),
                NameOf(i.PartyId, partyNames),
                Money(i.Amount),
                Date(i.ValidUntil),
                state,
                NameOf(i.EmpId, empNames)
            };
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["عدد عروض الأسعار المفتوحة في نطاقك"] = total,
            ["إجمالي قيمتها"] = Money(amount),
            ["عدد المعروض"] = rows.Count,
            ["عروض الأسعار"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json,
            rows.Count == 0 ? null : new ZakiTable("عروض الأسعار المفتوحة", headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 12) سجل التعديلات على فاتورة (بيع أو شراء) برقمها
    // ════════════════════════════════════════════════════════════
    private static async Task<ZakiToolOutcome> InvoiceEditHistoryAsync(
        db24804Context db, ZakiInvoiceScope scope, JsonElement args, CancellationToken ct)
    {
        var number = ReadRawString(args, "invoice_number");
        if (number.Length == 0)
            return Message("اكتب رقم الفاتورة اللي عايز سجل تعديلاتها.");

        var limit = ReadInt(args, "limit", 20, 1, 60);

        var found = await FindInvoicesAsync(db, scope, number, ct);
        if (found.Count == 0)
            return Message("مالقيتش فاتورة بالرقم ده.");

        var inv = found.FirstOrDefault(h => string.Equals(h.ReferenceNumber, number, StringComparison.OrdinalIgnoreCase))
                  ?? found[0];

        var pk = inv.TransactionId.ToString(Culture);
        var audits = await db.AuditLogs
            .AsNoTracking()
            .Where(a => a.TableName == "Transactions" && a.PrimaryKeyValue == pk)
            .OrderByDescending(a => a.AuditId)
            .Take(limit)
            .Select(a => new AuditRow(a.AuditId, a.ActionType, a.ActionDate, a.LoginName, a.AccessUserName, a.OldData, a.NewData))
            .ToListAsync(ct);

        var partyName = await PartyLabelAsync(db, inv, ct);
        var isPurchase = inv.TransactionType == TransactionTypes.Purchase;

        var headers = new[] { "التاريخ", "الحركة", "بواسطة", "التفاصيل" };
        var rows = audits.Select(a =>
        {
            var changes = ZakiInvoiceEditDiff.Compute(a.ActionType, a.OldData, a.NewData);
            var details = changes.Count == 0
                ? "—"
                : string.Join("؛ ", changes.Select(ZakiInvoiceEditDiff.Describe));

            return (IReadOnlyList<string>)new[]
            {
                a.ActionDate.HasValue ? a.ActionDate.Value.ToString("yyyy-MM-dd HH:mm", Culture) : "—",
                ZakiInvoiceEditDiff.ActionLabel(a.ActionType),
                string.IsNullOrWhiteSpace(a.LoginName) ? (a.AccessUserName ?? "—") : a.LoginName,
                details
            };
        }).ToList();

        var headerInfo = new Dictionary<string, object?>
        {
            ["رقم الفاتورة"] = inv.ReferenceNumber ?? "—",
            ["النوع"] = isPurchase ? "فاتورة شراء" : "فاتورة بيع",
            [isPurchase ? "العميل المرتبط" : "العميل"] = partyName,
            ["الحالة"] = inv.InvoiceStatus == CancelledStatus ? "ملغاة" : "نشطة",
            ["أنشئت بواسطة"] = inv.CreatedBy,
            ["تاريخ الإنشاء"] = Date(inv.CreatedAt),
            ["حالة طلب التعديل"] = ZakiInvoiceEditDiff.EditStatusLabel(inv.EditStatus),
            ["سبب آخر طلب تعديل"] = inv.EditReason ?? "—",
            ["آخر تعديل بواسطة"] = inv.EditBy ?? "—",
            ["تاريخ آخر تعديل"] = Date(inv.EditAt)
        };

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفاتورة"] = headerInfo,
            ["عدد الحركات المعروضة"] = rows.Count,
            ["الحركات"] = ToObjects(headers, rows)
        });

        var title = "سجل تعديلات الفاتورة " + (inv.ReferenceNumber ?? "") + " — " + partyName;
        return new ZakiToolOutcome(json, rows.Count == 0 ? null : new ZakiTable(title, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 13) طلبات التعديل المعلقة (بتنتظر اعتماد)
    // ════════════════════════════════════════════════════════════
    private static async Task<ZakiToolOutcome> PendingEditRequestsAsync(
        db24804Context db, ZakiInvoiceScope scope, JsonElement args, CancellationToken ct)
    {
        var limit = ReadInt(args, "limit", 15, 1, 20);

        var query = Base(db, scope, TransactionTypes.Sale, includeCancelled: true)
            .Where(t => t.EditStatus == InvoiceEditStatuses.Pending);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(t => t.EditRequestDate)
            .Take(limit)
            .Select(t => new PendingRow(
                t.ReferenceNumber, t.TransactionDate, t.GrandTotal, t.PartyId,
                t.EditReason, t.EditBy, t.EditRequestDate))
            .ToListAsync(ct);

        var names = await PartyNamesAsync(db, items.Select(i => i.PartyId), ct);

        var headers = new[] { "رقم الفاتورة", "التاريخ", "العميل", "الإجمالي", "سبب الطلب", "طلب بواسطة", "تاريخ الطلب" };
        var rows = items.Select(i => (IReadOnlyList<string>)new[]
        {
            i.ReferenceNumber ?? "—",
            Date(i.TransactionDate),
            NameOf(i.PartyId, names),
            Money(i.GrandTotal),
            i.EditReason ?? "—",
            i.EditBy ?? "—",
            Date(i.EditRequestDate)
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["عدد طلبات التعديل المعلقة"] = total,
            ["عدد المعروض"] = rows.Count,
            ["الطلبات"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json,
            rows.Count == 0 ? null : new ZakiTable("طلبات التعديل المعلقة", headers, rows));
    }

    /// <summary>الفواتير (بيع أو شراء) اللي رقمها بيطابق البحث، بنفس نطاق الشاشة</summary>
    private static async Task<List<InvoiceHead>> FindInvoicesAsync(
        db24804Context db, ZakiInvoiceScope scope, string number, CancellationToken ct)
    {
        var sales = await Base(db, scope, TransactionTypes.Sale, includeCancelled: true)
            .Where(t => t.ReferenceNumber != null && t.ReferenceNumber.Contains(number))
            .OrderByDescending(t => t.TransactionDate)
            .Take(10)
            .Select(t => new InvoiceHead(
                t.TransactionId, t.ReferenceNumber, t.TransactionType, t.PartyId, t.EmpId,
                t.CreatedBy, t.CreatedAt, t.InvoiceStatus, t.EditStatus, t.EditReason, t.EditBy, t.EditAt))
            .ToListAsync(ct);

        var purchases = await Base(db, scope, TransactionTypes.Purchase, includeCancelled: true)
            .Where(t => t.ReferenceNumber != null && t.ReferenceNumber.Contains(number))
            .OrderByDescending(t => t.TransactionDate)
            .Take(10)
            .Select(t => new InvoiceHead(
                t.TransactionId, t.ReferenceNumber, t.TransactionType, t.PartyId, t.EmpId,
                t.CreatedBy, t.CreatedAt, t.InvoiceStatus, t.EditStatus, t.EditReason, t.EditBy, t.EditAt))
            .ToListAsync(ct);

        return sales.Concat(purchases).ToList();
    }

    /// <summary>اسم العميل حسب نوع الفاتورة: بيع = العميل (PartyId)، شراء = العميل المرتبط (EmpId)</summary>
    private static async Task<string> PartyLabelAsync(db24804Context db, InvoiceHead inv, CancellationToken ct)
    {
        var id = inv.TransactionType == TransactionTypes.Purchase ? inv.EmpId : inv.PartyId;
        if (!id.HasValue) return "—";
        var names = await PartyNamesAsync(db, new[] { id.Value }, ct);
        return NameOf(id, names);
    }

    // ════════════════════════════════════════════════════════════
    // الصلاحيات
    // ════════════════════════════════════════════════════════════
    /// <summary>سجل التعديلات وطلبات الاعتماد: Admin ومدير الحسابات (زي شاشة سجل التعديلات)</summary>
    public static bool CanViewInvoiceHistory(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.IsInRole("AccountManager");

    private static IEnumerable<int> EmpIds(IEnumerable<int?> ids) =>
        ids.Where(i => i.HasValue).Select(i => i!.Value);

    // ════════════════════════════════════════════════════════════
    // تعريفات الأدوات (وصف قصير عشان نوفر التوكنز)
    // ════════════════════════════════════════════════════════════
    private static object PeriodSchema(string description) => new
    {
        type = "string",
        description,
        @enum = new[] { "today", "week", "month", "year", "all" }
    };

    /// <summary>فلتر الفرع اختياري (اسم الفرع، أو «فرعي» لفرع المستخدم)</summary>
    private static object BranchSchema() => new
    {
        type = "string",
        description = "اسم الفرع (اختياري). فاضي = كل الفروع. «فرعي» = فرع المستخدم."
    };

    private static AiToolDefinition SummaryDefinition() => new(
        GetSalesInvoicesSummary,
        "ملخص فواتير المبيعات لفترة: العدد والإجمالي والمحصّل والمتبقي والمتأخر والملغي والمرتجعات ومتوسط الفاتورة.",
        new
        {
            type = "object",
            properties = new { period = PeriodSchema("الافتراضي month"), branch_name = BranchSchema() }
        });

    private static AiToolDefinition OpenInvoicesDefinition() => new(
        GetOpenSalesInvoices,
        "الفواتير اللي عليها متبقي مرتبة بتاريخ الاستحقاق. overdue_only=true للمتأخرة بس.",
        new
        {
            type = "object",
            properties = new
            {
                branch_name = BranchSchema(),
                limit = new { type = "integer", minimum = 1, maximum = 20 },
                overdue_only = new { type = "boolean" }
            }
        });

    private static AiToolDefinition UndeliveredDefinition() => new(
        GetUndeliveredSalesInvoices,
        "فواتير البيع اللي لسه ماتسلمتش للعميل، من الأقدم للأحدث.",
        new
        {
            type = "object",
            properties = new { limit = new { type = "integer", minimum = 1, maximum = 20 }, branch_name = BranchSchema() }
        });

    private static AiToolDefinition LargestInvoicesDefinition() => new(
        GetLargestSalesInvoices,
        "أعلى الفواتير قيمة (إجمالي الفاتورة) وأول واحدة فيها هي أعلى فاتورة. الافتراضي all.",
        new
        {
            type = "object",
            properties = new
            {
                branch_name = BranchSchema(),
                period = PeriodSchema("الافتراضي all"),
                limit = new { type = "integer", minimum = 1, maximum = 20 }
            }
        });

    private static AiToolDefinition SalesByEmployeeDefinition() => new(
        GetSalesByEmployee,
        "ترتيب الموظفين حسب إجمالي مبيعاتهم، وأول واحد فيه هو أعلى موظف في المبيعات. الافتراضي all.",
        new
        {
            type = "object",
            properties = new
            {
                branch_name = BranchSchema(),
                period = PeriodSchema("الافتراضي all"),
                limit = new { type = "integer", minimum = 1, maximum = 20 }
            }
        });

    private static AiToolDefinition TopCustomersDefinition() => new(
        GetTopCustomers,
        "أعلى العملاء شراءً حسب إجمالي المشتريات، وأول واحد فيه هو أعلى عميل. الافتراضي all.",
        new
        {
            type = "object",
            properties = new
            {
                branch_name = BranchSchema(),
                period = PeriodSchema("الافتراضي all"),
                limit = new { type = "integer", minimum = 1, maximum = 20 }
            }
        });

    private static AiToolDefinition TopProductsDefinition() => new(
        GetTopProducts,
        "أعلى المنتجات (الأصناف) مبيعاً حسب القيمة مع الكمية، وأول واحد هو أعلى منتج. الافتراضي all.",
        new
        {
            type = "object",
            properties = new
            {
                branch_name = BranchSchema(),
                period = PeriodSchema("الافتراضي all"),
                limit = new { type = "integer", minimum = 1, maximum = 20 }
            }
        });

    private static AiToolDefinition BreakdownDefinition() => new(
        GetSalesBreakdown,
        "تفصيل المبيعات حسب المخزن (warehouse) أو طريقة الدفع (payment_method) أو الشهر (month). الافتراضي all.",
        new
        {
            type = "object",
            properties = new
            {
                branch_name = BranchSchema(),
                by = new { type = "string", @enum = new[] { "warehouse", "payment_method", "month" } },
                period = PeriodSchema("الافتراضي all"),
                limit = new { type = "integer", minimum = 1, maximum = 24 }
            }
        });

    private static AiToolDefinition InvoiceDetailsDefinition() => new(
        GetSalesInvoiceDetails,
        "تفاصيل فاتورة بيع برقمها: الأصناف والدفعات والمتبقي والتسليم. استخدمها لأي سؤال عن فاتورة بعينها.",
        new
        {
            type = "object",
            properties = new
            {
                invoice_number = new { type = "string", description = "رقم الفاتورة أو جزء منه" }
            },
            required = new[] { "invoice_number" }
        });

    private static AiToolDefinition CustomerInvoicesDefinition() => new(
        GetCustomerInvoices,
        "فواتير عميل بالاسم (أو جزء منه) ورصيده المتبقي. لو الاسم مش واضح ترجع أسماء المطابقين.",
        new
        {
            type = "object",
            properties = new
            {
                customer_name = new { type = "string", description = "اسم العميل أو جزء منه" },
                limit = new { type = "integer", minimum = 1, maximum = 20 }
            },
            required = new[] { "customer_name" }
        });

    private static AiToolDefinition EditHistoryDefinition() => new(
        GetInvoiceEditHistory,
        "سجل التعديلات على فاتورة (بيع أو شراء) برقمها: مين عمل إيه ومتى، والقيم القديمة والجديدة، وأسباب الطلبات. للـ Admin ومدير الحسابات بس.",
        new
        {
            type = "object",
            properties = new
            {
                invoice_number = new { type = "string", description = "رقم الفاتورة أو جزء منه" },
                limit = new { type = "integer", minimum = 1, maximum = 60 }
            },
            required = new[] { "invoice_number" }
        });

    private static AiToolDefinition PendingEditRequestsDefinition() => new(
        GetPendingEditRequests,
        "طلبات تعديل الفواتير اللي لسه معلقة وبتنتظر اعتماد، مع سببها ومين طلبها. للـ Admin ومدير الحسابات بس.",
        new
        {
            type = "object",
            properties = new { limit = new { type = "integer", minimum = 1, maximum = 20 } }
        });

    private static AiToolDefinition QuotationsDefinition() => new(
        GetOpenQuotations,
        "عروض الأسعار المفتوحة (مسودة أو تم الإرسال ولسه ماتحولتش لفاتورة) مع حالة صلاحيتها.",
        new
        {
            type = "object",
            properties = new { limit = new { type = "integer", minimum = 1, maximum = 20 } }
        });

    // ════════════════════════════════════════════════════════════
    // أنواع داخلية
    // ════════════════════════════════════════════════════════════
    private sealed record InvoiceRow(
        string? ReferenceNumber, DateTime TransactionDate, DateTime? DueDate,
        decimal GrandTotal, decimal PaidAmount, int PartyId, int? EmpId);

    private sealed record HeaderRow(
        int TransactionId, string? ReferenceNumber, DateTime TransactionDate, DateTime? DueDate,
        decimal GrandTotal, decimal PaidAmount, decimal? DiscountAmount, string? PaymentMethod,
        string? InvoiceStatus, bool? IsDelivered, DateTime? DeliveredAt, string? DeliveryEmployeeName,
        int WarehouseId, int PartyId, int? EmpId);

    private sealed record ItemRow(string ProductName, decimal Quantity, decimal UnitPrice, decimal Total);

    private sealed record PaymentRow(DateTime PaymentDate, decimal Amount, string? PaymentMethod);

    private sealed record EmpRow(int? EmpId, int Count, decimal Total, decimal Paid, decimal Largest);

    private sealed record CustomerRow(int PartyId, int Count, decimal Total, decimal Paid);

    private sealed record CustomerInvoiceRow(int PartyId, decimal GrandTotal, decimal PaidAmount);

    private sealed record ProductRow(int ProductId, decimal Quantity, decimal Amount);

    private sealed record MethodRow(string? Method, int Count, decimal Total, decimal Paid);

    private sealed record MonthRow(int Year, int Month, int Count, decimal Total, decimal Paid);

    private sealed record WarehouseRow(int WarehouseId, int Count, decimal Total, decimal Paid);

    private sealed record Crumb(string Label, int Count, decimal Total, decimal Paid);

    private sealed record AuditRow(
        long AuditId, string? ActionType, DateTime? ActionDate, string? LoginName,
        string? AccessUserName, string? OldData, string? NewData);

    private sealed record InvoiceHead(
        int TransactionId, string? ReferenceNumber, string TransactionType, int PartyId, int? EmpId,
        string CreatedBy, DateTime CreatedAt, string? InvoiceStatus, int? EditStatus, string? EditReason,
        string? EditBy, DateTime? EditAt);

    private sealed record PendingRow(
        string? ReferenceNumber, DateTime TransactionDate, decimal GrandTotal, int PartyId,
        string? EditReason, string? EditBy, DateTime? EditRequestDate);

    private sealed record QuotationRow(
        string? ReferenceNumber, DateTime QuotationDate, int PartyId,
        decimal Amount, DateTime? ValidUntil, string Status, int? EmpId);
}
