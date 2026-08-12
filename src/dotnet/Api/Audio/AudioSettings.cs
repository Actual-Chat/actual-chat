namespace ActualChat.Audio;

/// <summary>
/// Configuration settings for audio recording and listening behaviors.
/// </summary>
public sealed class AudioSettings
{
    public TimeSpan IdleRecordingCheckPeriod { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan IdleRecordingPreCountdownTimeout { get; init; }
        = Constants.Audio.RecordingDuration - TimeSpan.FromSeconds(10); // 10s to count
    // Not critical to stop it at the exact time
    public TimeSpan IdleListeningCheckPeriod { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan IdleListeningNewMessageTrigger { get; init; } = TimeSpan.FromMinutes(5);
    // A transcript grows a few times per second; this keeps GetMergedTranscript's readers off that rate.
    public TimeSpan MergedTranscriptInvalidationDelay { get; init; } = TimeSpan.FromSeconds(0.1);
    // Nothing invalidates "this stream isn't published yet", so that answer re-checks on its own
    public TimeSpan MergedTranscriptRetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan RecordingBeepInterval { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan RecordingStopWarningLeadTime { get; init; } = TimeSpan.FromSeconds(5);
    // The reminder is for a mic you forgot about, so remote speech in this window suppresses it.
    // The threshold is what keeps a stray VAD trigger from passing as a conversation.
    public TimeSpan RemoteSpeechWindow { get; init; } = TimeSpan.FromMinutes(3);
    public TimeSpan RemoteSpeechThreshold { get; init; } = TimeSpan.FromSeconds(10);
    // Where transcription is on, transcribed characters replace speech duration as the signal -
    // it can tell words from noise that merely trips VAD. Roughly a sentence or two from others.
    public TimeSpan TranscribedTextWindow { get; init; } = TimeSpan.FromMinutes(3);
    public int TranscribedTextThreshold { get; init; } = 80;
    // Must outlive entry finalization, which runs after the stream ends:
    // blob save + refine retranscription (RetranscriptionTimeout) + invalidation propagation
    public TimeSpan StreamExpirationDelay { get; init; } = TimeSpan.FromSeconds(60);
}
