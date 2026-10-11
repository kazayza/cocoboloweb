using ClosedXML.Excel;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// تصدير جدول زكي لملف Excel (من غير ما نبعت حاجة لقاعدة البيانات).
/// الملف RTL، العناوين بالعربي، والصف الأول ثابت.
/// </summary>
public static class ZakiExcelExporter
{
    private static readonly char[] InvalidSheetChars = { '[', ']', ':', '*', '?', '/', '\\' };

    /// <summary>بيرجّع الملف كـ Base64 عشان يتنزل من المتصفح</summary>
    public static string ToBase64(ZakiTable table)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SafeSheetName(table.Title));
        ws.RightToLeft = true;

        // العناوين
        for (var c = 0; c < table.Headers.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = table.Headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A5F");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // الصفوف
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            for (var c = 0; c < row.Count && c < table.Headers.Count; c++)
                ws.Cell(r + 2, c + 1).Value = row[c];
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>اسم ملف آمن للتنزيل</summary>
    public static string SafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(title.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        if (clean.Length == 0) clean = "zaki-report";
        return clean.Length > 80 ? clean[..80] : clean;
    }

    private static string SafeSheetName(string title)
    {
        var clean = new string(title.Select(ch => InvalidSheetChars.Contains(ch) ? ' ' : ch).ToArray()).Trim();
        if (clean.Length == 0) clean = "زكي";
        return clean.Length > 31 ? clean[..31] : clean;
    }
}
