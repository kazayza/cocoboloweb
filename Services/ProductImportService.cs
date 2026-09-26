using System.Security.Claims;
using ClosedXML.Excel;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

/// <summary>🌐 استيراد منتجات الموقع من Excel — Admin/AccountManager فقط (2026-09-17)</summary>
public class ProductImportService : IProductImportService
{
    private readonly IDbContextFactory<db24804Context> _factory;
    private readonly IAuditService _audit;
    private readonly ILogger<ProductImportService> _logger;
    private readonly IHttpContextAccessor _http;

    public const string DefaultGroupName = "منتجات الموقع";

    public ProductImportService(IDbContextFactory<db24804Context> factory, IAuditService audit,
        ILogger<ProductImportService> logger, IHttpContextAccessor http)
    {
        _factory = factory; _audit = audit; _logger = logger; _http = http;
    }

    private static bool IsAuthorized(ClaimsPrincipal? user) =>
        user != null && (user.IsInRole("Admin") || user.IsInRole(SystemRoles.Admin)
            || user.IsInRole("AccountManager") || user.IsInRole(SystemRoles.AccountManager));

    // ═══════════════ قراءة Excel والتحقق (بدون حفظ) ═══════════════
    public async Task<(bool Success, string Message, List<ProductWebsiteImportRowDto> Rows)> ParseAsync(Stream excelStream)
    {
        var user = _http.HttpContext?.User;
        if (!IsAuthorized(user))
            return (false, "الاستيراد متاح للمدير ومدير الحسابات فقط.", new());

        var rows = new List<ProductWebsiteImportRowDto>();
        try
        {
            // 🔄 مهم: stream الرفع من المتصفح غير قابل للـSeek — ClosedXML يحتاج نسخة في الذاكرة
            using var ms = new MemoryStream();
            await excelStream.CopyToAsync(ms);
            ms.Position = 0;
            using var wb = new XLWorkbook(ms);
            var ws = wb.Worksheets.FirstOrDefault();
            if (ws == null) return (false, "الملف فارغ.", rows);

            // خريطة الأعمدة بالترويسة (سطر 1) — مرن: يقبل العربية
            var headers = new Dictionary<string, int>();
            foreach (var cell in ws.Row(1).CellsUsed())
                headers[cell.GetString().Trim()] = cell.Address.ColumnNumber;

            // 🔍 مطابقة مرنة: عنوان العمود «يحتوي» على الاسم (القالب يضيف أوصاف بين قوسين)
            int Col(params string[] names) => headers
                .FirstOrDefault(kv => names.Any(n => kv.Key.Contains(n, StringComparison.OrdinalIgnoreCase))).Value;

            int cName  = Col("اسم المنتج", "الصنف", "الاسم");
            int cDesc  = Col("وصف المنتج", "الوصف");
            int cGroup = Col("المجموعة", "مجموعة المنتج");
            int cCs    = Col("تكلفة ستاندرد");
            int cCp    = Col("تكلفة بريميوم");
            int cCe    = Col("تكلفة إيليت");
            int cPs    = Col("سعر ستاندرد");
            int cPp    = Col("سعر بريميوم");
            int cPe    = Col("سعر إيليت");
            int cWeb   = Col("رقم المنتج بالموقع", "رقم المنتج في الموقع");

            if (cName == 0)
                return (false, "عمود «اسم المنتج» غير موجود في السطر الأول. حمّل القالب الجاهز واستخدم نفس الأعمدة.", rows);

            // 📚 خريطة المجموعات — المستخدم قد يكتب رقم المجموعة بدل اسمها
            var groupById   = new Dictionary<int, string>();
            await using var db = await _factory.CreateDbContextAsync();
            foreach (var g in await db.ProductGroups.AsNoTracking()
                         .Select(g => new { g.ProductGroupId, g.GroupName }).ToListAsync())
                groupById[g.ProductGroupId] = g.GroupName;

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int r = 2; r <= lastRow; r++)
            {
                var row = ws.Row(r);
                var dto = new ProductWebsiteImportRowDto
                {
                    RowNumber   = r,
                    ProductName = cName  > 0 ? row.Cell(cName).GetString().Trim()  : "",
                    ProductDescription = cDesc > 0 ? row.Cell(cDesc).GetString().Trim() : "",
                    WebsiteProductId = cWeb > 0 && int.TryParse(row.Cell(cWeb).GetString().Trim(), out var wpid) ? wpid : null,
                };

                // المجموعة: رقم موجود في النظام → ربط مباشر، نص → بالاسم (يُنشأ إن ناقص)
                var gRaw = cGroup > 0 ? row.Cell(cGroup).GetString().Trim() : "";
                if (gRaw != "" && int.TryParse(gRaw, out var gid))
                {
                    if (groupById.TryGetValue(gid, out var gname)) { dto.GroupId = gid; dto.GroupName = gname; }
                    else dto.Error = $"رقم مجموعة غير موجود في النظام: {gid}";
                }
                else dto.GroupName = gRaw;

                decimal? Num(int c) => c > 0 && decimal.TryParse(row.Cell(c).GetString().Trim(), out var d) && d >= 0 ? d : null;
                dto.CostStd = Num(cCs); dto.CostPremium = Num(cCp); dto.CostElite = Num(cCe);
                dto.PriceStd = Num(cPs); dto.PricePremium = Num(cPp); dto.PriceElite = Num(cPe);

                if (dto.ProductName == "")
                    dto.Error = "اسم المنتج مطلوب";
                else if (dto.ProductDescription == "")
                    dto.Error = "وصف المنتج مطلوب (حقل إلزامي)";
                else if (dto.ProductDescription.Length > 150)
                    dto.Error = $"الوصف يتجاوز 150 حرفاً ({dto.ProductDescription.Length})";
                rows.Add(dto);
            }

            if (rows.Count == 0)
                return (false, "الملف لا يحتوي على صفوف بيانات — اكتب المنتجات بدءًا من السطر الثاني (تحت العناوين مباشرة).", rows);

            if (rows.All(x => x.Error != null))
            {
                // 🔍 بدل رفض كامل بلا تفاصيل: اعرض الصفوف كلها مع سبب رفض كل صف في المعاينة
                var top = rows.GroupBy(x => x.Error).OrderByDescending(g => g.Count()).First().Key;
                return (true, $"⚠️ كل الصفوف ({rows.Count}) بها أخطاء — أشهر سبب: «{top}». التفاصيل في جدول المعاينة بالأسفل.", rows);
            }
            return (true, $"تمت قراءة {rows.Count} صف — راجع المعاينة قبل التنفيذ.", rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Website products Excel parse failed");
            return (false, "تعذّر قراءة الملف: " + ex.Message + " — تأكد أنه بصيغة .xlsx وسليم.", rows);
        }
    }

