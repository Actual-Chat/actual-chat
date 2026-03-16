using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

/// <summary>
/// RPC service for streaming historical audio from a chat as a multiplexed stream.
/// The server reads chat entries, downloads their audio blobs, and streams them
/// in the same <see cref="LiveStreamItem"/> format used by <see cref="ILiveStreams"/>.
/// </summary>
public interface IReplayStreams : IRpcService
{
    Task<RpcStream<LiveStreamItem>> GetReplayStream(
        Session session, ChatId chatId, Moment startAt, TimeSpan offset, CancellationToken cancellationToken);
}
