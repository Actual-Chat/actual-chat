namespace ActualChat.Audio;

public sealed class AudioSettings
{
    public TimeSpan IdleRecordingCheckPeriod { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan IdleRecordingPreCountdownTimeout { get; init; }
        = Constants.Audio.RecordingDuration - TimeSpan.FromSeconds(10); // 10s to count
    public TimeSpan IdleListeningNewMessageTrigger { get; init; } = TimeSpan.FromMinutes(5);
    // A transcript grows a few times per second; this keeps GetTranscriptSnapshot's readers off that rate.
    public TimeSpan TranscriptSnapshotInvalidationDelay { get; init; } = TimeSpan.FromSeconds(0.1);
    // Nothing invalidates "this stream isn't published yet", so that answer re-checks on its own
    public TimeSpan TranscriptSnapshotRetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan RecordingBeepInterval { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan RecordingStopWarningLeadTime { get; init; } = TimeSpan.FromSeconds(5);
    // The trailing window ConversationStats measures, and the period it re-measures on. The period
    // is also what paces every consumer: the stats are a polled snapshot, not a reactive value.
    public TimeSpan ConversationWindow { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan ConversationStatsPeriod { get; init; } = TimeSpan.FromSeconds(10);
    public int MaxConversationEntries { get; init; } = 20;
    // A session this young hasn't produced enough to judge, so it counts as a conversation.
    public TimeSpan ConversationMinAge { get; init; } = TimeSpan.FromSeconds(30);
    // What it takes to call it a real conversation rather than a stray VAD trigger. Where
    // transcription is on, characters replace speech duration - they can tell words from noise.
    public TimeSpan SpeechDurationThreshold { get; init; } = TimeSpan.FromSeconds(10);
    public int TranscriptSizeThreshold { get; init; } = 80;
    // Must outlive entry finalization, which runs after the stream ends:
    // blob save + refine retranscription (RetranscriptionTimeout) + invalidation propagation
    public TimeSpan StreamExpirationDelay { get; init; } = TimeSpan.FromSeconds(60);
}
