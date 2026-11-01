using ClosedXML.Excel;
using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public partial class FinancialReportsService
{
    private void BuildIncomeSummarySheet(XLWorkbook workbook, IncomeStatementDto dto, string branchName)
    {
        var ws = workbook.Worksheets.Add("ملخص الفترة");
        ws.RightToLeft = true;

        ws.Cell(1, 1).Value = "قائمة الدخل — " + (dto.PeriodLabel ?? "");
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, 2).Merge();

        ws.Cell(2, 1).Value = "الفترة";
        ws.Cell(2, 2).Value = $"{dto.FromDate:yyyy/MM/dd} — {dto.ToDate:yyyy/MM/dd}";
        ws.Cell(3, 1).Value = "الفرع";
        ws.Cell(3, 2).Value = branchName;

        int r = 5;
        ws.Cell(r, 1).Value = "المؤشر";
        ws.Cell(r, 2).Value = "القيمة";
        var hdr = ws.Row(r);
        hdr.Style.Font.Bold = true;
        hdr.Style.Font.FontColor = XLColor.White;
        hdr.Style.Fill.BackgroundColor = XLColor.MidnightBlue;
        hdr.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        r++;

        void AddMetric(string label, decimal value, string format = "#,##0.00", bool bold = false)
        {
            ws.Cell(r, 1).Value = label;
            var c = ws.Cell(r, 2);
            c.Value = value;
            c.Style.NumberFormat.Format = format;
            if (bold)
            {
                ws.Row(r).Style.Font.Bold = true;
                ws.Row(r).Style.Fill.BackgroundColor = XLColor.LightGray;
            }
            r++;
        }

        AddMetric("إجمالي الإيرادات", dto.TotalRevenue);
        AddMetric("إيرادات الفواتير", dto.InvoiceRevenue);
        AddMetric("إيرادات أخرى (رسوم)", dto.OtherRevenue);
        AddMetric("الإيرادات الصافية", dto.NetRevenue, "#,##0.00", true);
        AddMetric("تكلفة المبيعات (COGS)", dto.CostOfGoodsSold);
        AddMetric("الربح الإجمالي", dto.GrossProfit, "#,##0.00", true);
        AddMetric("هامش الربح الإجمالي %", dto.GrossProfitMargin, "0.00\"%\"");
        AddMetric("الرواتب", dto.PayrollExpense);
        AddMetric("المصروفات التشغيلية (شاملة الرواتب)", dto.TotalOperatingExpenses);
        AddMetric("صافي الربح", dto.NetProfit, "#,##0.00", true);
        AddMetric("هامش صافي الربح %", dto.NetProfitMargin, "0.00\"%\"");

        ws.Cell(r, 1).Value = "حالة الربح";
        ws.Cell(r, 2).Value = dto.ProfitStatus;
        r++;

        ws.Cell(r, 1).Value = "— نقطة التعادل —";
        ws.Cell(r, 1).Style.Font.Bold = true;
        r++;
        AddMetric("المصروفات الثابتة", dto.BreakEvenAnalysis.FixedExpenses);
        AddMetric("هامش الربح الإجمالي % (BEP)", dto.BreakEvenAnalysis.GrossProfitMargin, "0.00\"%\"");
        AddMetric("نقطة التعادل (إيرادات)", dto.BreakEvenAnalysis.BreakEvenRevenue);
        AddMetric("الإيرادات الحالية", dto.BreakEvenAnalysis.CurrentRevenue);
        AddMetric("هامش الأمان %", dto.BreakEvenAnalysis.SafetyMargin, "0.00\"%\"");
        ws.Cell(r, 1).Value = "الحالة";
        ws.Cell(r, 2).Value = dto.BreakEvenAnalysis.Status;
        r++;

        ws.Cell(r, 1).Value = "— الهدف الديناميكي —";
        ws.Cell(r, 1).Style.Font.Bold = true;
        r++;
        AddMetric("الهدف الحالي %", dto.ProfitTarget.CurrentTargetPercentage, "0.00\"%\"");
        AddMetric("الربح المحقق %", dto.ProfitTarget.CurrentMargin, "0.00\"%\"");
        AddMetric("الهدف القادم %", dto.ProfitTarget.NextTargetPercentage, "0.00\"%\"");
        AddMetric("نسبة التحقيق %", dto.ProfitTarget.AchievementPercentage, "0.00\"%\"");

        if (dto.PreviousPeriod != null)
        {
            ws.Cell(r, 1).Value = "— مقارنة بالفترة السابقة —";
            ws.Cell(r, 1).Style.Font.Bold = true;
            r++;
            AddMetric("الإيرادات (فترة سابقة)", dto.PreviousPeriod.Revenue);
            AddMetric("المصروفات (فترة سابقة)", dto.PreviousPeriod.Expenses);
            AddMetric("صافي الربح (فترة سابقة)", dto.PreviousPeriod.NetProfit);
            AddMetric("تغيير الإيرادات %", dto.PreviousPeriod.RevenueChange, "0.0\"%\"");
            AddMetric("تغيير المصروفات %", dto.PreviousPeriod.ExpensesChange, "0.0\"%\"");
            AddMetric("تغيير صافي الربح %", dto.PreviousPeriod.NetProfitChange, "0.0\"%\"");
        }
        if (dto.PreviousYear != null)
        {
            ws.Cell(r, 1).Value = "— مقارنة بنفس الفترة (السنة السابقة) —";
            ws.Cell(r, 1).Style.Font.Bold = true;
            r++;
            AddMetric("الإيرادات (سنة سابقة)", dto.PreviousYear.Revenue);
            AddMetric("المصروفات (سنة سابقة)", dto.PreviousYear.Expenses);
            AddMetric("صافي الربح (سنة سابقة)", dto.PreviousYear.NetProfit);
            AddMetric("تغيير الإيرادات %", dto.PreviousYear.RevenueChange, "0.0\"%\"");
            AddMetric("تغيير المصروفات %", dto.PreviousYear.ExpensesChange, "0.0\"%\"");
            AddMetric("تغيير صافي الربح %", dto.PreviousYear.NetProfitChange, "0.0\"%\"");
        }

        ws.Columns().AdjustToContents();
    }
}
