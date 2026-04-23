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
    Func<StreamId, string, int> getMaxSpatialLayer,
    Func<StreamId, CancellationToken, ValueTask<Computed<VideoQualityPreset>>> capturePreset,
    ILogger log,
    VideoStreamFilter.EgressControl? egressControl = null)
{
    // Server-edge fast-reaction cap controls. Decrement is called when the filter
    // detects a stall on write or walks past retention without finding a keyframe
    // on the selected spatial layer. Restore is called after EgressRecoveryWindow
    // of uneventful delivery at the reduced layer. Null in test scenarios where
    // egress fallback is not exercised.
    public sealed record EgressControl(
        Action<StreamId, string> Decrement,
        Action<StreamId, string> Restore,
        Func<StreamId, string, bool> HasFallback,
        Func<StreamId, string, CpuTimestamp> GetSetAt);

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

        // --- Per-peer layer caps: cached locally, refreshed once per second ---
        var maxTemporalLayer = getMaxTemporalLayer(streamId, peerId);
        var maxSpatialLayer = getMaxSpatialLayer(streamId, peerId);
        var layerCapsUpdatedAt = CpuTimestamp.Now;

        // --- Spatial layer selection state ---
        // Simulcast fan-out delivers exactly one spatial layer to each peer — mixing
        // multiple layers into a single decoder corrupts output. `selectedSpatialLayer`
        // is the layer this peer is currently receiving; `observedMaxSpatial` tracks
        // the highest layer the producer is actually emitting (may be < configured
        // max when sender has spun down upper encoders under VAD / feedback).
        // Switching layers is done at keyframe boundaries only: the target layer must
        // produce a keyframe before we snap to it, otherwise the decoder has no anchor.
        // Staleness decay — if no keyframe on observedMaxSpatial arrives within this
        // window, sender has spun down that layer and we demote to whatever keyframe
        // we're currently seeing. 2× the 1s forced-keyframe cadence.
        var selectedSpatialLayer = 0;
        var observedMaxSpatial = 0;
        var observedMaxSpatialSeenAt = CpuTimestamp.Now;
        var spatialStalenessWindow = TimeSpan.FromSeconds(2);

        // --- KeyFrame gap filter state ---
        // Start in skip mode: wait for the first keyframe on the selected spatial
        // layer before yielding anything, even if the memoizer replays older P-frames
        // first (or keyframes on other spatial layers).
        var lastKeyFrameNumber = -1L;
        var skipping = true;
        var skippedCount = 0;
        var joined = false;

        var yieldedCount = 0;
        // Egress back-pressure measurement: time between yielding a frame and the
        // consumer pulling the next one. If this exceeds EgressStallThreshold, the
        // consumer can't keep up at the selected layer → drop one layer via the
        // egress control.
        var lastYieldAt = CpuTimestamp.Now;
        try {
            await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                // 0. Egress back-pressure: how long did the consumer take to pull us?
                if (egressControl != null && yieldedCount > 0
                    && lastYieldAt.Elapsed > Constants.Video.EgressStallThreshold) {
                    log.LogInformation(
                        "VideoStreamFilter: egress stall ({ElapsedMs:F0}ms) for peer={PeerId}, decrementing",
                        lastYieldAt.Elapsed.TotalMilliseconds, peerId);
                    egressControl.Decrement(streamId, peerId);
                }

                // 0b. Egress recovery: if we've been at reduced layer for > EgressRecoveryWindow
                // with no new stall/gap bumping the timer, restore the cap.
                if (egressControl != null && egressControl.HasFallback(streamId, peerId)) {
                    var setAt = egressControl.GetSetAt(streamId, peerId);
                    if (setAt.Elapsed > Constants.Video.EgressRecoveryWindow) {
                        log.LogInformation(
                            "VideoStreamFilter: egress recovery — restoring cap for peer={PeerId}",
                            peerId);
                        egressControl.Restore(streamId, peerId);
                    }
                }

                // 1. Layer-cap refresh — re-read per-peer caps once per second
                if (layerCapsUpdatedAt.Elapsed.TotalSeconds >= 1) {
                    maxTemporalLayer = getMaxTemporalLayer(streamId, peerId);
                    maxSpatialLayer = getMaxSpatialLayer(streamId, peerId);
                    layerCapsUpdatedAt = CpuTimestamp.Now;
                }

                // 2. Spatial layer tracking + selection
                // Track the highest spatial layer the producer is emitting (via
                // keyframes — enhancement deltas may be present without a matching
                // keyframe). Switch `selectedSpatialLayer` only on a keyframe whose
                // SpatialLayerId equals the target (min of cap and observed).
                if (frame.IsKeyFrame) {
                    if (frame.SpatialLayerId >= observedMaxSpatial) {
                        observedMaxSpatial = frame.SpatialLayerId;
                        observedMaxSpatialSeenAt = CpuTimestamp.Now;
                    }
                    else if (observedMaxSpatialSeenAt.Elapsed > spatialStalenessWindow) {
                        // Top layer stopped producing — decay to whatever's arriving now.
                        observedMaxSpatial = frame.SpatialLayerId;
                        observedMaxSpatialSeenAt = CpuTimestamp.Now;
                    }
                }
                var targetSpatialLayer = Math.Min(maxSpatialLayer, observedMaxSpatial);
                if (frame.IsKeyFrame
                    && frame.SpatialLayerId == targetSpatialLayer
                    && selectedSpatialLayer != targetSpatialLayer) {
                    log.LogDebug(
                        "VideoStreamFilter: spatial switch {OldLayer} -> {NewLayer} at KF#{KeyFrameNumber}",
                        selectedSpatialLayer, targetSpatialLayer, frame.KeyFrameNumber);
                    selectedSpatialLayer = targetSpatialLayer;
                    // Force re-anchor on the new layer's keyframe — flush any prior
                    // GOP state from the old layer so the downstream decoder gets a
                    // clean start.
                    skipping = false;
                    lastKeyFrameNumber = frame.KeyFrameNumber;
                    skippedCount = 0;
                    if (!joined)
                        joined = true;
                    yieldedCount++;
                    yield return frame;
                    lastYieldAt = CpuTimestamp.Now;
                    continue;
                }
                if (frame.SpatialLayerId != selectedSpatialLayer)
                    continue;

                // 3. Temporal layer filter — drop enhancement layers for slow peers
                if (frame.TemporalLayerId > maxTemporalLayer)
                    continue;

                // 4. Pause filter — drop all frames when stream is paused by priority queue
                if (preset.Level == VideoQualityLevel.Paused)
                    continue;

                // 5. KeyFrame gap filter — ensure decoder-safe output
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
                    lastYieldAt = CpuTimestamp.Now;
                }
                else if (!skipping && frame.KeyFrameNumber == lastKeyFrameNumber) {
                    yieldedCount++;
                    yield return frame;
                    lastYieldAt = CpuTimestamp.Now;
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
                    if (egressControl != null && skippedCount == Constants.Video.EgressGapFrameThreshold
                        && selectedSpatialLayer > 0) {
                        log.LogInformation(
                            "VideoStreamFilter: egress gap exhausted ({Skipped} frames) for peer={PeerId}, decrementing",
                            skippedCount, peerId);
                        egressControl.Decrement(streamId, peerId);
                    }
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
