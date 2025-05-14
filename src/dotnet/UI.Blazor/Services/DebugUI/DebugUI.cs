using ActualChat.Hosting;
using ActualChat.UI.Blazor.Diagnostics;
using ActualChat.UI.Blazor.Module;
using ActualLab.Fusion.Diagnostics;
using ActualLab.Rpc;

namespace ActualChat.UI.Blazor.Services;

public sealed class DebugUI : UIServiceBase<UIHub>, IDisposable
{
    private static readonly string JSInitMethod = $"{BlazorUICoreModule.ImportName}.DebugUI.init";

    private DotNetObjectReference<DebugUI>? _blazorRef;

    public Task WhenReady { get; }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DebugUI))]
    public DebugUI(UIHub hub) : base(hub)
    {
        _blazorRef = DotNetObjectReference.Create(this);
        WhenReady = JS.InvokeVoidAsync(JSInitMethod, _blazorRef).AsTask();
    }

    public void Dispose()
    {
        _blazorRef.DisposeSilently();
        _blazorRef = null;
    }

    [JSInvokable]
    public void StartFusionMonitor()
    {
        var isServer = HostInfo.HostKind.IsServer();
        if (isServer)
            throw StandardError.Constraint("This method can be used only on WASM or MAUI client.");

        Services.GetRequiredService<FusionMonitor>().Start();
        Log.LogInformation("StartFusionMonitor: done");
    }

    [JSInvokable]
    public void StartTaskMonitor()
    {
        var isServer = HostInfo.HostKind.IsServer();
        if (isServer)
            throw StandardError.Constraint("This method can be used only on WASM or MAUI client.");

        Services.GetRequiredService<TaskMonitor>().Start();
        Services.GetRequiredService<TaskEventListener>().Start();
        Log.LogInformation("StartTaskMonitor: done");
    }

#pragma warning disable CA1822 // Can be static
    [JSInvokable]
    public string GetThreadPoolSettings()
#pragma warning restore CA1822
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

    [JSInvokable]
    public void DisconnectRpc()
    {
        if (HostInfo.AppKind == AppKind.Unknown)
            return;

        Log.LogInformation("Disconnecting RPC connection...");
        var rpcHub = Services.RpcHub();
        var clientPeer = rpcHub.GetClientPeer(RpcPeerRef.Default);
        _ = clientPeer.Disconnect();
    }
}
