using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public interface ILiveVideoStreams : IComputeService
{
    Task<RpcStream<VideoFrame>?> GetStream(
        Session session,
        StreamId streamId,
        CancellationToken cancellationToken);

    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<ApiArray<VideoStreamInfo>> List(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<int> GetMemberCount(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<ApiArray<string>> GetSupportedCodecs(Session session, ChatId chatId, CancellationToken cancellationToken);

    // Publisher-facing keyframe-request signal. Changes when a request is accepted.
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<Moment> LastKeyframeRequestAt(Session session, StreamId streamId, CancellationToken cancellationToken);

    // Max ReceiveQuality.LayerId across all subscribers; -1 = none subscribed.
    // Superseded by RequestedLayersMask; kept for older clients.
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<int> MaxRequestedLayerId(Session session, StreamId streamId, CancellationToken cancellationToken);

    // Demanded-layer bitmask across all subscribers (bit i = canonical ladder
    // index i is wanted; 0 = none subscribed) — the recorder skips undemanded tiers.
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<int> RequestedLayersMask(Session session, StreamId streamId, CancellationToken cancellationToken);

    // True iff at least one active (non-paused) viewer exists and every one
    // reports ReceiveQuality.IsThumbnail — the sender may shed fps. Zero viewers → false.
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<bool> ThumbnailViewersOnly(Session session, StreamId streamId, CancellationToken cancellationToken);

    // Full demand aggregate (mask + viewer/paused counts) for the publisher's
    // diagnostics — makes an empty mask attributable (no reports vs. all paused).
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<StreamDemandInfo> DemandInfo(Session session, StreamId streamId, CancellationToken cancellationToken);

    Task RegisterMember(
        Session session, ChatId chatId, ApiArray<string> supportedDecoderCodecs, CancellationToken cancellationToken);
    Task UnregisterMember(
        Session session, ChatId chatId, CancellationToken cancellationToken);

    [RpcMethod(RemoteExecutionMode = RpcRemoteExecutionMode.AwaitForConnection | RpcRemoteExecutionMode.AllowReconnect)]
    Task PushStream(
        Session session,
        string chatId,
        double clientStartAt, // Unix epoch (seconds, double)
        VideoFormat format,
        VideoSourceKind sourceKind,
        RpcStream<VideoFrameBundle> frameStream,
        CancellationToken cancellationToken);

    [RpcMethod(RemoteExecutionMode = RpcRemoteExecutionMode.AwaitForConnection, ConnectTimeout = 10)]
    Task RequestKeyFrame(Session session, string streamId, CancellationToken cancellationToken);

    [RpcMethod(RemoteExecutionMode = RpcRemoteExecutionMode.AwaitForConnection, ConnectTimeout = 10)]
    Task ChangeRecordingQuality(
        Session session,
        RecordingQualityState? state,
        RecordingQualityInfo? info,
        CancellationToken cancellationToken);

    [RpcMethod(RemoteExecutionMode = RpcRemoteExecutionMode.AwaitForConnection, ConnectTimeout = 10)]
    Task ChangePlaybackQuality(
        Session session,
        ApiMap<string, ReceiveQuality>? qualityByStream,
        PlaybackQualityInfo? info,
        CancellationToken cancellationToken);
}
