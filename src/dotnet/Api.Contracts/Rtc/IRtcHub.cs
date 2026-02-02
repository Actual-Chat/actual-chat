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
        RtcStreamingSettings settings,
        CancellationToken cancellationToken);

    Task ChangeSettings(
        Session session,
        ChatId chatId,
        RtcStreamingSettings settings,
        CancellationToken cancellationToken);
}
