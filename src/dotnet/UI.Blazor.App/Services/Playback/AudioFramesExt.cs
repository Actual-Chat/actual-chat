using ActualChat.Audio;

namespace ActualChat.UI.Blazor.App.Services;

internal static class AudioFramesExt
{
    // Re-sample the policy every N frames (10 × 20 ms ≈ 200 ms).
    private const int PolicySamplePeriodFrames = 10;

    /// <summary>
    /// Applies an <see cref="IAudioCatchUpPolicy"/> to an audio frame stream.
    /// The policy is sampled every ~200 ms; the transform either passes
    /// frames through, drops every <see cref="Constants.Audio.PlaybackSpeedUpDropEveryNFrames"/>-th
    /// frame (small correction), or hard-skips a chunk (correction
    /// ≥ <see cref="Constants.Audio.PlaybackHardSkipThreshold"/>).
    /// </summary>
    public static async IAsyncEnumerable<AudioFrame> ApplyCatchUp(
        this IAsyncEnumerable<AudioFrame> source,
        AuthorId authorId,
        IAudioCatchUpPolicy policy,
        ILogger? log,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var hardSkipThreshold = Constants.Audio.PlaybackHardSkipThreshold;
        var maxSpeedUpDuration = Constants.Audio.PlaybackMaxSpeedUpDuration;
        var dropEveryN = Constants.Audio.PlaybackSpeedUpDropEveryNFrames;

        var mode = CatchUpMode.Idle;
        var skipUntilOffset = TimeSpan.Zero;
        var speedUpStartedAtOffset = TimeSpan.Zero;
        var speedUpFrameCounter = 0;
        var sampleIn = 0; // frames remaining until next policy sample

        await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            if (mode != CatchUpMode.HardSkip && sampleIn <= 0) {
                var desired = TimeSpan.Zero;
                try {
                    desired = (await policy.GetDesiredCatchUp(authorId, cancellationToken).ConfigureAwait(false)).Desired;
                }
                catch (OperationCanceledException) {
                    throw;
                }
                catch (Exception e) {
                    log?.LogWarning(e, "Audio catch-up policy failed for {AuthorId}", authorId);
                }

                if (desired >= hardSkipThreshold) {
                    if (mode != CatchUpMode.HardSkip)
                        log?.LogDebug("Audio catch-up: hard-skip {Desired} for {AuthorId}", desired, authorId);
                    mode = CatchUpMode.HardSkip;
                    skipUntilOffset = frame.Offset + desired;
                }
                else if (desired > TimeSpan.Zero) {
                    if (mode != CatchUpMode.SpeedUp) {
                        log?.LogDebug("Audio catch-up: speed-up {Desired} for {AuthorId}", desired, authorId);
                        speedUpStartedAtOffset = frame.Offset;
                        speedUpFrameCounter = 0;
                    }
                    mode = CatchUpMode.SpeedUp;
                }
                else {
                    if (mode != CatchUpMode.Idle)
                        log?.LogDebug("Audio catch-up: idle for {AuthorId}", authorId);
                    mode = CatchUpMode.Idle;
                }

                sampleIn = PolicySamplePeriodFrames;
            }
            if (mode != CatchUpMode.HardSkip)
                sampleIn--;

            switch (mode) {
            case CatchUpMode.HardSkip:
                if (frame.Offset < skipUntilOffset)
                    continue; // drop
                mode = CatchUpMode.Idle;
                sampleIn = 0;
                break;
            case CatchUpMode.SpeedUp:
                // Safety bound: don't sustain speed-up indefinitely. Re-sampling
                // will decide whether to continue, escalate, or stop.
                if (frame.Offset - speedUpStartedAtOffset > maxSpeedUpDuration) {
                    log?.LogDebug("Audio catch-up: speed-up window exhausted for {AuthorId}", authorId);
                    mode = CatchUpMode.Idle;
                    break;
                }
                speedUpFrameCounter++;
                if (speedUpFrameCounter % dropEveryN == 0)
                    continue; // drop every N-th frame
                break;
            case CatchUpMode.Idle:
                break;
            }

            yield return frame;
        }
    }

    private enum CatchUpMode { Idle, SpeedUp, HardSkip }
}
