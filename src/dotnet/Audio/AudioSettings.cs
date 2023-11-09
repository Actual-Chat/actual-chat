namespace ActualChat.Audio;

public sealed class AudioSettings
{
    public string Redis { get; set; } = "";
    public string ServiceName { get; set; } = "actual-chat-app-service";
    public string Namespace { get; set; } = "default";

    public TimeSpan IdleRecordingCheckPeriod { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan IdleRecordingTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan IdleRecordingPreCountdownTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public TimeSpan IdleListeningCheckPeriod { get; set; } = TimeSpan.FromSeconds(3); // Not critical to stop it at the exact time
    public TimeSpan IdleListeningTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan IdleListeningPreCountdownTimeout
        // There is no actual countdown, all we need here is to make sure it starts to make the next check fail
        => IdleListeningTimeout - IdleListeningCheckPeriod + TimeSpan.FromSeconds(1);
    public TimeSpan RecordingBeepInterval { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan RecordingAggressiveBeepInterval { get; set; } = TimeSpan.FromSeconds(10);
}
