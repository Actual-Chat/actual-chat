using ActualChat.Hosting;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class DeviceAwakeUI : IDeviceAwakeUIBackend, IDisposable
{
    private readonly DotNetObjectReference<IDeviceAwakeUIBackend> _backendRef;
    private TaskSource<Unit> _whenNextAwake = TaskSource.New<Unit>(true);
    private HostInfo HostInfo { get; }
    private IJSRuntime JS { get; }

    private ILogger Log { get; }

    public DeviceAwakeUI(IServiceProvider services)
    {
        JS = services.GetRequiredService<IJSRuntime>();
        Log = services.LogFor<DeviceAwakeUI>();
        HostInfo = services.GetRequiredService<HostInfo>();
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
            Log.LogError(e, "Failed to initialize deviceAwakeUI js");
        }
    }

    public Task WhenNextAwake()
        => _whenNextAwake.Task;

    [JSInvokable()]
    public void OnDeviceAwake()
    {
        _whenNextAwake.SetResult(Unit.Default);
        _whenNextAwake = TaskSource.New<Unit>(true);
    }

    void IDisposable.Dispose()
        => _backendRef.DisposeSilently();
}
