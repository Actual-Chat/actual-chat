using ActualChat.Live;

namespace ActualChat.Streaming;

/// <summary>
/// Public facade for live-conversation activity in a chat: the in-progress block and join/leave.
/// Aggregates <see cref="ILiveAudioStreams"/> and <see cref="ILiveVideoStreams"/> at the backend.
/// </summary>
public interface ILiveSessions : IComputeService
{
    [ComputeMethod]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<LiveSessionState?> Get(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<LiveSession?> GetLiveSession(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<bool> HasRecorder(Session session, ChatId chatId, CancellationToken cancellationToken);

    Task SetParticipation(
        Session session,
        ChatId chatId,
        ParticipationKind kind,
        bool isActive,
        CancellationToken cancellationToken);
    Task SetMicMuted(Session session, ChatId chatId, bool micMuted, CancellationToken cancellationToken);
    Task SetRules(Session session, ChatId chatId, SessionRules rules, CancellationToken cancellationToken);
    Task MutePeer(Session session, ChatId chatId, AuthorId targetAuthorId, bool muted, CancellationToken cancellationToken);
    Task MuteAll(Session session, ChatId chatId, bool muted, CancellationToken cancellationToken);
}
