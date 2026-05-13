using ActualChat.Video;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Per-consumer video filter that clamps a raw stream to the client-requested
/// layer and temporal caps. Picks the layer per keyframe by clamping
/// the consumer's <see cref="ReceiveQuality.LayerId"/> into the
/// producer-declared range <c>[0, producerLayerCount - 1]</c> on the frame
/// itself; only switches layers on a keyframe. A quality change keeps
/// forwarding the currently selected layer until the requested layer's
/// keyframe arrives, so we don't manufacture delta-frame gaps while QC is
/// settling. Temporal upgrades are also delayed until a keyframe: once we
/// skip an enhancement-layer delta, later deltas from that temporal chain
/// are not safe to resume mid-GOP.
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
        var consumerLayerId = -1;
        var consumerTemporalLayerId = int.MaxValue;
        var selectedLayer = -1;
        var selectedTemporalLayerId = int.MaxValue;
        var lastKeyFrameNumber = -1;
        var skipping = true;

        await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            var q = getQuality();
            if (q.LayerId != consumerLayerId || q.TemporalLayerId != consumerTemporalLayerId) {
                consumerLayerId = q.LayerId;
                consumerTemporalLayerId = q.TemporalLayerId;
                if (!skipping && selectedLayer >= 0 && consumerTemporalLayerId > selectedTemporalLayerId)
                    selectedTemporalLayerId = consumerTemporalLayerId;
            }

            int producerLayerCount = frame.LayerCount;
            int desiredLayer = consumerLayerId < 0 ? 0
                : consumerLayerId >= producerLayerCount ? producerLayerCount - 1
                : consumerLayerId;

            if (frame.IsKeyFrame) {
                // Lock onto the desired layer on each matching keyframe; other-layer
                // keyframes (sibling simulcast bursts) get skipped.
                if (frame.LayerId == desiredLayer) {
                    if (ShouldDropByTemporalRequest(frame, consumerTemporalLayerId)) {
                        skipping = true;
                        continue;
                    }
                    selectedLayer = desiredLayer;
                    selectedTemporalLayerId = consumerTemporalLayerId;
                    lastKeyFrameNumber = frame.KeyFrameIndex;
                    skipping = false;
                    yield return frame;
                }
                continue;
            }

            if (skipping || selectedLayer < 0)
                continue;
            if (selectedLayer >= producerLayerCount) {
                skipping = true;
                continue;
            }
            if (frame.LayerId != selectedLayer)
                continue;
            if (frame.KeyFrameIndex != lastKeyFrameNumber) {
                skipping = true;
                continue;
            }
            if (ShouldDropByTemporalRequest(frame, selectedTemporalLayerId))
                continue;

            yield return frame;
        }
    }

    private static bool ShouldDropByTemporalRequest(VideoFrame frame, int requestedTemporalLayerId)
    {
        if (requestedTemporalLayerId <= 0)
            return false;

        var keptLayerCount = Math.Max(1, frame.TemporalLayerCount - requestedTemporalLayerId);
        return frame.TemporalLayerId >= keptLayerCount;
    }
}
