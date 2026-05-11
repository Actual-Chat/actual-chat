using ActualChat.Video;

namespace ActualChat.Streaming.Services;

// Server-side counterpart of TS `traceDrops`. See
// src/dotnet/UI.Blazor.App/Services/Video/frame-drop-trace.ts for the design.
//
// Insert TraceDrops(prevStage) between server-side stages on the per-layer
// VideoFrame stream. The detector tracks lastIndex *per LayerId* — the
// memoizer fans out multiple layers from the same bundle, and successive
// frames may belong to different layers, so a single counter would treat
// inter-layer interleaving as drops.
public static class FrameDropTrace
{
    public static async IAsyncEnumerable<VideoFrame> TraceDrops(
        this IAsyncEnumerable<VideoFrame> source,
        FrameDropStage prevStage)
    {
        var lastIndexByLayer = new Dictionary<byte, int>();
        await foreach (var frame in source.ConfigureAwait(false)) {
            var layerId = frame.LayerId;
            if (lastIndexByLayer.TryGetValue(layerId, out var lastIndex)) {
                var gap = frame.Index - lastIndex - 1;
                if (gap > 0) {
                    var have = frame.DropTrace.Length;
                    var missing = gap - have;
                    if (missing > 0)
                        yield return WithExtendedTrace(frame, prevStage, missing);
                    else
                        yield return frame;
                }
                else {
                    yield return frame;
                }
            }
            else {
                // First frame of this layer — never tag.
                yield return frame;
            }
            lastIndexByLayer[layerId] = frame.Index;
        }
    }

    // VideoFrame is `init`-only; produce a copy with the lengthened trace.
    // SerializedData is intentionally left default so the formatter re-encodes
    // the frame on next serialization (the cached bytes don't include the new
    // trace entries).
    private static VideoFrame WithExtendedTrace(VideoFrame source, FrameDropStage stage, int extraCount)
    {
        var have = source.DropTrace.Length;
        var bytes = new byte[have + extraCount];
        if (have > 0)
            source.DropTrace.Span.CopyTo(bytes);
        var stageByte = (byte)stage;
        for (var i = have; i < bytes.Length; i++)
            bytes[i] = stageByte;

        return new VideoFrame(source.IsKeyFrame) {
            Data = source.Data,
            Offset = source.Offset,
            OffsetEpoch = source.OffsetEpoch,
            Duration = source.Duration,
            Width = source.Width,
            Height = source.Height,
            Description = source.Description,
            Codec = source.Codec,
            LayerId = source.LayerId,
            MaxLayerId = source.MaxLayerId,
            TemporalLayerId = source.TemporalLayerId,
            SourceWidth = source.SourceWidth,
            SourceHeight = source.SourceHeight,
            MaxLayerWidth = source.MaxLayerWidth,
            MaxLayerHeight = source.MaxLayerHeight,
            Index = source.Index,
            DropTrace = bytes,
            KeyFrameNumber = source.KeyFrameNumber,
        };
    }

    // Bundle-level detector for the publisher leg. The bundle's index is
    // taken from Layers[0].Index (all layers share the source-moment index),
    // and on a gap the trace is extended on every layer in the bundle so
    // each per-frame DTO carries the full record after decompose.
    public static async IAsyncEnumerable<VideoFrameBundle> TraceBundleDrops(
        this IAsyncEnumerable<VideoFrameBundle> source,
        FrameDropStage prevStage)
    {
        var lastIndex = -1;
        await foreach (var bundle in source.ConfigureAwait(false)) {
            if (bundle.Layers.Length == 0) {
                yield return bundle;
                continue;
            }
            var index = bundle.Layers[0].Index;
            if (lastIndex >= 0) {
                var gap = index - lastIndex - 1;
                if (gap > 0) {
                    var have = bundle.Layers[0].DropTrace.Length;
                    var missing = gap - have;
                    if (missing > 0) {
                        var newLayers = new VideoFrame[bundle.Layers.Length];
                        for (var i = 0; i < bundle.Layers.Length; i++)
                            newLayers[i] = WithExtendedTrace(bundle.Layers[i], prevStage, missing);
                        yield return new VideoFrameBundle(newLayers);
                        lastIndex = index;
                        continue;
                    }
                }
            }
            lastIndex = index;
            yield return bundle;
        }
    }

}
