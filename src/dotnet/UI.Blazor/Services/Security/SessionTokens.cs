using ActualChat.Security;
using ActualChat.UI.Blazor.Module;
using Stl.Locking;

namespace ActualChat.UI.Blazor.Services;

public sealed class SessionTokens : WorkerBase, IComputeService
{
    private static readonly string JSSetCurrentMethod = $"{BlazorUICoreModule.ImportName}.SessionTokens.setCurrent";

    private readonly AsyncLock _asyncLock = AsyncLock.New(LockReentryMode.Unchecked);
    private volatile SecureToken? _current;

    private Session Session { get; }
    private ISecureTokens SecureTokens { get; }
    private IJSRuntime JS { get; }
    private IMomentClock Clock { get; }
    private ILogger Log { get; }
    private Moment Now => Clock.Now;

    public TimeSpan AsGoodAsNewLifespan { get; init; } = SecureToken.Lifespan - TimeSpan.FromMinutes(1);
    public TimeSpan MinLifespan { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan RefreshReserve { get; init; } = TimeSpan.FromMinutes(1);
    public SecureToken? Current => _current;

    public SessionTokens(IServiceProvider services)
    {
        Session = services.Session();
        SecureTokens = services.GetRequiredService<ISecureTokens>();
        JS = services.JSRuntime();
        Clock = services.Clocks().ServerClock;
        Log = services.LogFor(GetType());
    }

    public ValueTask<SecureToken> Get(CancellationToken cancellationToken = default)
        => Get(MinLifespan, cancellationToken);

    public async ValueTask<SecureToken> Get(TimeSpan minLifespan, CancellationToken cancellationToken = default)
    {
        minLifespan = minLifespan.Clamp(MinLifespan, AsGoodAsNewLifespan);
        var result = _current;
        if (result == null || result.ExpiresAt < Now + minLifespan)
            result = await GetNew(cancellationToken);
        return result;
    }

    public async ValueTask<SecureToken> GetNew(CancellationToken cancellationToken = default)
    {
        using var _ = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        var result = _current;
        if (result != null && result.ExpiresAt >= Now + AsGoodAsNewLifespan)
            return result;

        result = await SecureTokens.CreateSessionToken(Session, cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _current, result);
        return result;
    }

    // Protected & private methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var retryDelays = RetryDelaySeq.Exp(0.1, 5);
        await new AsyncChain(nameof(AutoRefresh), AutoRefresh)
            .Log(LogLevel.Debug, Log)
            .RetryForever(retryDelays, Log)
            .RunIsolated(cancellationToken);
    }

    private async Task AutoRefresh(CancellationToken cancellationToken)
    {
        var minLifespan = MinLifespan + RefreshReserve;
        while (!cancellationToken.IsCancellationRequested) {
            var current = await Get(minLifespan, cancellationToken);
            await JS.InvokeVoidAsync(JSSetCurrentMethod, cancellationToken, current.Token).ConfigureAwait(false);
            var refreshAt = current.ExpiresAt - minLifespan;
            await Clock.Delay(refreshAt, cancellationToken).ConfigureAwait(false);
        }
    }
}
