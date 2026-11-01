using System.Security.Claims;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// مراجعة الخزينة: أرقام فترة بالتعريف الموحد (أول المدة + وارد − صادر = آخر المدة)
/// + تفصيل مصادر + مساعدات مراجعة + أختام مراجعة خفيفة بكشف تغيير لاحق.
/// ⚠️ لا يوجد هنا أي قفل أو منع تعديل — الإقفال مؤجل وخارج النطاق؛ الختم سجل
/// تاريخي فقط، والتعديلات بعد الختم تُكشف بمقارنة البصمة ولا تُمنع.
/// جدول الأختام (CashBoxReviews) يُنشأ بـ cashbox-review.sql؛ غيابه يُدار
/// بتدهور آمن في القراءات ورسالة واضحة عند محاولة الختم (نمط طلبات تعديل
/// مواعيد التسليم).
/// </summary>
public class CashBoxReviewService
{
    /// <summary>حد عرض حركات الفترة — ما زاد عنه يُعلَّم بـ IsTruncated صراحة.</summary>
    public const int MovementCap = 2000;

    private const int MovementPageSize = 500;

    public const string MissingReviewsTableMessage =
        "جدول أختام مراجعة الخزينة غير مهيأ بعد — برجاء تشغيل سكربت cashbox-review.sql أولًا.";

    private readonly IDbContextFactory<db24804Context> _factory;
    private readonly ICashBoxService _cashBox;
    private readonly ILogger<CashBoxReviewService> _logger;

    public CashBoxReviewService(
        IDbContextFactory<db24804Context> factory,
        ICashBoxService cashBox,
        ILogger<CashBoxReviewService> logger)
    {
        _factory = factory;
        _cashBox = cashBox;
        _logger = logger;
    }

    // ════════════════════════════════════════════════════════
    //  مراجعة فترة
    // ════════════════════════════════════════════════════════

    public async Task<CashBoxReviewDto> GetReviewAsync(int cashBoxId, DateTime from, DateTime to)
    {
        (from, to) = NormalizePeriod(from, to);

        // ── رصيد أول المدة بالتعريف الموحد: ملخص بلا بداية حتى نهاية اليوم
        // السابق للفترة — خاصية CurrentBalance فيه = الافتتاحي المخزّن + كل
        // الحركات قبل الفترة (نفس تركيب الشاشات المُصلحة وزكي).
        var periodSummaries = await _cashBox.GetSummaryAsync(from, to);
        var boxSummary = periodSummaries.FirstOrDefault(b => b.CashBoxId == cashBoxId);
        decimal opening = boxSummary?.OpeningBalance ?? 0;
        if (from > DateTime.MinValue.Date)
        {
            var before = await _cashBox.GetSummaryAsync(null, from.AddDays(-1));
            opening = before.FirstOrDefault(b => b.CashBoxId == cashBoxId)?.CurrentBalance ?? opening;
        }

        // ── حركات الفترة عبر خدمة الخزينة نفسها (إثراء المصادر والروابط منها)،
        // بصفحات حتى الحد الأقصى، وبعلم اقتطاع صريح.
        var movements = new List<CashBoxTransactionDto>();
        var firstPage = await _cashBox.GetTransactionsAsync(NewMovementFilter(cashBoxId, from, to, 1));
        var totalCount = firstPage.TotalCount;
        movements.AddRange(firstPage.Items);
        var target = Math.Min(totalCount, MovementCap);
        var page = 2;
        while (movements.Count < target)
        {
            var next = await _cashBox.GetTransactionsAsync(NewMovementFilter(cashBoxId, from, to, page));
            if (next.Items.Count == 0) break;
            movements.AddRange(next.Items);
            page++;
        }
        if (movements.Count > target) movements = movements.Take(target).ToList();
        var isTruncated = totalCount > MovementCap;

        // ── إجماليات الفترة الكاملة (غير مقيدة بحد العرض)
        var (totalIn, totalOut) = await _cashBox.GetTransactionsTotalsAsync(new CashBoxTransactionFilterDto
        {
            CashBoxId = cashBoxId,
            DateFrom = from,
            DateTo = to
        });

        // ── البصمة الحية من البيانات الكاملة (لا من قائمة العرض المقطوعة)
        CashBoxReviewFingerprintDto liveFingerprint;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            liveFingerprint = await ComputeLiveFingerprintAsync(db, cashBoxId, from, to);
        }

