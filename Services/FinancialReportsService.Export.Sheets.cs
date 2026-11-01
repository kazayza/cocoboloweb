using ClosedXML.Excel;
using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public partial class FinancialReportsService
{
    private void BuildIncomeExpenseGroupsSheet(XLWorkbook workbook, IncomeStatementDto dto)
    {
        var ws = workbook.Worksheets.Add("المصروفات حسب المجموعة");
        ws.RightToLeft = true;
        string[] h = { "المجموعة", "المبلغ", "النسبة %", "العدد" };
        for (int i = 0; i < h.Length; i++) ws.Cell(1, i + 1).Value = h[i];
        var hdr = ws.Row(1);
        hdr.Style.Font.Bold = true;
        hdr.Style.Font.FontColor = XLColor.White;
        hdr.Style.Fill.BackgroundColor = XLColor.MidnightBlue;
        hdr.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        int r = 2;
        foreach (var g in dto.ExpensesByGroup)
        {
            ws.Cell(r, 1).Value = g.GroupName;
            ws.Cell(r, 2).Value = g.Amount;
            ws.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(r, 3).Value = g.Percentage;
            ws.Cell(r, 3).Style.NumberFormat.Format = "0.0\"%\"";
            ws.Cell(r, 4).Value = g.Count;
            r++;
        }
        var tot = ws.Row(r);
        tot.Style.Font.Bold = true;
        tot.Style.Fill.BackgroundColor = XLColor.LightGray;
        ws.Cell(r, 1).Value = "الإجمالي";
        ws.Cell(r, 2).Value = dto.TotalOperatingExpenses;
        ws.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
        ws.Columns().AdjustToContents();
    }

    private void BuildIncomeTopExpensesSheet(XLWorkbook workbook, IncomeStatementDto dto)
    {
        var ws = workbook.Worksheets.Add("أعلى 10 مصروفات");
        ws.RightToLeft = true;
        string[] h = { "المصروف", "المجموعة", "التاريخ", "المبلغ", "النسبة %" };
        for (int i = 0; i < h.Length; i++) ws.Cell(1, i + 1).Value = h[i];
        var hdr = ws.Row(1);
        hdr.Style.Font.Bold = true;
        hdr.Style.Font.FontColor = XLColor.White;
        hdr.Style.Fill.BackgroundColor = XLColor.MidnightBlue;
        hdr.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        int r = 2;
        foreach (var t in dto.TopExpenses)
        {
            ws.Cell(r, 1).Value = t.ExpenseName;
            ws.Cell(r, 2).Value = t.GroupName ?? "";
            ws.Cell(r, 3).Value = t.Date.ToString("yyyy/MM/dd");
            ws.Cell(r, 4).Value = t.Amount;
            ws.Cell(r, 4).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(r, 5).Value = t.PercentageOfTotal;
            ws.Cell(r, 5).Style.NumberFormat.Format = "0.0\"%\"";
            r++;
        }
        ws.Columns().AdjustToContents();
    }

    private void BuildIncomeOtherRevenueSheet(XLWorkbook workbook, IncomeStatementDto dto)
    {
        var ws = workbook.Worksheets.Add("إيرادات أخرى");
        ws.RightToLeft = true;
        string[] h = { "نوع الرسوم", "المبلغ", "العدد", "النسبة %" };
        for (int i = 0; i < h.Length; i++) ws.Cell(1, i + 1).Value = h[i];
        var hdr = ws.Row(1);
        hdr.Style.Font.Bold = true;
        hdr.Style.Font.FontColor = XLColor.White;
        hdr.Style.Fill.BackgroundColor = XLColor.MidnightBlue;
        hdr.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        int r = 2;
        foreach (var o in dto.OtherRevenueByType)
        {
            ws.Cell(r, 1).Value = string.IsNullOrWhiteSpace(o.ChargeTypeName) ? o.ChargeType : o.ChargeTypeName;
            ws.Cell(r, 2).Value = o.Amount;
            ws.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(r, 3).Value = o.Count;
            ws.Cell(r, 4).Value = o.Percentage;
            ws.Cell(r, 4).Style.NumberFormat.Format = "0.0\"%\"";
            r++;
        }
        var tot = ws.Row(r);
        tot.Style.Font.Bold = true;
        tot.Style.Fill.BackgroundColor = XLColor.LightGray;
        ws.Cell(r, 1).Value = "الإجمالي";
        ws.Cell(r, 2).Value = dto.OtherRevenue;
        ws.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
        ws.Columns().AdjustToContents();
    }
}
