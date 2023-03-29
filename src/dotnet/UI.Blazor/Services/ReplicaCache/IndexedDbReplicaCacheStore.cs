using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public interface IReplicaCacheStore
{
    Task<string?> TryGetValue(string key);
    Task SetValue(string key, string value);
}

public class IndexedDbReplicaCacheStore : IReplicaCacheStore
{
    private Func<IJSRuntime> JsAccessor { get; }

    public IndexedDbReplicaCacheStore(Func<IJSRuntime> jsAccessor)
        => JsAccessor = jsAccessor;

    public async Task<string?> TryGetValue(string key)
        => await JsAccessor().InvokeAsync<string?>(
            $"{BlazorUICoreModule.ImportName}.ReplicaCache.get",
            key);

    public async Task SetValue(string key, string value)
        => await JsAccessor().InvokeAsync<string?>(
            $"{BlazorUICoreModule.ImportName}.ReplicaCache.set",
            key, value);
}
