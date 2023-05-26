using ActualChat.Kvas;
using ActualChat.UI.Blazor.Module;
using Stl.Diagnostics;
using Stl.Locking;

namespace ActualChat.UI.Blazor.Services;

public sealed class LocalSettingsBackend : IBatchingKvasBackend, IAsyncDisposable
{
    private readonly AsyncLock _asyncLock = new AsyncLock(ReentryMode.CheckedPass);
    private IJSObjectReference? _jsRef;
    private bool _isDisposed;

    private IJSRuntime JS { get; }
    private ILogger? DebugLog { get; }

    public LocalSettingsBackend(IServiceProvider services)
    {
        JS = services.GetRequiredService<IJSRuntime>();
        DebugLog = services.GetRequiredService<ILogger<LocalSettingsBackend>>().IfEnabled(LogLevel.Debug);
    }

    public async Task<string?[]> GetMany(string[] keys, CancellationToken cancellationToken = default)
    {
        var jsRef = await GetInstance(cancellationToken);
        var result = await jsRef.InvokeAsync<string?[]>(
                "getMany",
                cancellationToken,
                new object[] {keys}
            );
        DebugLog?.LogDebug("GetMany. Keys: '{Keys}', Result: '{Result}'",
            keys.ToDelimitedString(), result.ToDelimitedString());
        return result;
    }

    public async Task SetMany(List<(string Key, string? Value)> updates, CancellationToken cancellationToken = default)
    {
        var dUpdates = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in updates)
            dUpdates[key] = value;
        if (DebugLog != null) {
            var dataAsString = updates.Select(c => $"'{c.Key}':'{c.Value}'").ToDelimitedString();
            DebugLog.LogDebug("About to invoke SetMany. Data: '{Data}'", dataAsString);
        }
        var jsRef = await GetInstance(cancellationToken);
        await jsRef.InvokeVoidAsync(
            "setMany",
            cancellationToken,
            dUpdates
        );
    }

    public async ValueTask DisposeAsync()
    {
        using var __ = await _asyncLock.Lock(default);
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(LocalSettingsBackend));
        _isDisposed = true;
        await _jsRef.DisposeSilentlyAsync();
        _jsRef = null;
    }

    private async Task<IJSObjectReference> GetInstance(CancellationToken cancellationToken)
    {
        using var __ = await _asyncLock.Lock(cancellationToken);
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(LocalSettingsBackend));
        return _jsRef ??= await JS.InvokeAsync<IJSObjectReference>(
            $"{BlazorUICoreModule.ImportName}.LocalSettings.getInstance",
            cancellationToken
        );
    }
}
