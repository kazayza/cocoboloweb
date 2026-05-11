using COCOBOLOERPNEW.Components;
using COCOBOLOERPNEW.Models;
using COCOBOLOERPNEW.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddMudServices();

builder.Services.AddDbContext<db24804Context>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);

        options.Cookie.Name = "COCOBOLO.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                if (IsApiRequest(context.Request))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                if (IsApiRequest(context.Request))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();

builder.Services.AddHttpClient();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<PartyService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();

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

app.MapPost("/auth/login", async (
    [FromBody] LoginRequest request,
    HttpContext http,
    db24804Context db) =>
{
    var username = request.Username?.Trim();
    var password = request.Password ?? string.Empty;

    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        return Results.BadRequest("يرجى إدخال اسم المستخدم وكلمة المرور.");

    var user = await db.Users
        .AsNoTracking()
        .SingleOrDefaultAsync(u => u.Username == username);

    if (user is null)
        return Results.BadRequest("اسم المستخدم غير موجود.");

    if (user.IsActive == false)
        return Results.BadRequest("الحساب غير مفعل.");

    // ملاحظة مهمة:
    // هذا يحافظ على التوافق مع قاعدة البيانات الحالية عندك.
    // لاحقًا لازم نستبدله بـ Password Hashing حقيقي.
    if (!VerifyPassword(user.Password, password))
        return Results.BadRequest("كلمة المرور غير صحيحة.");

    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Role, string.IsNullOrWhiteSpace(user.Role) ? "User" : user.Role),
        new Claim("UserId", user.UserId.ToString())
    };

    var permissions = await (
        from up in db.UserPermissions.AsNoTracking()
        join p in db.Permissions.AsNoTracking()
            on up.PermissionId equals p.PermissionId
        where up.UserId == user.UserId
        select new
        {
            p.FormName,
            up.CanView,
            up.CanAdd,
            up.CanEdit,
            up.CanDelete
        }
    ).ToListAsync();

    foreach (var item in permissions)
    {
        if (item.CanView)
            claims.Add(new Claim("Permission", $"{item.FormName}:View"));

        if (item.CanAdd)
            claims.Add(new Claim("Permission", $"{item.FormName}:Add"));

        if (item.CanEdit)
            claims.Add(new Claim("Permission", $"{item.FormName}:Edit"));

        if (item.CanDelete)
            claims.Add(new Claim("Permission", $"{item.FormName}:Delete"));
    }

    var identity = new ClaimsIdentity(
        claims,
        CookieAuthenticationDefaults.AuthenticationScheme);

    var principal = new ClaimsPrincipal(identity);

    await http.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal,
        new AuthenticationProperties
        {
            IsPersistent = true,
            AllowRefresh = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
        });

    return Results.Ok(new { message = "Authenticated" });
});

app.MapPost("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok(new { message = "Logged out" });
});

app.MapGet("/auth/current-user", (ClaimsPrincipal user) =>
{
    var result = new
    {
        Username = user.Identity?.Name,
        Role = user.FindFirst(ClaimTypes.Role)?.Value,
        UserId = user.FindFirst("UserId")?.Value,
        Permissions = user.Claims
            .Where(c => c.Type == "Permission")
            .Select(c => c.Value)
            .ToList()
    };

    return Results.Ok(result);
}).RequireAuthorization();

app.Run();

static bool IsApiRequest(HttpRequest request)
{
    if (request.Path.StartsWithSegments("/auth"))
        return true;

    return request.Headers.Accept.Any(h =>
        h?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);
}

static bool VerifyPassword(string? storedPassword, string? incomingPassword)
{
    return string.Equals(storedPassword, incomingPassword, StringComparison.Ordinal);
}

public sealed record LoginRequest(string Username, string Password);