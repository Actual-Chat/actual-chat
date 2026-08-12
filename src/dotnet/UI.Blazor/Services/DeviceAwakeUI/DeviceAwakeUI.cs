using ActualChat.Hardware;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Detects device sleep/wake cycles and triggers reconnection when the device wakes up.
/// </summary>
public class DeviceAwakeUI : UIServiceBase<UIHub>, ISleepDurationProvider, IDeviceAwakeUIBackend
{
    private static readonly string JSInitMethod = $"{BlazorUICoreModule.ImportName}.DeviceAwakeUI.init";

    private readonly DotNetObjectReference<IDeviceAwakeUIBackend> _backendRef;
    private readonly MutableState<TimeSpan> _totalSleepDuration;

    public IState<TimeSpan> TotalSleepDuration => _totalSleepDuration;

    public DeviceAwakeUI(UIHub hub) : base(hub)
    {
        _totalSleepDuration = StateFactory.NewMutable(
            TimeSpan.Zero,
            StateCategories.Get(GetType(), nameof(TotalSleepDuration)));
        _backendRef = DotNetObjectReference.Create<IDeviceAwakeUIBackend>(this);
        Hub.RegisterDisposable(_backendRef);
        _ = Initialize();
    }

    private async Task Initialize()
    {
        try {
            await JS.InvokeVoidAsync(JSInitMethod, _backendRef).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to initialize DeviceAwakeUI");
        }
    }

    public Task WhenSleepDetected(CancellationToken cancellationToken)
    {
        var totalSleepDuration = TotalSleepDuration.Value;
        return TotalSleepDuration.Computed.WhenUntyped(
            c => ((Computed<TimeSpan>)c).Value != totalSleepDuration,
            cancellationToken);
    }

    public Task Sleep(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var systemClock = Clocks.SystemClock;
        return Sleep(systemClock, systemClock.Now + duration, cancellationToken);
    }

    public async Task Sleep(MomentClock clock, Moment until, CancellationToken cancellationToken = default)
    {
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            var delay = until - clock.Now;
            if (delay <= TimeSpan.Zero)
                return;

            var cts = cancellationToken.CreateLinkedTokenSource();
            try {
                var delayTask = clock.Delay(delay, cts.Token);
                var whenSleepCompletedTask = TotalSleepDuration.Computed.WhenInvalidated(cts.Token);
                await Task.WhenAny(delayTask, whenSleepCompletedTask).ConfigureAwait(false);
            }
            finally {
                cts.CancelAndDisposeSilently();
            }
        }
    }

    [JSInvokable]
    public void OnDeviceAwake(double totalSleepDurationMs)
    {
        var totalSleepDuration = TimeSpan.FromMilliseconds(totalSleepDurationMs);
        _totalSleepDuration.Value = totalSleepDuration;
        Hub.ReconnectUI.TryReconnectOnDeviceAwake(totalSleepDuration);
        _ = ResyncServerTime();
    }

    // Private methods

    private async Task ResyncServerTime()
    {
        // Right on wake the server-clock offset deserves a fresh measurement (the wall
        // clock may have stepped, and the JS push could've been missed while frozen) —
        // don't leave it to ServerTimeSync's next drift-check tick, which background-tab
        // timer throttling can delay far past its nominal cadence.
        try {
            if (Services.GetService<ServerTimeSync>() is { } serverTimeSync)
                await serverTimeSync.EnsureSynced(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Post-wake server time re-sync failed");
        }
    }
}
