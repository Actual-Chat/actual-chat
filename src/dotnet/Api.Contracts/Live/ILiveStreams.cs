using ActualLab.Rpc;

namespace ActualChat.Live;

/// <summary>
/// RPC service for multiplexed real-time audio streaming.
/// Replaces multiple GetAudio calls with a single multiplexed stream.
/// </summary>
public interface ILiveStreams : IRpcService
{
    Task<RpcStream<LiveStreamItem>> GetLiveStream(
        Session session,
        ChatId chatId,
        LiveStreamSettings settings,
        CancellationToken cancellationToken);

    Task ChangeSettings(
        Session session,
        ChatId chatId,
        LiveStreamSettings settings,
        CancellationToken cancellationToken);
}
