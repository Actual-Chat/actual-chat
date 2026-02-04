using ActualLab.Rpc;

namespace ActualChat.Live;

/// <summary>
/// RPC service for multiplexed real-time audio streaming.
/// Replaces multiple GetAudio calls with a single multiplexed stream.
/// </summary>
public interface ILiveHub : IRpcService
{
    Task<RpcStream<LiveItem>> GetStream(
        Session session,
        ChatId chatId,
        LiveStreamingSettings settings,
        CancellationToken cancellationToken);

    Task ChangeSettings(
        Session session,
        ChatId chatId,
        LiveStreamingSettings settings,
        CancellationToken cancellationToken);
}
