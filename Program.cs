using COCOBOLOERPNEW.Components;
using COCOBOLOERPNEW.Models;
using COCOBOLOERPNEW.Services;
using COCOBOLOERPNEW.DTOs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using MudBlazor.Services;
using COCOBOLOERPNEW.Helpers;
using COCOBOLOERPNEW.Endpoints;
using QuestPDF.Infrastructure;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "DataProtection-Keys")))
    .SetApplicationName("COCOBOLOERP");
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddMudServices();

// ⭐ Rate Limiting - حماية من Brute Force
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429; // Too Many Requests
    
    // قاعدة الـ Login: 5 محاولات كل دقيقة لكل IP
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,                          // 5 محاولات
                Window = TimeSpan.FromMinutes(1),         // كل دقيقة
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

// ✅ Memory Cache
builder.Services.AddMemoryCache();
builder.Services.AddScoped<CacheService>();

builder.Services.AddDbContextFactory<db24804Context>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped(sp => 
    sp.GetRequiredService<IDbContextFactory<db24804Context>>().CreateDbContext());

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.LoginPath         = "/login";
        options.AccessDeniedPath  = "/access-denied";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
       // options.ExpireTimeSpan    = TimeSpan.FromHours(8);
        

        options.Cookie.Name         = "COCOBOLO.Auth";
        options.Cookie.HttpOnly     = true;
        options.Cookie.IsEssential  = true;
        //options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        //options.Cookie.SameSite     = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
    ? CookieSecurePolicy.SameAsRequest
    : CookieSecurePolicy.Always;
    
