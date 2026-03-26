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

    [ComputeMethod]
    Task<ApiArray<VideoStreamInfo>> List(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<int> GetMemberCount(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ApiArray<string>> GetSupportedCodecs(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<VideoQualityPreset> GetQualityPreset(Session session, StreamId streamId, CancellationToken cancellationToken);

    Task RegisterMember(
        Session session,
        ChatId chatId,
        ApiArray<string> supportedDecoderCodecs,
        CancellationToken cancellationToken);

    Task UnregisterMember(Session session, ChatId chatId, CancellationToken cancellationToken);
}
