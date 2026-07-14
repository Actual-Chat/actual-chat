using ActualChat.Hosting;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Gathers Audio Diagnostics UI data: the native audio-focus / session snapshot and
/// (web build only) the Web Audio playback state. <see cref="Get"/> auto-invalidates
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
    public virtual async Task<AudioDiagnostics> Get(CancellationToken cancellationToken)
    {
        var focus = AudioFocusUI.GetDiagnostics();
        var web = await CollectWebAudioDiagnostics().ConfigureAwait(false);
        return new AudioDiagnostics(focus, web);
    }

    [ComputeMethod]
    public virtual async Task<bool> IsEnabled(CancellationToken cancellationToken = default)
    {
        if (HostInfo.IsDevelopmentInstance)
            return true;

        var appSettings = await LocalSettings.LocalAppSettings().Get(cancellationToken).ConfigureAwait(false);
        return appSettings.IsAudioDiagnosticsEnabledOrDefault;
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
public sealed record AudioDiagnostics(
    AudioFocusDiagnostics Focus,
    WebAudioDiagnostics? Web);

/// <summary>
/// Typed mirror of the TS <c>collectWebAudioDiagnostics()</c> result.
/// <see cref="Context"/> is null on native apps, which have no Web Audio pipeline.
/// </summary>
public sealed record WebAudioDiagnostics(
    WebAudioContextDiagnostics? Context,
    WebAudioPlayerDiagnostics[] Players)
{
    public bool Equals(WebAudioDiagnostics? other)
        => other is not null
            && Equals(Context, other.Context)
            && Players.SequenceEqual(other.Players);

    public override int GetHashCode()
        => HashCode.Combine(Context, Players.Length);
}

public sealed record WebAudioPlayerDiagnostics(
    string InternalId,
    string? AuthorId,
    string PlaybackState,
    string BufferState,
    double? PresentationLagMs,
    double TargetBufferSizeMs,
    double PlayingAt,
    double BufferedDuration);
