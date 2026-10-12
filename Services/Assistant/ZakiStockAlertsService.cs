using System.Security.Claims;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;
using static COCOBOLOERPNEW.Services.Assistant.ZakiInvoiceQueries;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// أدوات تنبيهات المخزون في زكي:
/// - get_low_stock: الأصناف اللي رصيدها صفر أو تحت حد معين.
///   (مفيش «حد طلب» مخزن لكل صنف في النظام، فالحد بيتبعت في السؤال — الافتراضي صفر.)
/// - get_stock_by_warehouse: توزيع الكميات على المخازن.
/// المصدر: جدول StockLevels (نفس مصدر IStockService.GetCurrentStockAsync).
/// الصلاحيات: نفس أدوات مخزون زكي الحالية (frm_ProductList:View).
/// </summary>
public class ZakiStockAlertsService
{
    public const string GetLowStock = "get_low_stock";
    public const string GetStockByWarehouse = "get_stock_by_warehouse";

    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly ILogger<ZakiStockAlertsService> _logger;

    public ZakiStockAlertsService(
        IDbContextFactory<db24804Context> dbFactory,
        ILogger<ZakiStockAlertsService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public static bool Handles(string toolName) =>
        toolName is GetLowStock or GetStockByWarehouse;

    public static bool CanView(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("Permission", "frm_ProductList:View");

    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user)
    {
        if (!CanView(user)) return Array.Empty<AiToolDefinition>();

        return new[]
        {
            LowStockDefinition(),
            StockByWarehouseDefinition()
        };
    }

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string name, string argumentsJson, CancellationToken ct = default)
    {
        if (!CanView(user)) return Denied();

        var args = ParseArgs(argumentsJson);

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return name == GetLowStock
                ? await LowStockAsync(db, args, ct)
                : await StockByWarehouseAsync(db, args, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki stock alerts: query failed ({Tool})", name);
            return Message("ماقدرتش أجيب بيانات المخزون دلوقتي، حاول تاني بعد شوية.");
        }
    }

    // ── الأصناف اللي رصيدها صفر أو تحت حد ──
    private static async Task<ZakiToolOutcome> LowStockAsync(
        db24804Context db, System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var maxQty = ReadInt(args, "max_quantity", 0, 0, 1000);
        var limit = ReadInt(args, "limit", 30, 1, 100);
        var warehouseSearch = ReadRawString(args, "warehouse_name");

        var levels = db.StockLevels.AsNoTracking();

        if (warehouseSearch.Length > 0)
        {
            var whIds = await db.Warehouses.AsNoTracking()
                .Where(w => w.WarehouseName.Contains(warehouseSearch))
                .Select(w => w.WarehouseId)
                .ToListAsync(ct);

            if (whIds.Count == 0)
                return Message("مالقتش مخزن بالاسم ده.");

            levels = levels.Where(s => whIds.Contains(s.WarehouseId));
        }

        var grouped = levels
            .GroupBy(s => new { s.ProductId, s.Product.ProductName })
            .Select(g => new
            {
                g.Key.ProductName,
                Total = g.Sum(x => x.Quantity),
                Last = g.Max(x => x.LastUpdatedAt)
            })
            .Where(g => g.Total <= maxQty);

        var totalMatches = await grouped.CountAsync(ct);

        var items = await grouped
            .OrderBy(g => g.Total)
            .ThenBy(g => g.ProductName)
            .Take(limit)
            .ToListAsync(ct);

        var title = maxQty == 0 ? "أصناف رصيدها صفر" : $"أصناف رصيدها {maxQty} أو أقل";

        if (items.Count == 0)
            return Message(maxQty == 0
                ? "مفيش أصناف رصيدها صفر — كل المخزون موجود."
                : $"مفيش أصناف رصيدها {maxQty} أو أقل.");

        var headers = new[] { "الصنف", "الرصيد", "آخر تحديث" };
        var rows = items.Select(i => (IReadOnlyList<string>)new[]
        {
            i.ProductName,
            i.Total.ToString(Culture),
            i.Last.HasValue ? i.Last.Value.ToString("yyyy/MM/dd", Culture) : "—"
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["العنوان"] = title,
            ["إجمالي عدد الأصناف المطابقة"] = totalMatches,
            ["المعروض"] = items.Count,
            [title] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, new ZakiTable($"{title} (أول {items.Count} من {totalMatches})", headers, rows));
    }

    // ── توزيع الكميات على المخازن ──
    private static async Task<ZakiToolOutcome> StockByWarehouseAsync(
        db24804Context db, System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var includeInactive = ReadBool(args, "include_inactive", false);

        var q = from s in db.StockLevels.AsNoTracking()
                join w in db.Warehouses.AsNoTracking() on s.WarehouseId equals w.WarehouseId
                where includeInactive || w.IsActive == true
                group s by new { s.WarehouseId, w.WarehouseName } into g
                select new
                {
                    g.Key.WarehouseName,
                    Products = g.Select(x => x.ProductId).Distinct().Count(),
                    ZeroItems = g.Count(x => x.Quantity == 0),
                    TotalQty = g.Sum(x => x.Quantity)
                };

        var list = await q.OrderByDescending(x => x.TotalQty).ToListAsync(ct);

        if (list.Count == 0)
            return Message("مفيش مخازن نشطة فيها أرصدة.");

        var headers = new[] { "المخزن", "عدد الأصناف", "أصناف رصيدها صفر", "إجمالي الكميات" };
        var rows = list.Select(w => (IReadOnlyList<string>)new[]
        {
            w.WarehouseName,
            w.Products.ToString(Culture),
            w.ZeroItems.ToString(Culture),
            w.TotalQty.ToString(Culture)
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["عدد المخازن"] = list.Count,
            ["إجمالي الكميات"] = list.Sum(w => w.TotalQty),
            ["توزيع المخزون على المخازن"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, new ZakiTable("توزيع المخزون على المخازن", headers, rows));
    }

    private static AiToolDefinition LowStockDefinition() => new(
        GetLowStock,
        "الأصناف اللي رصيدها صفر أو تحت حد معين. max_quantity=0 (الافتراضي) يعني النافد بس، ورقم أكبر يعني الأصناف القليلة. للرد على: إيه اللي خلص من المخزون؟ إيه الأصناف اللي قربت تخلص؟",
        new
        {
            type = "object",
            properties = new
            {
                max_quantity = new { type = "integer", description = "الحد الأقصى للرصيد (0 = النافد فقط، الافتراضي 0)" },
                warehouse_name = new { type = "string", description = "فلتر باسم المخزن (اختياري — الافتراضي كل المخازن)" },
                limit = new { type = "integer", description = "عدد الصفوف (1-100، الافتراضي 30)" }
            }
        });

    private static AiToolDefinition StockByWarehouseDefinition() => new(
        GetStockByWarehouse,
        "توزيع المخزون على المخازن: عدد الأصناف والأصناف الصفرية وإجمالي الكميات لكل مخزن.",
        new
        {
            type = "object",
            properties = new
            {
                include_inactive = new { type = "boolean", description = "true لإدراج المخازن غير النشطة (الافتراضي false)" }
            }
        });
}
