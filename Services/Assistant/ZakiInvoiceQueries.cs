using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using COCOBOLOERPNEW.Services;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// نطاق المستخدم في أدوات الفواتير:
/// - AccessFrom: تاريخ الاطلاع (CrmAccessFrom) زي شاشة الفواتير.
/// - EmployeeId: لو البائع (Sales) بيشوف مستنداته هو بس. null = مفيش فلتر بائع.
/// - ProtectedCreators: مديرو الحسابات اللي فواتيرهم مخفية عن غير المسموح لهم (SalesInvoiceAccess).
/// </summary>
/// BranchId: فلتر فرع اختياري (الفاتورة ↔ المخزن ↔ Warehouse.BranchId). null = كل الفروع.
public sealed record ZakiInvoiceScope(
    DateTime? AccessFrom, int? EmployeeId, List<string> ProtectedCreators, int? BranchId = null);

/// <summary>
/// أساسيات مشتركة لأدوات الفواتير (المبيعات والمشتريات والمدفوعات).
/// كل قواعد الشاشات بتتطبق هنا في مكان واحد:
/// - الفواتير الملغاة مابتظهرش (إلا لو طلبناها صراحة).
/// - فواتير الشراء المرآة (MirrorOf:) مابتتحسبش في المشتريات (دي فواتير داخلية مرتبطة ببيع).
/// - تاريخ الاطلاع، وفواتير مديري الحسابات المخفية، والبائع يشوف بياناته هو بس.
/// </summary>
public static class ZakiInvoiceQueries
{
    public const string CancelledStatus = "Cancelled";
    public const string MirrorPrefix = "MirrorOf:";
    public const string PeriodAll = "all";

    public static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>يبني نطاق المستخدم (بيحمّل أسماء مديري الحسابات المخفيين لو لازم)</summary>
    public static async Task<ZakiInvoiceScope> BuildScopeAsync(
        db24804Context db, ClaimsPrincipal user, int? employeeId)
    {
        var protectedCreators = SalesInvoiceAccess.CanViewAccountManagerInvoices(user)
            ? new List<string>()
            : await SalesInvoiceAccess.GetProtectedCreatorUsernamesAsync(db);

        return new ZakiInvoiceScope(user.GetAccessFromDate(), employeeId, protectedCreators);
    }

    /// <summary>مستندات نوع معين داخل النطاق المسموح (الملغاة مستبعدة إلا لو طلبناها)</summary>
    public static IQueryable<Transaction> Base(
        db24804Context db, ZakiInvoiceScope scope, string txType, bool includeCancelled = false)
    {
        var q = db.Transactions.AsNoTracking().Where(t => t.TransactionType == txType);

        if (!includeCancelled)
            q = q.Where(t => t.InvoiceStatus == null || t.InvoiceStatus != CancelledStatus);

        if (scope.AccessFrom.HasValue)
            q = q.Where(t => t.TransactionDate >= scope.AccessFrom.Value);

        // فلتر الفرع: الفاتورة بتتربط بالفرع من خلال المخزن (Warehouse.BranchId)
        if (scope.BranchId.HasValue)
        {
            var branchWarehouses = db.Warehouses
                .AsNoTracking()
                .Where(w => w.BranchId == scope.BranchId.Value)
                .Select(w => w.WarehouseId);

            q = q.Where(t => branchWarehouses.Contains(t.WarehouseId));
        }

        // زي شاشة فواتير البيع: فواتير مديري الحسابات مخفية عن غير المسموح لهم
        if ((txType == TransactionTypes.Sale || txType == TransactionTypes.SaleReturn)
            && scope.ProtectedCreators.Count > 0)
        {
            q = q.Where(t => !scope.ProtectedCreators.Contains(t.CreatedBy));
        }

        // فواتير الشراء المرآة (MirrorOf:) داخلة في الأرقام زي شاشة فواتير الشراء

        return q;
    }

