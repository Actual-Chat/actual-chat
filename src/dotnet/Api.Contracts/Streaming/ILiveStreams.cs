using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

/// <summary>
/// RPC service for multiplexed real-time audio streaming.
/// Replaces multiple GetAudio calls with a single multiplexed stream.
/// </summary>
public interface ILiveStreams : IComputeService
{
    [ComputeMethod]
    Task<ApiArray<LiveStreamInfo>> ListActiveStreams(
        Session session, ChatId chatId, CancellationToken cancellationToken);

    Task<RpcStream<LiveStreamItem>> GetLiveStream(
        Session session, ChatId chatId, LiveStreamSettings settings, CancellationToken cancellationToken);
    Task ChangeSettings(
        Session session, ChatId chatId, LiveStreamSettings settings, CancellationToken cancellationToken);
}
