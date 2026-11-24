using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace COCOBOLOERPNEW.Services;

public static class JobSpecPdfBuilder
{
    private static readonly object FontLock = new();
    private static string? FontFamily;
    private const string Navy = "#1A2842";
    private const string Gold = "#B8860B";

    public static async Task<byte[]> CreateAsync(db24804Context db, string webRoot, JobSpecPdfDocument doc)
    {
        var font = EnsureFont(webRoot);
        var logo = TryRead(Path.Combine(webRoot, "logo.png"));
        foreach (var product in doc.Products)
        {
            product.ImageBytes = await LoadImagesAsync(db, webRoot, product.ImageSources, 8);
            foreach (var part in product.Parts)
                part.ImageBytes = await LoadImagesAsync(db, webRoot, part.ImageSources, 6);
            foreach (var alt in product.Alternatives)
                alt.ImageBytes = await LoadImagesAsync(db, webRoot, alt.ImageSources, 6);
        }

        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(12, Unit.Millimetre);
                page.PageColor(Colors.White);
                page.ContentFromRightToLeft();
                page.DefaultTextStyle(t => t.FontFamily(font).FontSize(10).FontColor(Navy).DirectionFromRightToLeft());
                page.Header().Element(c => BuildHeader(c, doc, logo));
                page.Content().Element(c => BuildBody(c, doc));
                page.Footer().Element(BuildFooter);
            });
        }).GeneratePdf();
    }

    private static void BuildHeader(IContainer container, JobSpecPdfDocument doc, byte[]? logo)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                if (logo != null)
                    row.ConstantItem(34).Height(42).Image(logo).FitArea();

                row.RelativeItem().PaddingHorizontal(8).Column(c =>
                {
                    c.Item().Text("كوكو بولو").Bold().FontSize(16).FontColor(Navy);
                    c.Item().Text("جوب أوردر — ملف مواصفات العميل").FontSize(9).FontColor(Gold);
                });

                row.ConstantItem(150).AlignLeft().Column(c =>
                {
                    c.Item().Text(doc.CustomerName).Bold().FontSize(11);
                    c.Item().Text(doc.IssuedAt.ToString("yyyy/MM/dd")).FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
            col.Item().PaddingTop(4).LineHorizontal(1.5f).LineColor(Gold);
        });
    }

    private static void BuildBody(IContainer container, JobSpecPdfDocument doc)
    {
        var showInvoices = doc.Products.Any(p => p.InvoiceNumbers.Count > 0);
        container.PaddingTop(8).Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Element(c => InfoBox(c, "بيانات العميل", box =>
                {
                    box.Item().Text(doc.CustomerName).Bold().FontSize(12);
                    Line(box, "الهاتف", doc.Phone);
                    Line(box, "المدينة", doc.City);
                    Line(box, "العنوان", doc.Address);
                }));
                row.ConstantItem(8);
                row.RelativeItem().Element(c => InfoBox(c, "بيانات الملف", box =>
                {
                    box.Item().Text($"عدد المنتجات: {doc.Products.Count}");
                    box.Item().Text($"تاريخ الإصدار: {doc.IssuedAt:yyyy/MM/dd HH:mm}");
                    Line(box, "أعد الملف", doc.PreparedBy);
                    Line(box, "طُبع بواسطة", doc.PrintedBy);
                }));
            });

            col.Item().PaddingTop(8).Border(1).BorderColor("#E7D7A1").Background("#FAF6E8").Padding(8).Text(
                "هذا الملف مواصفات للمراجعة والتسعير قبل الاتفاق. ليس أمر تنفيذ، ولا يبدأ التصنيع قبل الاتفاق.")
                .FontSize(9);

            col.Item().PaddingTop(12).Text("فهرس المنتجات").Bold().FontSize(12).FontColor(Gold);
            col.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(28);
                    c.RelativeColumn(4);
                    c.RelativeColumn(2);
                    c.ConstantColumn(70);
                    if (showInvoices)
                        c.RelativeColumn(2);
                });
                table.Header(h =>
                {
                    HeaderCell(h.Cell(), "#");
                    HeaderCell(h.Cell(), "المنتج");
                    HeaderCell(h.Cell(), "الحالة");
                    HeaderCell(h.Cell(), "المدة");
                    if (showInvoices)
                        HeaderCell(h.Cell(), "الفاتورة");
                });
                for (var i = 0; i < doc.Products.Count; i++)
                {
                    var product = doc.Products[i];
                    BodyCell(table.Cell(), i, product.Number.ToString(), true);
                    BodyCell(table.Cell(), i, product.Name, false);
                    BodyCell(table.Cell(), i, product.Status, false);
                    BodyCell(table.Cell(), i, product.Period, true);
                    if (showInvoices)
                        BodyCell(table.Cell(), i, product.InvoiceNumbers.Count == 0 ? "—" : string.Join("، ", product.InvoiceNumbers), false);
                }
            });

            if (doc.Products.Count > 0)
            {
                col.Item().PageBreak();
                col.Item().Element(c => BuildProduct(c, doc.Products[0]));
                foreach (var product in doc.Products.Skip(1))
                {
                    col.Item().PageBreak();
                    col.Item().Element(c => BuildProduct(c, product));
                }
            }

            col.Item().PageBreak();
            col.Item().PaddingTop(6).Text("اعتماد الملف").Bold().FontSize(12).FontColor(Gold);
            col.Item().PaddingTop(4).Text("التوقيع أدناه على مواصفات الملف قبل الاتفاق، وليس أمر تنفيذ.").FontSize(9);
            col.Item().PaddingTop(16).Row(row =>
            {
                Signature(row, "المبيعات");
                row.ConstantItem(16);
                Signature(row, "المصنع");
                row.ConstantItem(16);
                Signature(row, "التاريخ");
            });
        });
    }

    private static void BuildProduct(IContainer container, JobSpecPdfProduct product)
    {
        container.Decoration(decoration =>
        {
            decoration.Before().PaddingBottom(3).Background(Navy).Padding(7).Row(row =>
            {
                row.RelativeItem().Text($"{product.Number}. {product.Name}").Bold().FontSize(13).FontColor(Colors.White);
                row.ConstantItem(90).AlignLeft().Text($"كود {product.ProductId}").FontSize(8).FontColor("#F6E7B4");
            });
            decoration.Content().Column(col =>
        {
            col.Item().PaddingTop(4).Text($"{product.Status}  ·  مدة التصنيع {product.Period}  ·  {product.Created}")
                .FontSize(9).FontColor(Colors.Grey.Darken1);
            if (product.InvoiceNumbers.Count > 0)
                col.Item().Text("فاتورة: " + string.Join("، ", product.InvoiceNumbers)).FontSize(9);

            if (!string.IsNullOrWhiteSpace(product.Description))
                col.Item().PaddingTop(4).Text(product.Description).FontSize(10);

            Images(col, product.ImageBytes, large: true);
            Specs(col, "المواصفات", product.Specs);

            if (!string.IsNullOrWhiteSpace(product.Manufacturing))
                NoteBox(col, "ملاحظات التصنيع", product.Manufacturing);

            if (product.Parts.Count > 0)
            {
                col.Item().PaddingTop(8).Text("المكونات").Bold().FontColor(Gold);
                foreach (var part in product.Parts)
                {
                    col.Item().PaddingTop(3).Text($"{part.Name} — {part.Meta}").Bold().FontSize(9);
                    if (!string.IsNullOrWhiteSpace(part.LinkedName))
                        col.Item().Text("منتج مرتبط: " + part.LinkedName).FontSize(8);
                    Specs(col, null, part.Specs);
                    Images(col, part.ImageBytes, large: false);
                }
            }

            if (product.Alternatives.Count > 0)
            {
                col.Item().PaddingTop(8).Text("رد المصنع").Bold().FontColor(Gold);
                foreach (var alt in product.Alternatives)
                {
                    col.Item().PaddingTop(3).Text($"{alt.Name} — {alt.Status}").Bold().FontSize(9);
                    if (!string.IsNullOrWhiteSpace(alt.Summary))
                        col.Item().Text(alt.Summary).FontSize(8);
                    if (alt.Period.HasValue)
                        col.Item().Text($"مدة التصنيع: {alt.Period} يوم").FontSize(8);
                    if (!string.IsNullOrWhiteSpace(alt.Manufacturing))
                        col.Item().Text(alt.Manufacturing).FontSize(8);
                    Specs(col, null, alt.Specs);
                    Images(col, alt.ImageBytes, large: false);
                }
            }

            if (product.Notes.Count > 0)
            {
                col.Item().PaddingTop(6).Text("استفسارات وملاحظات المصنع").Bold().FontSize(9).FontColor(Gold);
                foreach (var note in product.Notes)
                    col.Item().PaddingTop(2).Text(note).FontSize(9);
            }
        });
        });
    }

    private static void BuildFooter(IContainer container)
    {
        container.BorderTop(1).BorderColor("#E2E8F0").PaddingTop(4).Row(row =>
        {
            row.RelativeItem().Text("مواصفات قبل الاتفاق — ليس أمر تنفيذ").FontSize(8).FontColor(Colors.Grey.Darken1);
            row.RelativeItem().AlignLeft().Text(t =>
            {
                t.Span("صفحة ").FontSize(8).FontColor(Colors.Grey.Darken1);
                t.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Darken1);
                t.Span(" من ").FontSize(8).FontColor(Colors.Grey.Darken1);
                t.TotalPages().FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        });
    }

    private static void InfoBox(IContainer container, string title, Action<ColumnDescriptor> fill)
    {
        container.Border(1).BorderColor("#E7D7A1").Background("#FFFdf8").Padding(8).Column(col =>
        {
            col.Item().Text(title).Bold().FontColor(Gold);
            col.Item().PaddingTop(3).Column(fill);
        });
    }

    private static void Line(ColumnDescriptor col, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            col.Item().Text($"{label}: {value}").FontSize(9);
    }

    private static void HeaderCell(IContainer cell, string text) =>
        cell.Background(Navy).Padding(4).AlignCenter().Text(text).FontSize(8).FontColor(Colors.White).Bold();

    private static void BodyCell(IContainer cell, int row, string text, bool center)
    {
        var box = cell.BorderBottom(0.4f).BorderColor("#E2E8F0")
            .Background(row % 2 == 0 ? "#FFFFFF" : "#FAFAFA")
            .Padding(4);
        var aligned = center ? box.AlignCenter() : box;
        aligned.Text(string.IsNullOrWhiteSpace(text) ? "—" : text).FontSize(8);
    }

    private static void Specs(ColumnDescriptor col, string? title, List<string> lines)
    {
        if (lines.Count == 0) return;
        if (!string.IsNullOrWhiteSpace(title))
            col.Item().PaddingTop(6).Text(title).Bold().FontSize(9).FontColor(Gold);
        col.Item().PaddingTop(2).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(2);
                c.RelativeColumn(3);
            });
            foreach (var line in lines)
            {
                var split = line.IndexOf(':');
                var label = split < 0 ? "بيان" : line[..split].Trim();
                var value = split < 0 ? line : line[(split + 1)..].Trim();
                table.Cell().BorderBottom(0.3f).BorderColor("#EEE").Padding(2).Text(label).FontSize(8).FontColor(Colors.Grey.Darken2);
                table.Cell().BorderBottom(0.3f).BorderColor("#EEE").Padding(2).Text(value).FontSize(8);
            }
        });
    }

    private static void Images(ColumnDescriptor col, List<byte[]> images, bool large)
    {
        if (images.Count == 0) return;
        col.Item().PaddingTop(8).Text("الصور").Bold().FontSize(10).FontColor(Gold);
        var single = images.Count == 1;
        var width = single ? (large ? 132f : 96f) : (large ? 88f : 72f);
        var height = single ? (large ? 96f : 70f) : (large ? 66f : 54f);
        var columns = single ? 1 : 2;
        for (var i = 0; i < images.Count; i += columns)
        {
            var slice = images.Skip(i).Take(columns).ToList();
            col.Item().ShowEntire().PaddingTop(3).Row(row =>
            {
                foreach (var image in slice)
                {
                    row.ConstantItem(width, Unit.Millimetre)
                        .Height(height, Unit.Millimetre)
                        .Padding(1.4f, Unit.Millimetre)
                        .Border(0.6f)
                        .BorderColor("#E7D7A1")
                        .Background(Colors.White)
                        .Image(image)
                        .FitArea()
                        .WithRasterDpi(220)
                        .WithCompressionQuality(ImageCompressionQuality.High);
                }
                if (slice.Count < columns)
                    row.RelativeItem();
            });
        }
    }

    private static void NoteBox(ColumnDescriptor col, string title, string text)
    {
        col.Item().PaddingTop(6).Background("#F8FAFC").Padding(6).Column(c =>
        {
            c.Item().Text(title).Bold().FontSize(8).FontColor(Gold);
            c.Item().Text(text).FontSize(8);
        });
    }

    private static void Signature(RowDescriptor row, string label)
    {
        row.RelativeItem().Column(c =>
        {
            c.Item().Text(label).Bold().FontSize(9);
            c.Item().PaddingTop(18).LineHorizontal(0.6f).LineColor(Navy);
        });
    }

    private static async Task<List<byte[]>> LoadImagesAsync(db24804Context db, string webRoot, IEnumerable<string> sources, int max)
    {
        var result = new List<byte[]>();
        foreach (var source in sources.Where(s => !string.IsNullOrWhiteSpace(s)).Take(max))
        {
            var bytes = await LoadOneAsync(db, webRoot, source);
            if (bytes != null && IsSupportedImage(bytes) && bytes.Length <= 8_000_000)
                result.Add(bytes);
        }
        return result;
    }

    private static async Task<byte[]?> LoadOneAsync(db24804Context db, string webRoot, string source)
    {
        const string marker = "/api/product-image-by-id/";
        var index = source.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0 && int.TryParse(source[(index + marker.Length)..].Trim('/'), out var imageId))
        {
            var image = await db.ProductImages.AsNoTracking().FirstOrDefaultAsync(i => i.ProductImagesId == imageId);
            if (image == null) return null;
            var fromFile = TryReadWebPath(webRoot, image.ImagePath);
            if (fromFile != null) return fromFile;
            return image.ImageProduct is { Length: > 0 } ? image.ImageProduct : null;
        }
        return TryReadWebPath(webRoot, source);
    }

    private static byte[]? TryReadWebPath(string webRoot, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains("://", StringComparison.Ordinal)) return null;
        return TryRead(Path.Combine(webRoot, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
    }

    private static byte[]? TryRead(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

    private static bool IsSupportedImage(byte[] bytes)
    {
        if (bytes.Length < 4) return false;
        if (bytes[0] == 0xFF && bytes[1] == 0xD8) return true;
        if (bytes[0] == 0x89 && bytes[1] == 0x50) return true;
        if (bytes[0] == 0x47 && bytes[1] == 0x49) return true;
        if (bytes[0] == 0x42 && bytes[1] == 0x4D) return true;
        return false;
    }

    private static string EnsureFont(string webRoot)
    {
        if (!string.IsNullOrWhiteSpace(FontFamily)) return FontFamily;
        lock (FontLock)
        {
            if (!string.IsNullOrWhiteSpace(FontFamily)) return FontFamily;
            var cairo = Path.Combine(webRoot, "fonts", "Cairo-Regular.ttf");
            var cairoBold = Path.Combine(webRoot, "fonts", "Cairo-Bold.ttf");
            if (File.Exists(cairo))
            {
                Register(cairo);
                if (File.Exists(cairoBold)) Register(cairoBold);
                FontFamily = "Cairo";
                return FontFamily;
            }

            foreach (var candidate in new[] { @"C:\Windows\Fonts\tahoma.ttf", @"C:\Windows\Fonts\arial.ttf" })
            {
                if (!File.Exists(candidate)) continue;
                Register(candidate);
                FontFamily = Path.GetFileNameWithoutExtension(candidate).Equals("tahoma", StringComparison.OrdinalIgnoreCase)
                    ? "Tahoma" : "Arial";
                return FontFamily;
            }

            throw new InvalidOperationException("تعذر إنشاء الملف لأن خط الطباعة العربي غير موجود.");
        }
    }

    private static void Register(string path)
    {
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            QuestPDF.Drawing.FontManager.RegisterFont(stream);
        }
        catch { }
    }
}