options.Cookie.SameSite = builder.Environment.IsDevelopment()
    ? SameSiteMode.Lax
    : SameSiteMode.Strict;

        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                if (IsApiRequest(context.Request))
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                else
                    context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                if (IsApiRequest(context.Request))
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                else
                    context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .Configure<IMemoryCache>((options, cache) =>
    {
        options.SessionStore = new MemoryCacheTicketStore(cache);
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

// ── Services ────────────────────────────────────────────────
builder.Services.AddScoped<IProductService,           ProductService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<IAuditService,             AuditService>();
builder.Services.AddScoped<PartyService>();
builder.Services.AddScoped<IInvoiceService,           InvoiceService>();
builder.Services.AddScoped<IPaymentService,           PaymentService>();
builder.Services.AddScoped<IAdditionalChargeService,  AdditionalChargeService>();
builder.Services.AddScoped<ICashBoxService,           CashBoxService>();
builder.Services.AddScoped<IPersonalAccountService,   PersonalAccountService>();
builder.Services.AddScoped<IExpenseService,           ExpenseService>();
builder.Services.AddScoped<IFinancialReportsService,  FinancialReportsService>();
builder.Services.AddScoped<ICashFlowService,          CashFlowService>();
builder.Services.AddScoped<IFinancialDashboardService,FinancialDashboardService>();
builder.Services.AddScoped<IEmployeeLoanService, EmployeeLoanService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IEmployeeShiftService, EmployeeShiftService>();
builder.Services.AddScoped<IAttendanceService, AttendanceService>();
builder.Services.AddScoped<IPayrollService, PayrollService>();
builder.Services.AddScoped<IQuotationService, QuotationService>();
builder.Services.AddSingleton<ShareTokenService>();
builder.Services.AddScoped<IQuotationExportService, QuotationExportService>();
builder.Services.AddScoped<ISalesDeliveryStatusService, SalesDeliveryStatusService>();
builder.Services.AddScoped<IComplaintService, ComplaintService>();
builder.Services.AddScoped<IPasswordService, PasswordService>();
builder.Services.AddScoped<ICompanyInfoService, CompanyInfoService>();
builder.Services.AddScoped<GlobalSearchService>();
builder.Services.AddScoped<ICrmDashboardService, CrmDashboardService>();
builder.Services.AddScoped<IOpportunityService, OpportunityService>();
builder.Services.AddScoped<IInteractionService, InteractionService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<ICrmSettingsService, CrmSettingsService>();


var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseRateLimiter();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
// ============================================================
// 🏢 Company Info API
// ============================================================

// جلب بيانات الشركة
app.MapGet("/api/company-info", async (ICompanyInfoService service) =>
{
    var info = await service.GetCompanyInfoAsync();
    return info is not null ? Results.Ok(info) : Results.NotFound("لا توجد بيانات للشركة");
}).RequireAuthorization();

// تحديث بيانات الشركة
app.MapPut("/api/company-info", async (
    CompanyInfoUpdateDto dto,
    ICompanyInfoService service) =>
{
    try
    {
        var result = await service.UpdateCompanyInfoAsync(dto);
        return Results.Ok(result);
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(ex.Message);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }
}).RequireAuthorization();

// رفع شعار الشركة
app.MapPost("/api/company-info/logo", async (
    int companyId,
    string base64Image,
    ICompanyInfoService service) =>
{
    try
    {
        var result = await service.UploadLogoAsync(companyId, base64Image);
        return Results.Ok(new { message = result });
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(ex.Message);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }
}).RequireAuthorization();

// جلب فروع الشركة
app.MapGet("/api/company-locations", async (ICompanyInfoService service) =>
{
    var locations = await service.GetLocationsAsync();
    return Results.Ok(locations);
}).RequireAuthorization();

// إضافة فرع جديد
app.MapPost("/api/company-locations", async (
    CompanyLocationFormDto dto,
    ICompanyInfoService service) =>
{
    try
    {
        var result = await service.AddLocationAsync(dto);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }
}).RequireAuthorization();

// تعديل فرع
app.MapPut("/api/company-locations", async (
    CompanyLocationFormDto dto,
    ICompanyInfoService service) =>
{
    try
    {
        var result = await service.UpdateLocationAsync(dto);
        return Results.Ok(result);
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(ex.Message);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }
}).RequireAuthorization();

// حذف فرع
app.MapDelete("/api/company-locations/{locationId:int}", async (
    int locationId,
    ICompanyInfoService service) =>
{
    var success = await service.DeleteLocationAsync(locationId);
    return success ? Results.Ok(new { message = "تم الحذف بنجاح" }) : Results.NotFound("لم يتم العثور على الفرع");
}).RequireAuthorization();


app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// ============================================================
// 🔐 Login
// ============================================================
app.MapPost("/auth/login", async (
    [FromBody] LoginRequest request,
    HttpContext http,
    db24804Context db) =>
{
    var username = request.Username?.Trim();
    var password = request.Password ?? string.Empty;

    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        return Results.BadRequest("يرجى إدخال اسم المستخدم وكلمة المرور.");

    // ⚠️ بدون AsNoTracking عشان نقدر نعمل Update
    var user = await db.Users.SingleOrDefaultAsync(u => u.Username == username);

    if (user is null) return Results.BadRequest("بيانات الدخول غير صحيحة.");
    if (user.IsActive == false) return Results.BadRequest("بيانات الدخول غير صحيحة.");

    // ✅ تحقق ذكي - BCrypt أو Plain Text (Migration تلقائي)
    bool passwordValid;
    bool needsMigration = false;

    if (!string.IsNullOrEmpty(user.HashedPassword) && PasswordHasher.IsBcryptHash(user.HashedPassword))
    {
        // مشفر → تحقق بـ BCrypt
        passwordValid = PasswordHasher.VerifyPassword(password, user.HashedPassword);
    }
    else
    {
        // قديم → تحقق بـ Plain Text + علّم للـ Migration
        passwordValid   = string.Equals(user.Password, password, StringComparison.Ordinal);
        needsMigration  = passwordValid;
    }

    if (!passwordValid)
        return Results.BadRequest("بيانات الدخول غير صحيحة.");

    // ✅ Migration تلقائي عند أول Login
    if (needsMigration)
        user.HashedPassword = PasswordHasher.HashPassword(password);

    // ✅ تسجيل آخر دخول
    user.LastLogin = DateTime.Now;
    await db.SaveChangesAsync();

    // ✅ Claims
    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, user.Username),
        new(ClaimTypes.Role, string.IsNullOrWhiteSpace(user.Role) ? "User" : user.Role),
        new("UserId", user.UserId.ToString())
    };
    
    // ⭐ CRM Data Scoping — تاريخ بدء الاطلاع للمستخدم
    if (user.CrmAccessFromDate.HasValue)
        claims.Add(new Claim("CrmAccessFrom", user.CrmAccessFromDate.Value.ToString("yyyy-MM-dd")));

    var permissions = await (
        from up in db.UserPermissions.AsNoTracking()
        join p  in db.Permissions.AsNoTracking() on up.PermissionId equals p.PermissionId
        where up.UserId == user.UserId
        select new { p.FormName, up.CanView, up.CanAdd, up.CanEdit, up.CanDelete }
    ).ToListAsync();

    foreach (var item in permissions)
    {
        if (item.CanView)   claims.Add(new Claim("Permission", $"{item.FormName}:View"));
        if (item.CanAdd)    claims.Add(new Claim("Permission", $"{item.FormName}:Add"));
        if (item.CanEdit)   claims.Add(new Claim("Permission", $"{item.FormName}:Edit"));
        if (item.CanDelete) claims.Add(new Claim("Permission", $"{item.FormName}:Delete"));
    }

    var principal = new ClaimsPrincipal(
        new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

    // ✅ RememberMe - 30 يوم أو 8 ساعات
    await http.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal,
        new AuthenticationProperties
        {
            //IsPersistent = request.RememberMe,
            IsPersistent = false,
            AllowRefresh  = true,
            //ExpiresUtc    = request.RememberMe
               // ? DateTimeOffset.UtcNow.AddDays(30)
              //  : DateTimeOffset.UtcNow.AddHours(8)
        });

    return Results.Ok(new { message = "Authenticated" });
    
}).RequireRateLimiting("login"); 

