using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

/// <summary>
///     RPC service for multiplexed real-time and replay audio streaming.
/// </summary>
[LegacyName("ILiveStreams", "2.6.9999")]
public interface ILiveAudioStreams : IComputeService
{
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    [LegacyName("ListActiveStreams", "2.6.9999")]
    Task<ApiArray<LiveStreamInfo>> List(Session session, ChatId chatId, CancellationToken cancellationToken);

    [LegacyName("GetLiveStream", "2.6.9999")]
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
