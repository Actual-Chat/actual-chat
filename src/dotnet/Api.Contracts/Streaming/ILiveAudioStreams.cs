using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

/// <summary>
///     RPC service for multiplexed real-time and replay audio streaming.
/// </summary>
public interface ILiveAudioStreams : IComputeService
{
    [ComputeMethod]
    Task<ApiArray<LiveStreamInfo>> List(Session session, ChatId chatId, CancellationToken cancellationToken);

    Task<RpcStream<LiveStreamItem>> GetStream(
        Session session,
        ChatId chatId,
        LiveStreamSettings settings,
        CancellationToken cancellationToken);

    Task ChangeSettings(
        Session session,
        ChatId chatId,
        LiveStreamSettings settings,
        CancellationToken cancellationToken);

    Task<RpcStream<LiveStreamItem>> GetReplayStream(
        Session session,
        ChatId chatId,
        Moment startAt,
        TimeSpan rewindOffset,
        double speed,
        CancellationToken cancellationToken);
}
