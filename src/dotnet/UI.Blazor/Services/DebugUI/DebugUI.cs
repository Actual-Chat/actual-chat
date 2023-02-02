using ActualChat.Hosting;
using ActualChat.UI.Blazor.Diagnostics;
using ActualChat.UI.Blazor.Module;
using Stl.Fusion.Diagnostics;

namespace ActualChat.UI.Blazor.Services;

public sealed class DebugUI : IDisposable
{
    private DotNetObjectReference<DebugUI>? _backendRef;

    private IServiceProvider Services { get; }
    private IJSRuntime JS { get; }
    private ILogger Log { get; }
    private HostInfo HostInfo { get; }

    public Task WhenReady { get; }

    public DebugUI(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        JS = services.GetRequiredService<IJSRuntime>();
        HostInfo = services.GetRequiredService<HostInfo>();
        WhenReady = Initialize();
    }

    public void Dispose()
        => _backendRef.DisposeSilently();

    private async Task Initialize()
    {
        _backendRef = DotNetObjectReference.Create(this);
        await JS.InvokeVoidAsync(
            $"{BlazorUICoreModule.ImportName}.DebugUI.init",
            _backendRef);
    }

    [JSInvokable]
    public void OnStartFusionMonitor()
    {
        Log.LogInformation("OnStartFusionMonitor");
        var isServer = HostInfo.AppKind.IsServer();
        if (isServer)
            throw StandardError.Constraint("This method can be used only on WASM or MAUI client.");

        Services.GetRequiredService<FusionMonitor>().Start();
    }

    [JSInvokable]
    public void OnStartTaskMonitor()
    {
        Log.LogInformation("OnStartTaskMonitor");
        var isServer = HostInfo.AppKind.IsServer();
        if (isServer)
            throw StandardError.Constraint("This method can be used only on WASM or MAUI client.");

        Services.GetRequiredService<TaskMonitor>().Start();
        Services.GetRequiredService<TaskEventListener>().Start();
    }

    [JSInvokable]
    public void OnRedirect(string url)
    {
        Log.LogInformation("OnRedirect, Url: {Url}", url);
        Services.GetRequiredService<NavigationManager>().NavigateTo(url);
    }
}