    // ════════════════════════════════════════════════════════════
    // الفروع (نفس منطق ZakiFinancialStatementsService: اسم الفرع / «فرعي» / تفضيل مش تقييد)
    // ════════════════════════════════════════════════════════════
    private sealed record BranchLite(int Id, string Name);

    /// <summary>
    /// يحوّل اسم الفرع لرقم. فاضي = كل الفروع. «فرعي» = فرع المستخدم.
    /// لو الاسم مش موجود أو أكتر من فرع بيطابقه بيرجع رسالة للموديل.
    /// </summary>
    public static async Task<(int? Id, string? Name, ZakiToolOutcome? Error)> ResolveBranchAsync(
        db24804Context db, ClaimsPrincipal user, string search, CancellationToken ct)
    {
        if (search.Length == 0)
            return (null, null, null);

        if (search.NormalizeArabic() == "فرعي".NormalizeArabic())
        {
            var myId = await MyBranchIdAsync(db, user, ct);
            if (!myId.HasValue)
                return (null, null, Message("حسابك مش مربوط بفرع. اكتب اسم الفرع اللي عايزه."));

            var myName = await db.Branches.AsNoTracking()
                .Where(b => b.BranchId == myId.Value)
                .Select(b => b.BranchNameAr)
                .FirstOrDefaultAsync(ct);

            return (myId, myName ?? "فرعي", null);
        }

        var branches = await db.Branches
            .AsNoTracking()
            .Select(b => new BranchLite(b.BranchId, b.BranchNameAr))
            .ToListAsync(ct);

        var matched = branches.Where(b => (b.Name ?? "").ContainsArabic(search)).ToList();

        if (matched.Count == 0)
            return (null, null, Message("مالقيتش فرع بالاسم ده."));

        if (matched.Count > 1)
        {
            return (null, null, new ZakiToolOutcome(ToJson(new Dictionary<string, object?>
            {
                ["ملاحظة"] = "في أكتر من فرع بالاسم ده. اطلب من المستخدم يكتب اسم الفرع بالكامل.",
                ["الفروع المطابقة"] = matched.Select(m => m.Name).ToList()
            }), null));
        }

        return (matched[0].Id, matched[0].Name, null);
    }

