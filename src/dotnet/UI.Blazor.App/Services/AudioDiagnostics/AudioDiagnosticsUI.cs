using ActualChat.Hosting;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Gathers Audio Diagnostics UI data: the native audio-focus / session snapshot and
/// (web build only) the Web Audio playback state. <see cref="GetState"/> auto-invalidates
/// on a cadence, so it polls only while observed (i.e. the diagnostics modal is open).
/// </summary>
public class AudioDiagnosticsUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private static readonly string JSCollectMethod = $"{BlazorUIAppModule.ImportName}.collectWebAudioDiagnostics";
    private static readonly string JSResumeContextMethod = $"{BlazorUIAppModule.ImportName}.audioDebugResumeContext";

    private AudioFocusUI AudioFocusUI => Hub.AudioFocusUI;
    private bool IsWebAudioUsed => !HostInfo.AppKind.IsMaui();

    // AutoInvalidationDelay unit is seconds.
    [ComputeMethod(AutoInvalidationDelay = 3)]
    public virtual async Task<AudioDiagnosticsState> GetState(CancellationToken cancellationToken)
    {
        var focus = AudioFocusUI.GetDiagnostics();
        var web = await CollectWebAudioDiagnostics().ConfigureAwait(false);
        return new AudioDiagnosticsState(focus, web);
    }

    public Task Reactivate(CancellationToken cancellationToken = default)
        => AudioFocusUI.TryRecover(cancellationToken);

    public ValueTask ResumeContext()
        => JS.InvokeVoidAsync(JSResumeContextMethod);

    // Private methods

    private async Task<WebAudioDiagnostics?> CollectWebAudioDiagnostics()
    {
        if (!IsWebAudioUsed)
            return null;
        return await JS.InvokeAsync<WebAudioDiagnostics>(JSCollectMethod).ConfigureAwait(false);
    }
}

/// <summary>
/// A single Audio Diagnostics snapshot: the native audio-focus / session state and
/// (web only) the Web Audio playback state. <see cref="Web"/> is null on native.
/// </summary>
public sealed record AudioDiagnosticsState(
    AudioFocusDiagnostics Focus,
    WebAudioDiagnostics? Web);
