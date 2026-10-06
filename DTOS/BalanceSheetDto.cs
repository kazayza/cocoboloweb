namespace COCOBOLOERPNEW.DTOs;

// ============================================================
// قائمة المركز المالي (الميزانية) — كشف المركز المالي كما في تاريخه
// ============================================================
public class BalanceSheetDto
{
    public DateTime AsOfDate { get; set; }
    public string AsOfLabel { get; set; } = "";

    // ── نطاق العرض: Null = الشركة كلها / قيمة = فرع محدد
    public int? BranchId { get; set; }
    public string? BranchName { get; set; }
    public bool IsBranchScoped => BranchId.HasValue;

    // ────────────── الأصول المتداولة ──────────────
    public decimal CashBalance { get; set; }                 // رصيد الخزائن (افتتاحي + قبض - صرف)
    public int CashBoxesCount { get; set; }
    public decimal AccountsReceivable { get; set; }          // مدينون: متبقي فواتير البيع (GrandTotal - Paid)
    public int ReceivableInvoicesCount { get; set; }
    public decimal InventoryValue { get; set; }              // المخزون: الأرصدة × تكلفة باقة المنتج نفسه
    public int InventoryItemsCount { get; set; }
    public decimal EmployeeLoansOutstanding { get; set; }    // سلف/قروض موظفين قائمة (أصل)
    public int ActiveLoansCount { get; set; }
    public decimal PrepaidExpensesAsset { get; set; }      // مصروفات مدفوعة مقدمًا: الأشهر المستقبلية للمصروف المقدم (أصل)

    public decimal TotalAssets { get; set; }

    // ────────────── الالتزامات ──────────────
    public decimal PayrollOutstanding { get; set; }          // رواتب غير مدفوعة (عدا المرفوض)
    public int PayrollPendingCount { get; set; }
    public decimal SupplierPayables { get; set; }            // دائنون: متبقي فواتير الشراء (GrandTotal - Paid)
    public int PayablePurchasesCount { get; set; }
    public decimal CustomerAdvances { get; set; }          // دفعات عملاء مقدمة مقبوضة لم تُطبق على فواتير (التزام)

    public decimal TotalLiabilities { get; set; }

    // ────────────── حقوق الملكية ──────────────
    public decimal OpeningCapital { get; set; }              // رأس مال افتتاحي (إدخال يدوي Admin)
    public string? CapitalNotes { get; set; }
    public DateTime? CapitalUpdatedAt { get; set; }
    public string? CapitalUpdatedBy { get; set; }
    public decimal RetainedEarnings { get; set; }            // أرباح محتجزة = صافي ربح قائمة الدخل التراكمي
    public decimal CumulativeRevenue { get; set; }           // تفصيل التراكمي: الإيرادات
    public decimal CumulativeCogs { get; set; }              // تفصيل التراكمي: تكلفة المبيعات
    public decimal CumulativeOperatingExpenses { get; set; } // تفصيل التراكمي: المصروفات التشغيلية
    public DateTime CumulativeFrom { get; set; }             // أول حركة في النظام
    public decimal TotalEquity { get; set; }

    // ────────────── التوازن ──────────────
    public decimal LiabilitiesAndEquity { get; set; }        // الالتزامات + حقوق الملكية
    public decimal BalanceDifference { get; set; }           // الأصول - (الالتزامات + حقوق الملكية)
    public bool IsBalanced => Math.Abs(BalanceDifference) < 0.5m;
    public string BalanceStatus => IsBalanced ? "متوازنة" : (BalanceDifference > 0 ? "فرق إيجابي" : "فرق سلبي");
}
