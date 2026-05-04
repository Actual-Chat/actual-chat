using ActualChat.Video;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Per-consumer video filter that clamps a raw stream to the client-requested
/// spatial and temporal caps. Picks the spatial layer per keyframe by clamping
/// the consumer's <see cref="ReceiveQuality.MaxSpatialLayer"/> into the
/// producer-declared range <c>[0, MaxSpatialLayerId]</c> on the frame itself;
/// only switches layers on a keyframe. A quality change keeps forwarding the
/// currently selected layer until the requested layer's keyframe arrives, so we
/// don't manufacture delta-frame gaps while QC is settling. Temporal increases
/// are also delayed until a keyframe: once we skip an enhancement-layer delta,
/// later deltas from that temporal chain are not safe to resume mid-GOP.
/// </summary>
public static class ReceiveQualityFilter
{
    public static async IAsyncEnumerable<VideoFrame> Apply(
        IAsyncEnumerable<VideoFrame> source,
        Func<ReceiveQuality> getQuality,
        ILogger? log,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _ = log;
        var consumerMaxSpatial = -1;
        var consumerMaxTemporal = int.MaxValue;
        var selectedLayer = -1;
        var selectedMaxTemporal = int.MaxValue;
        var lastKeyFrameNumber = -1L;
        var skipping = true;

        await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            var q = getQuality();
            if (q.MaxSpatialLayer != consumerMaxSpatial || q.MaxTemporalLayer != consumerMaxTemporal) {
                consumerMaxSpatial = q.MaxSpatialLayer;
                consumerMaxTemporal = q.MaxTemporalLayer;
                if (!skipping && selectedLayer >= 0 && consumerMaxTemporal < selectedMaxTemporal)
                    selectedMaxTemporal = consumerMaxTemporal;
            }

            int producerMax = frame.MaxSpatialLayerId;
            int desiredLayer = consumerMaxSpatial < 0 ? 0
                : consumerMaxSpatial > producerMax ? producerMax
                : consumerMaxSpatial;

            if (frame.IsKeyFrame) {
                // Lock onto the desired layer on each matching keyframe; other-layer
                // keyframes (sibling simulcast bursts) get skipped.
                if (frame.SpatialLayerId == desiredLayer) {
                    if (frame.TemporalLayerId > consumerMaxTemporal) {
                        skipping = true;
                        continue;
                    }
                    selectedLayer = desiredLayer;
                    selectedMaxTemporal = consumerMaxTemporal;
                    lastKeyFrameNumber = frame.KeyFrameNumber;
                    skipping = false;
                    yield return frame;
                }
                continue;
            }

            if (skipping || selectedLayer < 0)
                continue;
            // Producer dropped our layer mid-GOP — wait for the next keyframe to re-select.
            if (selectedLayer > producerMax) {
                skipping = true;
                continue;
            }
            if (frame.SpatialLayerId != selectedLayer)
                continue;
            // Bounded-replay channel may have evicted intervening frames; gap means
            // the GOP is broken and we have to wait for the next keyframe.
            if (frame.KeyFrameNumber != lastKeyFrameNumber) {
                skipping = true;
                continue;
            }
            if (frame.TemporalLayerId > selectedMaxTemporal)
                continue;

            yield return frame;
        }
    }
}
