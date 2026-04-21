using ActualChat.Video;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Per-consumer video filter pipeline. Applies fast-join (first keyframe from
/// retention), temporal layer filtering, pause-aware filtering, and keyframe
/// gap recovery.
/// </summary>
/// <remarks>
/// <para><b>skipTo is advisory.</b> The memoizer replays retention, which may contain
/// only frames older than skipTo — and may not advance past it at all if the
/// sender is idle (static screen). Blocking on <c>frame.Offset &gt;= skipTo</c>
/// would stall the consumer indefinitely in that case.</para>
/// <para><b>Join policy:</b> emit starting from the first keyframe seen. The
/// receiver's decoder handles the catch-up (small GOP → &lt;300ms of decode);
/// the client renders the latest decoded frame and drops stale ones.</para>
/// </remarks>
public class VideoStreamFilter(
    Func<StreamId, string, int> getMaxTemporalLayer,
    Func<StreamId, CancellationToken, ValueTask<Computed<VideoQualityPreset>>> capturePreset,
    ILogger log)
{
    public async IAsyncEnumerable<VideoFrame> Apply(
        StreamId streamId,
        string peerId,
        TimeSpan skipTo,
        IAsyncEnumerable<VideoFrame> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // --- Quality preset: background task refreshes on invalidation ---
        var preset = VideoQualityPreset.High;
        var refreshCts = cancellationToken.CreateLinkedTokenSource();
        var refreshToken = refreshCts.Token;
        var refreshTask = BackgroundTask.Run(async () => {
            try {
                while (!refreshToken.IsCancellationRequested) {
                    var computed = await capturePreset(streamId, refreshToken).ConfigureAwait(false);
                    preset = computed.Value;
                    await computed.WhenInvalidated(refreshToken).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                log.LogWarning(e, "VideoStreamFilter: preset refresh loop failed for #{StreamId}", streamId);
            }
        }, refreshToken);

        // --- Per-peer temporal layer: cached locally, refreshed once per second ---
        var maxTemporalLayer = getMaxTemporalLayer(streamId, peerId);
        var maxTemporalLayerUpdatedAt = CpuTimestamp.Now;

        // --- KeyFrame gap filter state ---
        // Start in skip mode: wait for the first keyframe before yielding anything,
        // even if the memoizer replays older P-frames first.
        var lastKeyFrameNumber = -1L;
        var skipping = true;
        var skippedCount = 0;
        var joined = false;

        var yieldedCount = 0;
        try {
            await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                // 1. Temporal layer filter — drop enhancement layers for slow peers
                if (maxTemporalLayerUpdatedAt.Elapsed.TotalSeconds >= 1) {
                    maxTemporalLayer = getMaxTemporalLayer(streamId, peerId);
                    maxTemporalLayerUpdatedAt = CpuTimestamp.Now;
                }
                if (frame.TemporalLayerId > maxTemporalLayer)
                    continue;

                // 2. Pause filter — drop all frames when stream is paused by priority queue
                if (preset.Level == VideoQualityLevel.Paused)
                    continue;

                // 3. KeyFrame gap filter — ensure decoder-safe output
                if (frame.IsKeyFrame) {
                    if (!joined) {
                        log.LogDebug(
                            "VideoStreamFilter: joined from KF#{KeyFrameNumber} at offset {KfOffset} (skipTo={SkipTo})",
                            frame.KeyFrameNumber, frame.Offset, skipTo);
                        joined = true;
                    }
                    else if (skipping && skippedCount > 0) {
                        log.LogInformation(
                            "VideoStreamFilter: found keyframe (KF#{KeyFrameNumber}) after skipping {Skipped} frames",
                            frame.KeyFrameNumber, skippedCount);
                    }
                    lastKeyFrameNumber = frame.KeyFrameNumber;
                    skipping = false;
                    skippedCount = 0;
                    yieldedCount++;
                    yield return frame;
                }
                else if (!skipping && frame.KeyFrameNumber == lastKeyFrameNumber) {
                    yieldedCount++;
                    yield return frame;
                }
                else {
                    if (!skipping) {
                        skipping = true;
                        // Gap is routine under packet loss — debug level to avoid log spam
                        // at scale. The recovery log (above) stays informational.
                        log.LogDebug(
                            "VideoStreamFilter: gap detected — expected KF#{Expected}, got KF#{Actual}, skipping to next keyframe",
                            lastKeyFrameNumber, frame.KeyFrameNumber);
                    }
                    skippedCount++;
                }
            }

            if (!joined)
                log.LogWarning(
                    "VideoStreamFilter: source completed without any keyframe (skipTo={SkipTo})",
                    skipTo);
            else if (yieldedCount == 0)
                log.LogWarning(
                    "VideoStreamFilter: source completed, yielded 0 frames (all filtered by quality/temporal/gap)");
        }
        finally {
            refreshCts.CancelAndDisposeSilently();
            await refreshTask.SuppressCancellationAwait(false);
        }
    }
}
