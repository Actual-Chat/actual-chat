using ActualChat.Video;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Per-consumer video filter pipeline. Applies skipTo, temporal layer filtering,
/// pause-aware filtering, and keyframe gap recovery.
/// Extracted from VideoStreamingBackend for reuse in StreamHub (remote stream caching).
/// </summary>
internal class VideoStreamFilter(
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
            while (!refreshToken.IsCancellationRequested) {
                var computed = await capturePreset(streamId, refreshToken).ConfigureAwait(false);
                preset = computed.Value;
                await computed.WhenInvalidated(refreshToken).ConfigureAwait(false);
            }
        }, refreshToken);

        // --- Per-peer temporal layer: cached locally, refreshed once per second ---
        var maxTemporalLayer = getMaxTemporalLayer(streamId, peerId);
        var maxTemporalLayerUpdatedAt = CpuTimestamp.Now;

        // --- SkipTo filter state: skip frames until we reach a keyframe at or past skipTo ---
        var skipToActive = skipTo > TimeSpan.Zero;
        var skipToFramesSkipped = 0;

        // --- KeyFrame gap filter state ---
        var lastKeyFrameNumber = -1L;
        var skipping = true; // Start in skip mode — wait for first keyframe
        var skippedCount = 0;

        try {
            await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                // 0. SkipTo filter — drop all frames before the requested offset (must land on keyframe)
                if (skipToActive) {
                    if (frame.Offset < skipTo || !frame.IsKeyFrame) {
                        skipToFramesSkipped++;
                        continue;
                    }
                    // Found a keyframe at or past skipTo — deactivate skipTo filter
                    if (skipToFramesSkipped > 0)
                        log.LogInformation(
                            "VideoStreamFilter: SkipTo={SkipTo} — skipped {Skipped} frames, resuming at offset {Offset}",
                            skipTo, skipToFramesSkipped, frame.Offset);
                    skipToActive = false;
                }

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
                    if (skipping && skippedCount > 0)
                        log.LogInformation(
                            "VideoStreamFilter: found keyframe (KF#{KeyFrameNumber}) after skipping {Skipped} frames",
                            frame.KeyFrameNumber, skippedCount);
                    lastKeyFrameNumber = frame.KeyFrameNumber;
                    skipping = false;
                    skippedCount = 0;
                    yield return frame;
                }
                else if (!skipping && frame.KeyFrameNumber == lastKeyFrameNumber)
                    yield return frame;
                else {
                    if (!skipping) {
                        skipping = true;
                        log.LogInformation(
                            "VideoStreamFilter: gap detected — expected KF#{Expected}, got KF#{Actual}, skipping to next keyframe",
                            lastKeyFrameNumber, frame.KeyFrameNumber);
                    }
                    skippedCount++;
                }
            }
        }
        finally {
            refreshCts.CancelAndDisposeSilently();
            await refreshTask.SuppressCancellationAwait(false);
        }
    }
}
