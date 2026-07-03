using ActualChat.Video;

namespace ActualChat.Streaming.Services;

// Canonical layer ids are stable (the layer→resolution map never changes
// mid-stream); a quality or mask change keeps forwarding the currently
// selected layer until the newly desired layer's keyframe arrives, so we
// don't manufacture delta-frame gaps while QC is settling.

/// <summary>
/// Per-consumer video filter that resolves the client-requested
/// <see cref="ReceiveQuality.LayerId"/> against the producer's live layers
/// (<see cref="VideoFrame.EffectiveLayerMask"/>); switches only on keyframes.
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

            var layerMask = frame.EffectiveLayerMask;
            var desiredLayer = SelectLayer(consumerLayerId, layerMask);
            if (desiredLayer < 0)
                continue;

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
            if ((layerMask & (1 << selectedLayer)) == 0) {
                // Producer stopped encoding the selected tier mid-GOP; re-anchor
                // on the next keyframe of the (new) desired layer.
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

    // Best live layer for a request: the largest available id ≤ requested
    // (never exceed the asked-for bandwidth), else the smallest one above it.
    // For a legacy contiguous mask this reduces to min(requested, count - 1).
    internal static int SelectLayer(int requested, int layerMask)
    {
        for (var i = Math.Min(requested, 30); i >= 0; i--)
            if ((layerMask & (1 << i)) != 0)
                return i;
        for (var i = requested + 1; i <= 30; i++)
            if ((layerMask & (1 << i)) != 0)
                return i;

        return -1;
    }
}
