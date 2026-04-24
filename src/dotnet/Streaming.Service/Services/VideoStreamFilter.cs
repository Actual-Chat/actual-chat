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
    VideoStreamFilter.EgressControl? egressControl = null,
    Func<StreamId, string, bool>? isPeerVisible = null)
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
        // is the layer this peer is currently receiving (-1 = not joined yet); once
        // joined, only external events (cap change, staleness decay, egress stall)
        // change it — burst-internal observedMax growth is NOT a switch trigger.
        // `selectedForCap` is the desiredLayer value at the time of last commit, used
        // to detect when desired has actually moved since our last selection.
        // `observedMaxSpatial` tracks the highest layer the producer is actually
        // emitting (may be < configured max when sender has spun down upper encoders
        // under VAD / feedback). Staleness decay: if no keyframe on observedMaxSpatial
        // arrives within `spatialStalenessWindow`, sender has spun down that layer
        // and we demote to whatever keyframe we're currently seeing. 2× the 1s
        // forced-keyframe cadence.
        // `joinPendingKF` + `joinStabilizationWindow` buffer the initial-join keyframe
        // burst: simulcast senders emit N keyframes at the same offset (one per
        // layer, ~6 ms apart). We hold the highest one seen and yield it once, so
        // the receiver's decoder configures with one description, not N.
        var selectedSpatialLayer = -1;
        var selectedForCap = -1;
        var observedMaxSpatial = 0;
        var observedMaxSpatialSeenAt = CpuTimestamp.Now;
        var observedMaxLastChange = CpuTimestamp.Now;
        var spatialStalenessWindow = TimeSpan.FromSeconds(2);
        var joinStabilizationWindow = TimeSpan.FromMilliseconds(50);
        VideoFrame? joinPendingKF = null;
        var joinPendingSince = CpuTimestamp.Now;

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
        // Rate-limit stall response: one skip-to-keyframe per stall transition.
        // Re-armed once the consumer successfully pulls within EgressStallThreshold.
        var stallHandled = false;
        try {
            await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                // 0. Egress back-pressure: consumer didn't pull for >
                // EgressStallThreshold. Response depends on the cause:
                //
                //  - Tab hidden / backgrounded (client reports isVisible=false):
                //    Fully expected — throttled rAF, paused decoder. Suppress
                //    skip-ahead AND cap decrement. When the tab returns visible
                //    the client re-requests via GetVideo with a fresh skipTo,
                //    so this loop will just idle until then.
                //  - Tab visible but pulling slow: consumer-side (decoder,
                //    paint throttle, GC). Skip to next keyframe so the consumer
                //    catches up at the live edge. Do NOT decrement spatial cap
                //    — sustained network slowness is caught by IsNetworkSlow on
                //    the latency path, not here.
                if (egressControl != null && yieldedCount > 0
                    && lastYieldAt.Elapsed > Constants.Video.EgressStallThreshold) {
                    if (!stallHandled) {
                        var visible = isPeerVisible?.Invoke(streamId, peerId) ?? true;
                        if (!visible) {
                            log.LogDebug(
                                "VideoStreamFilter: egress stall ({ElapsedMs:F0}ms) for peer={PeerId} — hidden, suppressing",
                                lastYieldAt.Elapsed.TotalMilliseconds, peerId);
                        }
                        else {
                            log.LogInformation(
                                "VideoStreamFilter: egress stall ({ElapsedMs:F0}ms) for peer={PeerId}, skipping to next keyframe",
                                lastYieldAt.Elapsed.TotalMilliseconds, peerId);
                            skipping = true;
                        }
                        stallHandled = true;
                    }
                }
                else {
                    // Consumer pulling in time — arm the next stall response.
                    stallHandled = false;
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

                // 2. Spatial observation.
                // Update observedMaxSpatial and observedMaxLastChange. Observation alone
                // does NOT drive layer switches — see the desiredChanged gate below.
                if (frame.IsKeyFrame) {
                    if (frame.SpatialLayerId > observedMaxSpatial) {
                        observedMaxSpatial = frame.SpatialLayerId;
                        observedMaxSpatialSeenAt = CpuTimestamp.Now;
                        observedMaxLastChange = CpuTimestamp.Now;
                    }
                    else if (frame.SpatialLayerId == observedMaxSpatial) {
                        observedMaxSpatialSeenAt = CpuTimestamp.Now;
                    }
                    else if (observedMaxSpatialSeenAt.Elapsed > spatialStalenessWindow) {
                        // Top layer stopped producing — decay to whatever's arriving now.
                        observedMaxSpatial = frame.SpatialLayerId;
                        observedMaxSpatialSeenAt = CpuTimestamp.Now;
                        observedMaxLastChange = CpuTimestamp.Now;
                    }
                }

                // 3. Desired layer = what we want to deliver, clamped to what producer
                // actually emits. Switch only when this differs from `selectedForCap`
                // (the value we committed to last) — within a burst, observedMax grows
                // but we commit only once, to the final value after stabilization.
                var desiredLayer = Math.Min(maxSpatialLayer, observedMaxSpatial);
                var desiredChanged = desiredLayer != selectedForCap;

                // 3a. Pending-join handling — absorb the initial keyframe burst.
                if (joinPendingKF != null) {
                    // Upgrade pending if a higher-layer KF arrives in the same burst —
                    // only when the peer's cap is explicitly set. Without a cap we stay
                    // at the lowest layer (which simulcast emits first) to avoid
                    // auto-climbing into an N-KF-per-cycle delivery pattern.
                    if (frame.IsKeyFrame
                        && frame.SpatialLayerId > joinPendingKF.SpatialLayerId
                        && maxSpatialLayer != int.MaxValue
                        && frame.SpatialLayerId <= maxSpatialLayer) {
                        joinPendingKF = frame;
                    }
                    var reachedCap = maxSpatialLayer != int.MaxValue
                        && joinPendingKF.SpatialLayerId >= maxSpatialLayer;
                    // Non-KF frame signals burst end: simulcast emits all layers'
                    // KFs back-to-back, then deltas. First delta = burst over.
                    var burstEnded = !frame.IsKeyFrame;
                    var stabilized = joinPendingSince.Elapsed >= joinStabilizationWindow;
                    if (reachedCap || burstEnded || stabilized) {
                        selectedSpatialLayer = joinPendingKF.SpatialLayerId;
                        selectedForCap = joinPendingKF.SpatialLayerId;
                        skipping = false;
                        lastKeyFrameNumber = joinPendingKF.KeyFrameNumber;
                        skippedCount = 0;
                        joined = true;
                        yieldedCount++;
                        var committed = joinPendingKF;
                        joinPendingKF = null;
                        log.LogInformation(
                            "VideoStreamFilter: peer={PeerId} joined at spatial={Layer}, KF#{KeyFrameNumber} (cap={Cap}, observedMax={ObservedMax})",
                            peerId, selectedSpatialLayer, committed.KeyFrameNumber,
                            maxSpatialLayer == int.MaxValue ? "∞" : maxSpatialLayer.ToString(),
                            observedMaxSpatial);
                        yield return committed;
                        lastYieldAt = CpuTimestamp.Now;
                        // If the current frame IS the committed KF, we're done with it.
                        // Otherwise fall through — it may be a delta on the selected layer
                        // worth yielding via the KF-gap filter below.
                        if (ReferenceEquals(frame, committed))
                            continue;
                    }
                    else {
                        continue; // still buffering
                    }
                }

                // 3b. Post-join runtime switch — only when desired has actually changed
                // (peer cap updated, or producer top decayed). Demotions always allowed;
                // promotions require an explicit cap signal so we don't auto-climb on
                // observedMax growth.
                if (selectedSpatialLayer >= 0
                    && frame.IsKeyFrame
                    && frame.SpatialLayerId == desiredLayer
                    && desiredChanged) {
                    var isPromotion = desiredLayer > selectedSpatialLayer;
                    var canSwitch = !isPromotion || maxSpatialLayer != int.MaxValue;
                    if (canSwitch) {
                        log.LogInformation(
                            "VideoStreamFilter: peer={PeerId} spatial switch {Old}->{New} at KF#{KF} (cap={Cap}, observedMax={Obs})",
                            peerId, selectedSpatialLayer, desiredLayer, frame.KeyFrameNumber,
                            maxSpatialLayer == int.MaxValue ? "∞" : maxSpatialLayer.ToString(),
                            observedMaxSpatial);
                        selectedSpatialLayer = desiredLayer;
                        selectedForCap = desiredLayer;
                        skipping = false;
                        lastKeyFrameNumber = frame.KeyFrameNumber;
                        skippedCount = 0;
                        yieldedCount++;
                        yield return frame;
                        lastYieldAt = CpuTimestamp.Now;
                        continue;
                    }
                }

                // 3c. Not joined, no pending yet — start buffering on the first KF.
                if (selectedSpatialLayer < 0 && joinPendingKF == null) {
                    if (frame.IsKeyFrame) {
                        joinPendingKF = frame;
                        joinPendingSince = CpuTimestamp.Now;
                    }
                    continue; // drop everything until we commit a selection
                }

                // 3d. Drop frames on layers we're not forwarding.
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
