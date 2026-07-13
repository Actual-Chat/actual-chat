using ActualChat.Hosting;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Gathers audio diagnostics for the Audio Diagnostics UI: the native audio-focus /
/// session snapshot, and — on the web build only — the Web Audio playback state.
/// The worker loop refreshes ~1 Hz and invalidates <see cref="GetState"/> only when
/// the snapshot actually changed, so observers don't re-render on every tick.
/// </summary>
public class AudioDiagnosticsUI(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub), IComputeService, INotifyInitialized
{
    private static readonly string JSCollectMethod = $"{BlazorUIAppModule.ImportName}.collectAudioPlaybackDiagnostics";
    private static readonly string JSResumeContextMethod = $"{BlazorUIAppModule.ImportName}.audioDebugResumeContext";
    private static readonly TimeSpan RefreshPeriod = TimeSpan.FromSeconds(1);
    private static readonly RetryDelaySeq RetryDelays = RetryDelaySeq.Exp(0.5, 1);

    private volatile AudioDiagnosticsState _state = AudioDiagnosticsState.None;

    private AudioFocusUI AudioFocusUI => Hub.AudioFocusUI;
    private bool IsWebAudioUsed => !Hub.HostInfo.AppKind.IsMaui();

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod]
    public virtual Task<AudioDiagnosticsState> GetState(CancellationToken cancellationToken)
        => Task.FromResult(_state);

    public Task Reactivate(CancellationToken cancellationToken = default)
        => AudioFocusUI.TryRecover(cancellationToken);

    public ValueTask ResumeContext()
        => JS.InvokeVoidAsync(JSResumeContextMethod);

    // Protected methods

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(RefreshState)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelays, Log)
            .Run(cancellationToken);

    // Private methods

    private async Task RefreshState(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            var focus = AudioFocusUI.GetDiagnostics();
            var playback = await CollectPlaybackDiagnostics().ConfigureAwait(false);
            var state = new AudioDiagnosticsState(focus, playback);
            // Skip the invalidation (and the observer re-render it triggers) when
            // nothing changed since the last tick.
            if (state != _state) {
                _state = state;
                using (Invalidation.Begin())
                    _ = GetState(default);
            }
            await Task.Delay(RefreshPeriod, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<AudioPlaybackDiagnostics?> CollectPlaybackDiagnostics()
    {
        if (!IsWebAudioUsed)
            return null;
        return await JS.InvokeAsync<AudioPlaybackDiagnostics>(JSCollectMethod).ConfigureAwait(false);
    }
}

/// <summary>
/// A single Audio Diagnostics snapshot: the native audio-focus / session state and
/// (web only) the Web Audio playback state. <see cref="Playback"/> is null on native.
/// </summary>
public sealed record AudioDiagnosticsState(
    AudioFocusDiagnostics Focus,
    AudioPlaybackDiagnostics? Playback)
{
    public static readonly AudioDiagnosticsState None = new(AudioFocusDiagnostics.Unsupported, null);
}
