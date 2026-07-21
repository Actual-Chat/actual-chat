using ActualChat.Hosting;
using ActualChat.UI.Blazor.Module;
using ActualLab.Rpc;
using Microsoft.Extensions.Hosting;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Provides debugging utilities accessible from JavaScript console for diagnostics.
/// </summary>
/// <remarks>
/// Method order roughly mirrors the JS-side <c>DebugUI</c> in
/// <c>debug-ui.ts</c>. Auth helpers live in <c>DebugUI.Auth.cs</c>;
/// configuration helpers in <c>DebugUI.Settings.cs</c>; runtime monitors in
/// <c>DebugUI.Monitors.cs</c>.
/// </remarks>
public sealed partial class DebugUI : UIServiceBase<UIHub>, IDisposable
{
    private static readonly string JSInitMethod = $"{BlazorUICoreModule.ImportName}.DebugUI.init";

    private DotNetObjectReference<DebugUI>? _blazorRef;

    public Func<Task>? ShowMicTroubleshooterHandler { get; set; }
    public Func<Task>? ShowPhotoTroubleshooterHandler { get; set; }
    public Func<Task>? ShowIncomingShareModalHandler { get; set; }
    public Action<int>? TestVideoRecordingQualityChangeHandler { get; set; }
    public Action<int>? TestVideoPlaybackQualityChangeHandler { get; set; }
    public Task WhenReady { get; }

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
    public void StopServer()
    {
        // Local-dev-only: same check as the HTTP /health/stop endpoint.
        if (HostInfo is not { IsDevelopmentInstance: true, BaseUrlKind: BaseUrlKind.Local })
            throw StandardError.Unauthorized("StopServer works on local-dev server instances only.");

        Log.LogInformation("StopServer requested via DebugUI");
        if (Services.GetService<IHostApplicationLifetime>() is not { } appLifetime) {
            Log.LogWarning("StopServer: IHostApplicationLifetime is unavailable");
            return;
        }
        appLifetime.StopApplication();
        // Same hard-exit watchdog the HTTP /health/stop endpoint uses.
        // Without it a hung disposer leaves the process running while
        // the loop sits on Step 3/3 forever.
        HardExit.Schedule(TimeSpan.FromSeconds(15),
            "DebugUI.StopServer: graceful shutdown didn't finish within 15s");
    }

    [JSInvokable]
    public void DisconnectRpc()
    {
        if (HostInfo.AppKind == AppKind.Unknown)
            return;

        Log.LogInformation("Disconnecting RPC connection...");
        var rpcHub = Services.RpcHub();
        var clientPeer = rpcHub.GetClientPeer(RpcRef.Default);
        _ = clientPeer.Disconnect();
    }

    [JSInvokable]
    public void NavigateTo(string url)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(url);
        Log.LogInformation("NavigateTo '{Url}': done", url);
    }

    [JSInvokable]
    public async Task ShowMicTroubleshooter()
    {
        if (ShowMicTroubleshooterHandler is { } handler)
            await handler().ConfigureAwait(true);
        Log.LogInformation("ShowMicTroubleshooter: done");
    }

    [JSInvokable]
    public async Task ShowPhotoTroubleshooter()
    {
        if (ShowPhotoTroubleshooterHandler is { } handler)
            await handler().ConfigureAwait(true);
        Log.LogInformation("ShowPhotoTroubleshooter: done");
    }

    [JSInvokable]
    public async Task ShowIncomingShareModal()
    {
        if (ShowIncomingShareModalHandler is { } handler)
            await handler().ConfigureAwait(true);
        Log.LogInformation("ShowIncomingShareModal: done");
    }

    [JSInvokable]
    public void TestVideoRecordingQualityChange(int periodSeconds = 30)
    {
        TestVideoRecordingQualityChangeHandler?.Invoke(periodSeconds);
        Log.LogInformation("TestVideoRecordingQualityChange({Period}s): done", periodSeconds);
    }

    [JSInvokable]
    public void TestLog(int count = 1, int lineCount = 1)
    {
        count = Math.Clamp(count, 1, 200);
        lineCount = Math.Clamp(lineCount, 1, 200);
        var testLog = Services.LoggerFactory().CreateLogger("ActualChat.DebugUI.TestLog");
        var line = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.";
        for (var i = 0; i < count; i++) {
            var body = lineCount == 1 ? line : string.Join('\n', Enumerable.Range(0, lineCount).Select(n => $"L{n + 1}: {line}"));
            testLog.LogInformation("TestLog #{Index}/{Count} ({Lines} lines):\n{Body}", i + 1, count, lineCount, body);
        }
    }

    [JSInvokable]
    public void TestVideoPlaybackQualityChange(int periodSeconds = 30)
    {
        TestVideoPlaybackQualityChangeHandler?.Invoke(periodSeconds);
        Log.LogInformation("TestVideoPlaybackQualityChange({Period}s): done", periodSeconds);
    }
}
