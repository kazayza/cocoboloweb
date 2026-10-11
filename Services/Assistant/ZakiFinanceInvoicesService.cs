using System.Security.Claims;
using System.Text.Json;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using COCOBOLOERPNEW.Services;
using Microsoft.EntityFrameworkCore;
using static COCOBOLOERPNEW.Services.Assistant.ZakiInvoiceQueries;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// أدوات فواتير الشراء والمدفوعات في زكي.
/// الصلاحيات: نفس شروط الشاشات (ZakiInvoiceQueries.CanView*):
/// - فواتير الشراء: Admin أو frm_SupplierInvoices:View أو دور factory (زي /factory/purchases).
/// - المقبوضات من العملاء: Admin أو frm_Payments:View.
/// - المدفوعات للموردين: Admin أو AccountManager أو Account (زي شاشة المدفوعات).
/// المورد واحد في النظام، فالمستحق عليه هو إجمالي المتبقي من فواتير الشراء.
/// EmpId في فاتورة الشراء = العميل المرتبط بالفاتورة (مش موظف).
/// </summary>
public class ZakiFinanceInvoicesService
{
    public const string GetPurchaseInvoicesSummary = "get_purchase_invoices_summary";
    public const string GetOpenPurchaseInvoices = "get_open_purchase_invoices";
    public const string GetPurchaseInvoiceDetails = "get_purchase_invoice_details";
    public const string GetPaymentsSummary = "get_payments_summary";

    private static readonly DateTime MinDate = new(1753, 1, 1);

    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly ILogger<ZakiFinanceInvoicesService> _logger;

