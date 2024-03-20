namespace ActualChat.Audio;

public sealed class AudioSettings
{
    public TimeSpan IdleRecordingCheckPeriod { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan IdleRecordingTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan IdleRecordingPreCountdownTimeout { get; init; } = TimeSpan.FromSeconds(20); // 10s to count
    public TimeSpan IdleListeningCheckPeriod { get; init; } = TimeSpan.FromSeconds(3); // Not critical to stop it at the exact time
    public TimeSpan IdleListeningTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan IdleListeningNewMessageTrigger { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan IdleListeningPreCountdownTimeout
        // There is no actual countdown, all we need here is to make sure it starts to make the next check fail
        => IdleListeningTimeout - IdleListeningCheckPeriod + TimeSpan.FromSeconds(1);
    public TimeSpan RecordingBeepInterval { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan RecordingAggressiveBeepInterval { get; init; } = TimeSpan.FromSeconds(10);
}
