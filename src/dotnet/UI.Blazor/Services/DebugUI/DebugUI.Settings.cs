using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

// Methods that mutate / read configurable client-side state — onboarding,
// bubbles, audio sync, render mode, thread-pool tuning.
public sealed partial class DebugUI
{
    [JSInvokable]
    public async Task SetVirtualListOverlay(bool enable)
    {
        await Hub.UserSettingsUI.UserAppSettings()
            .Update(x => x with { IsVirtualListOverlayEnabled = enable })
            .ConfigureAwait(false);
        Log.LogInformation("SetVirtualListOverlay({Enable}): done", enable);
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
    public void ResetOnboarding(bool enable)
    {
        Hub.OnboardingUI.ResetOnboarding(enable);
        Log.LogInformation("ResetOnboarding({Enable}): done", enable);
    }

    [JSInvokable]
    public async Task ResetBubbles(bool enable)
    {
        await Hub.BubbleUI.ResetBubbles(enable).ConfigureAwait(true);
        Log.LogInformation("ResetBubbles({Enable}): done", enable);
    }

    [JSInvokable]
    public void EnableAudioSync(bool enable)
    {
        Services.GetRequiredService<IDebugAudio>().IsAudioSyncEnabled = enable;
        Log.LogInformation("EnableAudioSync({Enable}): done", enable);
    }

    [JSInvokable]
    public void ForceRecordingStatus(string? status)
    {
        Services.GetRequiredService<IDebugAudio>().ForceRecordingStatus(status);
        Log.LogInformation("ForceRecordingStatus({Status}): done", status);
    }

    [JSInvokable]
    public Task SetRenderMode(string mode)
    {
        // mode is "a" (Auto), "s" (Server) or "w" (WASM); mirrors RenderModeSelector.ChangeMode
        var key = (mode ?? "").Trim().ToLower() switch {
            "a" => "a",
            "s" => "s",
            "w" => "w",
            _ => throw StandardError.Constraint(
                $"Unknown render mode '{mode}'. Use 'a' (Auto), 's' (Server), or 'w' (WASM)."),
        };
        var renderModeHelper = Services.GetRequiredService<RenderModeHelper>();
        var renderMode = RenderModeDef.GetOrDefault(key);
        renderModeHelper.ChangeMode(renderMode);
        Log.LogInformation("SetRenderMode('{Mode}'): done", key);
        return Task.CompletedTask;
    }
}
