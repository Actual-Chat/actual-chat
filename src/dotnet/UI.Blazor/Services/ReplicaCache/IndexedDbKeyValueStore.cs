using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class IndexedDbKeyValueStore : FlushingKeyValueStore
{
    private IJSRuntime JS { get; }

    public IndexedDbKeyValueStore(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        JS = services.GetRequiredService<IJSRuntime>();
    }

    protected override ValueTask<string?> StorageGet(HashedString key, CancellationToken cancellationToken)
        => JS.InvokeAsync<string?>(
            $"{BlazorUICoreModule.ImportName}.ReplicaCache.get", cancellationToken,
            key.Value);

    protected override ValueTask StorageSet(HashedString key, string? value, CancellationToken cancellationToken)
        => JS.InvokeVoidAsync(
            $"{BlazorUICoreModule.ImportName}.ReplicaCache.set", cancellationToken,
            key.Value, value);

    protected override ValueTask StorageClear()
        => JS.InvokeVoidAsync($"{BlazorUICoreModule.ImportName}.ReplicaCache.clear");
}
