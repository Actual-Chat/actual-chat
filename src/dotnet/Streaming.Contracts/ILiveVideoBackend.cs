using ActualChat.Attributes;
using ActualChat.Hosting;
using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

[BackendService(nameof(HostRole.VideoBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.VideoBackend))]
public interface ILiveVideoBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ApiArray<VideoStreamInfo>> ListActiveStreams(ChatId chatId, CancellationToken cancellationToken);

    [RpcMethod(LocalExecutionMode = RpcLocalExecutionMode.Unconstrained)]
    Task<RpcStream<VideoStreamInfo>> ObserveStreams(ChatId chatId, CancellationToken cancellationToken);

    [RpcMethod(LocalExecutionMode = RpcLocalExecutionMode.Unconstrained)]
    Task<RpcStream<VideoQualityPreset>> ObserveStreamQualityRequests(StreamId streamId, CancellationToken cancellationToken);

    Task<RpcStream<VideoFrame>?> GetVideo(StreamId streamId, TimeSpan skipTo, string peerId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<AuthorId[]> GetVideoStreamingAuthorIds(ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<int> GetVideoStreamMemberCount(ChatId chatId, CancellationToken cancellationToken);

    Task RegisterActiveStream(ChatId chatId, VideoStreamInfo streamInfo, CancellationToken cancellationToken);
    Task UnregisterActiveStream(ChatId chatId, StreamId streamId, CancellationToken cancellationToken);

    Task RegisterVideoStreamMember(ChatId chatId, string sessionId, CancellationToken cancellationToken);
    Task UnregisterVideoStreamMember(ChatId chatId, string sessionId, CancellationToken cancellationToken);

    Task PushVideo(VideoRecord record, RpcStream<VideoFrame> videoStream, CancellationToken cancellationToken);

    Task ReportPeerLatency(StreamId streamId, string peerId, double streamOffsetMs, CancellationToken cancellationToken = default);
}
