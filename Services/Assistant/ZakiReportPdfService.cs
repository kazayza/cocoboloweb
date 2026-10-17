using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// تصدير جداول زكي إلى PDF رسمي (P3).
/// QuestPDF موجودة أصلًا في المشروع (ترخيص Community في Program.cs)
/// وخط Cairo العربي من wwwroot/fonts — نفس نمط QuotationExportService.
/// القالب ثابت ومحترم: هيدر navy/gold + الجدول + فوتر بالتوقيت والمستخدم.
/// الموديل بيختار الجدول بس — التصميم هنا في الكود (صفر هلوسة شكل).
/// </summary>
public class ZakiReportPdfService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ZakiReportPdfService> _logger;

    private static byte[]? _arabicFont;
    private static readonly object _fontLock = new();

    public ZakiReportPdfService(IWebHostEnvironment env, ILogger<ZakiReportPdfService> logger)
    {
        _env = env;
        _logger = logger;
    }

    public byte[] GenerateTablePdf(ZakiTable table, string userName)
    {
        LoadArabicFontIfNeeded();

        var exportedAt = DateTime.Now;
        var navy = "#1E293B";
        var gold = "#D4AF37";

        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(26);
                page.PageColor(Colors.White);
                page.ContentFromRightToLeft(); // ترتيب الأعمدة والعناصر من اليمين لليسار

                page.DefaultTextStyle(t =>
                {
                    var style = t.FontSize(10).DirectionFromRightToLeft();
                    if (_arabicFont != null) style = style.FontFamily("Cairo");
                    return style;
                });

                // ── الهيدر ──
                page.Header().Column(col =>
                {
                    col.Item().Background(navy).PaddingVertical(10).PaddingHorizontal(14)
                        .Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("COCOBOLO ERP").FontSize(15).Bold().FontColor(gold);
                                c.Item().PaddingTop(2).Text(table.Title).FontSize(11).FontColor(Colors.White);
                            });

                            row.ConstantItem(170).Column(c =>
                            {
                                c.Item().AlignRight().Text("تقرير زكي — المساعد الذكي").FontSize(10).Bold().FontColor(gold);
                                c.Item().AlignRight().PaddingTop(2)
                                    .Text(exportedAt.ToString("yyyy/MM/dd HH:mm")).FontSize(8).FontColor(Colors.Grey.Lighten2);
                            });
                        });

                    col.Item().Height(3).Background(gold);
                });

                // ── الجدول ──
                page.Content().PaddingTop(12).Table(tbl =>
                {
                    tbl.ColumnsDefinition(cols =>
                    {
                        foreach (var h in table.Headers)
                            cols.RelativeColumn(Math.Max(2, Math.Min(h.Length + 2, 10)));
                    });

                    tbl.Header(header =>
                    {
                        foreach (var h in table.Headers)
                        {
                            header.Cell()
                                .Background(navy)
                                .PaddingVertical(6).PaddingHorizontal(6)
                                .Text(h).FontSize(9.5f).Bold().FontColor(Colors.White);
                        }
                    });

                    var rowIndex = 0;
                    foreach (var row in table.Rows)
                    {
                        var shade = rowIndex % 2 == 1 ? "#F8FAFC" : "#FFFFFF";
                        foreach (var cell in row)
                        {
                            tbl.Cell()
                                .Background(shade)
                                .BorderBottom(0.5f).BorderColor("#E2E8F0")
                                .PaddingVertical(5).PaddingHorizontal(6)
                                .Text(cell).FontSize(9);
                        }
                        rowIndex++;
                    }
                });

                // ── الفوتر ──
                page.Footer().AlignCenter().Text(text =>
                {
                    text.DefaultTextStyle(t =>
                    {
                        var style = t.FontSize(8).FontColor("#6B7280");
                        if (_arabicFont != null) style = style.FontFamily("Cairo");
                        return style;
                    });

                    text.Span($"صُدّر بواسطة زكي — المساعد الذكي   ·   المستخدم: {userName}   ·   {exportedAt:yyyy/MM/dd HH:mm}   ·   صفحة ");
                    text.CurrentPageNumber();
                    text.Span(" من ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    private void LoadArabicFontIfNeeded()
    {
        lock (_fontLock)
        {
            if (_arabicFont != null) return;

            try
            {
                var regularPath = Path.Combine(_env.WebRootPath, "fonts", "Cairo-Regular.ttf");
                if (File.Exists(regularPath))
                {
                    _arabicFont = File.ReadAllBytes(regularPath);
                    QuestPDF.Drawing.FontManager.RegisterFont(new MemoryStream(_arabicFont));
                }

                var boldPath = Path.Combine(_env.WebRootPath, "fonts", "Cairo-Bold.ttf");
                if (File.Exists(boldPath))
                    QuestPDF.Drawing.FontManager.RegisterFont(new MemoryStream(File.ReadAllBytes(boldPath)));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Zaki PDF: failed to load Arabic fonts, using fallback");
            }
        }
    }
}
