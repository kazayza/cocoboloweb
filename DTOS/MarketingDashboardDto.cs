namespace COCOBOLOERPNEW.DTOs;

// ═══════════════════════════════════════════════════════════════
// 📊 Marketing Performance Dashboard — DTOs
// ═══════════════════════════════════════════════════════════════

public class MarketingDashboardDto
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public string Channel { get; set; } = "all";
    public string ChannelName { get; set; } = "الكل";
    public double? OverallDelta { get; set; }

    // 🪜 القمع (5 مراحل)
    public List<MarketingFunnelStageDto> Funnel { get; set; } = new();

    // 🃏 الكروت
    public List<MarketingKpiDto> Kpis { get; set; } = new();

    // 📢 أداء الحملات (من AdSetName + AdName)
    public List<MarketingCampaignDto> TopCampaigns { get; set; } = new();
    public List<MarketingCampaignDto> WorstCampaigns { get; set; } = new();

    // 🌐 أداء المصادر (مصدر العميل)
    public List<MarketingChannelDto> Channels { get; set; } = new();

    // 🎯 المستهدف مقابل الفعلي
    public List<MarketingTargetDto> Targets { get; set; } = new();
    public decimal MonthlySalesTarget { get; set; } = 3_000_000m;
    public int SalesEmployeeCount { get; set; }
    public decimal PerEmployeeMonthlyTarget { get; set; }

    // 📊 التسويق مقابل المبيعات
    public MarketingVsSalesDto VsSales { get; set; } = new();

    // ⚠️ التنبيهات + المؤشر
    public List<MarketingAlertDto> Alerts { get; set; } = new();
    public double PerformanceScore { get; set; }
    public string ScoreLabel { get; set; } = "";
    public string ScoreClass { get; set; } = "score-excellent";

    // 📈 الاتجاهات
    public List<MarketingTrendDto> LeadsTrend { get; set; } = new();
    public List<MarketingTrendDto> ConversionTrend { get; set; } = new();
    public List<MarketingTrendDto> CplTrend { get; set; } = new();
    public List<MarketingTrendDto> RoasTrend { get; set; } = new();

    public bool RevenueIsOverall { get; set; }
}

// ─────────────────────────────────────────────
// 🃏 KPI Cards
// ─────────────────────────────────────────────
public class MarketingKpiDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public string Sub { get; set; } = "";
    public string SubClass { get; set; } = "";
    public double? Delta { get; set; }
    public bool GoodWhenUp { get; set; } = true;
    public bool IsMoney { get; set; }
    public string Color { get; set; } = "#3b82f6";
    public string Icon { get; set; } = "trending_up";
}

// ─────────────────────────────────────────────
// 🪜 القمع
// ─────────────────────────────────────────────
public class MarketingFunnelStageDto
{
    public int Step { get; set; }
    public string Name { get; set; } = "";
    public string Sub { get; set; } = "";
    public int Count { get; set; }
    public double PercentOfFirst { get; set; }
    public double? Retention { get; set; }
    public double? DropOff { get; set; }
}

// ─────────────────────────────────────────────
// 📢 الحملات
// ─────────────────────────────────────────────
public class MarketingCampaignDto
{
    public string Name { get; set; } = "";
    public int Leads { get; set; }
    public int Contacted { get; set; }
    public int Qualified { get; set; }
    public int Deals { get; set; }
    public double Rate { get; set; }
}

// ─────────────────────────────────────────────
// 🌐 المصادر
// ─────────────────────────────────────────────
public class MarketingChannelDto
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#94a3b8";
    public int Leads { get; set; }
    public int Contacted { get; set; }
    public int Qualified { get; set; }
    public int Customers { get; set; }
    public decimal Spend { get; set; }
    public decimal Cpl { get; set; }
    public decimal Cac { get; set; }
    public bool IsTotal { get; set; }
}

// ─────────────────────────────────────────────
// 🎯 المستهدف
// ─────────────────────────────────────────────
public class MarketingTargetDto
{
    public string Metric { get; set; } = "";
    public string Target { get; set; } = "";
    public string Actual { get; set; } = "";
    public double Percent { get; set; }
    public bool Achieved { get; set; }
}

// ─────────────────────────────────────────────
// 📊 التسويق مقابل المبيعات
// ─────────────────────────────────────────────
public class MarketingVsSalesDto
{
    public int Leads { get; set; }
    public int Contacted { get; set; }
    public int Qualified { get; set; }
    public int Opportunities { get; set; }
    public int Customers { get; set; }
    public int SalesInvoices { get; set; }
    public decimal SalesValue { get; set; }
    public double ContactedRate { get; set; }
    public double QualifiedRate { get; set; }
    public string StatusText { get; set; } = "";
    public string StatusClass { get; set; } = "";
}

// ─────────────────────────────────────────────
// ⚠️ التنبيهات
// ─────────────────────────────────────────────
public class MarketingAlertDto
{
    public string Text { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Type { get; set; } = "info";
}

// ─────────────────────────────────────────────
// 📈 الاتجاهات
// ─────────────────────────────────────────────
public class MarketingTrendDto
{
    public string Label { get; set; } = "";
    public double Value { get; set; }
    public string ChartLabel => "\u2067" + Label + "\u2069";
}
