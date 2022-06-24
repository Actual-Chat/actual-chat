namespace ActualChat.Audio;

public class AudioSettings
{
    public string Redis { get; set; } = "";
    public TimeSpan IdleRecordingTimeout { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan EndedStreamTtl { get; set; } = TimeSpan.FromMinutes(1);
}
