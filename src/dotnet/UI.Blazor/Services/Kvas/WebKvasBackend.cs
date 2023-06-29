using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

public sealed class WebKvasBackend : IBatchingKvasBackend
{
    private readonly string _getManyName;
    private readonly string _setManyName;
    private readonly string _clearName;

    private IServiceProvider Services { get; }
    private IJSRuntime JS { get; }

    public WebKvasBackend(string name, IServiceProvider services)
    {
        Services = services;
        JS = services.IsScoped()
            ? services.SafeJSRuntime()
            : services.JSRuntime();
        _getManyName = $"{name}.getMany";
        _setManyName = $"{name}.setMany";
        _clearName = $"{name}.clear";
    }

    public async ValueTask<byte[]?[]> GetMany(string[] keys, CancellationToken cancellationToken = default)
    {
        var values = await JS.InvokeAsync<string?[]>(_getManyName, cancellationToken, new object[] { keys });
        var result = new byte[]?[keys.Length];
        for (var i = 0; i < values.Length; i++) {
            var value = values[i];
            result[i] = value == null ? null : Convert.FromBase64String(value);
        }
        return result;
    }

    public async Task SetMany(List<(string Key, byte[]? Value)> updates, CancellationToken cancellationToken = default)
    {
        var keys = new string[updates.Count];
        var values = new string?[updates.Count];
        var i = 0;
        foreach (var (key, value) in updates) {
            keys[i] = key;
            values[i++] = value == null ? null : Convert.ToBase64String(value);
        }
        try {
            await JS.InvokeVoidAsync(_setManyName, cancellationToken, keys, values);
        }
        catch (JSDisconnectedException) {
            // Suppress exception to avoid reprocessing.
            // JS invokes are no longer possible in a current scope.
        }
    }

    public Task Clear(CancellationToken cancellationToken = default)
        => JS.InvokeVoidAsync(_clearName, cancellationToken).AsTask();
}
