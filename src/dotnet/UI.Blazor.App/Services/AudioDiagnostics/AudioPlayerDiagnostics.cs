namespace ActualChat.UI.Blazor.App.Services;

public sealed record AudioPlayerDiagnostics(
    string InternalId,
    string? AuthorId,
    string PlaybackState,
    string BufferState,
    double? PresentationLagMs,
    double TargetBufferSizeMs,
    double PlayingAt,
    double BufferedDuration);
