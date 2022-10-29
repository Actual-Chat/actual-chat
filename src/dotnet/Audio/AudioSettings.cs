namespace ActualChat.Audio;

public class AudioSettings
{
    public string Redis { get; set; } = "";
    public string ServiceName { get; set; } = "actual-chat-app-service";
    public string Namespace { get; set; } = "default";
    public TimeSpan IdleRecordingTimeout { get; set; } = TimeSpan.FromMinutes(2);
}
