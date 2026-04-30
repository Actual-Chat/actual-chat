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
    StreamLatencyStore latencyStore,
    Func<StreamId, CancellationToken, ValueTask<Computed<VideoQualityPreset>>> capturePreset,
    ILogger log,
    TimeSpan? spatialStalenessWindow = null,
    TimeSpan? burstStabilizationWindow = null)
{
    // Defaults sized for production keyframe cadence: encoder forces a KF every
    // ~3 s (recording-service.ts:maxKeyFrameIntervalMs=3000). Staleness window must
    // exceed that with margin (2× cadence) so a normal burst can't be misread as
    // "top layer disappeared". Burst stabilization holds a would-be decay decision
    // long enough for the rest of a multi-layer KF burst to arrive — extras emit
    // within ms, base trails by ~1.1 s, so 1.5 s covers the full burst envelope.
    private static readonly TimeSpan DefaultSpatialStalenessWindow = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan DefaultBurstStabilizationWindow = TimeSpan.FromMilliseconds(1500);

    private TimeSpan SpatialStalenessWindow { get; } = spatialStalenessWindow ?? DefaultSpatialStalenessWindow;
    private TimeSpan BurstStabilizationWindow { get; } = burstStabilizationWindow ?? DefaultBurstStabilizationWindow;

    // TEMP: simulcast disabled in dev. When true, the spatial-layer selection /
    // burst-stabilization / decay / egress-decrement logic below is bypassed
    // entirely — frames pass through with only KF-gap + pause + temporal
    // filtering. Flip to false to restore SFU-style spatial fan-out.
    private const bool TempBypassSimulcastLogic = false;

    public async IAsyncEnumerable<VideoFrame> Apply(
        StreamId streamId,
        string peerId,
        TimeSpan skipTo,
        IAsyncEnumerable<VideoFrame> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
#pragma warning disable CS0162 // Unreachable code — TempBypassSimulcastLogic gate
        if (TempBypassSimulcastLogic) {
            await foreach (var f in ApplySimple(streamId, peerId, source, cancellationToken).ConfigureAwait(false))
                yield return f;
            yield break;
        }

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
        var maxTemporalLayer = latencyStore.GetPeerMaxTemporalLayer(streamId, peerId);
        var maxSpatialLayer = latencyStore.GetPeerMaxSpatialLayer(streamId, peerId);
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
        var spatialStalenessWindow = SpatialStalenessWindow;
        var burstStabilizationWindow = BurstStabilizationWindow;
        var joinStabilizationWindow = TimeSpan.FromMilliseconds(50);
        VideoFrame? joinPendingKF = null;
        var joinPendingSince = CpuTimestamp.Now;
        // Pending-decay state for staleness-driven demotion. When a staleness
        // trigger fires we don't decay observedMaxSpatial synchronously — we record
        // the would-be target and wait BurstStabilizationWindow. If a higher-layer
        // keyframe arrives in that window the burst is intact (just out-of-order
        // arrival) and we abort. Otherwise the producer truly stopped emitting the
        // upper layer and we commit the decay.
        var pendingDecayLayer = -1;
        var pendingDecaySince = CpuTimestamp.Now;

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
                if (yieldedCount > 0
                    && lastYieldAt.Elapsed > Constants.Video.EgressStallThreshold) {
                    if (!stallHandled) {
                        var visible = latencyStore.IsPeerVisible(streamId, peerId);
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
                if (latencyStore.HasPeerEgressFallback(streamId, peerId)) {
                    var setAt = latencyStore.GetPeerEgressFallbackSetAt(streamId, peerId);
                    if (setAt.Elapsed > Constants.Video.EgressRecoveryWindow) {
                        log.LogInformation(
                            "VideoStreamFilter: egress recovery — restoring cap for peer={PeerId}",
                            peerId);
                        latencyStore.RestorePeerEgressFallback(streamId, peerId);
                    }
                }

                // 1. Layer-cap refresh — re-read per-peer caps once per second
                if (layerCapsUpdatedAt.Elapsed.TotalSeconds >= 1) {
                    maxTemporalLayer = latencyStore.GetPeerMaxTemporalLayer(streamId, peerId);
                    maxSpatialLayer = latencyStore.GetPeerMaxSpatialLayer(streamId, peerId);
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
                        // Higher KF arrived — any pending decay was a false alarm.
                        pendingDecayLayer = -1;
                    }
                    else if (frame.SpatialLayerId == observedMaxSpatial) {
                        observedMaxSpatialSeenAt = CpuTimestamp.Now;
                        pendingDecayLayer = -1;
                    }
                    else if (frame.SpatialLayerId > pendingDecayLayer
                        && pendingDecayLayer >= 0) {
                        // Same burst, higher than what we'd decay to but still below
                        // observedMax — promote the pending target. e.g. burst arrives
                        // spatial=0 KF first (sets pending=0), then spatial=1 KF
                        // (raise pending to 1). If spatial=2 also arrives we'll abort
                        // entirely on the previous branch.
                        pendingDecayLayer = frame.SpatialLayerId;
                    }
                    else if (observedMaxSpatialSeenAt.Elapsed > spatialStalenessWindow) {
                        // Top layer hasn't produced a KF in spatialStalenessWindow.
                        // Don't decay synchronously — record the would-be target and
                        // wait BurstStabilizationWindow. A higher-layer KF arriving
                        // before then means the burst is intact (out-of-order delivery,
                        // base lags extras by ~1 s); commit only if no higher KF
                        // shows up.
                        if (pendingDecayLayer < 0) {
                            pendingDecayLayer = frame.SpatialLayerId;
                            pendingDecaySince = CpuTimestamp.Now;
                        }
                    }
                }

                // 2b. Pending-decay commit.
                // If the burst-stabilization window has elapsed without a higher-layer
                // KF arriving, the upper layer is genuinely gone — apply the decay.
                if (pendingDecayLayer >= 0
                    && pendingDecaySince.Elapsed > burstStabilizationWindow) {
                    log.LogInformation(
                        "VideoStreamFilter: peer={PeerId} observedMaxSpatial decay {Old}->{New} after burst window expired",
                        peerId, observedMaxSpatial, pendingDecayLayer);
                    observedMaxSpatial = pendingDecayLayer;
                    observedMaxSpatialSeenAt = CpuTimestamp.Now;
                    observedMaxLastChange = CpuTimestamp.Now;
                    pendingDecayLayer = -1;
                }

                // 3. Desired layer = what we want to deliver, clamped to what producer
                // actually emits. Switch only when this differs from `selectedForCap`
                // (the value we committed to last) — within a burst, observedMax grows
                // but we commit only once, to the final value after stabilization.
                var desiredLayer = Math.Min(maxSpatialLayer, observedMaxSpatial);
                var desiredChanged = desiredLayer != selectedForCap;

                // 3a. Pending-join handling — absorb the initial keyframe burst.
                if (joinPendingKF != null) {
                    // Upgrade pending if a higher-layer KF arrives in the same burst.
                    // Climb whenever the peer's cap permits — including the uncapped
                    // case (int.MaxValue, the default for a fresh peer that hasn't
                    // sent a render hint yet). Bias on initial subscribe is the top
                    // layer the producer is emitting; receivers that want less
                    // bandwidth lower the cap via render hint after first frames,
                    // and the post-join switch (3b) demotes them. The previous
                    // policy (climb only when cap was explicitly finite) pinned
                    // every fresh subscribe to spatial=0 because peer caps default
                    // to int.MaxValue and the first ReportVideoLatency arrives
                    // ~1 s later — meaning every join landed at the lowest layer
                    // even when the producer was emitting all three.
                    if (frame.IsKeyFrame
                        && frame.SpatialLayerId > joinPendingKF.SpatialLayerId
                        && (maxSpatialLayer == int.MaxValue || frame.SpatialLayerId <= maxSpatialLayer)) {
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
                        latencyStore.RecordPeerForwarded(streamId, peerId,
                            committed.SpatialLayerId, committed.Width, committed.Height, observedMaxSpatial);
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
                        latencyStore.RecordPeerForwarded(streamId, peerId,
                            frame.SpatialLayerId, frame.Width, frame.Height, observedMaxSpatial);
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
                    latencyStore.RecordPeerForwarded(streamId, peerId,
                        frame.SpatialLayerId, frame.Width, frame.Height, observedMaxSpatial);
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
                    if (skippedCount == Constants.Video.EgressGapFrameThreshold
                        && selectedSpatialLayer > 0) {
                        log.LogInformation(
                            "VideoStreamFilter: egress gap exhausted ({Skipped} frames) for peer={PeerId}, decrementing",
                            skippedCount, peerId);
                        latencyStore.DecrementPeerEgressFallback(streamId, peerId);
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
#pragma warning restore CS0162
    }

    // TEMP: minimal pass-through filter for dev-env stability troubleshooting.
    // No spatial layer selection, no burst stabilization, no decay, no egress
    // decrement. Just KF-gap recovery + per-peer temporal cap + pause filter.
    // Used while client-side simulcast is force-disabled — every frame arrives
    // at SpatialLayerId=0 so the SFU fan-out logic is dead weight.
    private async IAsyncEnumerable<VideoFrame> ApplySimple(
        StreamId streamId,
        string peerId,
        IAsyncEnumerable<VideoFrame> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
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
                log.LogWarning(e, "VideoStreamFilter(simple): preset refresh loop failed for #{StreamId}", streamId);
            }
        }, refreshToken);

        var maxTemporalLayer = latencyStore.GetPeerMaxTemporalLayer(streamId, peerId);
        var layerCapsUpdatedAt = CpuTimestamp.Now;
        var lastKeyFrameNumber = -1L;
        var skipping = true;
        var skippedCount = 0;
        var joined = false;
        var yieldedCount = 0;

        try {
            await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                if (layerCapsUpdatedAt.Elapsed.TotalSeconds >= 1) {
                    maxTemporalLayer = latencyStore.GetPeerMaxTemporalLayer(streamId, peerId);
                    layerCapsUpdatedAt = CpuTimestamp.Now;
                }

                if (frame.TemporalLayerId > maxTemporalLayer)
                    continue;

                if (preset.Level == VideoQualityLevel.Paused)
                    continue;

                if (frame.IsKeyFrame) {
                    if (!joined) {
                        log.LogDebug(
                            "VideoStreamFilter(simple): joined from KF#{KeyFrameNumber} at offset {KfOffset}",
                            frame.KeyFrameNumber, frame.Offset);
                        joined = true;
                    }
                    else if (skipping && skippedCount > 0) {
                        log.LogInformation(
                            "VideoStreamFilter(simple): found keyframe (KF#{KeyFrameNumber}) after skipping {Skipped} frames",
                            frame.KeyFrameNumber, skippedCount);
                    }
                    lastKeyFrameNumber = frame.KeyFrameNumber;
                    skipping = false;
                    skippedCount = 0;
                    yieldedCount++;
                    latencyStore.RecordPeerForwarded(streamId, peerId,
                        frame.SpatialLayerId, frame.Width, frame.Height, frame.SpatialLayerId);
                    yield return frame;
                }
                else if (!skipping && frame.KeyFrameNumber == lastKeyFrameNumber) {
                    yieldedCount++;
                    yield return frame;
                }
                else {
                    if (!skipping) {
                        skipping = true;
                        log.LogDebug(
                            "VideoStreamFilter(simple): gap — expected KF#{Expected}, got KF#{Actual}, skipping to next keyframe",
                            lastKeyFrameNumber, frame.KeyFrameNumber);
                    }
                    skippedCount++;
                }
            }

            if (!joined)
                log.LogWarning("VideoStreamFilter(simple): source completed without any keyframe");
            else if (yieldedCount == 0)
                log.LogWarning("VideoStreamFilter(simple): source completed, yielded 0 frames");
        }
        finally {
            refreshCts.CancelAndDisposeSilently();
            await refreshTask.SuppressCancellationAwait(false);
        }
    }
}
