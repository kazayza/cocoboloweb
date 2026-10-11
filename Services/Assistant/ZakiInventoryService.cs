using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// أدوات المخزون في زكي.
/// الصلاحيات (زي الشاشات):
/// - رصيد الأصناف: frm_ProductList:View (البائع يشوف الكمية في كل مخزن).
/// - حركات المخزون: frm_StockTransactions:View.
/// - قيمة المخزون (بالتكلفة): Admin ومدير الحسابات بس (بيانات مالية).
/// مفيش حد أدنى للمخزون في الدفعة دي.
/// </summary>
public class ZakiInventoryService
{
    public const string GetProductStock = "get_product_stock";
    public const string GetStockMovements = "get_stock_movements";
    public const string GetStockValue = "get_stock_value";

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IStockService _stock;
    private readonly ILogger<ZakiInventoryService> _logger;

    public ZakiInventoryService(IStockService stock, ILogger<ZakiInventoryService> logger)
    {
        _stock = stock;
        _logger = logger;
    }

    public static bool Handles(string toolName) =>
        toolName is GetProductStock or GetStockMovements or GetStockValue;

    /// <summary>الأدوات اللي المستخدم مسموح بيها</summary>
    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user)
    {
        var list = new List<AiToolDefinition>();

        if (CanViewProducts(user)) list.Add(ProductStockDefinition());
        if (CanViewMovements(user)) list.Add(MovementsDefinition());
        if (CanViewValue(user)) list.Add(ValueDefinition());

        return list;
    }

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string name, string argumentsJson, CancellationToken ct = default)
    {
        var allowed = DefinitionsFor(user).Any(d => d.Name == name);
        if (!allowed)
        {
            return new ZakiToolOutcome(
                ToJson(new Dictionary<string, object?> { ["خطأ"] = "ليس لديك صلاحية لهذه البيانات." }),
                null,
                IsDenied: true);
        }

        var args = ParseArgs(argumentsJson);

        return name switch
        {
            GetProductStock => await ProductStockAsync(args, ct),
            GetStockMovements => await MovementsAsync(args, ct),
            GetStockValue => await StockValueAsync(ct),
            _ => Note("أداة غير معروفة.")
        };
    }

    // ════════════════════════════════════════════════════════════
    // 1) رصيد الصنف في كل مخزن
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> ProductStockAsync(JsonElement args, CancellationToken ct)
    {
        var name = (ReadString(args, "productName") ?? "").Trim();
        if (name.Length < 2)
            return Note("اكتب اسم الصنف (حرفين على الأقل).");

        var products = await _stock.SearchProductsAsync(name, take: 5);
        if (products.Count == 0)
            return Note("مفيش صنف بالاسم ده.");

        var warehouses = await _stock.GetWarehousesAsync();

        var headers = new[] { "الصنف", "المخزن", "الفرع", "الكمية المتاحة" };
        var rows = new List<IReadOnlyList<string>>();

        foreach (var p in products.Take(3))
        {
            foreach (var w in warehouses)
            {
                var qty = await _stock.GetCurrentStockAsync(p.ProductId, w.WarehouseId);
                rows.Add(new[]
                {
                    p.ProductName,
                    w.WarehouseName,
                    w.BranchNameAr ?? "—",
                    qty.ToString(Culture)
                });
            }
        }

        var totals = products.Take(3).Select(p => new Dictionary<string, object?>
        {
            ["الصنف"] = p.ProductName,
            ["النوع"] = p.IsShowroomProduct ? "صالة عرض" : "مخصص لعميل"
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["عدد الأصناف المطابقة"] = products.Count,
            ["الأصناف"] = totals,
            ["الأرصدة"] = ToObjects(headers, rows)
        });

        var title = "رصيد الصنف";
        return new ZakiToolOutcome(json, rows.Count == 0 ? null : new ZakiTable(title, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 2) آخر حركات المخزون
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> MovementsAsync(JsonElement args, CancellationToken ct)
    {
        var limit = ReadInt(args, "limit", 20, 1, 50);
        var name = (ReadString(args, "productName") ?? "").Trim();

        var filter = new StockTransactionFilterDto
        {
            DateFrom = DateTime.Today.AddDays(-60)
        };

        string scopeText = "آخر 60 يوم";

        if (name.Length >= 2)
        {
            var products = await _stock.SearchProductsAsync(name, take: 1);
            if (products.Count == 0)
                return Note("مفيش صنف بالاسم ده.");

            filter.ProductId = products[0].ProductId;
            scopeText = products[0].ProductName;
        }
        else
        {
            filter.DateFrom = DateTime.Today.AddDays(-7);
            scopeText = "آخر 7 أيام";
        }

        var list = await _stock.GetStockTransactionsAsync(filter);
        var shown = list
            .OrderByDescending(t => t.TransactionDate)
            .Take(limit)
            .ToList();

        var headers = new[] { "التاريخ", "الصنف", "المخزن", "نوع الحركة", "الكمية", "المرجع" };

        var rows = shown.Select(t => (IReadOnlyList<string>)new[]
        {
            Date(t.TransactionDate),
            t.ProductName,
            t.WarehouseName,
            t.TransactionType ?? "—",
            t.Quantity.ToString(Culture),
            t.ReferenceType ?? "—"
        }).ToList();

        var title = "حركات المخزون — " + scopeText;

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفترة"] = scopeText,
            ["عدد الحركات المعروضة"] = rows.Count,
            ["الحركات"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, rows.Count == 0 ? null : new ZakiTable(title, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 3) قيمة المخزون (Admin ومدير الحسابات)
    // ════════════════════════════════════════════════════════════
    private async Task<ZakiToolOutcome> StockValueAsync(CancellationToken ct)
    {
        var (_, s) = await _stock.GetStockValueAsync(null, null, null);

        var headers = new[] { "المؤشر", "القيمة" };
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "عدد الأصناف", s.TotalProducts.ToString(Culture) },
            new[] { "إجمالي الكمية", s.TotalQuantity.ToString(Culture) },
            new[] { "عدد المخازن", s.WarehousesCount.ToString(Culture) },
            new[] { "إجمالي قيمة المخزون بالتكلفة", Money(s.TotalCostValue) },
            new[] { "إجمالي قيمة البيع المتوقعة", Money(s.TotalSaleValue) },
            new[] { "الربح المتوقع", Money(s.TotalExpectedProfit) }
        };

        var json = ToJson(new Dictionary<string, object?>
        {
            ["قيمة المخزون"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, new ZakiTable("قيمة المخزون", headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // الصلاحيات
    // ════════════════════════════════════════════════════════════
    private static bool CanViewProducts(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("Permission", "frm_ProductList:View");

    private static bool CanViewMovements(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("Permission", "frm_StockTransactions:View");

    private static bool CanViewValue(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.IsInRole("AccountManager");

    // ════════════════════════════════════════════════════════════
    // تعريفات الأدوات
    // ════════════════════════════════════════════════════════════
    private static AiToolDefinition ProductStockDefinition() => new(
        GetProductStock,
        "ترجع الكمية المتاحة من صنف معين في كل مخزن وفرع. استخدمها لأي سؤال عن توفر صنف أو كميته أو رصيده.",
        new
        {
            type = "object",
            properties = new
            {
                productName = new { type = "string", description = "اسم الصنف أو جزء منه (حرفين على الأقل)" }
            },
            required = new[] { "productName" }
        });

    private static AiToolDefinition MovementsDefinition() => new(
        GetStockMovements,
        "ترجع آخر حركات المخزون (إضافة وتحويل وصرف) لصنف معين أو لكل الأصناف. استخدمها لأي سؤال عن حركة صنف أو آخر التحركات.",
        new
        {
            type = "object",
            properties = new
            {
                productName = new { type = "string", description = "اختياري: اسم الصنف" },
                limit = new { type = "integer", description = "عدد الحركات من 1 إلى 50", minimum = 1, maximum = 50 }
            }
        });

    private static AiToolDefinition ValueDefinition() => new(
        GetStockValue,
        "ترجع قيمة المخزون الإجمالية بالتكلفة وقيمة البيع المتوقعة والربح المتوقع. بيانات مالية، استخدمها بس لأسئلة قيمة المخزون.",
        new
        {
            type = "object",
            properties = new { }
        });

    // ════════════════════════════════════════════════════════════
    // مساعدات
    // ════════════════════════════════════════════════════════════
    private static ZakiToolOutcome Note(string message) =>
        new(ToJson(new Dictionary<string, object?> { ["ملاحظة"] = message }), null);

    private static string Money(decimal value) => value.ToString("#,0", Culture) + " ج.م";

    private static string Date(DateTime? value) =>
        value.HasValue ? value.Value.ToString("yyyy-MM-dd", Culture) : "—";

    private static List<Dictionary<string, object?>> ToObjects(string[] headers, IEnumerable<IReadOnlyList<string>> rows) =>
        rows.Select(r =>
        {
            var d = new Dictionary<string, object?>();
            for (var i = 0; i < headers.Length && i < r.Count; i++)
                d[headers[i]] = r[i];
            return d;
        }).ToList();

    private static JsonElement ParseArgs(string json)
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

    private static int ReadInt(JsonElement args, string name, int fallback, int min, int max)
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

    private static string? ReadString(JsonElement args, string name)
    {
        if (args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String)
        {
            return v.GetString();
        }
        return null;
    }

    private static string ToJson(object value) => JsonSerializer.Serialize(value, JsonOptions);
}
