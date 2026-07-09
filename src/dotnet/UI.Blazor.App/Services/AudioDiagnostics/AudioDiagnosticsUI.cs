using ActualChat.Hosting;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Gathers audio diagnostics for the Audio Diagnostics UI: the native audio-focus /
/// session snapshot, and — on the web build only — the Web Audio playback state.
/// <see cref="GetState"/> is invalidated ~1 Hz by the worker loop, so observers
/// re-read only while the panel is open.
/// </summary>
public class AudioDiagnosticsUI : UIWorkerBase<AppUIHub>, IComputeService
{
    private static readonly string JSCollectMethod = $"{BlazorUIAppModule.ImportName}.collectAudioPlaybackDiagnostics";
    private static readonly string JSResumeContextMethod = $"{BlazorUIAppModule.ImportName}.audioDebugResumeContext";
    private static readonly TimeSpan RefreshPeriod = TimeSpan.FromSeconds(1);

    private AudioFocusUI AudioFocusUI => Hub.AudioFocusUI;
    private bool IsWebAudioUsed => !Hub.HostInfo.AppKind.IsMaui();

    public AudioDiagnosticsUI(AppUIHub hub) : base(hub)
        => this.Start();

    [ComputeMethod]
    public virtual async Task<AudioDiagnosticsState> GetState(CancellationToken cancellationToken)
    {
        var focus = AudioFocusUI.GetDiagnostics();
        var playback = await CollectPlaybackDiagnostics().ConfigureAwait(false);
        return new AudioDiagnosticsState(focus, playback);
    }

    public Task Reactivate(CancellationToken cancellationToken = default)
        => AudioFocusUI.TryRecover(cancellationToken);

    public ValueTask ResumeContext()
        => JS.InvokeVoidAsync(JSResumeContextMethod);

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            using (Invalidation.Begin())
                _ = GetState(default);
            await Task.Delay(RefreshPeriod, cancellationToken).ConfigureAwait(false);
        }
    }

    // Private methods

    private async Task<AudioPlaybackDiagnostics?> CollectPlaybackDiagnostics()
    {
        if (!IsWebAudioUsed)
            return null;
        return await JS.InvokeAsync<AudioPlaybackDiagnostics>(JSCollectMethod).ConfigureAwait(false);
    }
}
