using Microsoft.JSInterop;

namespace ActualChat;

/// <summary>
/// Provides access to browser local storage via JavaScript interop.
/// </summary>
public class LocalStorage(IJSRuntime js)
{
    private IJSRuntime JS { get; } = js;

    public async Task<string?> GetString(string key)
        => await JS.InvokeAsync<string?>("localStorage.getItem", key).ConfigureAwait(false);

    public async Task SetString(string key, string value)
        => await JS.InvokeVoidAsync("localStorage.setItem", key, value).ConfigureAwait(false);

    public async Task RemoveItem(string key)
        => await JS.InvokeVoidAsync("localStorage.removeItem", key).ConfigureAwait(false);
}
