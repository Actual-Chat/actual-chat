using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public interface ILiveVideoStreams : IComputeService
{
    Task<RpcStream<VideoFrame>?> GetStream(
        Session session,
        StreamId streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken);

    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<ApiArray<VideoStreamInfo>> List(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<int> GetMemberCount(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<ApiArray<string>> GetSupportedCodecs(Session session, ChatId chatId, CancellationToken cancellationToken);

    // Remains as the propagation path for RequestKeyFrame: the publisher
    // observes IsKeyFrameRequested = true and forces the next frame to be a KF.
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<VideoQualityPreset> GetQualityPreset(Session session, StreamId streamId, CancellationToken cancellationToken);

    Task RegisterMember(
        Session session, ChatId chatId, ApiArray<string> supportedDecoderCodecs, CancellationToken cancellationToken);
    Task UnregisterMember(
        Session session, ChatId chatId, CancellationToken cancellationToken);

    // `clientStartOffset` is the legacy RPC name; the value is sourceStartOffsetSeconds on the server-synced clock.
    [RpcMethod(RemoteExecutionMode = RpcRemoteExecutionMode.AwaitForConnection | RpcRemoteExecutionMode.AllowReconnect)]
    Task PushStream(
        Session session,
        string chatId,
        double clientStartOffset,
        VideoFormat format,
        RpcStream<VideoFrame> frameStream,
        StreamKind streamKind,
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
