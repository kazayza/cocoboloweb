namespace COCOBOLOERPNEW.DTOs;

// ════════════════════════════════════════════════════════════
//  لوحة القيادة التنفيذية — Executive Cockpit DTOs
// ════════════════════════════════════════════════════════════

public class ExecutiveKpiDto
{
    public decimal Value { get; set; }
    public decimal PrevValue { get; set; }
    public decimal YearValue { get; set; }
    public decimal PrevPct => PrevValue == 0 ? 0 : Math.Round((Value - PrevValue) / Math.Abs(PrevValue) * 100, 1);
    public decimal YearPct => YearValue == 0 ? 0 : Math.Round((Value - YearValue) / Math.Abs(YearValue) * 100, 1);
}

public class ExecutiveTrendPointDto
{
    public string Label { get; set; } = "";
    public decimal Sales { get; set; }
    public decimal Collected { get; set; }
    public decimal Purchases { get; set; }
}

public class ExecutiveEmployeeRowDto
{
    public int EmployeeId { get; set; }
    public string FullName { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string Attendance { get; set; } = "";
    public int ActionsToday { get; set; }
    public int ActionsMonth { get; set; }
    public List<int> Heat7 { get; set; } = new();
    public DateTime? LastActivity { get; set; }
    public string LastActivityText { get; set; } = "";
    public decimal MonthSales { get; set; }
    public decimal MonthCollection { get; set; }
    public int TasksPending { get; set; }
    public int TasksOverdue { get; set; }
    public string Status { get; set; } = "green";   // green | yellow | red | off (موقوف/إجازة) | gray (بلا حساب نظام)
    public bool IsSales { get; set; }
    public string HrStatus { get; set; } = "";      // نشط / موقوف / بالإجازة / مستقيل
    public bool HasAccount { get; set; }
}

public class ExecutiveBranchSaleDto
{
    public string Name { get; set; } = "";
    public decimal Value { get; set; }
}

public class ExecutiveGmActionDto
{
    public string When { get; set; } = "";
    public string What { get; set; } = "";
}

public class ExecutiveGmDto
{
    public bool Found { get; set; }
    public string FullName { get; set; } = "";
    public string LastActivityText { get; set; } = "";
    public int ActionsWeek { get; set; }
    public List<ExecutiveGmActionDto> Recent { get; set; } = new();
    public int PendingPrice { get; set; }
    public int EscalatedComplaints { get; set; }
    public decimal TeamSalesMonth { get; set; }
    public int TeamActiveToday { get; set; }
    public int TeamTotal { get; set; }
}

public class ExecutiveExceptionsDto
{
    public int Debts30 { get; set; }
    public decimal Debts30Amount { get; set; }
    public int LateDeliveries { get; set; }
    public int PendingPrice { get; set; }
    public int NegativeStock { get; set; }
    public int StalledLeads { get; set; }
    public int OpenComplaints { get; set; }
}

public class ExecutiveOpsDto
{
    public int FactoryOpen { get; set; }
    public int FactoryMonth { get; set; }
    public int OpenComplaints { get; set; }
    public int NewLeads { get; set; }
}

public class ExecutiveCockpitDto
{
    public ExecutiveKpiDto Sales { get; set; } = new();
    public ExecutiveKpiDto Collected { get; set; } = new();
    public ExecutiveKpiDto Purchases { get; set; } = new();
    public ExecutiveKpiDto Expenses { get; set; } = new();
    public ExecutiveKpiDto Net { get; set; } = new();

    public List<ExecutiveTrendPointDto> Trend { get; set; } = new();
    public List<ExecutiveEmployeeRowDto> Employees { get; set; } = new();

    public ExecutiveGmDto Gm { get; set; } = new();
    public ExecutiveExceptionsDto Exceptions { get; set; } = new();
    public ExecutiveOpsDto Ops { get; set; } = new();

    public string PeriodLabel { get; set; } = "";
    public string Story { get; set; } = "";
    public int DecisionsPending { get; set; }
    public int DormantCount { get; set; }

    // مصادر إضافية: خزينة · بايبلاين الفرص · B2B · الفروع
    public decimal CashBalance { get; set; }
    public int PipelineOpen { get; set; }
    public decimal PipelineValue { get; set; }
    public int PipelineStalled { get; set; }
    public int B2bPending { get; set; }
    public List<ExecutiveBranchSaleDto> BranchSales { get; set; } = new();

    // توقع إقفال الشهر الحالي
    public decimal ForecastValue { get; set; }
    public decimal LastMonthSales { get; set; }
    public decimal ForecastVsLastPct { get; set; }

    public DateTime GeneratedAt { get; set; } = DateTime.Now;
}

public class ExecutiveTimelineRowDto
{
    public DateTime When { get; set; }
    public string What { get; set; } = "";
    public string Kind { get; set; } = "";   // audit | invoice
}
