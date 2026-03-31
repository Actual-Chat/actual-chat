using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public interface ILiveVideoStreams : IComputeService
{
    [LegacyName("GetVideo", "2.6.9999")]
    Task<RpcStream<VideoFrame>?> GetStream(
        Session session,
        StreamId streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken);

    [ComputeMethod]
    [LegacyName("ListActiveStreams", "2.6.9999")]
    Task<ApiArray<VideoStreamInfo>> List(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    [LegacyName("GetVideoStreamMemberCount", "2.6.9999")]
    Task<int> GetMemberCount(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    [LegacyName("GetSupportedDecoderCodecs", "2.6.9999")]
    Task<ApiArray<string>> GetSupportedCodecs(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<VideoQualityPreset> GetQualityPreset(Session session, StreamId streamId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<bool> HasActiveScreencast(Session session, ChatId chatId, CancellationToken cancellationToken);

    [LegacyName("RegisterVideoStreamMember", "2.6.9999")]
    Task RegisterMember(
        Session session,
        ChatId chatId,
        ApiArray<string> supportedDecoderCodecs,
        CancellationToken cancellationToken);

    [LegacyName("UnregisterVideoStreamMember", "2.6.9999")]
    Task UnregisterMember(Session session, ChatId chatId, CancellationToken cancellationToken);

    // Legacy v2.6 compatibility methods

    [ComputeMethod]
    [Obsolete("2025.03: Use List and extract AuthorIds client-side")]
    [LegacyName("GetVideoStreamingAuthorIds", "2.6.9999")]
    Task<ApiArray<AuthorId>> LegacyGetVideoStreamingAuthorIds(Session session, ChatId chatId, CancellationToken cancellationToken);

    [RpcMethod(LocalExecutionMode = RpcLocalExecutionMode.Unconstrained)]
    [Obsolete("2025.03: Use GetSupportedCodecs instead")]
    [LegacyName("ObserveSupportedDecoderCodecs", "2.6.9999")]
    Task<RpcStream<ApiArray<string>>> LegacyObserveSupportedDecoderCodecs(Session session, ChatId chatId, CancellationToken cancellationToken);

    [Obsolete("2025.03: Use GetQualityPreset instead")]
    [LegacyName("ObserveStreamQualityRequests", "2.6.9999")]
    Task<RpcStream<VideoQualityPreset>> LegacyObserveStreamQualityRequests(Session session, StreamId streamId, CancellationToken cancellationToken);
}
