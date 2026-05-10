using Microsoft.JSInterop;

namespace COCOBOLOERPNEW.Services
{
    public class BrowserStorageService
    {
        private readonly IJSRuntime _jsRuntime;

        public BrowserStorageService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task SetAsync(string key, string value)
        {
            await _jsRuntime.InvokeVoidAsync("sessionStorage.setItem", key, value);
        }

        public async Task<string?> GetAsync(string key)
        {
            return await _jsRuntime.InvokeAsync<string?>("sessionStorage.getItem", key);
        }

        public async Task RemoveAsync(string key)
        {
            await _jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", key);
        }
    }
}