namespace ActualChat.MediaPlayback;

public record TrackInfo(Symbol TrackId)
{
    // StartedAt is typically a historical time;
    public Moment RecordedAt { get; init; }
}
