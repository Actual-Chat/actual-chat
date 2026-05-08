using System.Runtime.ExceptionServices;
using ActualChat.Video;

namespace ActualChat.Streaming;

/// <summary>
/// AsyncMemoizer specialised for live video. Replaces count-based FIFO eviction
/// with a duration-tracked, keyframe-span eviction policy: the chain holds at
/// most ~<paramref name="targetDuration"/> of recoverable source time per
/// layer, and eviction drops complete keyframe-anchored spans rather
/// than individual frames.
/// </summary>
/// <remarks>
/// <para>
/// Per-layer accounting in a single chain: all layers share the
/// underlying linked list, but eviction state is tracked per
/// <see cref="VideoFrame.LayerId"/> so a quiet or paused layer does not
/// drag the active layer into eviction (and vice versa).
/// </para>
/// <para>
/// Each layer maintains an ordered queue of keyframe offsets currently in the
/// chain plus the latest <c>Offset + Duration</c> seen for that layer. When a
/// layer's <c>(latestEnd - oldestKeyframeOffset)</c> exceeds the target and at
/// least two keyframes remain (so the last decodable anchor is preserved),
/// the layer with the largest excess is picked and its oldest span is evicted
/// by advancing the chain head past every node with an offset older than the
/// new anchor — across all layers, since layer-layer keyframes from the
/// same source instant share an <see cref="VideoFrame.Offset"/>.
/// </para>
/// </remarks>
public sealed class VideoStreamMemoizer : AsyncMemoizer<VideoFrame>
{
    private readonly TimeSpan _targetDuration;
    // Ordered queues of keyframe offsets currently retained, keyed by LayerId.
    private readonly Dictionary<int, Queue<TimeSpan>> _kfOffsetsByLayer = new();
    // Most recent (Offset + Duration) appended for each layer.
    private readonly Dictionary<int, TimeSpan> _latestEndByLayer = new();
    // Per-layer latest keyframe offset. Read by the Replay override from the
    // consumer thread to anchor the start position; written from the producer
    // thread inside EvictIfNeeded. Never evicted: eviction requires
    // kfs.Count >= 2, so the newest entry is always retained.
    private readonly ConcurrentDictionary<int, TimeSpan> _latestKfByLayer = new();

    public VideoStreamMemoizer(
        IAsyncEnumerable<VideoFrame> source,
        TimeSpan targetDuration,
        CancellationToken cancellationToken = default)
        : base(source, int.MaxValue, mustStart: false, cancellationToken)
    {
        if (targetDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(targetDuration), targetDuration, "Must be positive.");
        _targetDuration = targetDuration;
        this.Start();
    }

    /// <summary>
    /// Replay starts from the oldest of the per-layer latest keyframes —
    /// MIN across layers so every active layer's freshest keyframe stays in
    /// the yielded prefix and the downstream per-layer filter
    /// (ReceiveQualityFilter) can pick any layer without waiting for the
    /// next natural KF. <paramref name="tailSize"/> is ignored: the keyframe
    /// anchor drives the start position. A count-based limit is meaningless
    /// under simulcast where multiple layers share one chain
    /// (3 × 30 fps = 90 fps for camera).
    /// </summary>
    /// <remarks>
    /// Cold path: when no keyframe has arrived yet, <c>_latestKfByLayer</c>
    /// is empty and we fall through to yielding from the chain head — the
    /// caller's <c>SkipWhile(!IsKeyFrame)</c> safety net (in
    /// <c>VideoStreamingBackend.GetVideoRaw</c>) handles the pre-keyframe
    /// span.
    /// </remarks>
    public override async IAsyncEnumerable<VideoFrame> Replay(
        int tailSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = tailSize;
        TimeSpan? startOffset = _latestKfByLayer.IsEmpty
            ? null
            : _latestKfByLayer.Values.Min();

        var current = CurrentHead;
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            var head = CurrentHead;
            if (current.Index < head.Index) {
                current = head;
                continue;
            }

            var next = current.Next;
            if (next != null) {
                current = next;
                if (startOffset is { } so && current.Value.Offset < so)
                    continue;
                yield return current.Value;
                continue;
            }
            var completion = current.Completion;
            if (completion != null) {
                if (completion is ChannelClosedException)
                    yield break;
                ExceptionDispatchInfo.Capture(completion).Throw();
            }
            await current.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // Protected methods

    protected override void EvictIfNeeded(Node newNode)
    {
        var frame = newNode.Value;
        var layer = frame.LayerId;

        // Update per-layer latest end. Take max in case of out-of-order arrivals.
        var frameEnd = frame.Offset + frame.Duration;
        if (!_latestEndByLayer.TryGetValue(layer, out var prevEnd) || frameEnd > prevEnd)
            _latestEndByLayer[layer] = frameEnd;

        // Track this keyframe in its layer's ordered queue.
        if (frame.IsKeyFrame) {
            _latestKfByLayer[layer] = frame.Offset;
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
                    && _kfOffsetsByLayer.TryGetValue(oldest.Value.LayerId, out var q)
                    && q.Count > 0
                    && q.Peek() == oldest.Value.Offset) {
                    q.Dequeue();
                    // If this layer just lost its last in-chain KF, drop its
                    // latest-KF entry too — otherwise a paused layer's stale
                    // offset would anchor Replay's start to a frame that no
                    // longer exists in the chain (Replay would skip every
                    // remaining frame's Offset < stale-offset check trivially
                    // and fall back to chain-head replay, regressing latency).
                    if (q.Count == 0)
                        _latestKfByLayer.TryRemove(oldest.Value.LayerId, out _);
                }
                if (!TryAdvanceHead())
                    break;
            }
        }
    }
}
