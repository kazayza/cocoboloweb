namespace COCOBOLOERPNEW.DTOs;

/// <summary>حالات ختم المراجعة لفترة خزينة.</summary>
public static class CashBoxReviewStatuses
{
    public const string NotReviewed = "NotReviewed";
    public const string Reviewed = "Reviewed";
    public const string ChangedAfterReview = "ChangedAfterReview";
}

/// <summary>بصمة فترة: عدد الحركات + إجماليا الاتجاهين + أحدث وقت إنشاء — أساس كشف التغيير بعد المراجعة.</summary>
public class CashBoxReviewFingerprintDto
{
    public int TxCount { get; set; }
    public decimal TotalIn { get; set; }
    public decimal TotalOut { get; set; }
    public DateTime? LastTxCreatedAt { get; set; }
}

/// <summary>صف تفصيل المصادر: فئة عربية مجمعة + الأنواع الخام التي كوّنتها.</summary>
public class CashBoxReviewSourceDto
{
    public string Category { get; set; } = "";
    public List<string> RawReferenceTypes { get; set; } = new();
    public int Count { get; set; }
    public decimal TotalIn { get; set; }
    public decimal TotalOut { get; set; }
}

/// <summary>يوم أُغلق رصيده الجاري (من أول الفترة) على قيمة سالبة.</summary>
public class CashBoxReviewNegativeDayDto
{
    public DateTime Date { get; set; }
    public decimal ClosingBalance { get; set; }
}

/// <summary>
/// مساعدات المراجعة المحسوبة من مجموعة حركات الفترة المعروضة.
/// قوائم المعرفات تتيح للصفحة عرض المجموعات الجزئية دون إعادة اشتقاق القواعد.
/// </summary>
public class CashBoxReviewAidsDto
{
    public List<int> ManualMovementIds { get; set; } = new();
    public List<int> NoReferenceMovementIds { get; set; } = new();
    public List<int> BackdatedMovementIds { get; set; } = new();
    public List<CashBoxTransactionDto> LargestMovements { get; set; } = new();
    public List<CashBoxReviewNegativeDayDto> NegativeDays { get; set; } = new();
    public List<int> NegativeDayMovementIds { get; set; } = new();

    /// <summary>الأيام السالبة تُحسب فقط عند اكتمال مجموعة الحركات (غير مقطوعة بالحد الأقصى).</summary>
    public bool NegativeDaysAvailable { get; set; }
}

/// <summary>حالة الختم الحالية لفترة مختارة، مع البصمة المخزنة والحية وفروق التغيير.</summary>
public class CashBoxReviewStampDto
{
    public string Status { get; set; } = CashBoxReviewStatuses.NotReviewed;

    public int? StampId { get; set; }
    public string? ReviewedByUserName { get; set; }
    public string? ReviewedByName { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? Note { get; set; }

    public CashBoxReviewFingerprintDto? StoredFingerprint { get; set; }
    public CashBoxReviewFingerprintDto LiveFingerprint { get; set; } = new();

    // فروق «الحي − المخزن» عند تغيّر الفترة بعد المراجعة
    public int CountDifference { get; set; }
    public decimal TotalInDifference { get; set; }
    public decimal TotalOutDifference { get; set; }
    public bool LastTxCreatedAtChanged { get; set; }
}

/// <summary>نتيجة مراجعة خزينة لفترة: أرقام التعريف الموحد + الحركات + المصادر + المساعدات + حالة الختم.</summary>
public class CashBoxReviewDto
{
    public int CashBoxId { get; set; }
    public string CashBoxName { get; set; } = "";
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }

    public decimal OpeningBalance { get; set; }
    public decimal TotalIn { get; set; }
    public decimal TotalOut { get; set; }
    public decimal ClosingBalance => OpeningBalance + TotalIn - TotalOut;

    /// <summary>حركات الفترة مرتبة زمنيًا تصاعديًا، بحد أقصى للعرض (IsTruncated عند تجاوزه).</summary>
    public List<CashBoxTransactionDto> Movements { get; set; } = new();
    public int TotalMovementCount { get; set; }
    public bool IsTruncated { get; set; }

    public List<CashBoxReviewSourceDto> SourceBreakdown { get; set; } = new();
    public CashBoxReviewAidsDto Aids { get; set; } = new();
    public CashBoxReviewStampDto Stamp { get; set; } = new();

    /// <summary>جدول الأختام غير مهيأ — القراءات تعمل والتدهور آمن، والختم معطّل حتى تشغيل cashbox-review.sql.</summary>
    public bool RequiresSql { get; set; }
}

/// <summary>أحدث ختم لكل (خزينة، فترة) مع حالته الحية — لشريط الأختام الأخيرة.</summary>
public class CashBoxReviewRecentStampDto
{
    public int StampId { get; set; }
    public int CashBoxId { get; set; }
    public string CashBoxName { get; set; } = "";
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    public string? ReviewedByUserName { get; set; }
    public string? ReviewedByName { get; set; }
    public DateTime ReviewedAt { get; set; }
    public string? Note { get; set; }

    public string Status { get; set; } = CashBoxReviewStatuses.Reviewed;
    public int CountDifference { get; set; }
    public decimal TotalInDifference { get; set; }
    public decimal TotalOutDifference { get; set; }
    public bool LastTxCreatedAtChanged { get; set; }
}

public class CashBoxReviewRecentStampsDto
{
    public List<CashBoxReviewRecentStampDto> Items { get; set; } = new();
    public bool RequiresSql { get; set; }
}
