using System.Security.Claims;
using System.Text.Json;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using COCOBOLOERPNEW.Services;
using Microsoft.EntityFrameworkCore;
using static COCOBOLOERPNEW.Services.Assistant.ZakiInvoiceQueries;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// طلبات الموافقة في زكي:
/// 1) طلبات تعديل أسعار المنتجات (جدول PriceChangeRequests).
///    الصلاحية: نفس صفحة طلبات تعديل الأسعار (Admin أو AccountManager أو SalesManager).
/// 2) طلبات إذن التسليم المعلقة (من سجل الفاتورة AuditLogs).
///    الصلاحية: نفس شاشة التسليم (DeliveryPermissions.CanView).
///    الطلب معلّق لو آخر حركة فيه طلب، وبعده مفيش اعتماد (نفس منطق شاشة التسليم).
/// خصومات عروض الأسعار مفيش ليها سجل في الجدول (بتتبعت إشعارات بس)، فمش هنا.
/// </summary>
public class ZakiApprovalsService
{
    public const string GetPriceChangeRequests = "get_price_change_requests";
    public const string GetPendingDeliveryPermissions = "get_pending_delivery_permissions";

    private const string DeliveryRequest = "DeliveryPermissionRequest";
    private const string DeliveryGranted = "DeliveryPermissionGranted";

    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly ILogger<ZakiApprovalsService> _logger;

    public ZakiApprovalsService(
        IDbContextFactory<db24804Context> dbFactory,
        ILogger<ZakiApprovalsService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public static bool Handles(string toolName) =>
        toolName is GetPriceChangeRequests or GetPendingDeliveryPermissions;

    /// <summary>نفس شرط صفحة طلبات تعديل الأسعار</summary>
    public static bool CanViewPriceRequests(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.IsInRole("AccountManager") || user.IsInRole("SalesManager");

    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user)
    {
        var list = new List<AiToolDefinition>();

        if (CanViewPriceRequests(user))
            list.Add(PriceRequestsDefinition());

        if (CanViewDeliveryStatus(user))
            list.Add(PendingDeliveryDefinition());

        return list;
    }

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string name, string argumentsJson, CancellationToken ct = default)
    {
        var allowed = name == GetPriceChangeRequests
            ? CanViewPriceRequests(user)
            : CanViewDeliveryStatus(user);

        if (!allowed)
            return Denied();

        var args = ParseArgs(argumentsJson);

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return name == GetPriceChangeRequests
                ? await PriceRequestsAsync(db, args, ct)
                : await PendingDeliveryAsync(db, user, args, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki approvals: query failed ({Tool})", name);
            return Message("ماقدرتش أجيب البيانات دلوقتي، حاول تاني بعد شوية.");
        }
    }

