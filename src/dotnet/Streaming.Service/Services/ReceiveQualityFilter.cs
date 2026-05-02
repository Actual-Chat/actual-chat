using ActualChat.Video;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Per-consumer video filter that clamps a raw stream to the client-requested
/// spatial and temporal caps. Forwards exactly one spatial layer (highest
/// available not exceeding MaxSpatialLayer) and drops frames above the
/// MaxTemporalLayer cap. Skip-until-keyframe on cap change and on
/// keyframe-number gaps for decoder safety.
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
        var maxSpatial = -1;
        var maxTemporal = int.MaxValue;
        var observedMaxSpatial = 0;
        var selectedLayer = -1;
        var lastKeyFrameNumber = -1L;
        var skipping = true;
        var capRefreshAt = CpuTimestamp.Now;

        await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            if (maxSpatial < 0 || capRefreshAt.Elapsed >= CapRefreshInterval) {
                var q = getQuality();
                if (q.MaxSpatialLayer != maxSpatial || q.MaxTemporalLayer != maxTemporal) {
                    maxSpatial = q.MaxSpatialLayer;
                    maxTemporal = q.MaxTemporalLayer;
                    skipping = true;
                }
                capRefreshAt = CpuTimestamp.Now;
            }

            if (frame.IsKeyFrame && frame.SpatialLayerId > observedMaxSpatial)
                observedMaxSpatial = frame.SpatialLayerId;

            var desiredLayer = Math.Min(maxSpatial, observedMaxSpatial);

            if (frame.TemporalLayerId > maxTemporal)
                continue;

            if (frame.IsKeyFrame && frame.SpatialLayerId == desiredLayer) {
                selectedLayer = desiredLayer;
                lastKeyFrameNumber = frame.KeyFrameNumber;
                skipping = false;
                yield return frame;
                continue;
            }

            if (skipping || selectedLayer < 0)
                continue;

            if (frame.SpatialLayerId != selectedLayer)
                continue;

            if (frame.KeyFrameNumber != lastKeyFrameNumber) {
                skipping = true;
                continue;
            }

            yield return frame;
        }
    }
}
