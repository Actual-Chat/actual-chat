using ActualChat.Video;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Per-consumer video filter that clamps a raw stream to the client-requested
/// spatial layer cap. Picks the layer per keyframe by clamping the consumer's
/// <see cref="ReceiveQuality.LayerId"/> into the producer-declared range
/// <c>[0, producerLayerCount - 1]</c> on the frame itself; only switches
/// layers on a keyframe. A quality change keeps forwarding the currently
/// selected layer until the requested layer's keyframe arrives, so we don't
/// manufacture delta-frame gaps while QC is settling.
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
        var selectedLayer = -1;
        var lastKeyFrameNumber = -1;
        var skipping = true;

        await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            var q = getQuality();
            if (q.LayerId != consumerLayerId) {
                consumerLayerId = q.LayerId;
                if (q.IsPaused) {
                    skipping = true;
                    selectedLayer = -1;
                }
            }

            if (consumerLayerId < 0)
                continue;

            int producerLayerCount = frame.LayerCount;
            int desiredLayer = consumerLayerId >= producerLayerCount ? producerLayerCount - 1
                : consumerLayerId;

            if (frame.IsKeyFrame) {
                if (frame.LayerId == desiredLayer) {
                    selectedLayer = desiredLayer;
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

            yield return frame;
        }
    }
}