    // ═══════════════ التنفيذ: معاملة واحدة ═══════════════
    public async Task<(bool Success, string Message, ProductWebsiteImportResultDto? Result)> ImportAsync(
        List<ProductWebsiteImportRowDto> rows, string currentUsername)
    {
        var user = _http.HttpContext?.User;
        if (!IsAuthorized(user))
            return (false, "الاستيراد متاح للمدير ومدير الحسابات فقط.", null);

        var valid = rows.Where(r => r.Error == null).ToList();
        if (!valid.Any()) return (false, "لا توجد صفوف صالحة للاستيراد.", null);

        await using var db = await _factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var norm = (string s) => s.Trim().ToLowerInvariant();

            var groups = await db.ProductGroups.AsNoTracking()
                .ToDictionaryAsync(g => norm(g.GroupName), g => g.ProductGroupId);
            var existingNames = (await db.Products.AsNoTracking()
                .Select(p => p.ProductName).ToListAsync()).Select(norm).ToHashSet();

            int groupsCreated = 0;
            var result = new ProductWebsiteImportResultDto();
            var now = DateTime.Now;

            foreach (var r in valid)
            {
                var nName = norm(r.ProductName);
                if (existingNames.Contains(nName))
                {
                    result.SkippedDuplicates++;
                    if (result.SampleDuplicateNames.Count < 10) result.SampleDuplicateNames.Add(r.ProductName);
                    continue;
                }

                // المجموعة: رقم مُتحقق من المعاينة أولاً — وإلا بالاسم (وإنشاء إن ناقص)
                int groupId;
                if (r.GroupId.HasValue)
                {
                    groupId = r.GroupId.Value;
                }
                else
                {
                var gKey = norm(string.IsNullOrWhiteSpace(r.GroupName) ? DefaultGroupName : r.GroupName);
                if (!groups.TryGetValue(gKey, out groupId))
                {
                    var g = new ProductGroup { GroupName = r.GroupName!.Trim(), CreatedBy = currentUsername, CreatedAt = now };
                    db.ProductGroups.Add(g);
                    await db.SaveChangesAsync();
                    groupId = g.ProductGroupId;
                    groups[gKey] = groupId;
                    groupsCreated++;
                }
                }

                db.Products.Add(new Product
                {
                    ProductName          = r.ProductName,
                    ProductDescription   = r.ProductDescription,
                    ProductGroupId       = groupId,
                    Customer             = null,               // كتالوج عام
                    PurchasePriceCClass  = r.CostStd,
                    PurchasePrice        = r.CostPremium,
                    PurchasePriceElite   = r.CostElite,
                    SuggestedSalePriceCClass = r.PriceStd,
                    SuggestedSalePrice       = r.PricePremium,
                    SuggestedSalePriceElite  = r.PriceElite,
                    PricingStatusId      = 1,                  // Draft
                    IsWebsite            = true,               // 🌐
                    WebsiteProductId     = r.WebsiteProductId,
                    CreatedBy            = currentUsername,
                    CreatedAt            = now
                });
                await db.SaveChangesAsync();

                existingNames.Add(nName);
                result.Added++;
                if (result.SampleAddedNames.Count < 10) result.SampleAddedNames.Add(r.ProductName);
            }

            await tx.CommitAsync();

            await _audit.LogAsync<object>("Products", "WebsiteImport",
                "Batch",
                new { Rows = valid.Count },
                new { result.Added, result.SkippedDuplicates, result.GroupsCreated },
                currentUsername);

            _logger.LogInformation("Website products import: added {A}, skipped {S}, groups created {G} by {U}",
                result.Added, result.SkippedDuplicates, groupsCreated, currentUsername);

            result.GroupsCreated = groupsCreated;
            return (true, $"✅ تم إضافة {result.Added} منتج موقع — تخطي {result.SkippedDuplicates} مكرر — مجموعات جديدة {groupsCreated}.", result);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Website products import failed");
            return (false, "فشل الاستيراد وتم التراجع بالكامل: " + ex.Message, null);
        }
    }

    // ═══════════════ القالب ═══════════════
    public byte[] GetTemplateBytes()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("منتجات الموقع");
        var headers = new[] { "اسم المنتج", "وصف المنتج (إلزامي - حتى 150 حرفاً)", "المجموعة (اسم أو رقم)",
                              "تكلفة ستاندرد", "تكلفة بريميوم", "تكلفة إيليت",
                              "سعر ستاندرد", "سعر بريميوم", "سعر إيليت", "رقم المنتج بالموقع" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#0D1B30");
            c.Style.Font.FontColor = XLColor.FromHtml("#D4AF37");
        }
        ws.Cell(2, 1).Value = "بلوفة صيفي كلاسيك";
        ws.Cell(2, 2).Value = "بلوفة صيفي قماش قطن 100% قصّة كلاسيك";
        ws.Cell(2, 3).Value = "بلوفات";   // أو رقم المجموعة مثل: 14
        ws.Cell(2, 4).Value = 180; ws.Cell(2, 7).Value = 260;
        ws.Cell(2, 10).Value = 1057;
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
