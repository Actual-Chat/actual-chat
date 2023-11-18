using ActualChat.Hosting;
using ActualChat.UI;
using Stl.Locking;

namespace ActualChat.Permissions;

public abstract class PermissionHandler : WorkerBase
{
    private readonly IMutableState<bool?> _cached;
    private HostInfo? _hostInfo;
    private SystemSettingsUI? _systemSettingsUI;
    private IDispatcherResolver? _dispatcherResolver;
    private IMomentClock? _clock;
    private ILogger? _log;

    protected IServiceProvider Services { get; }
    protected HostInfo HostInfo => _hostInfo ??= Services.GetRequiredService<HostInfo>();
    protected SystemSettingsUI SystemSettingsUI
        => _systemSettingsUI ??= Services.GetRequiredService<SystemSettingsUI>();
    protected IDispatcherResolver DispatcherResolver
        => _dispatcherResolver ??= Services.GetRequiredService<IDispatcherResolver>();
    protected IMomentClock Clock => _clock ??= Services.Clocks().CpuClock;
    protected ILogger Log => _log ??= Services.LogFor(GetType());

    protected AsyncLock AsyncLock { get; } = AsyncLock.New(LockReentryMode.CheckedPass);
    protected TimeSpan? ExpirationPeriod { get; init; } = TimeSpan.FromSeconds(15);

    public IState<bool?> Cached => _cached;

    protected PermissionHandler(IServiceProvider services, bool mustStart = true)
    {
        Services = services;
        _log = services.LogFor(GetType());

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

        if (!DispatcherResolver.WhenReady.IsCompleted)
            await DispatcherResolver.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await DispatcherResolver.Dispatcher.InvokeAsync(async () => {
            Log.LogDebug("Check");
            var isGranted = await Get(cancellationToken).ConfigureAwait(true);
            SetUnsafe(isGranted);
            if (isGranted ?? false)
                return true;
            if (!mustRequest)
                return false;

            Log.LogDebug("Request");
            var maybeGranted = await Request(cancellationToken).ConfigureAwait(true);
            if (!maybeGranted)
                await Troubleshoot(cancellationToken).ConfigureAwait(true);

            Log.LogDebug("Post-request check");
            isGranted = await Get(cancellationToken).ConfigureAwait(false);
            SetUnsafe(isGranted);
            return isGranted ?? false;
        }).ConfigureAwait(false);
    }

    public void ForgetCached()
        => _cached.Value = null;

    public async Task<bool?> Check(CancellationToken cancellationToken)
    {
        if (_cached.Value == true)
            return true;

        using var _ = await AsyncLock.Lock(cancellationToken).ConfigureAwait(false);

        if (_cached.Value == true)
            return true;

        if (!DispatcherResolver.WhenReady.IsCompleted)
            await DispatcherResolver.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await DispatcherResolver.Dispatcher.InvokeAsync(async () => {
            Log.LogDebug("Check");
            var isGranted = await Get(cancellationToken).ConfigureAwait(false);
            SetUnsafe(isGranted);
            return isGranted;
        }).ConfigureAwait(false);
    }

    // Protected methods

    protected abstract Task<bool?> Get(CancellationToken cancellationToken);
    protected abstract Task<bool> Request(CancellationToken cancellationToken);
    protected abstract Task Troubleshoot(CancellationToken cancellationToken);

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        CancellationTokenSource? expirationCts = null;
        await foreach (var cCached in Cached.Changes(cancellationToken).ConfigureAwait(false)) {
            Log.LogDebug("Cached: {Cached}", cCached.Value);
            expirationCts.CancelAndDisposeSilently();
            expirationCts = null;
            if (cCached.Value != true)
                continue;

#pragma warning disable CA2000
            expirationCts = cancellationToken.CreateLinkedTokenSource();
#pragma warning restore CA2000
            var expirationToken = expirationCts.Token;
            var cExpected = cCached;
            if (ExpirationPeriod is { } expirationPeriod)
                _ = BackgroundTask.Run(async () => {
                    await Clock.Delay(expirationPeriod, expirationToken).ConfigureAwait(false);
                    await Set(null, cExpected, cancellationToken).ConfigureAwait(false);
                }, Log, "Expiration task failed", CancellationToken.None);
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

    protected Task OpenSystemSettings()
        => SystemSettingsUI.Open();
}