// ============================================================
// Logout
// ============================================================
app.MapPost("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok(new { message = "Logged out" });
});

// ============================================================
// Current User Info
// ============================================================
app.MapGet("/auth/current-user", (ClaimsPrincipal user) => Results.Ok(new
{
    Username    = user.Identity?.Name,
    Role        = user.FindFirst(ClaimTypes.Role)?.Value,
    UserId      = user.FindFirst("UserId")?.Value,
    Permissions = user.Claims
                      .Where(c => c.Type == "Permission")
                      .Select(c => c.Value)
                      .ToList()
})).RequireAuthorization();
// ============================================================
// 🖼️ Product Image API
// ============================================================
app.MapGet("/api/product-images/{productId:int}", async (
    int productId,
    db24804Context db,
    IWebHostEnvironment env) =>
{
    // 1) جيب الصورة الرئيسية أولاً، وإلا الأحدث
    var image = await db.ProductImages
        .AsNoTracking()
        .Where(im => im.ProductId == productId)
        .OrderByDescending(im => im.IsPrimary)
        .ThenByDescending(im => im.CreatedAt)
        .ThenByDescending(im => im.ProductImagesId)
        .FirstOrDefaultAsync();

    if (image == null)
        return Results.NotFound();

    // 2) لو فيه ImagePath والملف موجود فعلاً
    if (!string.IsNullOrEmpty(image.ImagePath))
    {
        var relativePath = image.ImagePath.TrimStart('/');
        var fullPath = Path.Combine(env.WebRootPath, relativePath);

        if (File.Exists(fullPath))
        {
            var mimeType = GetMimeTypeFromExtension(fullPath);
            var fileBytes = await File.ReadAllBytesAsync(fullPath);
            return Results.File(fileBytes, mimeType, enableRangeProcessing: true);
        }
    }

    // 3) لو فيه ImageProduct (صورة قديمة من الداتابيز)
    if (image.ImageProduct != null && image.ImageProduct.Length > 0)
    {
        var mimeType = DetectMimeType(image.ImageProduct);
        return Results.File(image.ImageProduct, mimeType);
    }

    // 4) مفيش صورة صالحة
    return Results.NotFound();
}).RequireAuthorization();

