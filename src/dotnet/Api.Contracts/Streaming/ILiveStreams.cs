using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

/// <summary>
/// RPC service for multiplexed real-time and historical audio streaming.
/// </summary>
public interface ILiveStreams : IComputeService
{
    [ComputeMethod]
    Task<ApiArray<LiveStreamInfo>> ListActiveStreams(
        Session session, ChatId chatId, CancellationToken cancellationToken);

    Task<RpcStream<LiveStreamItem>> GetLiveStream(
        Session session, ChatId chatId, LiveStreamSettings settings, CancellationToken cancellationToken);
    Task ChangeLiveStreamSettings(
        Session session, ChatId chatId, LiveStreamSettings settings, CancellationToken cancellationToken);

    Task<RpcStream<LiveStreamItem>> GetReplayStream(
        Session session, ChatId chatId, Moment startAt, TimeSpan offset, CancellationToken cancellationToken);
}