    /// <summary>فرع المستخدم: الفرع الافتراضي في الحساب، ولو فاضي فرع الموظف المربوط بيه</summary>
    public static async Task<int?> MyBranchIdAsync(db24804Context db, ClaimsPrincipal user, CancellationToken ct)
    {
        var username = user.Identity?.Name ?? "";
        if (username.Length == 0) return null;

        var account = await db.Users
            .AsNoTracking()
            .Where(u => u.Username == username)
            .Select(u => new { u.DefaultBranchId, u.EmployeeId })
            .FirstOrDefaultAsync(ct);

        if (account == null) return null;
        if (account.DefaultBranchId.HasValue) return account.DefaultBranchId;
        if (!account.EmployeeId.HasValue) return null;

        return await db.Employees
            .AsNoTracking()
            .Where(e => e.EmployeeId == account.EmployeeId.Value)
            .Select(e => e.BranchId)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>فلتر الفترة من تاريخ البداية (null = كل الفترات)</summary>
    public static IQueryable<Transaction> FromDate(this IQueryable<Transaction> q, DateTime? start) =>
        start.HasValue ? q.Where(t => t.TransactionDate >= start.Value) : q;

    /// <summary>الفترة: today / week / month / year / all. الافتراضي الشهر ده</summary>
    public static (DateTime? Start, string Label) PeriodRange(string period, DateTime today)
    {
        switch (period)
        {
            case "today": return (today, "اليوم");
            case "week": return (today.AddDays(-6), "آخر 7 أيام");
            case "year": return (new DateTime(today.Year, 1, 1), "السنة دي");
            case PeriodAll: return (null, "كل الفترات");
            default: return (new DateTime(today.Year, today.Month, 1), "الشهر ده");
        }
    }

    /// <summary>
    /// نفس المدة من الفترة اللي قبلها بنفس عدد الأيام (للمقارنة). null لو الفترة all أو مش معروفة.
    /// - today: أمس بس. week: الـ 7 أيام اللي قبلها. month: نفس عدد الأيام من الشهر اللي فات.
    /// - year: نفس عدد الأيام من السنة اللي فاتت.
    /// </summary>
    public static (DateTime Start, DateTime End, string Label)? PreviousPeriodRange(string period, DateTime today)
    {
        switch (period)
        {
            case "today":
                return (today.AddDays(-1), today, "نفس اليوم اللي فات");
            case "week":
                return (today.AddDays(-13), today.AddDays(-6), "الـ 7 أيام اللي قبلها");
            case "month":
            {
                var first = new DateTime(today.Year, today.Month, 1);
                var prevFirst = first.AddMonths(-1);
                var prevEnd = prevFirst.AddDays(today.Day);
                if (prevEnd > first) prevEnd = first;
                return (prevFirst, prevEnd, "نفس المدة من الشهر اللي فات");
            }
            case "year":
            {
                var yearStart = new DateTime(today.Year, 1, 1);
                var prevStart = yearStart.AddYears(-1);
                var prevEnd = prevStart.AddDays(today.DayOfYear);
                if (prevEnd > yearStart) prevEnd = yearStart;
                return (prevStart, prevEnd, "نفس المدة من السنة اللي فاتت");
            }
            default:
                return null;
        }
    }

    /// <summary>نسبة التغيير بين الحالي والسابق (+/-). لو السابق صفر مابنحسبش.</summary>
    public static string ChangePercent(decimal current, decimal previous) =>
        previous == 0m
            ? "—"
            : ((current - previous) / previous * 100m).ToString("+0.##;-0.##;0", Culture) + "%";

    /// <summary>حالة الفاتورة زي الشاشة: مسددة / متأخرة / مفتوحة</summary>
    public static string InvoiceState(decimal total, decimal paid, DateTime? dueDate, DateTime today)
    {
        if (paid >= total) return "مسددة";
        if (dueDate.HasValue && dueDate.Value < today) return "متأخرة";
        return "مفتوحة";
    }

    // ════════════════════════════════════════════════════════════
    // أسماء العملاء والموظفين والمنتجات (بدون أرقام تليفونات)
    // ════════════════════════════════════════════════════════════
    public static async Task<Dictionary<int, string>> PartyNamesAsync(
        db24804Context db, IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, string>();

        return await db.Parties
            .AsNoTracking()
            .Where(p => list.Contains(p.PartyId))
            .ToDictionaryAsync(p => p.PartyId, p => p.PartyName, ct);
    }

    public static async Task<Dictionary<int, string>> EmployeeNamesAsync(
        db24804Context db, IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, string>();

        return await db.Employees
            .AsNoTracking()
            .Where(e => list.Contains(e.EmployeeId))
            .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName ?? "—", ct);
    }

    public static async Task<Dictionary<int, string>> ProductNamesAsync(
        db24804Context db, IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, string>();

        return await db.Products
            .AsNoTracking()
            .Where(p => list.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId, p => p.ProductName ?? "—", ct);
    }

    public static string NameOf(int? id, IReadOnlyDictionary<int, string> names) =>
        id.HasValue && names.TryGetValue(id.Value, out var n) ? n : "—";

    public static string MethodLabel(string? method) =>
        string.IsNullOrWhiteSpace(method) ? "غير محدد" : PaymentMethods.All.GetValueOrDefault(method, method);

    // ════════════════════════════════════════════════════════════
    // تنسيق
    // ════════════════════════════════════════════════════════════
    public static string Money(decimal value) => value.ToString("#,0", Culture) + " ج.م";

