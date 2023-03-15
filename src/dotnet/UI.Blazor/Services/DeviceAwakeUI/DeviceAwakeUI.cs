using ActualChat.Hosting;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class DeviceAwakeUI : IDeviceAwakeUIBackend, IDisposable
{
    private readonly DotNetObjectReference<IDeviceAwakeUIBackend> _backendRef;
    private readonly IMutableState<TimeSpan> _totalSleepDuration;

    private HostInfo HostInfo { get; }
    private IJSRuntime JS { get; }
    private ILogger Log { get; }

    public IState<TimeSpan> TotalSleepDuration => _totalSleepDuration;

    public DeviceAwakeUI(IServiceProvider services)
    {
        JS = services.GetRequiredService<IJSRuntime>();
        Log = services.LogFor<DeviceAwakeUI>();
        HostInfo = services.GetRequiredService<HostInfo>();

        _totalSleepDuration = services.StateFactory().NewMutable(
            TimeSpan.Zero,
            StateCategories.Get(GetType(), nameof(TotalSleepDuration)));
        _backendRef = DotNetObjectReference.Create<IDeviceAwakeUIBackend>(this);
        _ = Initialize();
    }

    private async Task Initialize()
    {
        if (HostInfo.AppKind == AppKind.WebServer)
            // we reload whole app for SSB on awake. See base-layout.ts
            return;

        try {
            await JS.InvokeVoidAsync($"{BlazorUICoreModule.ImportName}.{nameof(DeviceAwakeUI)}.init", _backendRef)
                .ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to initialize DeviceAwakeUI");
        }
    }

    [JSInvokable]
    public void OnDeviceAwake(double totalSleepDurationMs)
        => _totalSleepDuration.Value = TimeSpan.FromMilliseconds(totalSleepDurationMs);

    void IDisposable.Dispose()
        => _backendRef.DisposeSilently();
}
