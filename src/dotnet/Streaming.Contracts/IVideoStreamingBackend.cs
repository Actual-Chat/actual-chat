using ActualChat.Attributes;
using ActualChat.Sharding;
using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

[BackendService(nameof(HostRole.StreamingBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(ShardScheme.StreamingBackend))]
public interface IVideoStreamingBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ChatId?> GetChatId(StreamId streamId, CancellationToken cancellationToken);

    Task<RpcStream<VideoFrame>?> GetVideoRaw(StreamId streamId, CancellationToken cancellationToken);

    // Publisher-facing keyframe-request signal, invalidated by RequestKeyFrame.
    [ComputeMethod]
    Task<Moment> LastKeyframeRequestAt(StreamId streamId, CancellationToken cancellationToken);

    // The viewer-demand aggregate on the stream's owning node (StreamId routes
    // by NodeRef, so it sees all viewers regardless of their API pod).
    [ComputeMethod]
    Task<StreamDemandInfo> DemandInfo(StreamId streamId, CancellationToken cancellationToken);

    // Plain (non-compute) on purpose: the counters change on every viewer
    // join/leave/pause; diagnostics poll this instead of subscribing.
    Task<StreamDemandStats> GetDemandStats(StreamId streamId, CancellationToken cancellationToken);

    Task PushVideo(VideoRecord record, RpcStream<VideoFrameBundle> videoStream, CancellationToken cancellationToken);
    Task RequestKeyFrame(StreamId streamId, CancellationToken cancellationToken = default);
    // Null quality clears the session's entry (viewer left) — same convention as
    // ChangePlaybackQuality's null map; ReceiveQuality.Paused keeps the viewer.
    Task ReportDemand(
        StreamId streamId, string sessionId, ReceiveQuality? quality, CancellationToken cancellationToken = default);
}
