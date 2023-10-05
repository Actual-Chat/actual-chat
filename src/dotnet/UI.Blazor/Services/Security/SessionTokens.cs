using ActualChat.Security;
using ActualChat.UI.Blazor.Module;
using Stl.Locking;

namespace ActualChat.UI.Blazor.Services;

public sealed class SessionTokens(IServiceProvider services) : WorkerBase, IComputeService
{
    public const string HeaderName = "Session";

    private static readonly string JSSetCurrentMethod = $"{BlazorUICoreModule.ImportName}.SessionTokens.setCurrent";

    private readonly AsyncLock _asyncLock = AsyncLock.New(LockReentryMode.Unchecked);
    private volatile SecureToken? _current;
    private Session? _session;
    private ISecureTokens? _secureTokens;
    private DeviceAwakeUI? _deviceAwakeUI;
    private IJSRuntime? _js;
    private IServerClock? _clock;
    private ILogger? _log;

    private IServiceProvider Services { get; } = services;
    private Session Session => _session ??= Services.Session();
    private ISecureTokens SecureTokens => _secureTokens ??= Services.GetRequiredService<ISecureTokens>();
    private DeviceAwakeUI DeviceAwakeUI => _deviceAwakeUI ??= Services.GetRequiredService<DeviceAwakeUI>();
    private IJSRuntime JS => _js ??= Services.GetRequiredService<IJSRuntime>();
    private IMomentClock Clock => _clock ??= Services.Clocks().ServerClock;
    private ILogger Log => _log ??= Services.LogFor(GetType());
    private Moment Now => Clock.Now;

    public TimeSpan RefreshLifespan { get; init; } = SecureToken.Lifespan - TimeSpan.FromMinutes(5);
    public TimeSpan NoRefreshLifespan { get; init; } = SecureToken.Lifespan - TimeSpan.FromMinutes(1);
    public SecureToken? Current => _current;

    public ValueTask<SecureToken> Get(CancellationToken cancellationToken = default)
        => Get(RefreshLifespan, cancellationToken);

    public async ValueTask<SecureToken> Get(TimeSpan minLifespan, CancellationToken cancellationToken = default)
    {
        minLifespan = minLifespan.Clamp(default, NoRefreshLifespan);
        var minExpiresAt = Now + minLifespan;
        var result = _current;
        if (result != null && result.ExpiresAt >= minExpiresAt)
            return result;

        result = await GetNew(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async ValueTask<SecureToken> GetNew(CancellationToken cancellationToken = default)
    {
        using var _ = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        var result = _current;
        if (result != null && result.ExpiresAt >= Now + NoRefreshLifespan)
            return result;

        result = await SecureTokens.CreateSessionToken(Session, cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _current, result);
        return result;
    }

    // Protected & private methods

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var retryDelays = RetryDelaySeq.Exp(0.1, 5);
        return AsyncChainExt.From(AutoRefresh)
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
                await JS.InvokeVoidAsync(JSSetCurrentMethod, cancellationToken, current.Token).ConfigureAwait(false);
                jsToken = current.Token;
            }

            await DeviceAwakeUI
                .SleepUntil(Clock, current.ExpiresAt - minLifespan, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
