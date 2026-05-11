using ActualChat.Video;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Per-consumer video filter that clamps a raw stream to the client-requested
/// layer and temporal caps. Picks the layer per keyframe by clamping
/// the consumer's <see cref="ReceiveQuality.MaxLayerId"/> into the
/// producer-declared range <c>[0, MaxLayerId]</c> on the frame itself;
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
        var consumerMaxLayerId = -1;
        var consumerMaxTemporalLayerId = int.MaxValue;
        var selectedLayer = -1;
        var selectedMaxTemporalLayerId = int.MaxValue;
        var lastKeyFrameNumber = -1;
        var skipping = true;

        await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            var q = getQuality();
            if (q.MaxLayerId != consumerMaxLayerId || q.MaxTemporalLayerId != consumerMaxTemporalLayerId) {
                consumerMaxLayerId = q.MaxLayerId;
                consumerMaxTemporalLayerId = q.MaxTemporalLayerId;
                if (!skipping && selectedLayer >= 0 && consumerMaxTemporalLayerId < selectedMaxTemporalLayerId)
                    selectedMaxTemporalLayerId = consumerMaxTemporalLayerId;
            }

            int producerMax = frame.MaxLayerId;
            int desiredLayer = consumerMaxLayerId < 0 ? 0
                : consumerMaxLayerId > producerMax ? producerMax
                : consumerMaxLayerId;

            if (frame.IsKeyFrame) {
                // Lock onto the desired layer on each matching keyframe; other-layer
                // keyframes (sibling simulcast bursts) get skipped.
                if (frame.LayerId == desiredLayer) {
                    if (frame.TemporalLayerId > consumerMaxTemporalLayerId) {
                        skipping = true;
                        continue;
                    }
                    selectedLayer = desiredLayer;
                    selectedMaxTemporalLayerId = consumerMaxTemporalLayerId;
                    lastKeyFrameNumber = frame.KeyFrameIndex;
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
            if (frame.LayerId != selectedLayer)
                continue;
            // Bounded-replay channel may have evicted intervening frames; gap means
            // the GOP is broken and we have to wait for the next keyframe.
            if (frame.KeyFrameIndex != lastKeyFrameNumber) {
                skipping = true;
                continue;
            }
            if (frame.TemporalLayerId > selectedMaxTemporalLayerId)
                continue;

            yield return frame;
        }
    }
}
