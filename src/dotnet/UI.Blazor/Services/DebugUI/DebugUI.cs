using ActualChat.Hosting;
using ActualChat.UI.Blazor.Diagnostics;
using ActualChat.UI.Blazor.Module;
using Stl.Fusion.Diagnostics;

namespace ActualChat.UI.Blazor.Services;

public sealed class DebugUI : IDisposable
{
    private static readonly string JSInitMethod = $"{BlazorUICoreModule.ImportName}.DebugUI.init";

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
        JS = services.JSRuntime();
        HostInfo = services.GetRequiredService<HostInfo>();
        WhenReady = Initialize();
    }

    public void Dispose()
        => _backendRef.DisposeSilently();

    private Task Initialize()
    {
        _backendRef = DotNetObjectReference.Create(this);
        return JS.InvokeVoidAsync(JSInitMethod, _backendRef).AsTask();
    }

    [JSInvokable]
    public void StartFusionMonitor()
    {
        var isServer = HostInfo.AppKind.IsServer();
        if (isServer)
            throw StandardError.Constraint("This method can be used only on WASM or MAUI client.");

        Services.GetRequiredService<FusionMonitor>().Start();
        Log.LogInformation("StartFusionMonitor: done");
    }

    [JSInvokable]
    public void StartTaskMonitor()
    {
        var isServer = HostInfo.AppKind.IsServer();
        if (isServer)
            throw StandardError.Constraint("This method can be used only on WASM or MAUI client.");

        Services.GetRequiredService<TaskMonitor>().Start();
        Services.GetRequiredService<TaskEventListener>().Start();
        Log.LogInformation("StartTaskMonitor: done");
    }

    [JSInvokable]
    public string GetThreadPoolSettings()
    {
        ThreadPool.GetMinThreads(out var minThreads, out var minIOThreads);
        ThreadPool.GetMaxThreads(out var maxThreads, out var maxIOThreads);
        ThreadPool.GetAvailableThreads(out var threads, out var ioThreads);
        return $"Thread count: Available: {(threads, ioThreads)}, Range: [{(minThreads, minIOThreads)} ... {(maxThreads, maxIOThreads)}]";
    }

    [JSInvokable]
    public void ChangeThreadPoolSettings(int min, int minIO, int max, int maxIO)
    {
        var isDev = HostInfo.IsDevelopmentInstance;
        if (!isDev)
            throw StandardError.Constraint("This method can be used only on development instances.");

        ThreadPool.SetMinThreads(min, minIO);
        ThreadPool.SetMaxThreads(max, maxIO);
        Log.LogInformation("ChangeThreadPoolSettings: done, current settings: {Settings}", GetThreadPoolSettings());
    }

    [JSInvokable]
    public void NavigateTo(string url)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(url);
        Log.LogInformation("NavigateTo '{Url}': done", url);
    }
}
