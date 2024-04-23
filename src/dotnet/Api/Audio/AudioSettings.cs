namespace ActualChat.Audio;

public sealed class AudioSettings
{
    public TimeSpan IdleRecordingCheckPeriod { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan IdleRecordingPreCountdownTimeout { get; init; }
        = Constants.Audio.RecordingDuration - TimeSpan.FromSeconds(10); // 10s to count
    public TimeSpan IdleListeningCheckPeriod { get; init; } = TimeSpan.FromSeconds(3); // Not critical to stop it at the exact time
    public TimeSpan IdleListeningNewMessageTrigger { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan RecordingBeepInterval { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan RecordingAggressiveBeepInterval { get; init; } = TimeSpan.FromSeconds(10);
}
