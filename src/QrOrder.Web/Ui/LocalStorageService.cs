using Microsoft.JSInterop;

namespace QrOrder.Web.Ui
{
    public class LocalStorageService
    {
        private readonly IJSRuntime _js;
        public LocalStorageService(IJSRuntime js) => _js = js;

        public ValueTask<string?> GetAsync(string key) =>
            _js.InvokeAsync<string?>("qrLocalStorage.get", key);

        public ValueTask SetAsync(string key, string value) =>
            _js.InvokeVoidAsync("qrLocalStorage.set", key, value);

        public ValueTask RemoveAsync(string key) =>
            _js.InvokeVoidAsync("qrLocalStorage.remove", key);
    }
}