    // ════════════════════════════════════════════════════════════
    // 1) طلبات تعديل الأسعار
    // ════════════════════════════════════════════════════════════
    private static async Task<ZakiToolOutcome> PriceRequestsAsync(
        db24804Context db, JsonElement args, CancellationToken ct)
    {
        var limit = ReadInt(args, "limit", 15, 1, 20);
        var status = ReadString(args, "status", "pending");

        string? dbStatus = status switch
        {
            "approved" => "Approved",
            "rejected" => "Rejected",
            "all" => null,
            _ => "Pending"
        };

        var query = db.PriceChangeRequests.AsNoTracking().AsQueryable();
        if (dbStatus != null)
            query = query.Where(r => r.Status == dbStatus);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(r => r.RequestedAt)
            .Take(limit)
            .Select(r => new PriceRow(
                r.RequestId, r.ProductId, r.PriceType, r.CurrentPrice, r.RequestedPrice,
                r.Reason, r.Status, r.RequestedBy, r.RequestedAt, r.ReviewedBy))
            .ToListAsync(ct);

        var names = await ProductNamesAsync(db, items.Select(i => i.ProductId), ct);

        var headers = new[]
        {
            "رقم الطلب", "التاريخ", "المنتج", "نوع السعر", "السعر الحالي", "السعر المطلوب",
            "نسبة التغيير", "الحالة", "طلب بواسطة", "تمت المراجعة بواسطة", "السبب"
        };

        var rows = items.Select(i => (IReadOnlyList<string>)new[]
        {
            i.RequestId.ToString(Culture),
            Date(i.RequestedAt),
            NameOf(i.ProductId, names),
            PriceTypeLabel(i.PriceType),
            Money(i.CurrentPrice),
            Money(i.RequestedPrice),
            ChangePercent(i.CurrentPrice, i.RequestedPrice),
            StatusLabel(i.Status),
            i.RequestedBy,
            i.ReviewedBy ?? "—",
            i.Reason
        }).ToList();

        var statusText = status switch
        {
            "approved" => "المعتمدة",
            "rejected" => "المرفوضة",
            "all" => "كل الطلبات",
            _ => "المعلقة"
        };

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفلتر"] = statusText,
            ["عدد الطلبات بالفلتر"] = total,
            ["عدد المعروض"] = rows.Count,
            ["الطلبات"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json,
            rows.Count == 0 ? null : new ZakiTable("طلبات تعديل الأسعار — " + statusText, headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // 2) طلبات إذن التسليم المعلقة (على فواتير فيها مبالغ متبقية)
    // ════════════════════════════════════════════════════════════
    private static async Task<ZakiToolOutcome> PendingDeliveryAsync(
        db24804Context db, ClaimsPrincipal user, JsonElement args, CancellationToken ct)
    {
        var limit = ReadInt(args, "limit", 15, 1, 20);
        var scope = await BuildScopeAsync(db, user, null);

        var audits = await db.AuditLogs
            .AsNoTracking()
            .Where(a => a.TableName == "Transactions"
                        && a.PrimaryKeyValue != null
                        && (a.ActionType == DeliveryRequest || a.ActionType == DeliveryGranted))
            .OrderBy(a => a.AuditId)
            .Select(a => new PermAudit(a.AuditId, a.ActionType, a.PrimaryKeyValue, a.LoginName, a.ActionDate, a.NewData))
            .ToListAsync(ct);

        // نفس منطق شاشة التسليم: الطلب معلّق لو آخر طلب بعد آخر اعتماد
        var pending = new List<PendingPerm>();
        foreach (var grp in audits.GroupBy(a => a.PrimaryKeyValue!))
        {
            if (!int.TryParse(grp.Key, out var txId)) continue;

            var lastReq = grp.LastOrDefault(a => a.ActionType == DeliveryRequest);
            var lastGrant = grp.LastOrDefault(a => a.ActionType == DeliveryGranted);

            if (lastReq != null && (lastGrant == null || lastReq.AuditId > lastGrant.AuditId))
                pending.Add(new PendingPerm(txId, lastReq));
        }

        var pendingIds = pending.Select(p => p.TxId).Distinct().ToList();

        var invoices = pendingIds.Count == 0
            ? new List<PermInvoice>()
            : await Base(db, scope, TransactionTypes.Sale)
                .Where(t => pendingIds.Contains(t.TransactionId))
                .Select(t => new PermInvoice(t.TransactionId, t.ReferenceNumber, t.TransactionDate,
                    t.PartyId, t.GrandTotal, t.PaidAmount))
                .ToListAsync(ct);

        var byId = invoices.ToDictionary(i => i.TransactionId);

        var visible = pending
            .Where(p => byId.ContainsKey(p.TxId))
            .OrderBy(p => p.Req.ActionDate)
            .ToList();

        var shown = visible.Take(limit).ToList();

        var names = await PartyNamesAsync(db, shown.Select(p => byId[p.TxId].PartyId), ct);

        var headers = new[] { "رقم الفاتورة", "تاريخ الطلب", "العميل", "المتبقي", "طلب بواسطة", "سبب الطلب" };

        var rows = shown.Select(p =>
        {
            var inv = byId[p.TxId];
            var note = ZakiInvoiceEditDiff.Compute(DeliveryRequest, null, p.Req.NewData)
                .FirstOrDefault()?.New ?? "—";

            return (IReadOnlyList<string>)new[]
            {
                inv.ReferenceNumber ?? "—",
                Date(p.Req.ActionDate),
                NameOf(inv.PartyId, names),
                Money(inv.GrandTotal - inv.PaidAmount),
                string.IsNullOrWhiteSpace(p.Req.LoginName) ? "—" : p.Req.LoginName,
                note
            };
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["عدد طلبات إذن التسليم المعلقة في نطاقك"] = visible.Count,
            ["عدد المعروض"] = rows.Count,
            ["ملاحظة"] = "الاعتماد بيكون لـ Admin أو مدير الحسابات.",
            ["الطلبات"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json,
            rows.Count == 0 ? null : new ZakiTable("طلبات إذن التسليم المعلقة", headers, rows));
    }

    // ════════════════════════════════════════════════════════════
    // مساعدات
    // ════════════════════════════════════════════════════════════
    private static string PriceTypeLabel(string? type) => type switch
    {
        "Elite" => "Elite",
        "Premium" => "Premium",
        "CClass" => "C Class",
        _ => type ?? "—"
    };

    private static string StatusLabel(string? status) => status switch
    {
        "Pending" => "معلّق",
        "Approved" => "معتمد",
        "Rejected" => "مرفوض",
        _ => status ?? "—"
    };

    private static string ChangePercent(decimal current, decimal requested)
    {
        if (current <= 0) return "—";
        var pct = (requested - current) / current * 100m;
        return pct.ToString("+0.0;-0.0;0", Culture) + "%";
    }

    // ════════════════════════════════════════════════════════════
    // تعريفات الأدوات
    // ════════════════════════════════════════════════════════════
    private static AiToolDefinition PriceRequestsDefinition() => new(
        GetPriceChangeRequests,
        "طلبات تعديل أسعار المنتجات: المعلقة (الافتراضي) أو المعتمدة أو المرفوضة، مع السعر الحالي والمطلوب ونسبة التغيير والسبب.",
        new
        {
            type = "object",
            properties = new
            {
                status = new { type = "string", @enum = new[] { "pending", "approved", "rejected", "all" } },
                limit = new { type = "integer", minimum = 1, maximum = 20 }
            }
        });

    private static AiToolDefinition PendingDeliveryDefinition() => new(
        GetPendingDeliveryPermissions,
        "طلبات إذن التسليم المعلقة: فواتير فيها مبالغ متبقية وطلب تسليمها بيستنى اعتماد، مع المتبقي وسبب الطلب ومين طلبه.",
        new
        {
            type = "object",
            properties = new { limit = new { type = "integer", minimum = 1, maximum = 20 } }
        });

    // ════════════════════════════════════════════════════════════
    // أنواع داخلية
    // ════════════════════════════════════════════════════════════
    private sealed record PriceRow(
        int RequestId, int ProductId, string PriceType, decimal CurrentPrice, decimal RequestedPrice,
        string Reason, string Status, string RequestedBy, DateTime RequestedAt, string? ReviewedBy);

    private sealed record PermAudit(
        long AuditId, string? ActionType, string? PrimaryKeyValue, string? LoginName,
        DateTime? ActionDate, string? NewData);

    private sealed record PendingPerm(int TxId, PermAudit Req);

    private sealed record PermInvoice(
        int TransactionId, string? ReferenceNumber, DateTime TransactionDate,
        int PartyId, decimal GrandTotal, decimal PaidAmount);
}
