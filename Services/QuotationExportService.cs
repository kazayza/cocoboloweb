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
    // ============================================================
// ⭐ Export List to Excel (مع الفلاتر)
// ============================================================
public async Task<(bool Success, string? Error, byte[]? Excel, string FileName)> ExportQuotationsToExcelAsync(
    QuotationFilterDto filter)
{
    try
    {
        // نجيب القائمة بالفلاتر بتاعتها (بدون pagination)
        var bigFilter = new QuotationFilterDto
        {
            SearchText = filter.SearchText,
            PartyId = filter.PartyId,
            DateFrom = filter.DateFrom,
            DateTo = filter.DateTo,
            Status = filter.Status,
            IsConverted = filter.IsConverted,
            IsExpired = filter.IsExpired,
            SortBy = filter.SortBy,
            SortDescending = filter.SortDescending,
            PageNumber = 1,
            PageSize = 10000  // ⭐ نجيب كل النتائج
        };

        var result = await _quotations.GetQuotationsAsync(bigFilter);
        var items = result.Items;

        var fileName = $"Quotations-{DateTime.Now:yyyy-MM-dd-HHmm}.xlsx";

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("عروض الأسعار");
        ws.RightToLeft = true;

        // ============ Header ============
        ws.Cell("A1").Value = "COCOBOLO - قائمة عروض الأسعار";
        ws.Cell("A1").Style.Font.FontSize = 16;
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontColor = XLColor.FromHtml("#B8860B");
        ws.Range("A1:K1").Merge();
        ws.Range("A1:K1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Cell("A2").Value = $"تاريخ التصدير: {DateTime.Now:yyyy/MM/dd HH:mm}";
        ws.Range("A2:K2").Merge();

        if (filter.DateFrom.HasValue || filter.DateTo.HasValue)
        {
            var fromStr = filter.DateFrom?.ToString("yyyy/MM/dd") ?? "البداية";
            var toStr = filter.DateTo?.ToString("yyyy/MM/dd") ?? "اليوم";
            ws.Cell("A3").Value = $"الفترة: من {fromStr} إلى {toStr}";
            ws.Range("A3:K3").Merge();
        }

        // ============ Table Headers ============
        var headerRow = 5;
        var headers = new[]
        {
            "م", "رقم العرض", "التاريخ", "العميل", "التليفون",
            "الموظف", "الباقة", "الإجمالي", "الخصم", "الصافي", "الحالة"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(headerRow, i + 1).Value = headers[i];
        }

        var headerRange = ws.Range(headerRow, 1, headerRow, headers.Length);
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#B8860B");
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(headerRow).Height = 25;

        // ============ Data Rows ============
        var row = headerRow + 1;
        int idx = 1;
        decimal totalSum = 0, discountSum = 0, netSum = 0;

        foreach (var q in items)
        {
            ws.Cell(row, 1).Value = idx++;
            ws.Cell(row, 2).Value = q.ReferenceNumber;
            ws.Cell(row, 3).Value = q.QuotationDate;
            ws.Cell(row, 3).Style.DateFormat.Format = "yyyy/MM/dd";
            ws.Cell(row, 4).Value = q.PartyName;
            ws.Cell(row, 5).Value = q.PartyPhone ?? "";
            ws.Cell(row, 6).Value = q.EmpName ?? "";
            ws.Cell(row, 7).Value = QuotationPricingModes.All.GetValueOrDefault(q.PricingType ?? PricingTiers.Premium, "بريميوم");
            ws.Cell(row, 8).Value = q.TotalAmount;
            ws.Cell(row, 9).Value = q.DiscountAmount ?? 0;
            ws.Cell(row, 10).Value = q.GrandTotal;
            ws.Cell(row, 11).Value = GetStatusText(q.Status);

            // تنسيق الأرقام
            ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 10).Style.Font.Bold = true;

            // ألوان للحالة
            var statusCell = ws.Cell(row, 11);
            switch (q.Status)
            {
                case "Converted":
                    statusCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F5E9");
                    statusCell.Style.Font.FontColor = XLColor.FromHtml("#2E7D32");
                    break;
                case "Accepted":
                    statusCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E3F2FD");
                    statusCell.Style.Font.FontColor = XLColor.FromHtml("#1565C0");
                    break;
                case "Rejected":
                    statusCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFEBEE");
                    statusCell.Style.Font.FontColor = XLColor.FromHtml("#C62828");
                    break;
                case "Expired":
                    statusCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF8E1");
                    statusCell.Style.Font.FontColor = XLColor.FromHtml("#EF6C00");
                    break;
            }
            statusCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // صف مزدوج (Zebra)
            if (idx % 2 == 0)
            {
                for (int c = 1; c <= 11; c++)
                {
                    if (c != 11) // متفرشش لو الحالة عليها لون
                        ws.Cell(row, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#FAFAFA");
                }
            }

            totalSum += q.TotalAmount;
            discountSum += q.DiscountAmount ?? 0;
            netSum += q.GrandTotal;

            row++;
        }

        // ============ Totals Row ============
        var totalsRow = row;
        ws.Cell(totalsRow, 1).Value = "الإجمالي";
        ws.Range(totalsRow, 1, totalsRow, 7).Merge();
        ws.Cell(totalsRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Cell(totalsRow, 8).Value = totalSum;
        ws.Cell(totalsRow, 9).Value = discountSum;
        ws.Cell(totalsRow, 10).Value = netSum;

        ws.Cell(totalsRow, 8).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(totalsRow, 9).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(totalsRow, 10).Style.NumberFormat.Format = "#,##0.00";

        var totalsRange = ws.Range(totalsRow, 1, totalsRow, 11);
        totalsRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF8DC");
        totalsRange.Style.Font.Bold = true;
        totalsRange.Style.Font.FontSize = 12;
        totalsRange.Style.Border.TopBorder = XLBorderStyleValues.Double;
        totalsRange.Style.Border.TopBorderColor = XLColor.FromHtml("#B8860B");

        // ============ Stats Box (تحت الجدول) ============
        var statsRow = totalsRow + 2;
        ws.Cell(statsRow, 1).Value = "📊 ملخص إحصائي";
        ws.Cell(statsRow, 1).Style.Font.Bold = true;
        ws.Cell(statsRow, 1).Style.Font.FontSize = 13;
        ws.Cell(statsRow, 1).Style.Font.FontColor = XLColor.FromHtml("#B8860B");
        ws.Range(statsRow, 1, statsRow, 4).Merge();

        ws.Cell(statsRow + 1, 1).Value = "إجمالي عدد العروض:";
        ws.Cell(statsRow + 1, 2).Value = items.Count;

        ws.Cell(statsRow + 2, 1).Value = "تحوّلت لفواتير:";
        ws.Cell(statsRow + 2, 2).Value = items.Count(x => x.InvoiceId != null);

        ws.Cell(statsRow + 3, 1).Value = "مقبولة:";
        ws.Cell(statsRow + 3, 2).Value = items.Count(x => x.Status == "Accepted");

        ws.Cell(statsRow + 4, 1).Value = "مرفوضة:";
        ws.Cell(statsRow + 4, 2).Value = items.Count(x => x.Status == "Rejected");

        ws.Cell(statsRow + 5, 1).Value = "قيد المراجعة:";
        ws.Cell(statsRow + 5, 2).Value = items.Count(x =>
            x.Status == "Draft" || x.Status == "Sent");

        ws.Cell(statsRow + 6, 1).Value = "معدل التحويل:";
        var convertedCount = items.Count(x => x.InvoiceId != null);
        var rate = items.Count == 0 ? 0 : Math.Round(((decimal)convertedCount / items.Count) * 100, 1);
        ws.Cell(statsRow + 6, 2).Value = $"{rate}%";

        for (int r = statsRow + 1; r <= statsRow + 6; r++)
        {
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Cell(r, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }

        // ============ Borders & Auto-fit ============
        var dataRange = ws.Range(headerRow, 1, totalsRow, 11);
        dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        dataRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#B8860B");

        ws.Columns().AdjustToContents();

        // Freeze header
        ws.SheetView.FreezeRows(headerRow);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return (true, null, ms.ToArray(), fileName);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to export quotations list to Excel");
        return (false, "تعذّر تصدير القائمة لـ Excel.", null, "");
    }
}

// Helper للحالة
private static string GetStatusText(string status)
{
    return status switch
    {
        "Draft" => "مسودة",
        "Sent" => "تم الإرسال",
        "Accepted" => "مقبول",
        "Rejected" => "مرفوض",
        "Converted" => "تحوّل لفاتورة",
        "Expired" => "منتهي الصلاحية",
        _ => status
    };
}
// ============================================================
// ⭐ Export List to PDF (مع الفلاتر)
// ============================================================
public async Task<(bool Success, string? Error, byte[]? Pdf, string FileName)> 
    ExportQuotationsToPdfAsync(QuotationFilterDto filter)
{
    try
    {
        // نجيب كل النتائج
        var bigFilter = new QuotationFilterDto
        {
            SearchText = filter.SearchText,
            PartyId = filter.PartyId,
            DateFrom = filter.DateFrom,
            DateTo = filter.DateTo,
            Status = filter.Status,
            IsConverted = filter.IsConverted,
            IsExpired = filter.IsExpired,
            SortBy = filter.SortBy,
            SortDescending = filter.SortDescending,
            PageNumber = 1,
            PageSize = 10000
        };

        var result = await _quotations.GetQuotationsAsync(bigFilter);
        var items = result.Items;

        LoadArabicFontIfNeeded();
        QuestPDF.Settings.License = LicenseType.Community;

        var fileName = $"Quotations-List-{DateTime.Now:yyyy-MM-dd-HHmm}.pdf";

        var bytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());   // ⭐ عرضي عشان الجدول
                page.Margin(1, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(t => t
                    .FontFamily(GetFontFamily())
                    .FontSize(9)
                    .DirectionFromRightToLeft());

                // Header
                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text("📋 قائمة عروض الأسعار - COCOBOLO")
                            .Bold().FontSize(16).FontColor("#B8860B");
                        row.ConstantItem(200).AlignLeft().Text($"التاريخ: {DateTime.Now:yyyy/MM/dd HH:mm}")
                            .FontSize(9).FontColor(Colors.Grey.Darken1);
                    });

                    if (filter.DateFrom.HasValue || filter.DateTo.HasValue)
                    {
                        var fromStr = filter.DateFrom?.ToString("yyyy/MM/dd") ?? "البداية";
                        var toStr = filter.DateTo?.ToString("yyyy/MM/dd") ?? "اليوم";
                        col.Item().PaddingTop(4).Text($"الفترة: من {fromStr} إلى {toStr}")
                            .FontSize(10).Italic();
                    }

                    col.Item().PaddingTop(6).LineHorizontal(2).LineColor("#B8860B");
                });

                // Content - الجدول
                page.Content().PaddingVertical(8).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(30);     // م
                        c.ConstantColumn(85);     // رقم
                        c.ConstantColumn(65);     // التاريخ
                        c.RelativeColumn(2);      // العميل
                        c.ConstantColumn(80);     // التليفون
                        c.RelativeColumn(1.5f);   // الموظف
                        c.ConstantColumn(50);     // الباقة
                        c.ConstantColumn(75);     // الإجمالي
                        c.ConstantColumn(75);     // الصافي
                        c.ConstantColumn(70);     // الحالة
                    });

                    table.Header(h =>
                    {
                        static IContainer HCell(IContainer c) => c.Background("#B8860B")
                            .Padding(5).DefaultTextStyle(t => t.FontColor(Colors.White).Bold().FontSize(9));

                        h.Cell().Element(HCell).AlignCenter().Text("م");
                        h.Cell().Element(HCell).Text("رقم العرض");
                        h.Cell().Element(HCell).AlignCenter().Text("التاريخ");
                        h.Cell().Element(HCell).Text("العميل");
                        h.Cell().Element(HCell).AlignCenter().Text("التليفون");
                        h.Cell().Element(HCell).Text("الموظف");
                        h.Cell().Element(HCell).AlignCenter().Text("الباقة");
                        h.Cell().Element(HCell).AlignCenter().Text("الإجمالي");
                        h.Cell().Element(HCell).AlignCenter().Text("الصافي");
                        h.Cell().Element(HCell).AlignCenter().Text("الحالة");
                    });

                    int idx = 1;
                    decimal totalSum = 0, netSum = 0;
                    foreach (var q in items)
                    {
                        var rowIdx = idx;
                        IContainer Cell(IContainer c) => c
                            .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                            .Background(rowIdx % 2 == 0 ? "#FAFAFA" : Colors.White)
                            .Padding(4);

                        table.Cell().Element(Cell).AlignCenter().Text(idx.ToString()).FontSize(8);
                        table.Cell().Element(Cell).Text(q.ReferenceNumber).FontSize(8);
                        table.Cell().Element(Cell).AlignCenter().Text(q.QuotationDate.ToString("yyyy/MM/dd")).FontSize(8);
                        table.Cell().Element(Cell).Text(q.PartyName).FontSize(8);
                        table.Cell().Element(Cell).AlignCenter().Text(q.PartyPhone ?? "-").FontSize(8);
                        table.Cell().Element(Cell).Text(q.EmpName ?? "-").FontSize(8);
                        table.Cell().Element(Cell).AlignCenter().Text(QuotationPricingModes.All.GetValueOrDefault(q.PricingType ?? PricingTiers.Premium, "بريميوم")).FontSize(8);
                        table.Cell().Element(Cell).AlignCenter().Text(q.TotalAmount.ToString("N2")).FontSize(8);
                        table.Cell().Element(Cell).AlignCenter().Text(q.GrandTotal.ToString("N2")).Bold().FontSize(8);
                        table.Cell().Element(Cell).AlignCenter().Text(GetStatusText(q.Status)).FontSize(8);

                        totalSum += q.TotalAmount;
                        netSum += q.GrandTotal;
                        idx++;
                    }

                    // Totals row
                    IContainer TCell(IContainer c) => c.Background("#FFF8DC").Padding(6)
                        .BorderTop(2).BorderColor("#B8860B");

                    table.Cell().ColumnSpan(7).Element(TCell).AlignCenter().Text("الإجمالي").Bold().FontSize(11);
                    table.Cell().Element(TCell).AlignCenter().Text(totalSum.ToString("N2")).Bold().FontSize(11);
                    table.Cell().Element(TCell).AlignCenter().Text(netSum.ToString("N2")).Bold().FontColor("#B8860B").FontSize(11);
                    table.Cell().Element(TCell);
                });

                // Footer
                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("صفحة ").FontSize(8).FontColor(Colors.Grey.Medium);
                    t.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                    t.Span(" من ").FontSize(8).FontColor(Colors.Grey.Medium);
                    t.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                    t.Span($" | إجمالي: {items.Count} عرض").FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf();

        return (true, null, bytes, fileName);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to export quotations list to PDF");
        return (false, "تعذّر تصدير القائمة لـ PDF.", null, "");
    }
}
}
