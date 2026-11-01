using ClosedXML.Excel;
using COCOBOLOERPNEW.DTOs;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace COCOBOLOERPNEW.Services;

public partial class FinancialReportsService
{
    public async Task<byte[]> ExportIncomeStatementToExcelAsync(IncomeStatementFilterDto filter)
    {
        var exportFilter = new IncomeStatementFilterDto
        {
            FromDate = filter.FromDate,
            ToDate = filter.ToDate,
            PeriodType = filter.PeriodType,
            IncludeComparison = true,
            IncludeMonthlyTrend = true,
            BranchId = filter.BranchId
        };

        var dto = await GetIncomeStatementAsync(exportFilter);
        var scope = await GetBranchScopeAsync(filter.BranchId);

        string branchName = "كل الفروع";
        if (filter.BranchId.HasValue)
        {
            branchName = await _db.Branches.AsNoTracking()
                .Where(b => b.BranchId == filter.BranchId.Value)
                .Select(b => b.BranchNameAr)
                .FirstOrDefaultAsync() ?? $"فرع #{filter.BranchId.Value}";
        }

        using var workbook = new XLWorkbook();

        BuildIncomeSummarySheet(workbook, dto, branchName);
        await BuildIncomeMonthlySheetAsync(workbook, filter, scope);
        BuildIncomeExpenseGroupsSheet(workbook, dto);
        BuildIncomeTopExpensesSheet(workbook, dto);
        BuildIncomeOtherRevenueSheet(workbook, dto);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