// ⭐ Public endpoint للصور - للعميل من عرض السعر العام
app.MapGet("/api/public/product-images/{productId:int}", async (
    int productId,
    db24804Context db,
    IWebHostEnvironment env) =>
{
    var image = await db.ProductImages
        .AsNoTracking()
        .Where(im => im.ProductId == productId)
        .OrderByDescending(im => im.IsPrimary)
        .ThenByDescending(im => im.CreatedAt)
        .ThenByDescending(im => im.ProductImagesId)
        .FirstOrDefaultAsync();

    if (image == null)
        return Results.NotFound();

    if (!string.IsNullOrEmpty(image.ImagePath))
    {
        var relativePath = image.ImagePath.TrimStart('/');
        var fullPath = Path.Combine(env.WebRootPath, relativePath);

        if (File.Exists(fullPath))
        {
            var mimeType = GetMimeTypeFromExtension(fullPath);
            var fileBytes = await File.ReadAllBytesAsync(fullPath);
            return Results.File(fileBytes, mimeType, enableRangeProcessing: true);
        }
    }

    if (image.ImageProduct != null && image.ImageProduct.Length > 0)
    {
        var mimeType = DetectMimeType(image.ImageProduct);
        return Results.File(image.ImageProduct, mimeType);
    }

    return Results.NotFound();
});
// ============================================================
// 🖼️ Product Image By ID (للحصول على صورة معينة)
// ============================================================
app.MapGet("/api/product-image-by-id/{productImagesId:int}", async (
    int productImagesId,
    db24804Context db,
    IWebHostEnvironment env) =>
{
    // جيب الصورة بالـ ID
    var image = await db.ProductImages
        .AsNoTracking()
        .Where(im => im.ProductImagesId == productImagesId)
        .FirstOrDefaultAsync();

    if (image == null)
        return Results.NotFound();

    // 1) لو فيه ImagePath والملف موجود
    if (!string.IsNullOrEmpty(image.ImagePath))
    {
        var relativePath = image.ImagePath.TrimStart('/');
        var fullPath = Path.Combine(env.WebRootPath, relativePath);

        if (File.Exists(fullPath))
        {
            var mimeType = GetMimeTypeFromExtension(fullPath);
            var fileBytes = await File.ReadAllBytesAsync(fullPath);
            return Results.File(fileBytes, mimeType, enableRangeProcessing: true);
        }
    }

    // 2) لو فيه ImageProduct (صورة قديمة من الداتابيز)
    if (image.ImageProduct != null && image.ImageProduct.Length > 0)
    {
        var mimeType = DetectMimeType(image.ImageProduct);
        return Results.File(image.ImageProduct, mimeType);
    }

    // 3) مفيش صورة صالحة
    return Results.NotFound();
});



// ============================================================
// 🖼️ Image Helper Methods
// ============================================================
static string GetMimeTypeFromExtension(string filePath)
{
    var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
    return ext switch
    {
        ".png"  => "image/png",
        ".jpg"  => "image/jpeg",
        ".jpeg" => "image/jpeg",
        ".gif"  => "image/gif",
        ".webp" => "image/webp",
        ".bmp"  => "image/bmp",
        ".svg"  => "image/svg+xml",
        _       => "image/png"
    };
}

static string DetectMimeType(byte[] imageBytes)
{
    if (imageBytes.Length < 4) return "image/png";

    // PNG
    if (imageBytes[0] == 0x89 && imageBytes[1] == 0x50 &&
        imageBytes[2] == 0x4E && imageBytes[3] == 0x47)
        return "image/png";

    // JPEG
    if (imageBytes[0] == 0xFF && imageBytes[1] == 0xD8)
        return "image/jpeg";

    // GIF
    if (imageBytes[0] == 0x47 && imageBytes[1] == 0x49 &&
        imageBytes[2] == 0x46)
        return "image/gif";

    // WebP
    if (imageBytes.Length > 11 &&
        imageBytes[0] == 0x52 && imageBytes[1] == 0x49 &&
        imageBytes[2] == 0x46 && imageBytes[3] == 0x46 &&
        imageBytes[8] == 0x57 && imageBytes[9] == 0x45 &&
        imageBytes[10] == 0x42 && imageBytes[11] == 0x50)
        return "image/webp";

    // BMP
    if (imageBytes[0] == 0x42 && imageBytes[1] == 0x4D)
        return "image/bmp";

    return "image/png";
}
// ============ List Export Endpoints (للقائمة بالفلاتر) ============
app.MapPost("/api/quotations/export-excel", async (
    QuotationFilterDto filter,
    IQuotationExportService export) =>
{
    var (ok, error, xlsx, fileName) = await export.ExportQuotationsToExcelAsync(filter);
    if (!ok || xlsx == null) return Results.NotFound(new { error });
    return Results.File(xlsx,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        fileName);
}).RequireAuthorization();