        var (stamp, stampTableMissing) = await TryGetLatestStampAsync(cashBoxId, from, to);

        return new CashBoxReviewDto
        {
            CashBoxId = cashBoxId,
            CashBoxName = boxSummary?.CashBoxName ?? "",
            PeriodFrom = from,
            PeriodTo = to,
            OpeningBalance = opening,
            TotalIn = totalIn,
            TotalOut = totalOut,
            Movements = movements,
            TotalMovementCount = totalCount,
            IsTruncated = isTruncated,
            SourceBreakdown = BuildSourceBreakdown(movements),
            Aids = BuildAids(movements, opening, isTruncated),
            Stamp = BuildStampDto(stamp, liveFingerprint),
            RequiresSql = stampTableMissing
        };
    }

    private static CashBoxTransactionFilterDto NewMovementFilter(int cashBoxId, DateTime from, DateTime to, int pageNumber) => new()
    {
        CashBoxId = cashBoxId,
        DateFrom = from,
        DateTo = to,
        PageNumber = pageNumber,
        PageSize = MovementPageSize,
        SortDescending = false
    };

    // ════════════════════════════════════════════════════════
    //  تصنيف المصادر ومساعدات المراجعة
    // ════════════════════════════════════════════════════════

    /// <summary>الفئة العربية لمصدر حركة — تستخدمها الصفحة أيضًا لعرض مصدر كل حركة.</summary>
    public static string GetSourceCategory(string? referenceType)
    {
        if (string.IsNullOrWhiteSpace(referenceType)) return "حركات يدوية";
        var raw = referenceType.Trim();

        if (raw is CashBoxRefTypes.ManualReceipt or CashBoxRefTypes.ManualPayment)
            return "حركات يدوية";
        // LoanCancel قروض صراحة؛ أي Cancel آخر إلغاء/ارتجاع قيد
        if (raw.EndsWith("Cancel", StringComparison.OrdinalIgnoreCase)
            && !raw.Equals("LoanCancel", StringComparison.OrdinalIgnoreCase))
            return "إلغاءات ومرتجعات القيود";

        return raw switch
        {
            CashBoxRefTypes.SaleInvoice or "Invoice" or "SaleInvoiceEdit" => "تحصيل مبيعات",
            CashBoxRefTypes.AdvanceCharge => "رسوم ومصروفات إضافية",
            CashBoxRefTypes.PurchaseInvoice => "مدفوعات مشتريات",
            CashBoxRefTypes.OpeningBalance => "رصيد افتتاحي",
            CashBoxRefTypes.Loan or "LoanCancel" or "LoanIn" or "LoanRepayment" => "قروض",
            CashBoxRefTypes.TransferIn or CashBoxRefTypes.TransferOut
                or "WarehouseTransferIn" or "WarehouseTransferOut" => "تحويلات",
            CashBoxRefTypes.Expense => "مصروفات",
            CashBoxRefTypes.Payroll or CashBoxRefTypes.BonusSeparate or CashBoxRefTypes.CommissionSeparate => "رواتب ومستحقات",
            _ => $"أخرى ({raw})"
        };
    }

    private static bool IsIn(CashBoxTransactionDto m) => m.TransactionType == "قبض";
    private static bool IsOut(CashBoxTransactionDto m) => m.TransactionType == "صرف";
    private static decimal Signed(CashBoxTransactionDto m) => IsIn(m) ? m.Amount : IsOut(m) ? -m.Amount : 0;

    private static bool IsManual(CashBoxTransactionDto m) =>
        string.IsNullOrWhiteSpace(m.ReferenceType)
        || m.ReferenceType is CashBoxRefTypes.ManualReceipt or CashBoxRefTypes.ManualPayment;

    private static List<CashBoxReviewSourceDto> BuildSourceBreakdown(List<CashBoxTransactionDto> movements)
    {
        var groups = new Dictionary<string, CashBoxReviewSourceDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in movements)
        {
            var raw = (m.ReferenceType ?? "").Trim();
            var category = GetSourceCategory(m.ReferenceType);
            // الفئات المعروفة تتجمع؛ «أخرى» تبقى منفصلة لكل نوع خام حتى لا يختفي
            var key = category.StartsWith("أخرى (") ? $"other:{raw}" : $"cat:{category}";
            if (!groups.TryGetValue(key, out var row))
            {
                row = new CashBoxReviewSourceDto { Category = category };
                groups[key] = row;
            }
            if (raw.Length > 0 && !row.RawReferenceTypes.Contains(raw))
                row.RawReferenceTypes.Add(raw);
            row.Count++;
            if (IsIn(m)) row.TotalIn += m.Amount;
            else if (IsOut(m)) row.TotalOut += m.Amount;
        }

        return groups.Values
            .OrderByDescending(r => r.TotalIn + r.TotalOut)
            .ThenBy(r => r.Category)
            .ToList();
    }

    private static CashBoxReviewAidsDto BuildAids(
        List<CashBoxTransactionDto> movements, decimal opening, bool isTruncated)
    {
        var aids = new CashBoxReviewAidsDto
        {
            ManualMovementIds = movements.Where(IsManual)
                .Select(m => m.CashboxTransactionId).ToList(),
            NoReferenceMovementIds = movements
                .Where(m => (!m.ReferenceId.HasValue || m.ReferenceId.Value == 0)
                            && string.IsNullOrWhiteSpace(m.ReferenceType)
                            && string.IsNullOrWhiteSpace(m.SourceTitle))
                .Select(m => m.CashboxTransactionId).ToList(),
            BackdatedMovementIds = movements
                .Where(m => m.CreatedAt.HasValue && m.CreatedAt.Value.Date > m.TransactionDate.Date)
                .Select(m => m.CashboxTransactionId).ToList(),
            LargestMovements = movements
                .OrderByDescending(m => m.Amount)
                .ThenByDescending(m => m.TransactionDate)
                .ThenByDescending(m => m.CashboxTransactionId)
                .Take(5)
                .ToList(),
            NegativeDaysAvailable = !isTruncated
        };

        // أيام الإغلاق السالب: رصيد جارٍ من أول المدة عبر أيام الفترة بالترتيب
        // الزمني — تُحسب فقط عند اكتمال مجموعة الحركات.
        if (!isTruncated)
        {
            var running = opening;
            foreach (var day in movements.GroupBy(m => m.TransactionDate.Date).OrderBy(g => g.Key))
            {
                running += day.Sum(Signed);
                if (running < 0)
                {
                    aids.NegativeDays.Add(new CashBoxReviewNegativeDayDto
                    {
                        Date = day.Key,
                        ClosingBalance = running
                    });
                    aids.NegativeDayMovementIds.AddRange(day.Select(m => m.CashboxTransactionId));
                }
            }
        }

        return aids;
    }

    // ════════════════════════════════════════════════════════
    //  البصمة والأختام
    // ════════════════════════════════════════════════════════

    private static async Task<CashBoxReviewFingerprintDto> ComputeLiveFingerprintAsync(
        db24804Context db, int cashBoxId, DateTime from, DateTime to)
    {
        var query = db.CashboxTransactions.AsNoTracking()
            .Where(t => t.CashBoxId == cashBoxId
                     && t.TransactionDate >= from
                     && t.TransactionDate <= to.AddDays(1).AddTicks(-1));

        return new CashBoxReviewFingerprintDto
        {
            TxCount = await query.CountAsync(),
            TotalIn = await query.Where(t => t.TransactionType == "قبض")
                .SumAsync(t => (decimal?)t.Amount) ?? 0,
            TotalOut = await query.Where(t => t.TransactionType == "صرف")
                .SumAsync(t => (decimal?)t.Amount) ?? 0,
            LastTxCreatedAt = await query.MaxAsync(t => t.CreatedAt)
        };
    }

    private static CashBoxReviewStampDto BuildStampDto(CashBoxReview? stamp, CashBoxReviewFingerprintDto live)
    {
        var dto = new CashBoxReviewStampDto { LiveFingerprint = live };
        if (stamp == null) return dto;

        var stored = new CashBoxReviewFingerprintDto
        {
            TxCount = stamp.TxCount,
            TotalIn = stamp.TotalIn,
            TotalOut = stamp.TotalOut,
            LastTxCreatedAt = stamp.LastTxCreatedAt
        };

        dto.StampId = stamp.Id;
        dto.ReviewedByUserName = stamp.ReviewedByUserName;
        dto.ReviewedByName = stamp.ReviewedByName;
        dto.ReviewedAt = stamp.ReviewedAt;
        dto.Note = stamp.Note;
        dto.StoredFingerprint = stored;
        dto.CountDifference = live.TxCount - stored.TxCount;
        dto.TotalInDifference = live.TotalIn - stored.TotalIn;
        dto.TotalOutDifference = live.TotalOut - stored.TotalOut;
        dto.LastTxCreatedAtChanged = live.LastTxCreatedAt != stored.LastTxCreatedAt;
        dto.Status = dto.CountDifference == 0
                     && dto.TotalInDifference == 0
                     && dto.TotalOutDifference == 0
                     && !dto.LastTxCreatedAtChanged
            ? CashBoxReviewStatuses.Reviewed
            : CashBoxReviewStatuses.ChangedAfterReview;
        return dto;
    }

    private async Task<(CashBoxReview? Stamp, bool TableMissing)> TryGetLatestStampAsync(
        int cashBoxId, DateTime from, DateTime to)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            var stamp = await db.CashBoxReviews.AsNoTracking()
                .Where(r => r.CashBoxId == cashBoxId && r.PeriodFrom == from && r.PeriodTo == to)
                .OrderByDescending(r => r.ReviewedAt).ThenByDescending(r => r.Id)
                .FirstOrDefaultAsync();
            return (stamp, false);
        }
        catch (Exception ex) when (IsMissingTableError(ex))
        {
            _logger.LogWarning("CashBoxReviews table is missing — stamp state skipped until cashbox-review.sql runs.");
            return (null, true);
        }
    }

    /// <summary>ختم مراجعة جديد (صف تاريخي جديد — الأحدث يغلب، والتاريخ محفوظ).</summary>
    public async Task<(bool Success, string Message)> StampAsync(
        int cashBoxId, DateTime from, DateTime to, string? note, ClaimsPrincipal user)
    {
        (from, to) = NormalizePeriod(from, to);

        var userName = user?.Identity?.Name;
        if (user?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userName))
            return (false, "تعذر تحديد المستخدم الحالي.");
        if (!string.IsNullOrWhiteSpace(note) && note.Trim().Length > 1000)
            return (false, "ملاحظة المراجعة أطول من الحد المسموح (1000 حرف).");

        try
        {
            await using var db = await _factory.CreateDbContextAsync();

            var boxExists = await db.CashBoxes.AsNoTracking().AnyAsync(c => c.CashBoxId == cashBoxId);
            if (!boxExists) return (false, "الخزينة المختارة غير موجودة.");

            var fingerprint = await ComputeLiveFingerprintAsync(db, cashBoxId, from, to);

            var displayName = await db.Users.AsNoTracking()
                .Where(u => u.Username == userName)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync();
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = user.FindFirst("FullName")?.Value;

            var row = new CashBoxReview
            {
                CashBoxId = cashBoxId,
                PeriodFrom = from,
                PeriodTo = to,
                ReviewedByUserName = userName,
                ReviewedByName = string.IsNullOrWhiteSpace(displayName) ? null : displayName,
                ReviewedAt = DateTime.Now,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                TxCount = fingerprint.TxCount,
                TotalIn = fingerprint.TotalIn,
                TotalOut = fingerprint.TotalOut,
                LastTxCreatedAt = fingerprint.LastTxCreatedAt
            };
            db.CashBoxReviews.Add(row);
            await db.SaveChangesAsync();
            return (true, "تم حفظ ختم المراجعة.");
        }
        catch (Exception ex) when (IsMissingTableError(ex))
        {
            return (false, MissingReviewsTableMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CashBox review stamp failed for box {CashBoxId} {From}..{To}", cashBoxId, from, to);
            return (false, $"حدث خطأ أثناء حفظ ختم المراجعة: {ex.Message}");
        }
    }

    /// <summary>أحدث ختم لكل (خزينة، فترة) عبر كل الخزن، بحالته الحية، بحد أقصى 20.</summary>
    public async Task<CashBoxReviewRecentStampsDto> GetRecentStampsAsync()
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync();

            var all = await db.CashBoxReviews.AsNoTracking()
                .OrderByDescending(r => r.ReviewedAt).ThenByDescending(r => r.Id)
                .ToListAsync();

            var latest = all
                .GroupBy(r => (r.CashBoxId, r.PeriodFrom, r.PeriodTo))
                .Select(g => g.First())
                .OrderByDescending(r => r.ReviewedAt).ThenByDescending(r => r.Id)
                .Take(20)
                .ToList();

            var boxNames = (await _cashBox.GetCashBoxesAsync(false))
                .ToDictionary(b => b.CashBoxId, b => b.CashBoxName);

            var items = new List<CashBoxReviewRecentStampDto>();
            foreach (var stamp in latest)
            {
                var live = await ComputeLiveFingerprintAsync(db, stamp.CashBoxId, stamp.PeriodFrom, stamp.PeriodTo);
                var state = BuildStampDto(stamp, live);
                items.Add(new CashBoxReviewRecentStampDto
                {
                    StampId = stamp.Id,
                    CashBoxId = stamp.CashBoxId,
                    CashBoxName = boxNames.GetValueOrDefault(stamp.CashBoxId, $"#{stamp.CashBoxId}"),
                    PeriodFrom = stamp.PeriodFrom,
                    PeriodTo = stamp.PeriodTo,
                    ReviewedByUserName = stamp.ReviewedByUserName,
                    ReviewedByName = stamp.ReviewedByName,
                    ReviewedAt = stamp.ReviewedAt,
                    Note = stamp.Note,
                    Status = state.Status,
                    CountDifference = state.CountDifference,
                    TotalInDifference = state.TotalInDifference,
                    TotalOutDifference = state.TotalOutDifference,
                    LastTxCreatedAtChanged = state.LastTxCreatedAtChanged
                });
            }

            return new CashBoxReviewRecentStampsDto { Items = items };
        }
        catch (Exception ex) when (IsMissingTableError(ex))
        {
            _logger.LogWarning("CashBoxReviews table is missing — recent stamps skipped until cashbox-review.sql runs.");
            return new CashBoxReviewRecentStampsDto { RequiresSql = true };
        }
    }

    // ════════════════════════════════════════════════════════
    //  أدوات
    // ════════════════════════════════════════════════════════

    private static (DateTime From, DateTime To) NormalizePeriod(DateTime from, DateTime to)
    {
        var f = from.Date;
        var t = to.Date;
        return f <= t ? (f, t) : (t, f);
    }

    /// <summary>خطأ «الجدول غير موجود» (208) — شبكة أمان إن نُشر الكود قبل سكربت SQL.</summary>
    private static bool IsMissingTableError(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
            if (e is Microsoft.Data.SqlClient.SqlException sql && sql.Number == 208)
                return true;
        return false;
    }
}
