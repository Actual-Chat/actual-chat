using ActualChat.Kvas;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class LocalSettingsBackend : IBatchingKvasBackend
{
    private IJSRuntime JS { get; }

    public LocalSettingsBackend(IServiceProvider services)
        => JS = services.GetRequiredService<IJSRuntime>();

    public Task<string?[]> GetMany(string[] keys, CancellationToken cancellationToken = default)
        => JS.InvokeAsync<string?[]>(
            $"{BlazorUICoreModule.ImportName}.LocalSettings.getMany",
            cancellationToken,
            new object[] { keys }
            ).AsTask();

    public Task SetMany(List<(string Key, string? Value)> updates, CancellationToken cancellationToken = default)
    {
        var dUpdates = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in updates)
            dUpdates[key] = value;
        return JS.InvokeVoidAsync(
            $"{BlazorUICoreModule.ImportName}.LocalSettings.setMany",
            cancellationToken,
            dUpdates
            ).AsTask();
    }
}
