namespace ActualChat.Audio;

/// <summary>
/// Configuration settings for audio recording and listening behaviors.
/// </summary>
public sealed class AudioSettings
{
    public TimeSpan IdleRecordingCheckPeriod { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan IdleRecordingPreCountdownTimeout { get; init; }
        = Constants.Audio.RecordingDuration - TimeSpan.FromSeconds(10); // 10s to count
    public TimeSpan IdleListeningCheckPeriod { get; init; } = TimeSpan.FromSeconds(3); // Not critical to stop it at the exact time
    public TimeSpan IdleListeningNewMessageTrigger { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan RecordingBeepInterval { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan RecordingAggressiveBeepInterval { get; init; } = TimeSpan.FromSeconds(10);
    // Streams must outlive entry finalization, which runs after the stream ends:
    // audio blob save + refine retranscription (Constants.Transcription.RetranscriptionTimeout)
    // + invalidation propagation to clients.
    public TimeSpan StreamExpirationDelay { get; init; } = TimeSpan.FromSeconds(60);
}
