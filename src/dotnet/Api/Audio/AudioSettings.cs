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
    public TimeSpan StreamExpirationDelay { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// When true, ChatListener/ChatReplayer delegate audio playback to the
    /// TS-side pull path (blazorApp.LiveAudioPullBridge) — bypassing the .NET
    /// AudioTrackPlayer frame pump and its Blazor-interop hot path.
    /// Default false: existing .NET-pull behavior is preserved. TS-pull mode
    /// currently skips audio focus / notification sounds / CanContinuePlayback
    /// / sleep-drift handling; enable only for perf validation until those
    /// policies are ported to TS.
    /// </summary>
    public bool UseTsAudioPull { get; init; } = true;
}
