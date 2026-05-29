using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Per-author audio catch-up signal source. Returns the desired catch-up
/// duration for the given author. Positive = audio playback should advance by
/// that much (drop frames or hard-skip a chunk); ≤ 0 = no correction needed.
/// </summary>
/// <remarks>
/// The signal originates from the side that owns the presentation timeline
/// (currently planned to be the video pipeline). The audio listener path
/// re-samples this every ~200 ms inside the catch-up transform; the policy
/// is expected to be cheap to call.
/// </remarks>
public interface IAudioCatchUpPolicy
{
    Task<AudioCatchUpDecision> GetDesiredCatchUp(AuthorId authorId, CancellationToken cancellationToken);
}

[StructLayout(LayoutKind.Auto)]
public readonly record struct AudioCatchUpDecision(TimeSpan Desired, AudioSyncSkipReason Reason);

/// <summary>
/// Aligns audio playback to video presentation by reading per-author lag
/// samples published by the JS audio and video playback paths. Returns
/// max(0, audioLag - videoLag) when both signals are present; returns Zero
/// when video is paused, hidden, or the chat is audio-only — in which case
/// the audio buffer keeps playing at its natural rate.
/// </summary>
public sealed class LiveAudioCatchUpPolicy(IServiceProvider services) : IAudioCatchUpPolicy
{
    private static readonly bool IsDiagnosticLogEnabled = false;
    private const long DiagnosticLogPeriodMs = 2_000;

    private readonly ConcurrentDictionary<AuthorId, long> _lastDiagnosticLogAtMs = new();

    private IServiceProvider Services { get; } = services;
    private PlaybackLagTracker PlaybackLagTracker => field ??= Services.GetRequiredService<PlaybackLagTracker>();
    private ILogger Log => field ??= Services.LogFor<LiveAudioCatchUpPolicy>();

    public Task<AudioCatchUpDecision> GetDesiredCatchUp(AuthorId authorId, CancellationToken cancellationToken)
    {
        var snapshot = PlaybackLagTracker.GetSnapshot(authorId);
        var video = snapshot.VideoLag;
        if (video is null) {
            LogMissingSignal(authorId, "video", snapshot);
            return Task.FromResult(new AudioCatchUpDecision(TimeSpan.Zero, AudioSyncSkipReason.MissingVideoLag));
        }

        var audio = snapshot.AudioLag;
        if (audio is null) {
            LogMissingSignal(authorId, "audio", snapshot);
            return Task.FromResult(new AudioCatchUpDecision(TimeSpan.Zero, AudioSyncSkipReason.MissingAudioLag));
        }

        // Audio-master under degradation: while video is actively skipping it
        // moves to the audio timeline (skip-to-audio), so suppress audio
        // catch-up rather than chasing a transiently-jumpy video target.
        const double videoSkippingThreshold = 0.2;
        if (snapshot.VideoInputs.SkipRatio > videoSkippingThreshold)
            return Task.FromResult(new AudioCatchUpDecision(TimeSpan.Zero, AudioSyncSkipReason.VideoSkipping));

        var desired = audio.Value - video.Value;
        var baseline = Constants.Audio.AudioCatchUpBaselineDelta;
        var syncError = desired - baseline;
        var insideDeadband = syncError < Constants.Audio.AudioCatchUpDeadband;
        var desiredCatchUp = insideDeadband ? TimeSpan.Zero : syncError;
        var reason = insideDeadband ? AudioSyncSkipReason.InsideDeadband : AudioSyncSkipReason.None;
        if (IsDiagnosticLogEnabled)
            Log.LogWarning(
                "Audio/video latency delta for {AuthorId}: "
                + "audio = {AudioLagMs:F0}ms, video = {VideoLagMs:F0}ms, delta = {DeltaMs:F0}ms, "
                + "baseline = {BaselineDeltaMs}ms, sync error = {SyncErrorMs:F0}ms, "
                + "desired catch-up = {DesiredCatchUpMs:F0}ms, deadband = {DeadbandMs:F0}ms",
                authorId,
                audio.Value.TotalMilliseconds,
                video.Value.TotalMilliseconds,
                desired.TotalMilliseconds,
                baseline.TotalMilliseconds,
                syncError.TotalMilliseconds,
                desiredCatchUp.TotalMilliseconds,
                Constants.Audio.AudioCatchUpDeadband.TotalMilliseconds);
        return Task.FromResult(new AudioCatchUpDecision(desiredCatchUp, reason));
    }

    private void LogMissingSignal(AuthorId authorId, string missingSignal, PlaybackLagSnapshot snapshot)
    {
        if (!IsDiagnosticLogEnabled)
            return;
        if (!ShouldLogDiagnostic(authorId))
            return;

        Log.LogWarning(
            "Audio/video latency delta skipped for {AuthorId}: missing fresh {MissingSignal} lag. "
            + "audioLag = {AudioLagMs}ms, videoLag = {VideoLagMs}ms, "
            + "freshAudioStreams = {FreshAudioStreamCount}, staleAudioStreams = {StaleAudioStreamCount}, "
            + "freshVideoStreams = {FreshVideoStreamCount}, staleVideoStreams = {StaleVideoStreamCount}, "
            + "freshestAudioAge = {FreshestAudioAgeMs}ms, freshestVideoAge = {FreshestVideoAgeMs}ms, staleAfter = {StaleAfterMs}ms",
            authorId,
            missingSignal,
            ToMilliseconds(snapshot.AudioLag),
            ToMilliseconds(snapshot.VideoLag),
            snapshot.FreshAudioStreamCount,
            snapshot.StaleAudioStreamCount,
            snapshot.FreshVideoStreamCount,
            snapshot.StaleVideoStreamCount,
            ToMilliseconds(snapshot.FreshestAudioAge),
            ToMilliseconds(snapshot.FreshestVideoAge),
            Constants.Audio.PlaybackLagStaleAfter.TotalMilliseconds);
    }

    private bool ShouldLogDiagnostic(AuthorId authorId)
    {
        var nowMs = Environment.TickCount64;
        var lastMs = _lastDiagnosticLogAtMs.GetOrAdd(authorId, 0);
        if (nowMs - lastMs < DiagnosticLogPeriodMs)
            return false;

        _lastDiagnosticLogAtMs[authorId] = nowMs;
        return true;
    }

    private static double? ToMilliseconds(TimeSpan? value)
        => value?.TotalMilliseconds;
}
