using ActualChat.Hosting;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Gathers audio diagnostics for the Audio Diagnostics UI: the native audio-focus /
/// session snapshot, and — on the web build only — the Web Audio playback state.
/// <see cref="GetState"/> re-collects on a fixed cadence via auto-invalidation, so it
/// polls only while something observes it (i.e. while the diagnostics modal is open).
/// </summary>
public class AudioDiagnosticsUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private static readonly string JSCollectMethod = $"{BlazorUIAppModule.ImportName}.collectAudioPlaybackDiagnostics";
    private static readonly string JSResumeContextMethod = $"{BlazorUIAppModule.ImportName}.audioDebugResumeContext";

    private AudioFocusUI AudioFocusUI => Hub.AudioFocusUI;
    private bool IsWebAudioUsed => !HostInfo.AppKind.IsMaui();

    // AutoInvalidationDelay is in seconds: re-collect every 3s while observed.
    [ComputeMethod(AutoInvalidationDelay = 3)]
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

    // Private methods

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
    AudioPlaybackDiagnostics? Playback);
