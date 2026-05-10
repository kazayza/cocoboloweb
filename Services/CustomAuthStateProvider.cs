using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.JSInterop;

namespace COCOBOLOERPNEW.Services
{
    public class CustomAuthStateProvider : AuthenticationStateProvider
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IJSRuntime _jsRuntime;
        private ClaimsPrincipal _cachedUser = new ClaimsPrincipal(new ClaimsIdentity());

        public CustomAuthStateProvider(IHttpContextAccessor httpContextAccessor, IJSRuntime jsRuntime)
        {
            _httpContextAccessor = httpContextAccessor;
            _jsRuntime = jsRuntime;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            // أولاً: حاول من HttpContext (أول تحميل)
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.User?.Identity?.IsAuthenticated == true)
            {
                _cachedUser = httpContext.User;
                await CacheUserInSession(_cachedUser);
                return new AuthenticationState(_cachedUser);
            }

            // ثانياً: حاول من SessionStorage (بعد Refresh)
            try
            {
                var json = await _jsRuntime.InvokeAsync<string>("sessionStorage.getItem", "user_session");
                if (!string.IsNullOrEmpty(json))
                {
                    var userData = JsonSerializer.Deserialize<UserSessionData>(json);
                    if (userData != null)
                    {
                        var claims = new List<Claim>
                        {
                            new Claim(ClaimTypes.Name, userData.Username),
                            new Claim(ClaimTypes.Role, userData.Role ?? "User"),
                            new Claim("UserId", userData.UserId.ToString())
                        };
                        
                        foreach (var perm in userData.Permissions)
                        {
                            claims.Add(new Claim("Permission", perm));
                        }
                        
                        var identity = new ClaimsIdentity(claims, "Cookies");
                        _cachedUser = new ClaimsPrincipal(identity);
                        return new AuthenticationState(_cachedUser);
                    }
                }
            }
            catch { }

            return new AuthenticationState(_cachedUser);
        }

        public async Task SetUser(ClaimsPrincipal user)
        {
            _cachedUser = user;
            await CacheUserInSession(user);
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_cachedUser)));
        }

        private async Task CacheUserInSession(ClaimsPrincipal user)
        {
            try
            {
                if (user.Identity?.IsAuthenticated == true)
                {
                    var userData = new UserSessionData
                    {
                        Username = user.Identity.Name ?? "",
                        Role = user.FindFirst(ClaimTypes.Role)?.Value ?? "User",
                        UserId = int.Parse(user.FindFirst("UserId")?.Value ?? "0"),
                        Permissions = user.Claims
                            .Where(c => c.Type == "Permission")
                            .Select(c => c.Value)
                            .ToList()
                    };
                    
                    var json = JsonSerializer.Serialize(userData);
                    await _jsRuntime.InvokeVoidAsync("sessionStorage.setItem", "user_session", json);
                }
            }
            catch { }
        }

        public async Task ClearUser()
        {
            _cachedUser = new ClaimsPrincipal(new ClaimsIdentity());
            try
            {
                await _jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", "user_session");
            }
            catch { }
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_cachedUser)));
        }

        private class UserSessionData
        {
            public string Username { get; set; } = "";
            public string Role { get; set; } = "User";
            public int UserId { get; set; }
            public List<string> Permissions { get; set; } = new();
        }
    }
}