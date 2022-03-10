namespace ActualChat.MediaPlayback;

public record TrackInfo(Symbol TrackId)
{
    public Moment RecordedAt { get; init; }
    public Moment ClientSideRecordedAt { get; init; }

    public bool IsRealtime { get; init; }
}
