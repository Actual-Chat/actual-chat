namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Typed mirror of the TS <c>collectAudioPlaybackDiagnostics()</c> result.
/// <see cref="Context"/> is null on native apps, which have no Web Audio pipeline.
/// </summary>
public sealed record AudioPlaybackDiagnostics(
    AudioContextDiagnostics? Context,
    AudioPlayerDiagnostics[] Players);