    public static string Date(DateTime? value) =>
        value.HasValue ? value.Value.ToString("yyyy-MM-dd", Culture) : "—";

    public static ZakiToolOutcome Message(string text) =>
        new(ToJson(new Dictionary<string, object?> { ["ملاحظة"] = text }), null);

    public static ZakiToolOutcome Denied() =>
        new(ToJson(new Dictionary<string, object?> { ["خطأ"] = "ليس لديك صلاحية لهذه البيانات." }), null, IsDenied: true);

    public static List<Dictionary<string, object?>> ToObjects(string[] headers, IEnumerable<IReadOnlyList<string>> rows) =>
        rows.Select(r =>
        {
            var d = new Dictionary<string, object?>();
            for (var i = 0; i < headers.Length && i < r.Count; i++)
                d[headers[i]] = r[i];
            return d;
        }).ToList();

    /// <summary>أول صف من الجدول كـ Object (عشان الموديل يعرف الأعلى بسهولة)</summary>
    public static object? FirstRow(string[] headers, List<IReadOnlyList<string>> rows) =>
        rows.Count > 0 ? ToObjects(headers, rows.Take(1)).First() : null;

    public static string ToJson(object value) => JsonSerializer.Serialize(value, JsonOptions);

    // ════════════════════════════════════════════════════════════
    // قراءة الـ Arguments
    // ════════════════════════════════════════════════════════════
    public static JsonElement ParseArgs(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public static int ReadInt(JsonElement args, string name, int fallback, int min, int max)
    {
        if (args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt32(out var n))
        {
            return Math.Clamp(n, min, max);
        }
        return fallback;
    }

    public static bool ReadBool(JsonElement args, string name, bool fallback)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v))
        {
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
        }
        return fallback;
    }

    /// <summary>نص بحروف صغيرة (للقيم المغلقة زي period و by)</summary>
    public static string ReadString(JsonElement args, string name, string fallback)
    {
        var raw = ReadRawString(args, name);
        return raw.Length == 0 ? fallback : raw.ToLowerInvariant();
    }

    /// <summary>نص زي ما هو (لأرقام الفواتير وأسماء العملاء)</summary>
    public static string ReadRawString(JsonElement args, string name)
    {
        if (args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String)
        {
            return (v.GetString() ?? "").Trim();
        }
        return "";
    }

    // ════════════════════════════════════════════════════════════
    // الصلاحيات (نفس شروط الشاشات الفعلية في المشروع)
    // ════════════════════════════════════════════════════════════

    /// <summary>فواتير البيع: شاشة الفواتير (frm_PartiesInvoices:View)</summary>
    public static bool CanViewSalesInvoices(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("Permission", "frm_PartiesInvoices:View");

    /// <summary>فواتير الشراء: شاشة فواتير الشراء (/factory/purchases)</summary>
    public static bool CanViewPurchaseInvoices(ClaimsPrincipal user) =>
        user.IsInRole("Admin")
        || user.HasClaim("Permission", "frm_SupplierInvoices:View")
        || user.IsInRole("factory");

    /// <summary>حالة التسليم: نفس بند القائمة الجانبية (frmInvoiceDeliveryStatus:View)</summary>
    public static bool CanViewDeliveryStatus(ClaimsPrincipal user) =>
        DeliveryPermissions.CanView(user);

    /// <summary>عروض الأسعار: شاشة عروض الأسعار (frmQuotationsList:View)</summary>
    public static bool CanViewQuotations(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("Permission", "frmQuotationsList:View");

    /// <summary>مقبوضات العملاء: شاشة المدفوعات (frm_Payments:View)</summary>
    public static bool CanViewReceipts(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("Permission", "frm_Payments:View");

    /// <summary>مدفوعات الموردين: شاشة المدفوعات بتقصرها على الأدوار دي</summary>
    public static bool CanViewPurchasePayments(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.IsInRole("AccountManager") || user.IsInRole("Account");
}
