using COCOBOLOERPNEW.Components;
using COCOBOLOERPNEW.Models;
using COCOBOLOERPNEW.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddMudServices();

// ✅ Memory Cache
builder.Services.AddMemoryCache();
builder.Services.AddScoped<CacheService>();

builder.Services.AddDbContext<db24804Context>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.LoginPath         = "/login";
        options.AccessDeniedPath  = "/access-denied";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan    = TimeSpan.FromHours(8);

        options.Cookie.Name         = "COCOBOLO.Auth";
        options.Cookie.HttpOnly     = true;
        options.Cookie.IsEssential  = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite     = SameSiteMode.Lax;

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

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

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

    if (user is null)          return Results.BadRequest("اسم المستخدم غير موجود.");
    if (user.IsActive == false) return Results.BadRequest("الحساب غير مفعل.");

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
        return Results.BadRequest("كلمة المرور غير صحيحة.");

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
            IsPersistent = request.RememberMe,
            AllowRefresh  = true,
            ExpiresUtc    = request.RememberMe
                ? DateTimeOffset.UtcNow.AddDays(30)
                : DateTimeOffset.UtcNow.AddHours(8)
        });

    return Results.Ok(new { message = "Authenticated" });
});

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
