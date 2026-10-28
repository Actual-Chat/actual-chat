namespace ActualChat.Streaming.Module;

public class StreamingSettings
{
    // How long a peer may stay disconnected before the participations it claimed are released:
    // long enough for a reconnecting blip, short next to the 90s participant staleness.
    public TimeSpan ParticipationDisconnectGrace { get; set; } = TimeSpan.FromSeconds(10);
    // Soniox clones the pool may hold at once; null means Constants.Audio.VoiceCloneQuota
    public int? SonioxVoiceQuota { get; set; }
}
