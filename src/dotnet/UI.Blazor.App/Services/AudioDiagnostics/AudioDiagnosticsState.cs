using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

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
