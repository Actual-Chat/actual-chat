using ActualChat.Kvas;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class LocalStorageBackend : IBatchingKvasBackend
{
    private IJSRuntime JS { get; }

    public LocalStorageBackend(IJSRuntime js)
        => JS = js;

    public async Task<string?[]> GetMany(string[] keys, CancellationToken cancellationToken = default)
        => await JS.InvokeAsync<string?[]>(
            $"{BlazorUICoreModule.ImportName}.LocalStorage.getMany",
            cancellationToken,
            keys);

    public async Task SetMany(List<(string Key, string? Value)> updates, CancellationToken cancellationToken = default)
    {
        var dUpdates = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in updates)
            dUpdates[key] = value;
        await JS.InvokeVoidAsync(
            $"{BlazorUICoreModule.ImportName}.LocalStorage.setMany",
            cancellationToken,
            dUpdates);
    }
}
