using System;

namespace COCOBOLOERPNEW.Models;

/// <summary>
/// ختم مراجعة خزينة لفترة محددة — سجل تاريخي خفيف بلا أي قفل أو منع تعديل.
/// كل ختم صف جديد؛ الأحدث لنفس (الخزينة، من، إلى) هو الحالة الفعالة، وبصمته
/// (عدد الحركات وإجماليا الوارد والصادر وأحدث وقت إنشاء) تُقارن بالبيانات
/// الحية لكشف أي تغيير لاحق داخل الفترة المراجعة.
/// الجدول يُنشأ بـ cashbox-review.sql.
/// </summary>
public partial class CashBoxReview
{
    public int Id { get; set; }

    public int CashBoxId { get; set; }

    /// <summary>بداية الفترة المراجعة (تاريخ فقط)</summary>
    public DateTime PeriodFrom { get; set; }

    /// <summary>نهاية الفترة المراجعة (تاريخ فقط)</summary>
    public DateTime PeriodTo { get; set; }

    /// <summary>اسم مستخدم المراجع</summary>
    public string ReviewedByUserName { get; set; } = null!;

    /// <summary>لقطة الاسم المعروض للمراجع وقت الختم</summary>
    public string? ReviewedByName { get; set; }

    public DateTime ReviewedAt { get; set; }

    /// <summary>ملاحظة اختيارية من المراجع</summary>
    public string? Note { get; set; }

    // ── بصمة الفترة وقت الختم ──────────────────────────────
    public int TxCount { get; set; }

    public decimal TotalIn { get; set; }

    public decimal TotalOut { get; set; }

    /// <summary>أحدث CreatedAt بين حركات الفترة وقت الختم — NULL إن لم توجد حركات أو أوقات إنشاء</summary>
    public DateTime? LastTxCreatedAt { get; set; }

    public virtual CashBox CashBox { get; set; } = null!;
}
