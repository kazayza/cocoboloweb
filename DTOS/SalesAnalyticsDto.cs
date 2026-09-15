namespace COCOBOLOERPNEW.DTOs;

// ═══════════════════════════════════════════════════════════
// 📈 لوحة تحليلات المبيعات — DTOs
// ═══════════════════════════════════════════════════════════

public class SalesAnalyticsFilterDto
{
    public DateTime DateFrom { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    public DateTime DateTo { get; set; } = DateTime.Today;
    public int? BranchId { get; set; }
    public int? EmployeeId { get; set; }
    public string? InvoiceStatus { get; set; }   // null = الكل (عدا الملغي دائماً)
}

public class SalesAnalyticsOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? BranchId { get; set; }           // للموظفين فقط
}

public class SalesAnalyticsFilterDataDto
{
    public List<SalesAnalyticsOptionDto> Branches { get; set; } = new();
    public List<SalesAnalyticsOptionDto> SalesEmployees { get; set; } = new();
    public List<string> InvoiceStatuses { get; set; } = new();
}

public class SalesAnalyticsDashboardDto
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public DateTime PrevFrom { get; set; }
    public DateTime PrevTo { get; set; }
    public int PeriodDays { get; set; }

    // 🛡️ حالة السكوب المطبّق (للشارة فوق اللوحة)
    public DateTime? ScopeFromDate { get; set; }        // تاريخ بدء الاطلاع (null للـ Admin)
    public bool UserCanSeeAccountManagerInvoices { get; set; }
    public bool HiddenAccountManagerSales { get; set; } // هل استُبعدت فواتير مديري الحسابات فعلياً؟
    // ── مؤشرات الكروت (الحالية مقابل السابقة) ──
    public decimal TotalAmount { get; set; }
    public decimal PrevTotalAmount { get; set; }
    public int InvoiceCount { get; set; }
    public int PrevInvoiceCount { get; set; }
    public decimal AvgInvoice { get; set; }
    public decimal PrevAvgInvoice { get; set; }
    public int Customers { get; set; }
    public int PrevCustomers { get; set; }

    // ── معدل تحويل الفريق (من فرص البيع) ──
    public int TeamOpportunities { get; set; }
    public int TeamWonDeals { get; set; }
    public double TeamConversionRate { get; set; }   // %
    public int PrevTeamOpportunities { get; set; }
    public int PrevTeamWonDeals { get; set; }
    public double PrevTeamConversionRate { get; set; }

    // ── أداء الموظفين ──
    public List<SalesAnalyticsEmployeeDto> Employees { get; set; } = new();

    // ── بيانات الرسوم ──
    public List<string> TrendLabels { get; set; } = new();
    public List<decimal> TrendCurrent { get; set; } = new();
    public List<decimal> TrendPrev { get; set; } = new();
    public List<SalesAnalyticsLabelValueDto> Branches { get; set; } = new();
    public List<SalesAnalyticsLabelValueDto> Share { get; set; } = new();
    public List<SalesAnalyticsLabelValueDto> PaymentMethods { get; set; } = new();

    // ── التحصيل وجودة العملاء ──
    public double CollectionRate { get; set; }          // % من قيمة الفواتير تم تحصيله
    public decimal CollectedAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public int NewCustomers { get; set; }
    public int ReturningCustomers { get; set; }

    // ── حالات فرص الفريق وأسباب الخسارة ──
    public int TeamInProgress { get; set; }             // فرص شغّالة (لم تغلق بعد)
    public int TeamLostOpportunities { get; set; }      // خسارة + غير مهتم
    public List<SalesAnalyticsLabelValueDto> LostReasons { get; set; } = new();

    // ── أهم العملاء + قراءات ذكية ──
    public List<SalesAnalyticsCustomerDto> TopCustomers { get; set; } = new();
    public List<string> Insights { get; set; } = new();

    // اختصارات نسب التغير (نسبة مئوية أو null لو الأساس صفر)
    public double? TotalGrowthPct => CalcPct(TotalAmount, PrevTotalAmount);
    public double? CountGrowthPct => CalcPct(InvoiceCount, PrevInvoiceCount);
    public double? AvgGrowthPct => CalcPct(AvgInvoice, PrevAvgInvoice);
    public double? CustomersGrowthPct => CalcPct(Customers, PrevCustomers);

    private static double? CalcPct(decimal cur, decimal prev)
        => prev > 0 ? (double)((cur - prev) / prev * 100m) : (double?)null;
    private static double? CalcPct(int cur, int prev)
        => prev > 0 ? (double)(cur - prev) / prev * 100 : (double?)null;
}

public class SalesAnalyticsEmployeeDto
{
    public int EmpId { get; set; }                    // للربط الداخلي بفواتير الموظف
    public string Name { get; set; } = "";
    public int Opportunities { get; set; }        // إجمالي فرصه البيعية في النطاق
    public int WonDeals { get; set; }             // المغلقة (تم البيع)
    public double ConversionRate { get; set; }    // % = مغلقة ÷ فرصه
    public int InvoiceCount { get; set; }
    public decimal SalesAmount { get; set; }      // قيمة فواتيره
    public decimal AvgInvoice { get; set; }       // متوسط فاتورته
    public double AvgCycleDays { get; set; }      // متوسط أيام دورة البيع (فرصة ← فاتورة)
    public List<SalesAnalyticsInvoiceItemDto> Invoices { get; set; } = new(); // للـ Tooltip + دايلوج التفاصيل
}

/// <summary>عميل ضمن قائمة «أهم العملاء» مع فواتيره (للدايلوج)</summary>
public class SalesAnalyticsCustomerDto
{
    public int PartyId { get; set; }
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public int InvoiceCount { get; set; }
    public double Pct { get; set; }                 // % من إجمالي قيمة الفترة
    public List<SalesAnalyticsInvoiceItemDto> Invoices { get; set; } = new();
}

/// <summary>فاتورة واحدة تخص موظفاً (اسم العميل + رقم الفاتورة + المبلغ + تواريخ)</summary>
public class SalesAnalyticsInvoiceItemDto
{
    public string Customer { get; set; } = "";
    public string Reference { get; set; } = "";
    public decimal Net { get; set; }
    public DateTime Date { get; set; }               // تاريخ الفاتورة
    public DateTime? OpportunityDate { get; set; }   // تاريخ الفرصة المرتبطة (إن وُجدت)
}

public class SalesAnalyticsLabelValueDto
{
    public string Label { get; set; } = "";
    public decimal Value { get; set; }
}

// ── تصدير Excel ──
public class SalesAnalyticsInvoiceRowDto
{
    public DateTime Date { get; set; }
    public string ReferenceNumber { get; set; } = "";
    public string PartyName { get; set; } = "";
    public string EmployeeName { get; set; } = "";   // المالك (مسؤول الفرصة) أو منشئ الفاتورة
    public string BranchName { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal Net { get; set; }
}
