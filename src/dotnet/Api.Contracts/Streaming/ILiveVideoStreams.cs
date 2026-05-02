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

    [RpcMethod(RemoteExecutionMode = RpcRemoteExecutionMode.AwaitForConnection | RpcRemoteExecutionMode.AllowReconnect)]
    Task PushStream(
        Session session,
        string chatId,
        double clientStartOffset,
        VideoFormat format,
        RpcStream<VideoFrame> frameStream,
        StreamKind streamKind,
        CancellationToken cancellationToken);

    Task<RpcNoWait> RequestKeyFrame(Session session, string streamId, CancellationToken cancellationToken);

    Task<RpcNoWait> ChangeRecordingQuality(
        Session session,
        RecordingQualityState? state,
        RecordingQualityInfo? info,
        CancellationToken cancellationToken);

    Task<RpcNoWait> ChangePlaybackQuality(
        Session session,
        ApiMap<string, ReceiveQuality>? requestedQuality,
        PlaybackQualityInfo? info,
        CancellationToken cancellationToken);
}
