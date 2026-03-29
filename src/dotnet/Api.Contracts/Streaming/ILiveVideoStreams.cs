using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public interface ILiveVideoStreams : IComputeService
{
    [LegacyName("GetVideo")]
    Task<RpcStream<VideoFrame>?> GetStream(
        Session session,
        StreamId streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken);

    [ComputeMethod]
    [LegacyName("ListActiveStreams")]
    Task<ApiArray<VideoStreamInfo>> List(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    [LegacyName("GetVideoStreamMemberCount")]
    Task<int> GetMemberCount(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    [LegacyName("GetSupportedDecoderCodecs")]
    Task<ApiArray<string>> GetSupportedCodecs(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<VideoQualityPreset> GetQualityPreset(Session session, StreamId streamId, CancellationToken cancellationToken);

    [LegacyName("RegisterVideoStreamMember")]
    Task RegisterMember(
        Session session,
        ChatId chatId,
        ApiArray<string> supportedDecoderCodecs,
        CancellationToken cancellationToken);

    [LegacyName("UnregisterVideoStreamMember")]
    Task UnregisterMember(Session session, ChatId chatId, CancellationToken cancellationToken);

    // Legacy v2.6 compatibility methods

    [ComputeMethod]
    [Obsolete("2025.03: Use List and extract AuthorIds client-side")]
    Task<ApiArray<AuthorId>> GetVideoStreamingAuthorIds(Session session, ChatId chatId, CancellationToken cancellationToken);

    [RpcMethod(LocalExecutionMode = RpcLocalExecutionMode.Unconstrained)]
    [Obsolete("2025.03: Use GetSupportedCodecs instead")]
    Task<RpcStream<ApiArray<string>>> ObserveSupportedDecoderCodecs(Session session, ChatId chatId, CancellationToken cancellationToken);

    [Obsolete("2025.03: Use GetQualityPreset instead")]
    Task<RpcStream<VideoQualityPreset>> ObserveStreamQualityRequests(Session session, StreamId streamId, CancellationToken cancellationToken);
}
