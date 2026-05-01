using ActualChat.Video;

namespace ActualChat.Streaming;

/// <summary>
/// AsyncMemoizer specialised for live video. Replaces count-based FIFO eviction
/// with a duration-tracked, keyframe-span eviction policy: the chain holds at
/// most ~<paramref name="targetDuration"/> of recoverable source time per
/// spatial layer, and eviction drops complete keyframe-anchored spans rather
/// than individual frames.
/// </summary>
/// <remarks>
/// <para>
/// Per-layer accounting in a single chain: all spatial layers share the
/// underlying linked list, but eviction state is tracked per
/// <see cref="VideoFrame.SpatialLayerId"/> so a quiet or paused layer does not
/// drag the active layer into eviction (and vice versa).
/// </para>
/// <para>
/// Each layer maintains an ordered queue of keyframe offsets currently in the
/// chain plus the latest <c>Offset + Duration</c> seen for that layer. When a
/// layer's <c>(latestEnd - oldestKeyframeOffset)</c> exceeds the target and at
/// least two keyframes remain (so the last decodable anchor is preserved),
/// the layer with the largest excess is picked and its oldest span is evicted
/// by advancing the chain head past every node with an offset older than the
/// new anchor — across all layers, since spatial-layer keyframes from the
/// same source instant share an <see cref="VideoFrame.Offset"/>.
/// </para>
/// </remarks>
public sealed class VideoStreamMemoizer : AsyncMemoizer<VideoFrame>
{
    private readonly TimeSpan _targetDuration;
    // Ordered queues of keyframe offsets currently retained, keyed by SpatialLayerId.
    private readonly Dictionary<int, Queue<TimeSpan>> _kfOffsetsByLayer = new();
    // Most recent (Offset + Duration) appended for each layer.
    private readonly Dictionary<int, TimeSpan> _latestEndByLayer = new();

    public VideoStreamMemoizer(
        IAsyncEnumerable<VideoFrame> source,
        TimeSpan targetDuration,
        CancellationToken cancellationToken = default)
        : base(source, int.MaxValue, cancellationToken)
    {
        if (targetDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(targetDuration), targetDuration, "Must be positive.");
        _targetDuration = targetDuration;
    }

    protected override void EvictIfNeeded(Node newNode)
    {
        var frame = newNode.Value;
        var layer = frame.SpatialLayerId;

        // Update per-layer latest end. Take max in case of out-of-order arrivals.
        var frameEnd = frame.Offset + frame.Duration;
        if (!_latestEndByLayer.TryGetValue(layer, out var prevEnd) || frameEnd > prevEnd)
            _latestEndByLayer[layer] = frameEnd;

        // Track this keyframe in its layer's ordered queue.
        if (frame.IsKeyFrame) {
            if (!_kfOffsetsByLayer.TryGetValue(layer, out var kfQueue)) {
                kfQueue = new Queue<TimeSpan>();
                _kfOffsetsByLayer[layer] = kfQueue;
            }
            kfQueue.Enqueue(frame.Offset);
        }

        // Outer loop: while at least one layer is over target AND has a spare
        // keyframe to fall back on, evict its oldest span. Continue until no
        // layer exceeds the target, or only single-span layers remain.
        while (true) {
            int? layerStar = null;
            var maxBuffered = TimeSpan.Zero;
            foreach (var (l, kfs) in _kfOffsetsByLayer) {
                if (kfs.Count < 2)
                    continue; // keep the only anchor
                if (!_latestEndByLayer.TryGetValue(l, out var latest))
                    continue;
                var buffered = latest - kfs.Peek();
                if (buffered > _targetDuration && buffered > maxBuffered) {
                    maxBuffered = buffered;
                    layerStar = l;
                }
            }
            if (layerStar is not { } picked)
                break;

            // Drop L*'s oldest keyframe; the next one becomes the new anchor.
            var kfQueueStar = _kfOffsetsByLayer[picked];
            kfQueueStar.Dequeue();
            var newAnchorOffset = kfQueueStar.Peek(); // safe: Count was >= 2

            // Advance head until the oldest live node's offset reaches the new
            // L* anchor. Pop matching offsets from other layers' queues whose
            // keyframes fall inside the dropped section.
            while (true) {
                var head = CurrentHead;
                var oldest = head.Next;
                if (oldest is null)
                    break;
                if (oldest.Value.Offset >= newAnchorOffset)
                    break;
                if (oldest.Value.IsKeyFrame
                    && _kfOffsetsByLayer.TryGetValue(oldest.Value.SpatialLayerId, out var q)
                    && q.Count > 0
                    && q.Peek() == oldest.Value.Offset) {
                    q.Dequeue();
                }
                if (!TryAdvanceHead())
                    break;
            }
        }
    }
}
