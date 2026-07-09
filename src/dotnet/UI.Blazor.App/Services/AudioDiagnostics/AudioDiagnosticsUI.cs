using ActualChat.Hosting;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Gathers audio diagnostics for the Audio Diagnostics UI: the native audio-focus /
/// session snapshot, and — on the web build only — the Web Audio playback state.
/// </summary>
// TODO: UIWorkerBase
public sealed class AudioDiagnosticsUI(AppUIHub hub)
{
    private static readonly string JSCollectMethod = $"{BlazorUIAppModule.ImportName}.collectAudioPlaybackDiagnostics";
    private static readonly string JSResumeContextMethod = $"{BlazorUIAppModule.ImportName}.audioDebugResumeContext";

    private IJSRuntime JS => hub.JS;
    private AudioFocusUI AudioFocusUI => hub.AudioFocusUI;

    public bool IsWebAudioUsed => !hub.HostInfo.AppKind.IsMaui();

    public AudioFocusDiagnostics GetFocusDiagnostics()
        => AudioFocusUI.GetDiagnostics();

    // TODO: GetState must be a compute method

    public async Task<AudioPlaybackDiagnostics?> CollectPlaybackDiagnostics()
    {
        if (!IsWebAudioUsed)
            return null;
        return await JS.InvokeAsync<AudioPlaybackDiagnostics>(JSCollectMethod).ConfigureAwait(false);
    }

    public Task Reactivate(CancellationToken cancellationToken = default)
        => AudioFocusUI.TryRecover(cancellationToken);

    public ValueTask ResumeContext()
        => JS.InvokeVoidAsync(JSResumeContextMethod);
}
