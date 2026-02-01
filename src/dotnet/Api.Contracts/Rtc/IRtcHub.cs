using ActualChat.Rtc;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

/// <summary>
/// RPC service for multiplexed real-time audio streaming.
/// Replaces multiple GetAudio calls with a single multiplexed stream.
/// </summary>
public interface IRtcHub : IRpcService
{
    Task<RpcStream<RtcItem>> GetStream(
        Session session,
        ChatId chatId,
        RtcStreamConfig config,
        CancellationToken cancellationToken);

    Task UpdateConfig(
        Session session,
        ChatId chatId,
        RtcStreamConfig config,
        CancellationToken cancellationToken);
}
