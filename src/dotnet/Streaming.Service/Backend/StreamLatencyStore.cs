namespace ActualChat.Streaming;

/// <summary>
/// Node-local store for pending keyframe requests and their cooldown timestamps.
/// Quality / latency / pause logic was removed in Step 8.5; only the
/// keyframe-request signal path remains, consumed by
/// <see cref="VideoStreamingBackend"/>'s <c>RequestKeyFrame</c> / <c>GetQualityPreset</c>.
/// </summary>
public sealed class StreamLatencyStore
{
    internal readonly ConcurrentDictionary<StreamId, bool> KeyFrameRequests = new();
    internal readonly ConcurrentDictionary<StreamId, CpuTimestamp> LastKeyFrameRequestTime = new();

    public void OnStreamExpire(StreamId streamId)
    {
        KeyFrameRequests.TryRemove(streamId, out _);
        LastKeyFrameRequestTime.TryRemove(streamId, out _);
    }
}
