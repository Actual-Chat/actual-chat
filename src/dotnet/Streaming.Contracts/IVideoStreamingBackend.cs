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

    // Publisher-facing viewer-demand aggregates. Demand reports from every API
    // pod route to the stream's owning node (StreamId routes by NodeRef), so
    // these see all viewers regardless of which pod each viewer is connected to.
    [ComputeMethod]
    Task<int> DemandedLayersMask(StreamId streamId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<int> MaxDemandedLayerId(StreamId streamId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<bool> ThumbnailViewersOnly(StreamId streamId, CancellationToken cancellationToken);

    Task ReportDemand(
        StreamId streamId, string sessionId, ReceiveQuality quality, CancellationToken cancellationToken = default);
    Task ClearDemand(StreamId streamId, string sessionId, CancellationToken cancellationToken = default);
}
