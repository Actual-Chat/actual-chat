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
    Task<LiveSessionState?> GetState(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<LiveSession?> Get(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<bool> HasRecorder(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<CallStatus> GetCallStatus(Session session, ChatId chatId, CancellationToken cancellationToken);

    Task SetParticipation(
        Session session,
        ChatId chatId,
        ParticipationKind kind,
        bool isActive,
        CancellationToken cancellationToken);
    Task SetRules(Session session, ChatId chatId, SessionRules rules, CancellationToken cancellationToken);
    Task MutePeer(Session session, ChatId chatId, AuthorId targetAuthorId, bool muted, CancellationToken cancellationToken);
    Task MuteAll(Session session, ChatId chatId, bool muted, CancellationToken cancellationToken);

    // Voice-call ring lifecycle (StartCall invitees empty = every other chat member).
    // Caller methods
    Task StartCall(
        Session session,
        ChatId chatId,
        ApiArray<AuthorId> invitees,
        bool hasVideo,
        CancellationToken cancellationToken);
    Task CancelCall(Session session, ChatId chatId, CancellationToken cancellationToken);
    Task DismissCallStatus(Session session, ChatId chatId, CancellationToken cancellationToken);
    // Callee methods
    Task AcceptCall(Session session, ChatId chatId, CancellationToken cancellationToken);
    Task DeclineCall(Session session, ChatId chatId, CancellationToken cancellationToken);
    // Either party hangs up an answered call
    Task LeaveCall(Session session, ChatId chatId, CancellationToken cancellationToken);
}
