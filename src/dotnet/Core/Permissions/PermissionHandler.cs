using Stl.Locking;

namespace ActualChat.Permissions;

public abstract class PermissionHandler : WorkerBase
{
    private readonly IMutableState<bool?> _cached;

    protected IServiceProvider Services { get; }
    protected ILogger Log { get; }

    protected IMomentClock Clock { get; init; }
    protected AsyncLock AsyncLock { get; } = new (ReentryMode.CheckedPass);
    protected TimeSpan? ExpirationPeriod { get; init; } = TimeSpan.FromSeconds(15);

    public IState<bool?> Cached => _cached;

    protected PermissionHandler(IServiceProvider services, bool mustStart = true)
    {
        Services = services;
        Log = services.LogFor(GetType());

        Clock = services.Clocks().CpuClock;
        _cached = services.StateFactory().NewMutable(
            (bool?)null,
            StateCategories.Get(GetType(), nameof(Cached)));
        if (mustStart)
            this.Start();
    }

    public ValueTask<bool> CheckOrRequest(CancellationToken cancellationToken = default)
        => CheckOrRequest(true, cancellationToken);
    public async ValueTask<bool> CheckOrRequest(bool mustRequest, CancellationToken cancellationToken = default)
    {
        if (_cached.Value == true)
            return true;

        using var _ = await AsyncLock.Lock(cancellationToken).ConfigureAwait(false);

        if (_cached.Value == true)
            return true;

        Log.LogDebug("Check");
        var isGranted = await Check(cancellationToken).ConfigureAwait(false);
        SetUnsafe(isGranted);
        if (isGranted)
            return true;

        if (!mustRequest)
            return false;

        Log.LogDebug("Request");
        var wasRequested = await Request(cancellationToken).ConfigureAwait(false);
        if (!wasRequested)
            return false;

        Log.LogDebug("Post-request check");
        isGranted = await Check(cancellationToken).ConfigureAwait(false);
        SetUnsafe(isGranted);
        return isGranted;
    }

    public void Reset()
        => _cached.Value = null;

    // Protected methods

    protected abstract Task<bool> Check(CancellationToken cancellationToken);
    protected abstract Task<bool> Request(CancellationToken cancellationToken);

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        CancellationTokenSource? expirationCts = null;
        await foreach (var cCached in Cached.Changes(cancellationToken).ConfigureAwait(false)) {
            Log.LogDebug("Cached: {Cached}", cCached.Value);
            expirationCts.CancelAndDisposeSilently();
            expirationCts = null;
            if (cCached.Value != true)
                continue;

            expirationCts = cancellationToken.CreateLinkedTokenSource();
            var expirationToken = expirationCts.Token;
            var cExpected = cCached;
            if (ExpirationPeriod is { } expirationPeriod)
                _ = BackgroundTask.Run(async () => {
                        await Clock.Delay(expirationPeriod, expirationToken).ConfigureAwait(false);
                        await Set(null, cExpected, cancellationToken).ConfigureAwait(false);
                    },
                    Log,
                    "Expiration task failed",
                    CancellationToken.None);
        }
    }

    protected ValueTask Set(bool? value, CancellationToken cancellationToken = default)
        => Set(value, null, cancellationToken);
    protected async ValueTask Set(bool? value, Computed<bool?>? expected = null, CancellationToken cancellationToken = default)
    {
        using var _ = await AsyncLock.Lock(cancellationToken).ConfigureAwait(false);
        SetUnsafe(value, expected);
    }

    protected void SetUnsafe(bool? value, Computed<bool?>? expected = null)
    {
        if (expected != null && _cached.Computed != expected)
            return;

        if (_cached.Value != value)
            _cached.Value = value;
    }
}
