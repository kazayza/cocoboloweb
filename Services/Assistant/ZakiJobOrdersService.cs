using System.Security.Claims;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using COCOBOLOERPNEW.Services;
using Microsoft.EntityFrameworkCore;
using static COCOBOLOERPNEW.Services.Assistant.ZakiInvoiceQueries;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// أوامر التشغيل في زكي — نفس منطق شاشة /production/job-orders بالظبط:
/// - الفواتير (بيع + شراء غير المرآة) غير الملغاة.
/// - الفلترة بحالة التسليم (افتراضي: اللي لسه ماتسلمتش) وبموعد التسليم (DueDate).
/// - الصلاحية: نفس شروط الشاشة (frm_JobOrders:View أو الأدوار).
/// - المبالغ بتظهر بس لـ Admin / AccountManager / Account (زي الشاشة).
/// </summary>
public class ZakiJobOrdersService
{
    public const string GetJobOrders = "get_job_orders";

    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly ILogger<ZakiJobOrdersService> _logger;

    public ZakiJobOrdersService(
        IDbContextFactory<db24804Context> dbFactory,
        ILogger<ZakiJobOrdersService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public static bool Handles(string toolName) => toolName == GetJobOrders;

    public static bool CanView(ClaimsPrincipal user) =>
        user.IsInRole(SystemRoles.Admin)
        || user.IsInRole(SystemRoles.SalesManager)
        || user.IsInRole(SystemRoles.Sales)
        || user.IsInRole(SystemRoles.AccountManager)
        || user.IsInRole(SystemRoles.HrManager)
        || user.IsInRole(SystemRoles.ProductionManager)
        || user.IsInRole(SystemRoles.FactoryManager)
        || user.IsInRole("factory")
        || user.HasClaim("Permission", "frm_JobOrders:View");

    public static bool CanSeeAmounts(ClaimsPrincipal user) =>
        user.IsInRole(SystemRoles.Admin)
        || user.IsInRole(SystemRoles.AccountManager)
        || user.IsInRole(SystemRoles.Account);

    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user) =>
        CanView(user) ? new[] { Definition() } : Array.Empty<AiToolDefinition>();

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string name, string argumentsJson, CancellationToken ct = default)
    {
        if (!CanView(user))
            return Denied();

        var args = ParseArgs(argumentsJson);

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            return await JobOrdersAsync(db, user, args, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki job orders: query failed ({Tool})", name);
            return Message("ماقدرتش أجيب أوامر التشغيل دلوقتي، حاول تاني بعد شوية.");
        }
    }

    private sealed record JobRow(
        int TransactionId, string? ReferenceNumber, string TransactionType, DateTime TransactionDate,
        DateTime? DueDate, bool Delivered, int PartyId, int? EmpId, decimal GrandTotal);

    private sealed record ItemCount(int TransactionId, int Items, int Alternatives);

    private static async Task<ZakiToolOutcome> JobOrdersAsync(
        db24804Context db, ClaimsPrincipal user, System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var typeRaw = ReadString(args, "type", "all");                   // sale / purchase / all
        var deliveryRaw = ReadString(args, "delivery", "undelivered");   // undelivered / delivered / all
        var dueRaw = ReadString(args, "due", "all");                     // overdue / today / week / all
        var limit = ReadInt(args, "limit", 15, 1, 30);
        var today = DateTime.Today;

        // نفس قاعدة الشاشة: مش ملغاة، والشراء المرآة (MirrorOf:) مستبعد
        var query = db.Transactions.AsNoTracking().Where(t =>
            (t.InvoiceStatus == null || t.InvoiceStatus != CancelledStatus)
            && ((t.TransactionType == TransactionTypes.Sale)
                || (t.TransactionType == TransactionTypes.Purchase
                    && (t.ReferenceType == null || !t.ReferenceType.StartsWith(MirrorPrefix)))));

        // زي شاشة الفواتير: فواتير البيع لمديري الحسابات مخفية عن غير المسموح لهم
        if (!SalesInvoiceAccess.CanViewAccountManagerInvoices(user))
        {
            var protectedCreators = await SalesInvoiceAccess.GetProtectedCreatorUsernamesAsync(db);
            if (protectedCreators.Count > 0)
            {
                query = query.Where(t => t.TransactionType != TransactionTypes.Sale
                                         || !protectedCreators.Contains(t.CreatedBy));
            }
        }

        query = typeRaw switch
        {
            "sale" => query.Where(t => t.TransactionType == TransactionTypes.Sale),
            "purchase" => query.Where(t => t.TransactionType == TransactionTypes.Purchase),
            _ => query
        };

        query = deliveryRaw switch
        {
            "delivered" => query.Where(t => t.IsDelivered == true),
            "all" => query,
            _ => query.Where(t => t.IsDelivered != true)
        };

        var (dueLabel, dueFilter) = dueRaw switch
        {
            "overdue" => ("المتأخرة في التسليم", query.Where(t => t.DueDate.HasValue && t.DueDate.Value < today)),
            "today" => ("مواعيدها النهاردة", query.Where(t => t.DueDate.HasValue && t.DueDate.Value >= today && t.DueDate.Value < today.AddDays(1))),
            "week" => ("مواعيدها خلال 7 أيام", query.Where(t => t.DueDate.HasValue && t.DueDate.Value >= today && t.DueDate.Value < today.AddDays(7))),
            _ => ("كل المواعيد", query)
        };
        query = dueFilter;

        var total = await query.CountAsync(ct);
        if (total == 0)
            return Message("مفيش أوامر تشغيل مطابقة للفلتر ده.");

        var overdueCount = await query
            .Where(t => t.DueDate.HasValue && t.DueDate.Value < today)
            .CountAsync(ct);

        var withAlternatives = await query
            .Where(t => db.TransactionDetails.Any(d => d.TransactionId == t.TransactionId && d.SelectedAlternativeId != null))
            .CountAsync(ct);

        // الأقرب موعدًا أولاً، والمواعيد الفاضية في الآخر
        var rowsRaw = await query
            .OrderBy(t => t.DueDate == null)
            .ThenBy(t => t.DueDate)
            .ThenByDescending(t => t.TransactionDate)
            .Take(limit)
            .Select(t => new JobRow(
                t.TransactionId, t.ReferenceNumber, t.TransactionType, t.TransactionDate,
                t.DueDate, t.IsDelivered == true, t.PartyId, t.EmpId, t.GrandTotal))
            .ToListAsync(ct);

        var ids = rowsRaw.Select(r => r.TransactionId).ToList();
        var counts = (await db.TransactionDetails.AsNoTracking()
                .Where(d => ids.Contains(d.TransactionId))
                .GroupBy(d => d.TransactionId)
                .Select(g => new ItemCount(g.Key, g.Count(), g.Count(d => d.SelectedAlternativeId != null)))
                .ToListAsync(ct))
            .ToDictionary(x => x.TransactionId);

        var partyNames = await PartyNamesAsync(db, rowsRaw.Select(r => r.PartyId), ct);
        var empNames = await EmployeeNamesAsync(db,
            rowsRaw.Where(r => r.EmpId.HasValue).Select(r => r.EmpId!.Value), ct);

        var showAmounts = CanSeeAmounts(user);

        var headers = showAmounts
            ? new[] { "المرجع", "النوع", "التاريخ", "موعد التسليم", "العميل", "الموظف", "البنود", "بنود بديلة", "الحالة", "المبلغ" }
            : new[] { "المرجع", "النوع", "التاريخ", "موعد التسليم", "العميل", "الموظف", "البنود", "بنود بديلة", "الحالة" };

        var rows = rowsRaw.Select(r =>
        {
            counts.TryGetValue(r.TransactionId, out var c);
            var overdue = !r.Delivered && r.DueDate.HasValue && r.DueDate.Value < today;

            var cells = new List<string>
            {
                string.IsNullOrWhiteSpace(r.ReferenceNumber) ? $"#{r.TransactionId}" : r.ReferenceNumber!,
                r.TransactionType == TransactionTypes.Purchase ? "شراء" : "بيع",
                Date(r.TransactionDate),
                Date(r.DueDate),
                NameOf(r.PartyId, partyNames),
                r.EmpId.HasValue ? NameOf(r.EmpId.Value, empNames) : "—",
                (c?.Items ?? 0).ToString(Culture),
                (c?.Alternatives ?? 0).ToString(Culture),
                r.Delivered ? "اتسلّم" : overdue ? "متأخر التسليم" : "لسه ماتسلّمش"
            };

            if (showAmounts) cells.Add(Money(r.GrandTotal));
            return (IReadOnlyList<string>)cells;
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفلتر"] = dueLabel,
            ["إجمالي أوامر التشغيل"] = total,
            ["المتأخرة في التسليم"] = overdueCount,
            ["فيها بنود بديلة"] = withAlternatives,
            ["أوامر التشغيل"] = ToObjects(headers, rows),
            ["ملاحظة"] = total > rows.Count
                ? $"الجدول بيعرض أول {rows.Count} من {total}."
                : "الجدول بيعرض كل النتايج."
        });

        return new ZakiToolOutcome(json, new ZakiTable("أوامر التشغيل — " + dueLabel, headers, rows));
    }

    private static AiToolDefinition Definition() => new(
        GetJobOrders,
        "أوامر التشغيل (الفواتير اللي محتاجة تشغيل وتسليم) مرتبة بأقرب موعد تسليم. فيها عدد البنود والبنود البديلة، والمتأخرة في التسليم. الافتراضي: اللي لسه ماتسلمتش.",
        new
        {
            type = "object",
            properties = new
            {
                type = new { type = "string", @enum = new[] { "sale", "purchase", "all" }, description = "بيع / شراء / الكل (الافتراضي all)" },
                delivery = new { type = "string", @enum = new[] { "undelivered", "delivered", "all" }, description = "الافتراضي undelivered" },
                due = new { type = "string", @enum = new[] { "overdue", "today", "week", "all" }, description = "متأخرة / مواعيدها النهاردة / خلال 7 أيام / الكل (الافتراضي all)" },
                limit = new { type = "integer", minimum = 1, maximum = 30 }
            }
        });
}