    public ZakiFinanceInvoicesService(
        IDbContextFactory<db24804Context> dbFactory,
        ILogger<ZakiFinanceInvoicesService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public static bool Handles(string toolName) =>
        toolName is GetPurchaseInvoicesSummary
            or GetOpenPurchaseInvoices
            or GetPurchaseInvoiceDetails
            or GetPaymentsSummary;

    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user)
    {
        var list = new List<AiToolDefinition>();

        if (CanViewPurchaseInvoices(user))
        {
            list.Add(PurchaseSummaryDefinition());
            list.Add(OpenPurchasesDefinition());
            list.Add(PurchaseDetailsDefinition());
        }

        if (CanViewReceipts(user) || CanViewPurchasePayments(user))
            list.Add(PaymentsDefinition());

        return list;
    }

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string name, string argumentsJson, CancellationToken ct = default)
    {
        var allowed = name == GetPaymentsSummary
            ? CanViewReceipts(user) || CanViewPurchasePayments(user)
            : CanViewPurchaseInvoices(user);

        if (!allowed)
            return Denied();

        var args = ParseArgs(argumentsJson);

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var scope = await BuildScopeAsync(db, user, null);

            return name switch
            {
                GetPurchaseInvoicesSummary => await PurchaseSummaryAsync(db, scope, args, ct),
                GetOpenPurchaseInvoices => await OpenPurchasesAsync(db, scope, args, ct),
                GetPurchaseInvoiceDetails => await PurchaseDetailsAsync(db, scope, args, ct),
                GetPaymentsSummary => await PaymentsAsync(db, user, args, ct),
                _ => Message("أداة غير معروفة.")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki finance: query failed ({Tool})", name);
            return Message("ماقدرتش أجيب البيانات دلوقتي، حاول تاني بعد شوية.");
        }
    }

    // ════════════════════════════════════════════════════════════
    // 1) ملخص فواتير الشراء لفترة (ممكن بفلتر عميل مرتبط)
    // ════════════════════════════════════════════════════════════
    private static async Task<ZakiToolOutcome> PurchaseSummaryAsync(
        db24804Context db, ZakiInvoiceScope scope, System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var today = DateTime.Today;
        var (start, label) = PeriodRange(ReadString(args, "period", "month"), today);

        var customerSearch = ReadRawString(args, "customer_name");
        var (customerIds, error) = await CustomerFilterAsync(db, customerSearch, ct);
        if (error != null) return error;

        var purchases = PurchaseQuery(db, scope, customerIds).FromDate(start);
        var count = await purchases.CountAsync(ct);
        var total = await purchases.SumAsync(t => (decimal?)t.GrandTotal, ct) ?? 0m;
        var paid = await purchases.SumAsync(t => (decimal?)t.PaidAmount, ct) ?? 0m;

        var overdue = purchases.Where(t => t.GrandTotal > t.PaidAmount && t.DueDate.HasValue && t.DueDate.Value < today);
        var overdueCount = await overdue.CountAsync(ct);
        var overdueRemaining = await overdue.SumAsync(t => (decimal?)(t.GrandTotal - t.PaidAmount), ct) ?? 0m;

        var average = count > 0 ? total / count : 0m;

        var headers = new[] { "المؤشر", "القيمة" };
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "عدد فواتير الشراء", count.ToString(Culture) },
            new[] { "إجمالي المشتريات", Money(total) },
            new[] { "المدفوع من فواتير الفترة", Money(paid) },
            new[] { "المتبقي علينا للمورد", Money(total - paid) },
            new[] { "متوسط قيمة الفاتورة", Money(average) },
            new[] { "فواتير متأخرة السداد", overdueCount.ToString(Culture) },
            new[] { "المتأخر المتبقي", Money(overdueRemaining) }
        };

        // المرتجعات بتتعرض لو مفيش فلتر عميل (مفيش ربط واضح بالعميل في المرتجع)
        if (customerIds is null)
        {
            var returns = Base(db, scope, TransactionTypes.PurchaseReturn).FromDate(start);
            var returnsCount = await returns.CountAsync(ct);
            var returnsTotal = await returns.SumAsync(t => (decimal?)t.GrandTotal, ct) ?? 0m;

            rows.Add(new[] { "مرتجعات الشراء (عدد)", returnsCount.ToString(Culture) });
            rows.Add(new[] { "مرتجعات الشراء (قيمة)", Money(returnsTotal) });
        }

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفترة"] = label,
            ["فلتر العميل المرتبط"] = customerSearch.Length == 0 ? "كل العملاء" : customerSearch,
            ["ملخص فواتير الشراء"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, new ZakiTable("ملخص فواتير الشراء — " + label, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 2) فواتير شراء عليها متبقي (ممكن بفلتر عميل مرتبط أو المتأخرة بس)
    // ════════════════════════════════════════════════════════════
    private static async Task<ZakiToolOutcome> OpenPurchasesAsync(
        db24804Context db, ZakiInvoiceScope scope, System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var limit = ReadInt(args, "limit", 15, 1, 20);
        var overdueOnly = ReadBool(args, "overdue_only", false);
        var today = DateTime.Today;

        var (customerIds, error) = await CustomerFilterAsync(db, ReadRawString(args, "customer_name"), ct);
        if (error != null) return error;

        var query = PurchaseQuery(db, scope, customerIds).Where(t => t.GrandTotal > t.PaidAmount);
        if (overdueOnly)
            query = query.Where(t => t.DueDate.HasValue && t.DueDate.Value < today);

        var total = await query.CountAsync(ct);
        var remainingSum = await query.SumAsync(t => (decimal?)(t.GrandTotal - t.PaidAmount), ct) ?? 0m;

        var items = await query
            .OrderBy(t => t.DueDate == null)
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.TransactionDate)
            .Take(limit)
            .Select(t => new PurchaseRow(
                t.ReferenceNumber, t.TransactionDate, t.DueDate,
                t.GrandTotal, t.PaidAmount, t.EmpId))
            .ToListAsync(ct);

        var customerNames = await PartyNamesAsync(db, EmpIds(items.Select(i => i.CustomerId)), ct);

        var headers = new[]
        {
            "رقم الفاتورة", "التاريخ", "العميل المرتبط", "الإجمالي", "المدفوع", "المتبقي", "تاريخ الاستحقاق", "الحالة"
        };

        var rows = items.Select(i => (IReadOnlyList<string>)new[]
        {
            i.ReferenceNumber ?? "—",
            Date(i.TransactionDate),
            NameOf(i.CustomerId, customerNames),
            Money(i.GrandTotal),
            Money(i.PaidAmount),
            Money(i.GrandTotal - i.PaidAmount),
            Date(i.DueDate),
            InvoiceState(i.GrandTotal, i.PaidAmount, i.DueDate, today)
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["عدد فواتير الشراء المفتوحة"] = total,
            ["إجمالي المتبقي علينا للمورد"] = Money(remainingSum),
            ["عدد المعروض"] = rows.Count,
            ["الفواتير"] = ToObjects(headers, rows)
        });

        var title = overdueOnly ? "فواتير شراء متأخرة السداد" : "فواتير شراء عليها متبقي";
        return new ZakiToolOutcome(json, rows.Count == 0 ? null : new ZakiTable(title, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 3) تفاصيل فاتورة شراء برقمها (الأصناف + الدفعات + العميل المرتبط)
    // ════════════════════════════════════════════════════════════
    private static async Task<ZakiToolOutcome> PurchaseDetailsAsync(
        db24804Context db, ZakiInvoiceScope scope, System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var number = ReadRawString(args, "invoice_number");
        if (number.Length == 0)
            return Message("اكتب رقم فاتورة الشراء اللي عايز تفاصيلها.");

        var matches = await Base(db, scope, TransactionTypes.Purchase, includeCancelled: true)
            .Where(t => t.ReferenceNumber != null && t.ReferenceNumber.Contains(number))
            .OrderByDescending(t => t.TransactionDate)
            .Take(10)
            .Select(t => new PurchaseHeader(
                t.TransactionId, t.ReferenceNumber, t.TransactionDate, t.DueDate,
                t.GrandTotal, t.PaidAmount, t.DiscountAmount, t.PaymentMethod, t.InvoiceStatus,
                t.ReferenceType, t.EmpId, t.WarehouseId))
            .ToListAsync(ct);

        if (matches.Count == 0)
            return Message("مالقيتش فاتورة شراء بالرقم ده.");

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

        var customerNames = await PartyNamesAsync(db, EmpIds(new[] { inv.CustomerId }), ct);
        var warehouseName = await db.Warehouses.AsNoTracking()
            .Where(w => w.WarehouseId == inv.WarehouseId)
            .Select(w => w.WarehouseName)
            .FirstOrDefaultAsync(ct) ?? "—";

        // فاتورة مرآة: نوضح الفاتورة اللي اتعملت عشانها (لو الموظف يقدر يشوفها)
        string? linkedSale = null;
        if (inv.ReferenceType != null && inv.ReferenceType.StartsWith(MirrorPrefix)
            && int.TryParse(inv.ReferenceType.Substring(MirrorPrefix.Length), out var saleId))
        {
            var saleRef = await Base(db, scope, TransactionTypes.Sale, includeCancelled: true)
                .Where(t => t.TransactionId == saleId)
                .Select(t => t.ReferenceNumber)
                .FirstOrDefaultAsync(ct);
            linkedSale = saleRef ?? "فاتورة بيع مرتبطة (مش ظاهرة في صلاحياتك)";
        }

        var remaining = inv.GrandTotal - inv.PaidAmount;
        var state = inv.InvoiceStatus == CancelledStatus
            ? "ملغاة"
            : InvoiceState(inv.GrandTotal, inv.PaidAmount, inv.DueDate, today);

        var refNo = inv.ReferenceNumber ?? "—";
        var customerName = NameOf(inv.CustomerId, customerNames);

        var headerInfo = new Dictionary<string, object?>
        {
            ["رقم الفاتورة"] = refNo,
            ["التاريخ"] = Date(inv.TransactionDate),
            ["العميل المرتبط"] = customerName,
            ["المخزن"] = warehouseName,
            ["الإجمالي"] = Money(inv.GrandTotal),
            ["الخصم"] = Money(inv.DiscountAmount ?? 0m),
            ["المدفوع"] = Money(inv.PaidAmount),
            ["المتبقي"] = Money(remaining),
            ["الحالة"] = state,
            ["طريقة الدفع"] = MethodLabel(inv.PaymentMethod),
            ["تاريخ الاستحقاق"] = Date(inv.DueDate),
            ["فاتورة البيع المرتبطة"] = linkedSale ?? "—",
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

        var title = "أصناف فاتورة الشراء " + refNo + " — العميل المرتبط: " + customerName
                    + " — الإجمالي: " + Money(inv.GrandTotal) + " — المتبقي: " + Money(remaining);

        return new ZakiToolOutcome(json, rows.Count == 0 ? null : new ZakiTable(title, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 4) المدفوعات: المقبوضات من العملاء والمدفوعات للموردين
    //    (كل جزء حسب صلاحيته زي شاشة المدفوعات)
    // ════════════════════════════════════════════════════════════
    private static async Task<ZakiToolOutcome> PaymentsAsync(
        db24804Context db, ClaimsPrincipal user, System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var showReceipts = CanViewReceipts(user);
        var showPayouts = CanViewPurchasePayments(user);

        var (start, label) = PeriodRange(ReadString(args, "period", "month"), DateTime.Today);
        var startValue = start ?? MinDate;

        // الدفعات بتاريخ الدفع نفسه (تدفق نقدي)، والفواتير الملغاة مستبعدة
        var groups = await (
            from p in db.Payments.AsNoTracking()
            join t in db.Transactions.AsNoTracking() on p.TransactionId equals t.TransactionId
            where t.InvoiceStatus != CancelledStatus
                  && p.PaymentDate >= startValue
                  && ((showReceipts && t.TransactionType == TransactionTypes.Sale)
                      || (showPayouts && t.TransactionType == TransactionTypes.Purchase))
            group p by new { t.TransactionType, p.PaymentMethod } into g
            select new PaymentGroup(g.Key.TransactionType, g.Key.PaymentMethod, g.Count(), g.Sum(x => x.Amount))
        ).ToListAsync(ct);

        var receipts = groups.Where(g => g.TxType == TransactionTypes.Sale).Sum(g => g.Total);
        var payouts = groups.Where(g => g.TxType == TransactionTypes.Purchase).Sum(g => g.Total);

        var headers = new[] { "النوع", "طريقة الدفع", "عدد الدفعات", "الإجمالي" };

        var rows = groups
            .OrderBy(g => g.TxType == TransactionTypes.Sale ? 0 : 1)
            .ThenByDescending(g => g.Total)
            .Select(g => (IReadOnlyList<string>)new[]
            {
                g.TxType == TransactionTypes.Sale ? "مقبوضات من العملاء" : "مدفوعات للموردين",
                MethodLabel(g.Method),
                g.Count.ToString(Culture),
                Money(g.Total)
            }).ToList();

        var json = new Dictionary<string, object?> { ["الفترة"] = label };

        if (showReceipts)
            json["إجمالي المقبوضات من العملاء"] = Money(receipts);
        if (showPayouts)
            json["إجمالي المدفوع للموردين"] = Money(payouts);
        if (showReceipts && showPayouts)
            json["الفرق (مقبوضات - مدفوعات)"] = Money(receipts - payouts);

        json["التفصيل"] = ToObjects(headers, rows);

        return new ZakiToolOutcome(ToJson(json),
            rows.Count == 0 ? null : new ZakiTable("المقبوضات والمدفوعات — " + label, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // مساعدات
    // ════════════════════════════════════════════════════════════

    /// <summary>فواتير الشراء (ممكن فلترة بالعميل المرتبط)</summary>
    private static IQueryable<Transaction> PurchaseQuery(
        db24804Context db, ZakiInvoiceScope scope, List<int>? customerIds)
    {
        var q = Base(db, scope, TransactionTypes.Purchase);
        if (customerIds != null)
            q = q.Where(t => t.EmpId.HasValue && customerIds.Contains(t.EmpId.Value));
        return q;
    }

    /// <summary>
    /// فلتر العميل المرتبط بالاسم (أو جزء منه). بيرجّع null لو مفيش فلتر.
    /// لو الاسم بيطابق أكتر من عميل من غير تطابق كامل، بيرجّع رسالة تطلب الاسم بالكامل.
    /// </summary>
    private static async Task<(List<int>? Ids, ZakiToolOutcome? Error)> CustomerFilterAsync(
        db24804Context db, string search, CancellationToken ct)
    {
        if (search.Length == 0)
            return (null, null);

        var parties = await db.Parties
            .AsNoTracking()
            .Select(p => new PartyLite(p.PartyId, p.PartyName))
            .ToListAsync(ct);

        var matched = parties.Where(p => (p.Name ?? "").ContainsArabic(search)).ToList();
        if (matched.Count == 0)
            return (null, Message("مالقيتش عميل بالاسم ده."));

        var exact = matched.Where(p => string.Equals(p.Name, search, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 0 && matched.Count > 1)
        {
            return (null, new ZakiToolOutcome(ToJson(new Dictionary<string, object?>
            {
                ["ملاحظة"] = "في أكتر من عميل بالاسم ده. اطلب من المستخدم يكتب الاسم بالكامل.",
                ["العملاء المطابقين"] = matched.Take(8).Select(p => p.Name).ToList()
            }), null));
        }

        var chosen = exact.Count > 0 ? exact : matched;
        return (chosen.Select(p => p.Id).ToList(), null);
    }

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

    private static AiToolDefinition PurchaseSummaryDefinition() => new(
        GetPurchaseInvoicesSummary,
        "ملخص فواتير الشراء لفترة: الإجمالي والمدفوع والمتبقي علينا للمورد والمتأخر. ممكن فلترة بالعميل المرتبط بالفاتورة.",
        new
        {
            type = "object",
            properties = new
            {
                period = PeriodSchema("الافتراضي month"),
                customer_name = new { type = "string", description = "اسم العميل المرتبط (اختياري)" }
            }
        });

    private static AiToolDefinition OpenPurchasesDefinition() => new(
        GetOpenPurchaseInvoices,
        "فواتير الشراء اللي عليها متبقي (مستحق علينا للمورد). overdue_only=true للمتأخرة بس. ممكن فلترة بالعميل المرتبط.",
        new
        {
            type = "object",
            properties = new
            {
                limit = new { type = "integer", minimum = 1, maximum = 20 },
                overdue_only = new { type = "boolean" },
                customer_name = new { type = "string", description = "اسم العميل المرتبط (اختياري)" }
            }
        });

    private static AiToolDefinition PurchaseDetailsDefinition() => new(
        GetPurchaseInvoiceDetails,
        "تفاصيل فاتورة شراء برقمها: الأصناف والدفعات والمتبقي والعميل المرتبط وفاتورة البيع اللي اتعملت عشانها.",
        new
        {
            type = "object",
            properties = new
            {
                invoice_number = new { type = "string", description = "رقم فاتورة الشراء أو جزء منه" }
            },
            required = new[] { "invoice_number" }
        });

    private static AiToolDefinition PaymentsDefinition() => new(
        GetPaymentsSummary,
        "المدفوعات في الفترة: المقبوضات من العملاء والمدفوعات للموردين، مفصّلة بطريقة الدفع.",
        new
        {
            type = "object",
            properties = new { period = PeriodSchema("الافتراضي month") }
        });

    // ════════════════════════════════════════════════════════════
    // أنواع داخلية
    // ════════════════════════════════════════════════════════════
    private sealed record PartyLite(int Id, string Name);

    private sealed record PurchaseRow(
        string? ReferenceNumber, DateTime TransactionDate, DateTime? DueDate,
        decimal GrandTotal, decimal PaidAmount, int? CustomerId);

    private sealed record PurchaseHeader(
        int TransactionId, string? ReferenceNumber, DateTime TransactionDate, DateTime? DueDate,
        decimal GrandTotal, decimal PaidAmount, decimal? DiscountAmount, string? PaymentMethod,
        string? InvoiceStatus, string? ReferenceType, int? CustomerId, int WarehouseId);

    private sealed record ItemRow(string ProductName, decimal Quantity, decimal UnitPrice, decimal Total);

    private sealed record PaymentRow(DateTime PaymentDate, decimal Amount, string? PaymentMethod);

    private sealed record PaymentGroup(string TxType, string? Method, int Count, decimal Total);
}
