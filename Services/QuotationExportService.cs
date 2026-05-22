using ClosedXML.Excel;
using COCOBOLOERPNEW.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// مولّد PDF و Excel لعروض الأسعار.
/// PDF: QuestPDF + خط Cairo للعربي.
/// Excel: ClosedXML.
/// </summary>
public class QuotationExportService : IQuotationExportService
{
    private readonly IQuotationService _quotations;
    private readonly ILogger<QuotationExportService> _logger;
    private readonly IWebHostEnvironment _env;

    // يتم تحميل الخط مرة واحدة وتخزينه
    private static byte[]? _arabicFont;
    private static byte[]? _arabicFontBold;
    private static readonly object _fontLock = new();

    public QuotationExportService(
        IQuotationService quotations,
        ILogger<QuotationExportService> logger,
        IWebHostEnvironment env)
    {
        _quotations = quotations;
        _logger = logger;
        _env = env;
    }

    // ============================================================
    // PDF
    // ============================================================
    public async Task<(bool Success, string? Error, byte[]? Pdf, string FileName)> GeneratePdfAsync(int quotationId)
    {
        try
        {
            var data = await _quotations.GetQuotationForPrintAsync(quotationId);
            if (data == null)
                return (false, "عرض السعر غير موجود.", null, "");

            LoadArabicFontIfNeeded();

            var fileName = $"Quotation-{data.Quotation.ReferenceNumber}.pdf";

            QuestPDF.Settings.License = LicenseType.Community;

            var bytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(t => t
                        .FontFamily(GetFontFamily())
                        .FontSize(10)
                        .DirectionFromRightToLeft());

                    page.Header().Element(c => BuildHeader(c, data));
                    page.Content().Element(c => BuildBody(c, data));
                    page.Footer().Element(c => BuildFooter(c, data));
                });
            }).GeneratePdf();

            return (true, null, bytes, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate PDF for quotation {Id}", quotationId);
            return (false, "تعذّر توليد ملف PDF.", null, "");
        }
    }

    private void BuildHeader(IContainer container, QuotationPrintDto data)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(data.CompanyName ?? "COCOBOLO")
                        .Bold().FontSize(20).FontColor("#B8860B");
                    if (!string.IsNullOrEmpty(data.CompanyAddress))
                        c.Item().Text(data.CompanyAddress).FontSize(9).FontColor(Colors.Grey.Darken1);
                    if (!string.IsNullOrEmpty(data.CompanyPhone))
                        c.Item().Text($"📞 {data.CompanyPhone}").FontSize(9);
                    if (!string.IsNullOrEmpty(data.CompanyTaxNumber))
                        c.Item().Text($"الرقم الضريبي: {data.CompanyTaxNumber}").FontSize(9);
                });

                row.ConstantItem(120).AlignRight().Column(c =>
                {
                    c.Item().Background("#B8860B").Padding(8).Text("عرض سعر")
                        .Bold().FontSize(14).FontColor(Colors.White);
                    c.Item().PaddingTop(4).Text(data.Quotation.ReferenceNumber ?? "")
                        .Bold().FontSize(11);
                });
            });

            col.Item().PaddingTop(8).LineHorizontal(2).LineColor("#B8860B");
        });
    }

    private void BuildBody(IContainer container, QuotationPrintDto data)
    {
        var q = data.Quotation;

        container.PaddingVertical(10).Column(col =>
        {
            // معلومات العميل والعرض
            col.Item().Row(row =>
            {
                row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(c =>
                {
                    c.Item().Text("بيانات العميل").Bold().FontColor("#B8860B");
                    c.Item().PaddingTop(4).Text(q.PartyName ?? "").Bold();
                    if (!string.IsNullOrEmpty(data.CustomerPhone))
                        c.Item().Text($"📞 {data.CustomerPhone}").FontSize(9);
                    if (!string.IsNullOrEmpty(data.CustomerAddress))
                        c.Item().Text($"📍 {data.CustomerAddress}").FontSize(9);
                    if (!string.IsNullOrEmpty(data.CustomerCity))
                        c.Item().Text(data.CustomerCity).FontSize(9);
                });

                row.ConstantItem(10);

                row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(c =>
                {
                    c.Item().Text("تفاصيل العرض").Bold().FontColor("#B8860B");
                    c.Item().PaddingTop(4).Text($"التاريخ: {q.QuotationDate:yyyy/MM/dd}");
                    if (q.ValidUntil.HasValue)
                        c.Item().Text($"صالح حتى: {q.ValidUntil.Value:yyyy/MM/dd}");
                    if (!string.IsNullOrEmpty(q.EmpName))
                        c.Item().Text($"المسؤول: {q.EmpName}");
                });
            });

            // جدول الأصناف
            col.Item().PaddingTop(12).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(30);    // #
                    c.RelativeColumn(4);     // الصنف
                    c.ConstantColumn(50);    // الكمية
                    c.ConstantColumn(70);    // السعر
                    c.ConstantColumn(80);    // الإجمالي
                });

                table.Header(h =>
                {
                    static IContainer HeaderCell(IContainer c) =>
                        c.Background("#B8860B").Padding(6).DefaultTextStyle(t => t.FontColor(Colors.White).Bold());

                    h.Cell().Element(HeaderCell).AlignCenter().Text("#");
                    h.Cell().Element(HeaderCell).Text("الصنف / الوصف");
                    h.Cell().Element(HeaderCell).AlignCenter().Text("الكمية");
                    h.Cell().Element(HeaderCell).AlignCenter().Text("السعر");
                    h.Cell().Element(HeaderCell).AlignCenter().Text("الإجمالي");
                });

                int idx = 1;
                foreach (var item in q.Items)
                {
                    static IContainer Cell(IContainer c, int row) =>
                        c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                            .Background(row % 2 == 0 ? Colors.White : "#FAFAFA")
                            .Padding(5);

                    var rowIdx = idx;
                    table.Cell().Element(c => Cell(c, rowIdx)).AlignCenter().Text(idx.ToString());

                    table.Cell().Element(c => Cell(c, rowIdx)).Column(cc =>
                    {
                        cc.Item().Text(item.ProductName ?? "").Bold();
                        if (!string.IsNullOrEmpty(item.ProductDescription))
                            cc.Item().Text(item.ProductDescription).FontSize(8).FontColor(Colors.Grey.Darken1);
                    });

                    table.Cell().Element(c => Cell(c, rowIdx)).AlignCenter().Text(item.Quantity.ToString("0.##"));
                    table.Cell().Element(c => Cell(c, rowIdx)).AlignCenter().Text(item.UnitPrice.ToString("N2"));
                    table.Cell().Element(c => Cell(c, rowIdx)).AlignCenter().Text(item.TotalAmount.ToString("N2")).Bold();
                    idx++;
                }
            });

            // الإجماليات
            col.Item().PaddingTop(15).AlignLeft().Width(260).Column(c =>
            {
                c.Item().Row(r =>
                {
                    r.RelativeItem().Text("الإجمالي قبل الخصم:");
                    r.ConstantItem(100).AlignLeft().Text(q.TotalAmount.ToString("N2") + " ج");
                });

                if (q.DiscountAmount is > 0)
                {
                    c.Item().PaddingTop(2).Row(r =>
                    {
                        r.RelativeItem().Text($"الخصم{(q.DiscountPercentage > 0 ? $" ({q.DiscountPercentage}%)" : "")}:").FontColor(Colors.Red.Darken1);
                        r.ConstantItem(100).AlignLeft().Text($"- {q.DiscountAmount:N2} ج").FontColor(Colors.Red.Darken1);
                    });
                }

                c.Item().PaddingTop(6).BorderTop(2).BorderColor("#B8860B").PaddingTop(6).Row(r =>
                {
                    r.RelativeItem().Text("الإجمالي النهائي:").Bold().FontSize(13);
                    r.ConstantItem(100).AlignLeft().Text(q.GrandTotal.ToString("N2") + " ج")
                        .Bold().FontSize(14).FontColor("#B8860B");
                });
            });

            // الملاحظات
            if (!string.IsNullOrEmpty(q.Notes))
            {
                col.Item().PaddingTop(15).Background(Colors.Grey.Lighten4).Padding(8).Column(c =>
                {
                    c.Item().Text("ملاحظات:").Bold().FontSize(10);
                    c.Item().PaddingTop(2).Text(q.Notes).FontSize(9);
                });
            }

            // الشروط
            col.Item().PaddingTop(15).Column(c =>
            {
                c.Item().Text("الشروط والأحكام:").Bold().FontSize(10).FontColor("#B8860B");
                c.Item().PaddingTop(3).Text("• الأسعار شاملة الضريبة وبعملة الجنيه المصري.").FontSize(8);
                if (q.ValidUntil.HasValue)
                    c.Item().Text($"• العرض ساري حتى {q.ValidUntil.Value:yyyy/MM/dd}.").FontSize(8);
                c.Item().Text("• يُحفظ هذا العرض كوثيقة رسمية بين الطرفين عند القبول.").FontSize(8);
            });
        });
    }

    private void BuildFooter(IContainer container, QuotationPrintDto data)
    {
        container.BorderTop(1).BorderColor(Colors.Grey.Lighten2).PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text(t =>
            {
                t.Span("صفحة ").FontSize(8).FontColor(Colors.Grey.Medium);
                t.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                t.Span(" من ").FontSize(8).FontColor(Colors.Grey.Medium);
                t.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
            });

            row.RelativeItem().AlignRight().Text(
                $"تم الإنشاء بواسطة {data.Quotation.CreatedBy} - {data.Quotation.CreatedAt:yyyy/MM/dd HH:mm}")
                .FontSize(8).FontColor(Colors.Grey.Medium);
        });
    }

    // ============================================================
    // Excel
    // ============================================================
    public async Task<(bool Success, string? Error, byte[]? Excel, string FileName)> GenerateExcelAsync(int quotationId)
    {
        try
        {
            var data = await _quotations.GetQuotationForPrintAsync(quotationId);
            if (data == null)
                return (false, "عرض السعر غير موجود.", null, "");

            var q = data.Quotation;
            var fileName = $"Quotation-{q.ReferenceNumber}.xlsx";

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("عرض سعر");
            ws.RightToLeft = true;

            // الهيدر
            ws.Cell("A1").Value = data.CompanyName ?? "COCOBOLO";
            ws.Cell("A1").Style.Font.FontSize = 18;
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontColor = XLColor.FromHtml("#B8860B");
            ws.Range("A1:E1").Merge();

            ws.Cell("A2").Value = "عرض سعر";
            ws.Cell("A2").Style.Font.FontSize = 14;
            ws.Cell("A2").Style.Font.Bold = true;
            ws.Range("A2:E2").Merge();

            ws.Cell("A3").Value = $"رقم العرض: {q.ReferenceNumber}";
            ws.Cell("D3").Value = $"التاريخ: {q.QuotationDate:yyyy/MM/dd}";

            if (q.ValidUntil.HasValue)
                ws.Cell("A4").Value = $"صالح حتى: {q.ValidUntil.Value:yyyy/MM/dd}";

            // بيانات العميل
            ws.Cell("A6").Value = "بيانات العميل";
            ws.Cell("A6").Style.Font.Bold = true;
            ws.Cell("A6").Style.Fill.BackgroundColor = XLColor.FromHtml("#B8860B");
            ws.Cell("A6").Style.Font.FontColor = XLColor.White;
            ws.Range("A6:E6").Merge();

            ws.Cell("A7").Value = "الاسم:";
            ws.Cell("B7").Value = q.PartyName ?? "";
            ws.Cell("A8").Value = "التليفون:";
            ws.Cell("B8").Value = data.CustomerPhone ?? q.PartyPhone ?? "";
            ws.Cell("A9").Value = "العنوان:";
            ws.Cell("B9").Value = data.CustomerAddress ?? "";

            // جدول الأصناف
            var startRow = 11;
            ws.Cell(startRow, 1).Value = "م";
            ws.Cell(startRow, 2).Value = "الصنف";
            ws.Cell(startRow, 3).Value = "الوصف";
            ws.Cell(startRow, 4).Value = "الكمية";
            ws.Cell(startRow, 5).Value = "سعر الوحدة";
            ws.Cell(startRow, 6).Value = "الإجمالي";

            var headerRange = ws.Range(startRow, 1, startRow, 6);
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#B8860B");
            headerRange.Style.Font.FontColor = XLColor.White;
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int row = startRow + 1;
            int idx = 1;
            foreach (var item in q.Items)
            {
                ws.Cell(row, 1).Value = idx++;
                ws.Cell(row, 2).Value = item.ProductName ?? "";
                ws.Cell(row, 3).Value = item.ProductDescription ?? "";
                ws.Cell(row, 4).Value = item.Quantity;
                ws.Cell(row, 5).Value = item.UnitPrice;
                ws.Cell(row, 6).Value = item.TotalAmount;

                ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(row, 6).Style.Font.Bold = true;

                if (idx % 2 == 0)
                    ws.Range(row, 1, row, 6).Style.Fill.BackgroundColor = XLColor.FromHtml("#FAFAFA");

                row++;
            }

            // الإجماليات
            row++;
            ws.Cell(row, 5).Value = "الإجمالي قبل الخصم:";
            ws.Cell(row, 6).Value = q.TotalAmount;
            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 5).Style.Font.Bold = true;
            row++;

            if (q.DiscountAmount is > 0)
            {
                ws.Cell(row, 5).Value = "الخصم:";
                ws.Cell(row, 6).Value = q.DiscountAmount;
                ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(row, 5).Style.Font.Bold = true;
                ws.Cell(row, 6).Style.Font.FontColor = XLColor.Red;
                row++;
            }

            ws.Cell(row, 5).Value = "الإجمالي النهائي:";
            ws.Cell(row, 6).Value = q.GrandTotal;
            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
            ws.Range(row, 5, row, 6).Style.Font.Bold = true;
            ws.Range(row, 5, row, 6).Style.Font.FontSize = 12;
            ws.Range(row, 5, row, 6).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF8DC");
            ws.Cell(row, 6).Style.Font.FontColor = XLColor.FromHtml("#B8860B");

            // الملاحظات
            if (!string.IsNullOrEmpty(q.Notes))
            {
                row += 2;
                ws.Cell(row, 1).Value = "ملاحظات:";
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Range(row, 1, row, 6).Merge();
                row++;
                ws.Cell(row, 1).Value = q.Notes;
                ws.Range(row, 1, row, 6).Merge();
                ws.Cell(row, 1).Style.Alignment.WrapText = true;
            }

            // ضبط عرض الأعمدة
            ws.Columns().AdjustToContents();
            ws.Column(2).Width = Math.Min(40, ws.Column(2).Width);
            ws.Column(3).Width = Math.Min(50, ws.Column(3).Width);

            // إطار للجدول
            var tableRange = ws.Range(startRow, 1, row, 6);
            tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            tableRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#B8860B");

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return (true, null, ms.ToArray(), fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate Excel for quotation {Id}", quotationId);
            return (false, "تعذّر توليد ملف Excel.", null, "");
        }
    }

    // ============================================================
    // Font Loading
    // ============================================================
    private void LoadArabicFontIfNeeded()
    {
        if (_arabicFont != null) return;

        lock (_fontLock)
        {
            if (_arabicFont != null) return;

            try
            {
                var fontsDir = Path.Combine(_env.WebRootPath, "fonts");
                var regularPath = Path.Combine(fontsDir, "Cairo-Regular.ttf");
                var boldPath = Path.Combine(fontsDir, "Cairo-Bold.ttf");

                if (File.Exists(regularPath))
                {
                    _arabicFont = File.ReadAllBytes(regularPath);
                    QuestPDF.Drawing.FontManager.RegisterFont(new MemoryStream(_arabicFont));
                }

                if (File.Exists(boldPath))
                {
                    _arabicFontBold = File.ReadAllBytes(boldPath);
                    QuestPDF.Drawing.FontManager.RegisterFont(new MemoryStream(_arabicFontBold));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load Arabic fonts, will use fallback");
            }
        }
    }

    private static string GetFontFamily()
    {
        // Cairo لو متحمّل، ولو لأ نستخدم Arial كـ fallback (مدعوم في Windows)
        return _arabicFont != null ? "Cairo" : "Arial";
    }
}
