using ActualChat.Video;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Per-consumer video filter that clamps a raw stream to the client-requested
/// spatial and temporal caps. Picks the spatial layer per keyframe by clamping
/// the consumer's <see cref="ReceiveQuality.MaxSpatialLayer"/> into the
/// producer-declared range <c>[0, MaxSpatialLayerId]</c> on the frame itself;
/// only switches layers on a keyframe so a producer-side ladder change mid-GOP
/// triggers skip-until-next-keyframe, not torn output.
/// </summary>
public static class ReceiveQualityFilter
{
    private static readonly TimeSpan CapRefreshInterval = TimeSpan.FromMilliseconds(500);

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
        var lastKeyFrameNumber = -1L;
        var skipping = true;
        var capRefreshAt = CpuTimestamp.Now;

        await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            if (consumerMaxSpatial < 0 || frame.IsKeyFrame || capRefreshAt.Elapsed >= CapRefreshInterval) {
                var q = getQuality();
                consumerMaxSpatial = q.MaxSpatialLayer;
                consumerMaxTemporal = q.MaxTemporalLayer;
                capRefreshAt = CpuTimestamp.Now;
            }

            if (frame.TemporalLayerId > consumerMaxTemporal)
                continue;

            int producerMax = frame.MaxSpatialLayerId;
            int desiredLayer = consumerMaxSpatial < 0 ? 0
                : consumerMaxSpatial > producerMax ? producerMax
                : consumerMaxSpatial;

            if (frame.IsKeyFrame) {
                // Lock onto the desired layer on each matching keyframe; other-layer
                // keyframes (sibling simulcast bursts) get skipped.
                if (frame.SpatialLayerId == desiredLayer) {
                    selectedLayer = desiredLayer;
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

            yield return frame;
        }
    }
}
