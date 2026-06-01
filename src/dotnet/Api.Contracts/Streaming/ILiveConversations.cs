using ActualChat.Live;

namespace ActualChat.Streaming;

/// <summary>
/// Public facade for live-conversation activity in a chat: the in-progress block and join/leave.
/// Aggregates <see cref="ILiveAudioStreams"/> and <see cref="ILiveVideoStreams"/> at the backend.
/// </summary>
public interface ILiveConversations : IComputeService
{
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<LiveConversation?> Get(Session session, ChatId chatId, CancellationToken cancellationToken);

    Task SetParticipation(
        Session session,
        ChatId chatId,
        ParticipationKind kind,
        bool isActive,
        CancellationToken cancellationToken);
}