public sealed class JobSpecPdfDocument
{
    public string CustomerName { get; set; } = "";
    public string? Phone { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }
    public string PrintedBy { get; set; } = "";
    public string? PreparedBy { get; set; }
    public DateTime IssuedAt { get; set; }
    public List<JobSpecPdfProduct> Products { get; set; } = new();
}

public sealed class JobSpecPdfProduct
{
    public int Number { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string Period { get; set; } = "";
    public string Created { get; set; } = "";
    public string? Description { get; set; }
    public string? Manufacturing { get; set; }
    public List<string> InvoiceNumbers { get; set; } = new();
    public List<string> ImageSources { get; set; } = new();
    public List<byte[]> ImageBytes { get; set; } = new();
    public List<string> Specs { get; set; } = new();
    public List<JobSpecPdfPart> Parts { get; set; } = new();
    public List<JobSpecPdfAlt> Alternatives { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

public sealed class JobSpecPdfPart
{
    public string Name { get; set; } = "";
    public string Meta { get; set; } = "";
    public string? LinkedName { get; set; }
    public List<string> Specs { get; set; } = new();
    public List<string> ImageSources { get; set; } = new();
    public List<byte[]> ImageBytes { get; set; } = new();
}

public sealed class JobSpecPdfAlt
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Summary { get; set; }
    public string? Manufacturing { get; set; }
    public int? Period { get; set; }
    public List<string> Specs { get; set; } = new();
    public List<string> ImageSources { get; set; } = new();
    public List<byte[]> ImageBytes { get; set; } = new();
}
