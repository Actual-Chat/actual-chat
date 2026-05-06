using ActualChat.Hosting;
using ActualChat.UI.Blazor.Diagnostics;
using ActualChat.UI.Blazor.Module;
using ActualLab.Fusion.Diagnostics;
using ActualLab.Rpc;
using Microsoft.Extensions.Hosting;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Provides debugging utilities accessible from JavaScript console for diagnostics.
/// </summary>
public sealed class DebugUI : UIServiceBase<UIHub>, IDisposable
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
    }

    [JSInvokable]
    public void ResetOnboarding(bool enable)
    {
        Hub.OnboardingUI.ResetOnboarding(enable);
        Log.LogInformation("ResetOnboarding({Enable}): done", enable);
    }

    [JSInvokable]
    public async Task ResetBubbles(bool enable)
    {
        await Hub.BubbleUI.ResetBubbles(enable).ConfigureAwait(false);
        Log.LogInformation("ResetBubbles({Enable}): done", enable);
    }

    [JSInvokable]
    public void EnableAudioSync(bool enable)
    {
        Services.GetRequiredService<IDebugAudioSync>().IsAudioSyncEnabled = enable;
        Log.LogInformation("EnableAudioSync({Enable}): done", enable);
    }

    [JSInvokable]
    public async Task SignIn(string phoneOrEmail, bool register = true, bool skipOnboarding = true, bool skipBubbles = true)
    {
        if (HostInfo is not { IsDevelopmentInstance: true, BaseUrlKind: BaseUrlKind.Local })
            throw StandardError.Unauthorized("SignIn works on local-dev server instances only.");

        var session = Hub.Session;
        var commander = Hub.Commander;
        var input = (phoneOrEmail ?? "").Trim();
        if (input.Length == 0)
            throw StandardError.Constraint("phoneOrEmail must be non-empty.");

        if (input.Contains('@')) {
            var email = Email.Parse(input);
            await commander.Call(new EmailAuth_SendTotp(session, email)).ConfigureAwait(false);
            var ok = await commander.Call(new EmailAuth_ValidateTotp(session, email, 111111)).ConfigureAwait(false);
            if (!ok)
                throw StandardError.Internal($"EmailAuth.ValidateTotp failed for '{email}'.");
        }
        else {
            var phone = await Hub.Phones.ParseWithCountryFallback(session, input, default).ConfigureAwait(false)
                ?? throw StandardError.Constraint($"Cannot parse phone '{input}'.");
            await commander.Call(new PhoneAuth_SendTotp(session, phone)).ConfigureAwait(false);
            var ok = await commander.Call(new PhoneAuth_ValidateTotp(session, phone, 111111)).ConfigureAwait(false);
            if (!ok)
                throw StandardError.Internal(
                    $"PhoneAuth.ValidateTotp failed for '{phone}'.");
        }

        if (register) {
            var json = await Hub.SessionTemporals
                .Get(session, Constants.SessionTemporals.PendingRegistrationKey, default)
                .ConfigureAwait(false);
            if (PendingRegistrationInfo.TryParseJson(json) is { } info)
                await commander.Call(new Accounts_ConfirmRegister(session, info.Token)).ConfigureAwait(false);
        }

        // Wait for the client-side AccountUI to observe the new (non-guest)
        // account before mutating onboarding/bubble state — those rely on
        // OwnAccount being settled. 5s is generous; locally it's typically <1s.
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5))) {
            try {
                await Hub.AccountUI.OwnAccount.Computed
                    .When(x => !x.IsGuest, cts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) {
                throw StandardError.Internal("SignIn timed out waiting for AccountUI.OwnAccount to become non-guest after 5s.");
            }
        }

        // Onboarding/bubble UIs render Blazor components on completion — must
        // run on the Dispatcher because the awaits above have already migrated
        // off it via the Commander/RPC pipeline.
        if (skipOnboarding) {
            await Hub.Dispatcher.InvokeAsync(() =>
                Hub.OnboardingUI.ResetOnboarding(false))
                .ConfigureAwait(false);
        }
        if (skipBubbles) {
            await Hub.Dispatcher.InvokeAsync(() =>
                Hub.BubbleUI.ResetBubbles(false))
                .ConfigureAwait(false);
        }

        Log.LogInformation(
            "SignIn('{Input}', register={Register}, skipOnboarding={SkipOnboarding}, skipBubbles={SkipBubbles}): done",
            input, register, skipOnboarding, skipBubbles);
    }

    [JSInvokable]
    public async Task SignOut()
    {
        await Hub.AccountUI.SignOut().ConfigureAwait(false);
        Log.LogInformation("SignOut: done");
    }

    [JSInvokable]
    public Task<string> GetUserId()
        => Task.FromResult(Hub.AccountUI.OwnAccount.Value.Id.Value);

    [JSInvokable]
    public async Task ShowMicTroubleshooter()
    {
        if (ShowMicTroubleshooterHandler is { } handler)
            await handler().ConfigureAwait(false);
        Log.LogInformation("ShowMicTroubleshooter: done");
    }

    [JSInvokable]
    public async Task ShowPhotoTroubleshooter()
    {
        if (ShowPhotoTroubleshooterHandler is { } handler)
            await handler().ConfigureAwait(false);
        Log.LogInformation("ShowPhotoTroubleshooter: done");
    }

    [JSInvokable]
    public async Task ShowIncomingShareModal()
    {
        if (ShowIncomingShareModalHandler is { } handler)
            await handler().ConfigureAwait(false);
        Log.LogInformation("ShowIncomingShareModal: done");
    }

    [JSInvokable]
    public void TestVideoRecordingQualityChange(int periodSeconds = 30)
    {
        TestVideoRecordingQualityChangeHandler?.Invoke(periodSeconds);
        Log.LogInformation("TestVideoRecordingQualityChange({Period}s): done", periodSeconds);
    }

    [JSInvokable]
    public void TestVideoPlaybackQualityChange(int periodSeconds = 30)
    {
        TestVideoPlaybackQualityChangeHandler?.Invoke(periodSeconds);
        Log.LogInformation("TestVideoPlaybackQualityChange({Period}s): done", periodSeconds);
    }
}
