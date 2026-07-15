namespace ActualChat.Streaming;

public enum PlaybackQualityReason
{
    Stable = 0,
    Climb,
    Backoff,
    FloorReached,
    ActiveSetChanged,
    ReconnectPush,
    ColdStartTick,
    Stalled,
}
