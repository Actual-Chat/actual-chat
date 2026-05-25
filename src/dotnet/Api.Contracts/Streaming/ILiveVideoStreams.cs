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

    // Publisher-facing aggregate of max ReceiveQuality.LayerId across all
    // subscribers of this stream. Returns -1 when nobody is subscribed
    // (or every consumer is paused). The recorder caps its encoder ladder
    // at min(EncodingCap.Layers, MaxRequestedLayerId + 1), so a call with
    // a single L0-pinned viewer collapses the sender to L0.
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<int> MaxRequestedLayerId(Session session, StreamId streamId, CancellationToken cancellationToken);

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
