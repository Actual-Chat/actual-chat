namespace ActualChat.Audio;

public sealed class AudioSettings
{
    public string Redis { get; set; } = "";
    public string ServiceName { get; set; } = "actual-chat-app-service";
    public string Namespace { get; set; } = "default";
    public TimeSpan IdleRecordingTimeout { get; set; } = TimeSpan.FromMinutes(3);
    public TimeSpan IdleRecordingTimeoutBeforeCountdown { get; set; } = TimeSpan.FromMinutes(2.5);
    public TimeSpan IdleRecordingCheckInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan IdleListeningTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan IdleListeningCheckInterval { get; set; } = TimeSpan.FromSeconds(3); // not critical to stop at exact time for playback
    public TimeSpan RecordingBeepInterval { get; set; } = TimeSpan.FromMinutes(1);
}
