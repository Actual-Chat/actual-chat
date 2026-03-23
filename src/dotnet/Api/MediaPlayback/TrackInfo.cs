namespace ActualChat.MediaPlayback;

/// <summary>
/// Metadata about a media track being played.
/// </summary>
public record TrackInfo(Symbol TrackId, bool IsStreaming)
{
    public Moment RecordedAt { get; init; }
    public Moment ClientSideRecordedAt { get; init; }
    public double Speed { get; init; } = 1.0;
}
