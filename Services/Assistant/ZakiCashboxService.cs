using System.Globalization;
using System.Security.Claims;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static COCOBOLOERPNEW.Services.Assistant.ZakiInvoiceQueries;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// أدوات الخزينة في زكي — إعادة استخدام ICashBoxService الموجود
/// عشان الأرقام تفضل مطابقة لشاشات الخزينة بالظبط.
/// الصلاحيات: نفس بنود السايدبار:
/// - الملخص: frmCashBoxDashboard:View أو frmCashBox:View.
/// - الحركات: frmCashBoxTransactions:View أو frm_CashBoxTransaction:View.
/// </summary>
public class ZakiCashboxService
{
    public const string GetCashboxSummary = "get_cashbox_summary";
    public const string GetCashboxTransactions = "get_cashbox_transactions";
    public const string GetCashboxDayClosing = "get_cashbox_day_closing";

    private readonly ICashBoxService _cashbox;
    private readonly IDbContextFactory<db24804Context> _dbFactory;
    private readonly ILogger<ZakiCashboxService> _logger;

    public ZakiCashboxService(
        ICashBoxService cashbox,
        IDbContextFactory<db24804Context> dbFactory,
        ILogger<ZakiCashboxService> logger)
    {
        _cashbox = cashbox;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public static bool Handles(string toolName) =>
        toolName is GetCashboxSummary or GetCashboxTransactions or GetCashboxDayClosing;

    public static bool CanViewSummary(ClaimsPrincipal user) =>
        user.IsInRole("Admin")
        || user.HasClaim("Permission", "frmCashBoxDashboard:View")
        || user.HasClaim("Permission", "frmCashBox:View");

    public static bool CanViewTransactions(ClaimsPrincipal user) =>
        user.IsInRole("Admin")
        || user.HasClaim("Permission", "frmCashBoxTransactions:View")
        || user.HasClaim("Permission", "frm_CashBoxTransaction:View");

    public IReadOnlyList<AiToolDefinition> DefinitionsFor(ClaimsPrincipal user)
    {
        var list = new List<AiToolDefinition>();
        if (CanViewSummary(user))
        {
            list.Add(SummaryDefinition());
            list.Add(DayClosingDefinition());
        }
        if (CanViewTransactions(user)) list.Add(TransactionsDefinition());
        return list;
    }

    public async Task<ZakiToolOutcome> ExecuteAsync(
        ClaimsPrincipal user, string name, string argumentsJson, CancellationToken ct = default)
    {
        var allowed = name is GetCashboxSummary or GetCashboxDayClosing
            ? CanViewSummary(user)
            : CanViewTransactions(user);
        if (!allowed) return Denied();

        var args = ParseArgs(argumentsJson);

        try
        {
            return name switch
            {
                GetCashboxSummary => await SummaryAsync(user, args, ct),
                GetCashboxDayClosing => await DayClosingAsync(user, args, ct),
                _ => await TransactionsAsync(user, args, ct)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zaki cashbox: query failed ({Tool})", name);
            return Message("ماقدرتش أجيب بيانات الخزينة دلوقتي، حاول تاني بعد شوية.");
        }
    }

    // ── ملخص الخزن: أرصدة + وارد/صادر ──
    private async Task<ZakiToolOutcome> SummaryAsync(
        ClaimsPrincipal user, System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var (start, label) = PeriodRange(ReadString(args, "period", "all"), DateTime.Today);

        var boxes = await _cashbox.GetSummaryAsync(start, null);

        // الرصيد الحالي الحقيقي = الافتتاحي المخزّن + كل الحركات من غير فلتر فترة.
        // (الـ DTO بيحسب CurrentBalance من إجماليات الفترة نفسها، فمن غير الخطوة دي
        // أي فترة غير «all» كانت بتعرض افتتاحي + صافي الفترة بس كأنه الرصيد الحالي.)
        var trueBalanceByBox = (await _cashbox.GetSummaryAsync(null, null))
            .ToDictionary(b => b.CashBoxId, b => b.CurrentBalance);

        // رصيد أول الفترة = الافتتاحي المخزّن + صافي كل الحركات قبل بداية الفترة
        // (ملخص لحد نهاية اليوم اللي قبلها). لفترة «all» هو الافتتاحي المخزّن نفسه.
        var periodStartByBox = new Dictionary<int, decimal>();
        if (start.HasValue)
        {
            var before = await _cashbox.GetSummaryAsync(null, start.Value.AddDays(-1));
            foreach (var b in before)
                periodStartByBox[b.CashBoxId] = b.CurrentBalance;
        }

        decimal PeriodStartOf(CashBoxBalanceSummaryDto b) =>
            start.HasValue ? periodStartByBox.GetValueOrDefault(b.CashBoxId, b.OpeningBalance) : b.OpeningBalance;
        decimal TrueBalanceOf(CashBoxBalanceSummaryDto b) =>
            trueBalanceByBox.GetValueOrDefault(b.CashBoxId, b.CurrentBalance);

        // فلتر الفرع اختياري: الخزينة مربوطة بالفرع (CashBox.BranchId)
        var branchSearch = ReadRawString(args, "branch_name");
        if (branchSearch.Length > 0)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var (branchId, branchName, branchError) = await ResolveBranchAsync(db, user, branchSearch, ct);
            if (branchError != null)
                return branchError;

            var branchBoxIds = await db.CashBoxes.AsNoTracking()
                .Where(c => c.BranchId == branchId)
                .Select(c => c.CashBoxId)
                .ToListAsync(ct);

            boxes = boxes.Where(b => branchBoxIds.Contains(b.CashBoxId)).ToList();
            label = label + " — " + branchName;

            if (boxes.Count == 0)
                return Message("مفيش خزن مربوطة بالفرع ده.");
        }

        if (boxes.Count == 0)
            return Message("مفيش خزن معرّفة في النظام.");

        var headers = new[] { "الخزينة", "رصيد أول الفترة", "وارد", "صادر", "الرصيد الحالي" };
        var rows = boxes
            .OrderByDescending(b => TrueBalanceOf(b))
            .Select(b => (IReadOnlyList<string>)new[]
            {
                b.CashBoxName,
                Money(PeriodStartOf(b)),
                Money(b.TotalIn),
                Money(b.TotalOut),
                Money(TrueBalanceOf(b))
            }).ToList();

        var totalStartBalance = boxes.Sum(b => PeriodStartOf(b));
        var totalBalance = boxes.Sum(b => TrueBalanceOf(b));
        var totalIn = boxes.Sum(b => b.TotalIn);
        var totalOut = boxes.Sum(b => b.TotalOut);

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفترة"] = label,
            ["عدد الخزن"] = boxes.Count,
            ["إجمالي رصيد أول الفترة"] = Money(totalStartBalance),
            ["إجمالي الوارد"] = Money(totalIn),
            ["إجمالي الصادر"] = Money(totalOut),
            ["إجمالي الأرصدة"] = Money(totalBalance),
            ["ملخص الخزائن"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, new ZakiTable("ملخص الخزائن — " + label, headers, rows));
    }

    // ── إقفال يوم محدد: رصيد أول اليوم / وارد اليوم / صادر اليوم / رصيد آخر اليوم ──
    // الحساب من الحركات المؤرخة مباشرة وبنفس دلالات ملخص الشاشات (قبض = وارد، صرف = صادر):
    // رصيد أول اليوم = الافتتاحي المخزّن + صافي كل الحركات قبل اليوم،
    // ورصيد آخر اليوم = رصيد أول اليوم + وارد اليوم − صادر اليوم.
    private async Task<ZakiToolOutcome> DayClosingAsync(
        ClaimsPrincipal user, System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var today = DateTime.Today;
        var dateRaw = ReadRawString(args, "date").Trim();
        if (dateRaw.Length == 0)
            return Message("حدّد اليوم اللي عايز إقفاله — مثال: «إقفال الخزينة يوم 2026-10-27» أو «إقفال امبارح».");

        DateTime day;
        var lowered = dateRaw.ToLowerInvariant();
        if (lowered == "today") day = today;
        else if (lowered == "yesterday") day = today.AddDays(-1);
        else if (!DateTime.TryParseExact(dateRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)
                 && !DateTime.TryParse(dateRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
            return Message($"ماقدرتش أفهم التاريخ «{dateRaw}» — اكتبه بصيغة سنة-شهر-يوم زي 2026-10-27.");
        day = day.Date;
        var nextDay = day.AddDays(1);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var boxes = await db.CashBoxes.AsNoTracking()
            .Select(c => new { c.CashBoxId, c.CashBoxName, c.OpeningBalance, c.BranchId, c.IsActive })
            .ToListAsync(ct);

        // فلتر الفرع اختياري (نفس أسلوب الملخص)
        var label = "يوم " + Date(day);
        var branchSearch = ReadRawString(args, "branch_name");
        if (branchSearch.Length > 0)
        {
            var (branchId, branchName, branchError) = await ResolveBranchAsync(db, user, branchSearch, ct);
            if (branchError != null)
                return branchError;

            boxes = boxes.Where(b => b.BranchId == branchId).ToList();
            label += " — " + branchName;

            if (boxes.Count == 0)
                return Message("مفيش خزن مربوطة بالفرع ده.");
        }

        // فلتر خزينة بعينها بالاسم (مطابقة مرنة على الصورة المُطبّعة)
        var boxSearch = ReadRawString(args, "cashbox_name").Trim();
        if (boxSearch.Length > 0)
        {
            var needle = boxSearch.NormalizeArabic();
            boxes = boxes
                .Where(b => b.CashBoxName.NormalizeArabic().Contains(needle)
                            || needle.Contains(b.CashBoxName.NormalizeArabic()))
                .ToList();

            if (boxes.Count == 0)
                return Message($"ملقتش خزينة بالاسم «{boxSearch}».");
        }

        if (boxes.Count == 0)
            return Message("مفيش خزن معرّفة في النظام.");

        var boxIds = boxes.Select(b => b.CashBoxId).ToList();
        var tx = db.CashboxTransactions.AsNoTracking().Where(t => boxIds.Contains(t.CashBoxId));

        var before = await tx.Where(t => t.TransactionDate < day)
            .GroupBy(t => t.CashBoxId)
            .Select(g => new
            {
                CashBoxId = g.Key,
                In = g.Where(t => t.TransactionType == "قبض").Sum(t => (decimal?)t.Amount) ?? 0,
                Out = g.Where(t => t.TransactionType == "صرف").Sum(t => (decimal?)t.Amount) ?? 0
            })
            .ToListAsync(ct);
        var beforeByBox = before.ToDictionary(x => x.CashBoxId, x => (x.In, x.Out));

        var during = await tx.Where(t => t.TransactionDate >= day && t.TransactionDate < nextDay)
            .GroupBy(t => t.CashBoxId)
            .Select(g => new
            {
                CashBoxId = g.Key,
                In = g.Where(t => t.TransactionType == "قبض").Sum(t => (decimal?)t.Amount) ?? 0,
                Out = g.Where(t => t.TransactionType == "صرف").Sum(t => (decimal?)t.Amount) ?? 0
            })
            .ToListAsync(ct);
        var duringByBox = during.ToDictionary(x => x.CashBoxId, x => (x.In, x.Out));

        var computed = boxes.Select(b =>
        {
            var (bIn, bOut) = beforeByBox.GetValueOrDefault(b.CashBoxId, (0m, 0m));
            var (dIn, dOut) = duringByBox.GetValueOrDefault(b.CashBoxId, (0m, 0m));
            var opening = b.OpeningBalance + bIn - bOut;
            return new
            {
                b.CashBoxName,
                b.IsActive,
                Opening = opening,
                DayIn = dIn,
                DayOut = dOut,
                Closing = opening + dIn - dOut
            };
        })
        .OrderByDescending(x => x.IsActive)
        .ThenByDescending(x => x.Closing)
        .ToList();

        var headers = new[] { "الخزينة", "رصيد أول اليوم", "وارد اليوم", "صادر اليوم", "رصيد آخر اليوم" };
        var rows = computed
            .Select(x => (IReadOnlyList<string>)new[]
            {
                x.CashBoxName,
                Money(x.Opening),
                Money(x.DayIn),
                Money(x.DayOut),
                Money(x.Closing)
            }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["التاريخ"] = Date(day),
            ["عدد الخزن"] = computed.Count,
            ["إجمالي رصيد أول اليوم"] = Money(computed.Sum(x => x.Opening)),
            ["إجمالي وارد اليوم"] = Money(computed.Sum(x => x.DayIn)),
            ["إجمالي صادر اليوم"] = Money(computed.Sum(x => x.DayOut)),
            ["إجمالي رصيد آخر اليوم"] = Money(computed.Sum(x => x.Closing)),
            ["إقفال الخزائن"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, new ZakiTable("إقفال الخزينة — " + label, headers, rows));
    }

    // ── حركات الخزينة (وارد/صادر) لفترة ──
    private async Task<ZakiToolOutcome> TransactionsAsync(
        ClaimsPrincipal user, System.Text.Json.JsonElement args, CancellationToken ct)
    {
        var (start, label) = PeriodRange(ReadString(args, "period", "today"), DateTime.Today);
        var typeRaw = ReadString(args, "type", "all").ToLowerInvariant();
        var limit = ReadInt(args, "limit", 20, 1, 50);
        var typeFilter = typeRaw == "in" ? "In" : typeRaw == "out" ? "Out" : null;

        // فلتر الفرع اختياري: الخزينة مربوطة بالفرع (CashBox.BranchId).
        // الخدمة بتقرأ خزينة واحدة في المرة، فبنجيب حركات كل خزن الفرع ونجمعهم ونرتبهم.
        var branchSearch = ReadRawString(args, "branch_name");
        List<int>? branchBoxIds = null;
        if (branchSearch.Length > 0)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var (branchId, branchName, branchError) = await ResolveBranchAsync(db, user, branchSearch, ct);
            if (branchError != null)
                return branchError;

            branchBoxIds = await db.CashBoxes.AsNoTracking()
                .Where(c => c.BranchId == branchId)
                .Select(c => c.CashBoxId)
                .ToListAsync(ct);

            if (branchBoxIds.Count == 0)
                return Message("مفيش خزن مربوطة بالفرع ده.");

            label = label + " — " + branchName;
        }

        var filters = branchBoxIds == null
            ? new List<CashBoxTransactionFilterDto>
            {
                new() { DateFrom = start, TransactionType = typeFilter, PageNumber = 1, PageSize = limit }
            }
            : branchBoxIds.Select(id => new CashBoxTransactionFilterDto
            {
                CashBoxId = id,
                DateFrom = start,
                TransactionType = typeFilter,
                PageNumber = 1,
                PageSize = limit
            }).ToList();

        var items = new List<CashBoxTransactionDto>();
        decimal totalIn = 0m;
        decimal totalOut = 0m;
        var totalCount = 0;

        foreach (var filter in filters)
        {
            var page = await _cashbox.GetTransactionsAsync(filter);
            var (inSum, outSum) = await _cashbox.GetTransactionsTotalsAsync(filter);

            items.AddRange(page.Items);
            totalIn += inSum;
            totalOut += outSum;
            totalCount += page.TotalCount;
        }

        items = items.OrderByDescending(t => t.TransactionDate).Take(limit).ToList();

        if (items.Count == 0)
            return Message($"مفيش حركات خزينة في الفترة دي ({label}).");

        var headers = new[] { "التاريخ", "الخزينة", "النوع", "المبلغ", "البيان", "بواسطة" };
        var rows = items.Select(t => (IReadOnlyList<string>)new[]
        {
            Date(t.TransactionDate),
            t.CashBoxName,
            t.TransactionType == "In" ? "وارد" : "صادر",
            Money(t.Amount),
            string.IsNullOrWhiteSpace(t.Notes) ? "—" : t.Notes!,
            string.IsNullOrWhiteSpace(t.CreatedBy) ? "—" : t.CreatedBy!
        }).ToList();

        var json = ToJson(new Dictionary<string, object?>
        {
            ["الفترة"] = label,
            ["الفلتر"] = typeRaw switch { "in" => "وارد فقط", "out" => "صادر فقط", _ => "الكل" },
            ["إجمالي الوارد"] = Money(totalIn),
            ["إجمالي الصادر"] = Money(totalOut),
            ["صافي الفترة"] = Money(totalIn - totalOut),
            ["عدد الحركات المعروضة"] = items.Count,
            ["إجمالي عدد الحركات"] = totalCount,
            ["حركات الخزينة"] = ToObjects(headers, rows)
        });

        return new ZakiToolOutcome(json, new ZakiTable("حركات الخزينة — " + label, headers, rows));
    }

    private static AiToolDefinition SummaryDefinition() => new(
        GetCashboxSummary,
        "ملخص الخزائن: رصيد أول الفترة والوارد والصادر والرصيد الحالي الحقيقي لكل خزينة. للرد على: رصيد الخزينة كام؟ فلوس الصندوق كام؟",
        new
        {
            type = "object",
            properties = new
            {
                period = PeriodSchema("الافتراضي all — الوارد/صادر بيتحسب من بداية الفترة"),
                branch_name = new { type = "string", description = "اسم الفرع (اختياري). فاضي = كل الفروع. «فرعي» = فرع المستخدم." }
            }
        });

    private static AiToolDefinition DayClosingDefinition() => new(
        GetCashboxDayClosing,
        "إقفال الخزينة ليوم محدد: رصيد أول اليوم (الافتتاحي + كل الحركات قبل اليوم)، وارد اليوم، صادر اليوم، ورصيد آخر اليوم — لكل خزينة أو لخزينة/فرع بعينه. "
        + "للرد على: «إقفال خزينة [اسم] يوم …»، «الخزينة بدأت بكام يوم …»، «إقفال امبارح». "
        + "التاريخ إلزامي بصيغة yyyy-MM-dd: لو المستخدم قال «اليوم» ابعت تاريخ النهاردة، ولو قال «أمس» أو «امبارح» ابعت تاريخ امبارح، "
        + "ولو ماحددش يوم أصلاً اسأله عن اليوم وماتخمنش تاريخ من عندك.",
        new
        {
            type = "object",
            properties = new
            {
                date = new { type = "string", description = "اليوم المطلوب إقفاله بصيغة yyyy-MM-dd (إلزامي)" },
                cashbox_name = new { type = "string", description = "اسم خزينة بعينها (اختياري). فاضي = كل الخزن." },
                branch_name = new { type = "string", description = "اسم الفرع (اختياري). فاضي = كل الفروع. «فرعي» = فرع المستخدم." }
            },
            required = new[] { "date" }
        });

    private static AiToolDefinition TransactionsDefinition() => new(
        GetCashboxTransactions,
        "حركات الخزينة (وارد/صادر) لفترة. type: in للوارد، out للصادر، all للكل.",
        new
        {
            type = "object",
            properties = new
            {
                period = PeriodSchema("الافتراضي today"),
                type = new { type = "string", description = "in / out / all (الافتراضي all)", @enum = new[] { "in", "out", "all" } },
                limit = new { type = "integer", description = "عدد الصفوف (1-50، الافتراضي 20)" },
                branch_name = new { type = "string", description = "اسم الفرع (اختياري). فاضي = كل الفروع. «فرعي» = فرع المستخدم." }
            }
        });

    private static object PeriodSchema(string description) => new
    {
        type = "string",
        description,
        @enum = new[] { "today", "week", "month", "year", "all" }
    };
}