app.MapPost("/api/quotations/export-pdf", async (
    QuotationFilterDto filter,
    IQuotationExportService export) =>
{
    var (ok, error, pdf, fileName) = await export.ExportQuotationsToPdfAsync(filter);
    if (!ok || pdf == null) return Results.NotFound(new { error });
    return Results.File(pdf, "application/pdf", fileName);
}).RequireAuthorization();



// ============ Single Quotation Endpoints + Public Share ============

// ============================================================
// ⭐ Customer Response Endpoint (للصفحة العامة - بدون login)
// ============================================================
app.MapPost("/api/public/quotations/{quotationId:int}/respond", async (
    int quotationId,
    [FromBody] CustomerResponseDto request,
    IQuotationService quotationService,
    NotificationService notify) => 
{
    // ✅ Validation
    if (request == null)
        return Results.BadRequest(new { error = "بيانات غير صحيحة" });

    if (string.IsNullOrEmpty(request.Response))
        return Results.BadRequest(new { error = "الرد مطلوب" });

    // ✅ تأكد من أن الرد صحيح
    if (request.Response != "Accepted" && request.Response != "Rejected")
        return Results.BadRequest(new { error = "الرد يجب أن يكون Accepted أو Rejected" });

    // ✅ تأكد من أن العرض موجود
    var quote = await quotationService.GetQuotationPublicAsync(quotationId);
    if (quote == null)
        return Results.NotFound(new { error = "عرض السعر غير موجود" });

    // ✅ منع التعديل لو العرض متحول لفاتورة
    if (quote.IsConverted)
        return Results.BadRequest(new { error = "هذا العرض تم تحويله لفاتورة بالفعل" });

    // ✅ منع التعديل لو العرض رد عليه مسبقاً
    if (quote.Status == "Accepted" || quote.Status == "Rejected")
        return Results.BadRequest(new { 
            error = $"تم تسجيل ردك مسبقاً ({(quote.Status == "Accepted" ? "موافقة" : "رفض")})" 
        });

    // ✅ تحديد اسم المسجل (اسم اللي دخل من اللينك أو اسم العميل من DB)
var responderName = !string.IsNullOrWhiteSpace(request.CustomerName)
    ? $"{request.CustomerName.Trim()} (Public Link)"
    : $"{quote.PartyName ?? "العميل"} (Public Link)";

// ✅ تحديث الحالة
var (ok, msg) = await quotationService.ChangeStatusAsync(
    quotationId, request.Response, responderName, isPublic: true);

    if (!ok)
        return Results.BadRequest(new { error = msg });

    // ✅ Logging الرفض مع السبب (لو فيه)
    // ✅ حفظ سبب الرفض لو موجود
if (request.Response == "Rejected" && !string.IsNullOrWhiteSpace(request.Reason))
{
    var savedReason = await quotationService.SaveRejectionReasonAsync(
        quotationId, request.Reason.Trim());
    
    if (!savedReason)
    {
        // الحالة اتغيرت بس السبب ما اتسجلش - log warning
        Console.WriteLine($"[WARN] Quote {quotationId} rejected but reason save FAILED. Reason: {request.Reason}");
    }
}
// ⭐ إشعار للموظف اللي عمل العرض + للأدمن
try
{
    var actionEmoji = request.Response == "Accepted" ? "✅" : "❌";
    var actionText = request.Response == "Accepted" ? "وافق على" : "رفض";
    var customerLabel = !string.IsNullOrWhiteSpace(request.CustomerName)
        ? request.CustomerName.Trim()
        : (quote.PartyName ?? "العميل");

    var title = $"{actionEmoji} {actionText} العميل: {customerLabel}";
    
    var message = $"العميل {customerLabel} {actionText} عرض السعر {quote.ReferenceNumber} " +
                  $"بقيمة {quote.GrandTotal:N2} ج";
    
    if (request.Response == "Rejected" && !string.IsNullOrWhiteSpace(request.Reason))
    {
        message += $"\nالسبب: {request.Reason.Trim()}";
    }

    // إشعار للموظف اللي عمل العرض
    if (!string.IsNullOrWhiteSpace(quote.CreatedBy))
    {
        await notify.AddAsync(
            title: title,
            message: message,
            recipientUser: quote.CreatedBy,
            createdBy: "Customer",
            formName: "quotations",
            relatedTable: "Quotations",
            relatedId: quotationId);
    }

    // إشعار للأدمن
    await notify.NotifyRoleAsync(
        title: title,
        message: message,
        role: "Admin",
        createdBy: "Customer",
        formName: "quotations",
        relatedTable: "Quotations",
        relatedId: quotationId);

    // إشعار لمدير المبيعات
    await notify.NotifyRoleAsync(
        title: title,
        message: message,
        role: "SalesManager",
        createdBy: "Customer",
        formName: "quotations",
        relatedTable: "Quotations",
        relatedId: quotationId);
}
catch (Exception notifyEx)
{
    // مش بنفشل العملية لو الإشعار فشل
    Console.WriteLine($"[WARN] Failed to send notification: {notifyEx.Message}");
}


    return Results.Ok(new { 
        success = true, 
        message = request.Response == "Accepted" 
            ? "تم تسجيل موافقتك بنجاح. سيتم التواصل معك قريباً." 
            : "تم تسجيل ردك. شكراً لاهتمامك."
    });
});

