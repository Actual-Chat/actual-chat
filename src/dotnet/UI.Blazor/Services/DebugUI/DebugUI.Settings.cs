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
        Services.GetRequiredService<IDebugAudioSync>().IsAudioSyncEnabled = enable;
        Log.LogInformation("EnableAudioSync({Enable}): done", enable);
    }

    /// <summary>
    /// Switches the rendering mode without going through Settings -> User
    /// Interface -> Rendering Mode. Mirrors <c>RenderModeSelector.ChangeMode</c>.
    /// </summary>
    /// <param name="mode">"a" (Auto), "s" (Server), or "w" (WASM).</param>
    [JSInvokable]
    public Task SetRenderMode(string mode)
    {
        var key = (mode ?? "").Trim().ToLowerInvariant() switch {
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
