using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public interface ILiveVideoStreams : IComputeService
{
    Task<RpcStream<VideoFrame>?> GetStream(
        Session session,
        StreamId streamId,
        CancellationToken cancellationToken);

    Task<RpcStream<VideoFrame>?> GetStreamWithCapabilities(
        Session session,
        StreamId streamId,
        ApiArray<string> supportedDecoderCodecs,
        CancellationToken cancellationToken);

    // ReturnDefault: "no streams" is the right answer offline and until the server answers
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.ReturnDefault)]
    Task<ApiArray<VideoStreamInfo>> List(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.ReturnDefault)]
    Task<int> GetMemberCount(Session session, ChatId chatId, CancellationToken cancellationToken);

    // NoCache, unlike List: the recorder picks its codec from this, so a default empty list would mislead it
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<ApiArray<string>> GetSupportedCodecs(Session session, ChatId chatId, CancellationToken cancellationToken);

    // Publisher-facing keyframe-request signal. Changes when a request is accepted.
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<Moment> LastKeyframeRequestAt(Session session, StreamId streamId, CancellationToken cancellationToken);

    // The viewer-demand aggregate: layer bitmask (bit i = canonical ladder
    // index i is wanted; 0 = none subscribed) + thumbnail-only ("every active
    // viewer displays this stream as a thumbnail" — the sender may shed fps).
    // NoCache: a default mask of 0 reads as "nobody subscribed", and the publisher would shed layers on it.
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<StreamDemandInfo> DemandInfo(Session session, StreamId streamId, CancellationToken cancellationToken);

    // Demand provenance for diagnostics (poll-only; deliberately not a compute
    // method — see StreamDemandStats).
    Task<StreamDemandStats> GetDemandStats(Session session, StreamId streamId, CancellationToken cancellationToken);

    Task RegisterMember(
        Session session, ChatId chatId, ApiArray<string> supportedDecoderCodecs, CancellationToken cancellationToken);
    Task UnregisterMember(
        Session session, ChatId chatId, CancellationToken cancellationToken);

    // The call completes only when its stream does, so the default 30s DelayTimeout is pure log noise
    [RpcMethod(
        RemoteExecutionMode = RpcRemoteExecutionMode.AwaitForConnection | RpcRemoteExecutionMode.AllowReconnect,
        DelayTimeout = double.PositiveInfinity)]
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

    // Legacy methods

    // 2.11 and older clients read demand through these; 2.12-2.22 ones fall back to them when DemandInfo fails
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    [LegacyName("MaxRequestedLayerId", "2.22.9999")]
    [Obsolete("2026.07: Use DemandInfo. Old clients only.")]
    Task<int> LegacyMaxRequestedLayerId(Session session, StreamId streamId, CancellationToken cancellationToken);

    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    [LegacyName("RequestedLayersMask", "2.22.9999")]
    [Obsolete("2026.07: Use DemandInfo. Old clients only.")]
    Task<int> LegacyRequestedLayersMask(Session session, StreamId streamId, CancellationToken cancellationToken);

    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    [LegacyName("ThumbnailViewersOnly", "2.22.9999")]
    [Obsolete("2026.07: Use DemandInfo. Old clients only.")]
    Task<bool> LegacyThumbnailViewersOnly(Session session, StreamId streamId, CancellationToken cancellationToken);
}
