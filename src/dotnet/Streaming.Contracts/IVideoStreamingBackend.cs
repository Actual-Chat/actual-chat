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
}
