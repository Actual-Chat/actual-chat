namespace ActualChat.Playback;

public record PlaybackState
{
    public ChatId ChatId { get; init; }
    public bool IsOn => Realtime.IsOn || Replay.IsOn;
    public RealtimePlaybackState Realtime { get; init; } = null!;
    public ReplayState Replay { get; init; } = null!;
}

public record RealtimePlaybackState
{
    public bool IsOn { get; init; }
}

public record ReplayState
{
    public bool IsOn { get; init; }
    public Moment StartedAt { get; init; }
    public Moment StartedAtCpuTime { get; init; }
    public double Speed { get; init; } = 1;
}
