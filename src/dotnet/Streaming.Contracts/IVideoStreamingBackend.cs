using ActualChat.Attributes;
using ActualChat.Hosting;
using ActualChat.Sharding;
using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

[BackendService(nameof(HostRole.StreamingBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(ShardScheme.StreamingBackend))]
public interface IVideoStreamingBackend : IComputeService, IRpcService, IBackendService
{
    Task<RpcStream<VideoFrame>?> GetVideo(StreamId streamId, TimeSpan skipTo, string peerId, CancellationToken cancellationToken);
    Task<RpcStream<VideoFrame>?> GetVideoRaw(StreamId streamId, CancellationToken cancellationToken);
    Task PushVideo(VideoRecord record, RpcStream<VideoFrame> videoStream, CancellationToken cancellationToken);

    // Quality control — stream-local state, routed by StreamId.NodeRef
    [ComputeMethod]
    Task<VideoQualityPreset> GetQualityPreset(StreamId streamId, CancellationToken cancellationToken);

    Task RequestKeyFrame(StreamId streamId, CancellationToken cancellationToken = default);

    Task ReportPeerLatency(
        StreamId streamId,
        string peerId,
        double streamOffsetMs,
        double medianDecodeTimeMs = -1,
        int bufferDepth = -1,
        double bufferSpanMs = -1,
        int renderQualityLevel = -1,
        CancellationToken cancellationToken = default);
}
