using ActualChat.Attributes;
using ActualChat.Hosting;
using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

[BackendService(nameof(HostRole.VideoBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.VideoBackend))]
public interface IVideoStreamingBackend : IRpcService, IBackendService
{
    Task<RpcStream<VideoFrame>?> GetVideo(StreamId streamId, TimeSpan skipTo, string peerId, CancellationToken cancellationToken);
    Task PushVideo(VideoRecord record, RpcStream<VideoFrame> videoStream, CancellationToken cancellationToken);

    [RpcMethod(LocalExecutionMode = RpcLocalExecutionMode.Unconstrained)]
    Task<RpcStream<VideoQualityPreset>> ObserveStreamQualityRequests(StreamId streamId, CancellationToken cancellationToken);

    Task ReportPeerLatency(
        StreamId streamId,
        string peerId,
        double streamOffsetMs,
        double medianDecodeTimeMs = -1,
        int bufferDepth = -1,
        double bufferSpanMs = -1,
        CancellationToken cancellationToken = default);
}
