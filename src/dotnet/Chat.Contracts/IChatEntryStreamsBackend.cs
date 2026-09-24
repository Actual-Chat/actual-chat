using ActualChat.Attributes;
using ActualChat.Sharding;
using ActualLab.Rpc;

namespace ActualChat.Chat;

// Distributed rather than the assembly's Server default: a lease is the open producer side of a
// live stream, so it lives in one node's memory, and StreamId's NodeRef routes every later call
// on it back to that node.

/// <summary>
/// Backend for entry streams driven call-by-call, for producers that cannot hold an
/// <see cref="ActualLab.Rpc.RpcStream{T}"/> open. <see cref="IChats.StreamEntry"/> is the
/// stream-capable path to the same thing.
/// </summary>
[BackendService(nameof(HostRole.ChatBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(ShardScheme.ChatBackend))]
public interface IChatEntryStreamsBackend : IComputeService, IBackendService
{
    Task<ChatEntryStream> Start(
        ChatId chatId,
        AuthorId authorId,
        UserId userId,
        long? localId,
        bool? isViaApi,
        Language? language,
        CancellationToken cancellationToken);

    Task<ChatEntryStream> Append(
        StreamId streamId,
        UserId userId,
        int offset,
        string text,
        CancellationToken cancellationToken);

    Task<ChatEntryStream> Finish(StreamId streamId, UserId userId, CancellationToken cancellationToken);
}
