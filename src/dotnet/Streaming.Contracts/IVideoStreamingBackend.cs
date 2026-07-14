using ActualChat.Attributes;
using ActualChat.Hosting;
using ActualChat.Sharding;
using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

[BackendService(nameof(HostRole.StreamingBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(ShardScheme.StreamingBackend))]
public interface IVideoStreamingBackend : IComputeService, IBackendService
{
    Task<RpcStream<VideoFrame>?> GetVideoRaw(StreamId streamId, CancellationToken cancellationToken);
    Task PushVideo(VideoRecord record, RpcStream<VideoFrameBundle> videoStream, CancellationToken cancellationToken);

    // Publisher-facing keyframe-request signal. RequestKeyFrame invalidates
    // this computed method so the recorder can force the next frame to be a
    // keyframe when the observed value changes.
    [ComputeMethod]
    Task<Moment> LastKeyframeRequestAt(StreamId streamId, CancellationToken cancellationToken);

    Task RequestKeyFrame(StreamId streamId, CancellationToken cancellationToken = default);

    // Publisher-facing viewer-demand aggregate. Demand reports from every API
    // pod route to the stream's owning node (StreamId routes by NodeRef), so it
    // sees all viewers regardless of which pod each viewer is connected to.
    // The API service projects the per-question views (mask, max, thumbnail)
    // from this single compute.
    [ComputeMethod]
    Task<StreamDemandInfo> DemandInfo(StreamId streamId, CancellationToken cancellationToken);
    // Plain (non-compute) on purpose: the counters change on every viewer
    // join/leave/pause; diagnostics poll this instead of subscribing.
    Task<StreamDemandStats> GetDemandStats(StreamId streamId, CancellationToken cancellationToken);

    Task ReportDemand(
        StreamId streamId, string sessionId, ReceiveQuality quality, CancellationToken cancellationToken = default);
    Task ClearDemand(StreamId streamId, string sessionId, CancellationToken cancellationToken = default);
}
