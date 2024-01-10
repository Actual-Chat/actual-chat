using ActualChat.Security;
using ActualChat.UI.Blazor.Module;
using ActualLab.Locking;

namespace ActualChat.UI.Blazor.Services;

public sealed class SessionTokens(UIHub hub) : ScopedWorkerBase<UIHub>(hub), IComputeService
{
    public const string HeaderName = "Session";

    private static readonly string JSSetCurrentMethod = $"{BlazorUICoreModule.ImportName}.SessionTokens.setCurrent";

    private readonly AsyncLock _asyncLock = new(LockReentryMode.CheckedFail);
    private volatile SecureToken? _current;
    private ISecureTokens? _secureTokens;
    private DeviceAwakeUI? _deviceAwakeUI;
    private IJSRuntime? _js;

    private ISecureTokens SecureTokens => _secureTokens ??= Services.GetRequiredService<ISecureTokens>();
    private DeviceAwakeUI DeviceAwakeUI => _deviceAwakeUI ??= Services.GetRequiredService<DeviceAwakeUI>();
    private IMomentClock ServerClock => Clocks.ServerClock;
    private IJSRuntime JS => _js ??= Services.GetRequiredService<IJSRuntime>();

    public TimeSpan MinLifespan { get; init; } = TimeSpan.FromMinutes(60);
    public TimeSpan RefreshLifespan { get; init; } = TimeSpan.FromMinutes(15);
    public SecureToken? Current => _current;

    public ValueTask<SecureToken> Get(CancellationToken cancellationToken = default)
        => Get(MinLifespan, cancellationToken);

    // Protected & private methods

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var retryDelays = RetryDelaySeq.Exp(0.1, 5);
        return AsyncChain.From(AutoRefresh)
            .Log(LogLevel.Debug, Log)
            .RetryForever(retryDelays, Log)
            .RunIsolated(cancellationToken);
    }

    private async Task AutoRefresh(CancellationToken cancellationToken)
    {
        var jsToken = "";
        var minLifespan = RefreshLifespan;
        while (!cancellationToken.IsCancellationRequested) {
            var current = await Get(minLifespan, cancellationToken).ConfigureAwait(false);
            if (!OrdinalEquals(jsToken, current.Token)) {
                await JS.InvokeVoidAsync(JSSetCurrentMethod, CancellationToken.None, current.Token)
                    .AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
                jsToken = current.Token;
            }

            var now = ServerClock.Now;
            await DeviceAwakeUI
                .SleepUntil(ServerClock,  now + ((current.ExpiresAt - now) / 2) , cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask<SecureToken> Get(TimeSpan minLifespan, CancellationToken cancellationToken = default)
    {
        minLifespan = minLifespan
            .Add(TimeSpan.FromMinutes(1))
            .Clamp(default, SecureToken.Lifespan / 2);
        var minExpiresAt = ServerClock.Now + minLifespan;
        var result = _current;
        if (result != null && result.ExpiresAt >= minExpiresAt)
            return result;

        result = await GetNew(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async ValueTask<SecureToken> GetNew(CancellationToken cancellationToken = default)
    {
        using var releaser = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        releaser.MarkLockedLocally();

        var result = _current;
        if (result != null && result.ExpiresAt >= ServerClock.Now + (SecureToken.Lifespan / 2))
            return result;

        result = await SecureTokens.CreateSessionToken(Session, cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _current, result);
        return result;
    }

}
