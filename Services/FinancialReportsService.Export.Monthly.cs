using ClosedXML.Excel;
using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public partial class FinancialReportsService
{
    private async Task BuildIncomeMonthlySheetAsync(XLWorkbook workbook, IncomeStatementFilterDto filter, BranchScope scope)
    {
        var ws = workbook.Worksheets.Add("تفصيل شهري");
        ws.RightToLeft = true;

        string[] h = { "الشهر", "الإيرادات", "تكلفة المبيعات", "المصروفات", "الرواتب", "إجمالي المصروفات", "صافي الربح", "الهامش %" };
        for (int i = 0; i < h.Length; i++)
            ws.Cell(1, i + 1).Value = h[i];
        var hdr = ws.Row(1);
        hdr.Style.Font.Bold = true;
        hdr.Style.Font.FontColor = XLColor.White;
        hdr.Style.Fill.BackgroundColor = XLColor.MidnightBlue;
        hdr.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        int row = 2;
        decimal tRev = 0, tCogs = 0, tExp = 0, tPay = 0, tTot = 0, tProfit = 0;

        var curMonth = new DateTime(filter.FromDate.Year, filter.FromDate.Month, 1);
        var lastMonth = new DateTime(filter.ToDate.Year, filter.ToDate.Month, 1);
        while (curMonth <= lastMonth)
        {
            var ms = curMonth < filter.FromDate.Date ? filter.FromDate.Date : curMonth;
            var me = curMonth.AddMonths(1).AddDays(-1);
            if (me > filter.ToDate.Date) me = filter.ToDate.Date;

            var summary = await GetSummaryAsync(ms, me, scope);
            decimal revenue = 0, cogs = 0, expenses = 0, payroll = 0;
            if (summary.HasValue)
            {
                revenue = summary.Value.Revenue;
                cogs = summary.Value.Cogs;
                expenses = summary.Value.Expenses;
                payroll = summary.Value.Payroll;
            }
            var totalExpenses = expenses + payroll;
            var profit = revenue - cogs - totalExpenses;
            var margin = revenue == 0 ? 0m : Math.Round(profit / revenue * 100m, 2);

            ws.Cell(row, 1).Value = curMonth.ToString("yyyy/MM");
            ws.Cell(row, 2).Value = revenue;
            ws.Cell(row, 3).Value = cogs;
            ws.Cell(row, 4).Value = expenses;
            ws.Cell(row, 5).Value = payroll;
            ws.Cell(row, 6).Value = totalExpenses;
            ws.Cell(row, 7).Value = profit;
            ws.Cell(row, 8).Value = margin;

            for (int c = 2; c <= 7; c++)
                ws.Cell(row, c).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 8).Style.NumberFormat.Format = "0.00\"%\"";

            tRev += revenue; tCogs += cogs; tExp += expenses; tPay += payroll; tTot += totalExpenses; tProfit += profit;

            row++;
            curMonth = curMonth.AddMonths(1);
        }

        var totRow = ws.Row(row);
        totRow.Style.Font.Bold = true;
        totRow.Style.Fill.BackgroundColor = XLColor.LightGray;
        ws.Cell(row, 1).Value = "الإجمالي";
        ws.Cell(row, 2).Value = tRev;
        ws.Cell(row, 3).Value = tCogs;
        ws.Cell(row, 4).Value = tExp;
        ws.Cell(row, 5).Value = tPay;
        ws.Cell(row, 6).Value = tTot;
        ws.Cell(row, 7).Value = tProfit;
        var tMargin = tRev == 0 ? 0m : Math.Round(tProfit / tRev * 100m, 2);
        ws.Cell(row, 8).Value = tMargin;
        for (int c = 2; c <= 7; c++)
            ws.Cell(row, c).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(row, 8).Style.NumberFormat.Format = "0.00\"%\"";

        ws.Columns().AdjustToContents();
    }
}
