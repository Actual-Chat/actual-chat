using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public interface IVideoStreamingBackend : IRpcService, IBackendService
{
    Task<RpcStream<VideoFrame>?> GetVideo(StreamId streamId, TimeSpan skipTo, string peerId, CancellationToken cancellationToken);
    Task PushVideo(VideoRecord record, RpcStream<VideoFrame> videoStream, CancellationToken cancellationToken);

    [RpcMethod(LocalExecutionMode = RpcLocalExecutionMode.Unconstrained)]
    Task<RpcStream<VideoQualityPreset>> ObserveStreamQualityRequests(StreamId streamId, CancellationToken cancellationToken);

    Task ReportPeerLatency(StreamId streamId, string peerId, double streamOffsetMs, CancellationToken cancellationToken = default);
}
