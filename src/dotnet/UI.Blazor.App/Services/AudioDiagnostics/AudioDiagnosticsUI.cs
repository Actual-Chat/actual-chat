using System.Text.Json;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Gathers audio diagnostics for the Audio Diagnostics UI: the native audio-focus /
/// session snapshot, and — on the web build only — the Web Audio playback state.
/// The worker loop refreshes ~1 Hz and invalidates <see cref="GetState"/> only when
/// the snapshot actually changed, so observers don't re-render on every tick.
/// </summary>
public class AudioDiagnosticsUI : UIWorkerBase<AppUIHub>, IComputeService
{
    private static readonly string JSCollectMethod = $"{BlazorUIAppModule.ImportName}.collectAudioPlaybackDiagnostics";
    private static readonly string JSResumeContextMethod = $"{BlazorUIAppModule.ImportName}.audioDebugResumeContext";
    private static readonly TimeSpan RefreshPeriod = TimeSpan.FromSeconds(1);
    private static readonly RetryDelaySeq RetryDelays = RetryDelaySeq.Exp(0.5, 1);

    private volatile AudioDiagnosticsState _state = AudioDiagnosticsState.None;
    private string _stateJson = "";

    private AudioFocusUI AudioFocusUI => Hub.AudioFocusUI;
    private bool IsWebAudioUsed => !Hub.HostInfo.AppKind.IsMaui();

    public AudioDiagnosticsUI(AppUIHub hub) : base(hub)
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
            // TODO: don't compare JSON, records must be comparable
            // Skip the invalidation (and the observer re-render it triggers) when
            // nothing changed; the DTO collections rule out record value equality,
            // so compare a serialized signature instead.
            var stateJson = JsonSerializer.Serialize(state);
            if (!string.Equals(stateJson, _stateJson, StringComparison.Ordinal)) {
                _state = state;
                _stateJson = stateJson;
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
