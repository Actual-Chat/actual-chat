using System.Diagnostics.CodeAnalysis;
using ActualChat.Hardware;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class DeviceAwakeUI : ScopedServiceBase<UIHub>, ISleepDurationProvider, IDeviceAwakeUIBackend
{
    private static readonly string JSInitMethod = $"{BlazorUICoreModule.ImportName}.DeviceAwakeUI.init";

    private readonly DotNetObjectReference<IDeviceAwakeUIBackend> _backendRef;
    private readonly IMutableState<TimeSpan> _totalSleepDuration;

    private IJSRuntime JS => Hub.JSRuntime();

    public IState<TimeSpan> TotalSleepDuration => _totalSleepDuration;

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DeviceAwakeUI))]
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
        if (HostInfo.AppKind == AppKind.WebServer)
            // We reload whole app for SSB on awake. See base-layout.ts
            return;

        try {
            await JS.InvokeVoidAsync(JSInitMethod, _backendRef).ConfigureAwait(false);
            // Debug logic
            // _ = Task.Run(async () => {
            //     while (true) {
            //         await Task.Delay(TimeSpan.FromSeconds(10));
            //         _totalSleepDuration.Set(r => r.Value + TimeSpan.FromSeconds(5));
            //     }
            // });
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to initialize DeviceAwakeUI");
        }
    }

    public async Task SleepUntil(IMomentClock clock, Moment until, CancellationToken cancellationToken = default)
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
        => _totalSleepDuration.Value = TimeSpan.FromMilliseconds(totalSleepDurationMs);
}