// ============================================================
// ⭐ Track Customer Viewing (تتبع المشاهدة)
// ============================================================
app.MapPost("/api/public/quotations/{quotationId:int}/viewed", async (
    int quotationId,
    HttpContext http,
    IQuotationService quotationService) =>
{
    var quote = await quotationService.GetQuotationPublicAsync(quotationId);
    if (quote == null) return Results.NotFound();

    // ✅ سجل المشاهدة (يمكن إضافة جدول QuotationViews لاحقاً)
    var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var userAgent = http.Request.Headers.UserAgent.ToString();
    
    Console.WriteLine($"[Quote {quotationId}] Viewed by customer. IP: {ip}");

    return Results.Ok(new { tracked = true });
});
// ⭐ Public PDF endpoint (بدون auth - للعميل من الصفحة العامة)
app.MapGet("/api/public/quotations/{id:int}/pdf-view", async (
    int id,
    IQuotationExportService export) =>
{
    var (ok, error, pdf, fileName) = await export.GeneratePdfAsync(id);
    if (!ok || pdf == null) return Results.NotFound();
    
    // ⭐ inline display بدل download
    return Results.File(pdf, "application/pdf", 
        fileDownloadName: null, // null = inline
        enableRangeProcessing: true);
});
// ⭐ Public Excel endpoint
app.MapGet("/api/public/quotations/{id:int}/excel-view", async (
    int id,
    IQuotationExportService export) =>
{
    var (ok, error, xlsx, fileName) = await export.GenerateExcelAsync(id);
    if (!ok || xlsx == null) return Results.NotFound();
    return Results.File(xlsx,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        fileName);
});


app.MapQuotationExports();
app.Run();

// ============================================================
// Helpers
// ============================================================
static bool IsApiRequest(HttpRequest request)
{
    if (request.Path.StartsWithSegments("/auth")) return true;
    return request.Headers.Accept.Any(h =>
        h?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);
}

// ✅ RememberMe اتضاف هنا
public sealed record LoginRequest(string Username, string Password, bool RememberMe = false);
