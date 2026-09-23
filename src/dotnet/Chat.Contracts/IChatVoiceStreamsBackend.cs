using ActualChat.Attributes;
using ActualChat.Sharding;
using ActualLab.Rpc;

namespace ActualChat.Chat;

// Distributed rather than the assembly's Server default: the lease holds an open audio and
// transcript stream in one node's memory, and StreamId's NodeRef routes later calls back there.

/// <summary>
/// Backend for voice entries pushed call by call, for producers that cannot hold an
/// <see cref="RpcStream{T}"/> open. <see cref="ILiveAudioStreams"/> is the stream-capable path.
/// </summary>
[BackendService(nameof(HostRole.ChatBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(ShardScheme.ChatBackend))]
public interface IChatVoiceStreamsBackend : IComputeService, IBackendService
{
    Task<ChatVoiceStream> Start(
        ChatId chatId,
        Session session,
        UserId userId,
        long? repliedEntryLid,
        Language? language,
        CancellationToken cancellationToken);

    Task<ChatVoiceStream> Append(
        StreamId streamId,
        UserId userId,
        int textOffset,
        string? text,
        byte[]? audio,
        double? audioOffset,
        CancellationToken cancellationToken);

    Task<ChatVoiceStream> Finish(StreamId streamId, UserId userId, CancellationToken cancellationToken);
}
