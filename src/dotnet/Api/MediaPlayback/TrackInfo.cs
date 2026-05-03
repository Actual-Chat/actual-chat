namespace ActualChat.MediaPlayback;

/// <summary>
/// Metadata about a media track being played.
/// </summary>
public record TrackInfo(Symbol TrackId, bool IsStreaming)
{
    public Moment RecordedAt { get; init; }
    public Moment SourceRecordedAt { get; init; }
    public double Speed { get; init; } = 1.0;
    public TimeSpan TargetBufferSize { get; init; }
}
