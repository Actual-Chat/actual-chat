using ActualChat.Kvas;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class LocalSettingsBackend : IBatchingKvasBackend
{
    private Dispatcher Dispatcher { get; }
    private IJSRuntime JS { get; }

    public LocalSettingsBackend(Dispatcher dispatcher, IJSRuntime js)
    {
        Dispatcher = dispatcher;
        JS = js;
    }

    public Task<string?[]> GetMany(string[] keys, CancellationToken cancellationToken = default)
        => Dispatcher.InvokeAsync(
            () => JS.InvokeAsync<string?[]>(
                $"{BlazorUICoreModule.ImportName}.LocalSettings.getMany",
                cancellationToken,
                new object[] { keys }
                ).AsTask());

    public Task SetMany(List<(string Key, string? Value)> updates, CancellationToken cancellationToken = default)
    {
        var dUpdates = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in updates)
            dUpdates[key] = value;
        return Dispatcher.InvokeAsync(
            () => JS.InvokeVoidAsync(
            $"{BlazorUICoreModule.ImportName}.LocalSettings.setMany",
            cancellationToken,
            dUpdates
            ).AsTask());
    }
}
