using ActualChat.Comparison;
using ActualChat.Live;

namespace ActualChat.Streaming;

/// <summary>
/// Public facade for live-conversation activity in a chat: the in-progress block and join/leave.
/// Aggregates <see cref="ILiveAudioStreams"/> and <see cref="ILiveVideoStreams"/> at the backend.
/// </summary>
public interface ILiveSessions : IComputeService
{
    // ReturnDefault, not NoCache: live state must never come off the disk cache, but a NoCache call
    // parks until reconnect - and "no session" is both the right offline answer and a non-blocking one.
    [ComputeMethod]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.ReturnDefault)]
    Task<LiveSessionState?> GetState(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.ReturnDefault)]
    Task<LiveSession?> Get(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.ReturnDefault)]
    Task<bool> HasRecorder(Session session, ChatId chatId, CancellationToken cancellationToken);
    // The signal behind the idle stop-listening / stop-recording timers. Deliberately unrelated to transcription.
    // Consolidated: a stream list change that leaves the bool alone must not be pushed to every listener.
    [ComputeMethod(ConsolidationDelay = 0.5)]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.ReturnDefault)]
    Task<bool> HasActivity(Session session, ChatId chatId, CancellationToken cancellationToken);
    // Who is speaking right now (VAD-gated, audio only). Consolidated because ILiveAudioStreams.List
    // rebuilds its array per register/unregister while the author set behind it rarely moves;
    // ApiArray compares that array by reference, so the comparer isn't optional.
    [ComputeMethod(ConsolidationDelay = 0.5, ConsolidationComparer = typeof(ApiArrayComparer<AuthorId>))]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.ReturnDefault)]
    Task<ApiArray<AuthorId>> GetAudioStreamingAuthorIds(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);
    // The "is anyone actually talking" signal: stream activity alone can't tell speech from noise
    // that trips VAD. Null means there is no live conversation at all - no session, or one that
    // never latched to 2+ authors. Re-measured on a fixed period rather than on chat traffic, so
    // it also paces its consumers; see LiveSessions.GetConversationStats.
    [ComputeMethod]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.ReturnDefault)]
    Task<ConversationStats?> GetConversationStats(Session session, ChatId chatId, CancellationToken cancellationToken);
    // Obsolete: the outgoing-call banner it fed is gone - the caller's own status now follows the
    // call slot (see CallUI.Apply / ISystemCallUI). The result type must stay the bare CallStatus
    // enum v2.20 clients read it as: they subscribe to this compute method, and a nil where they
    // expect an enum faults their state and takes the whole chat page down with it. Only None is
    // ever returned, and None is 0 in both numberings, so the renumbering never reaches the wire.
    [Obsolete("2026.09: Old clients only. Always None. Remove once no installed app version calls it.")]
    [ComputeMethod]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.ReturnDefault)]
    Task<CallStatus> GetCallStatus(Session session, ChatId chatId, CancellationToken cancellationToken);
    // The one call this user is in, whichever device they're on and whoever started it. Null means
    // free - which is also what a disconnected client reads, so it is "unknown" until it reconnects.
    [ComputeMethod(ConsolidationDelay = 0)]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.ReturnDefault)]
    Task<UserCall?> GetMyCall(Session session, CancellationToken cancellationToken);

    Task SetParticipation(
        Session session,
        ChatId chatId,
        ParticipationKind kind,
        bool isActive,
        CancellationToken cancellationToken);
    Task SetRules(Session session, ChatId chatId, SessionRules rules, CancellationToken cancellationToken);
    Task MutePeer(
        Session session,
        ChatId chatId,
        AuthorId targetAuthorId,
        bool muted,
        CancellationToken cancellationToken);
    Task MuteAll(Session session, ChatId chatId, bool muted, CancellationToken cancellationToken);
    Task SetHost(Session session, ChatId chatId, AuthorId targetAuthorId, CancellationToken cancellationToken);

    // Voice-call ring lifecycle (StartCall invitees empty = every other chat member).
    // Caller methods
    Task StartCall(
        Session session,
        ChatId chatId,
        ApiArray<AuthorId> invitees,
        bool hasVideo,
        CancellationToken cancellationToken);
    Task CancelCall(Session session, ChatId chatId, CancellationToken cancellationToken);
    // Obsolete: there is no caller-visible status left to dismiss - see GetCallStatus. Kept as a
    // throwing stub rather than removed, in case a stale client build still calls it.
    [Obsolete("2026.09: Old MAUI clients only. Throws. Remove once no installed app version calls it.")]
    Task DismissCallStatus(Session session, ChatId chatId, CancellationToken cancellationToken);
    // Callee methods
    Task AcceptCall(Session session, ChatId chatId, CancellationToken cancellationToken);
    Task DeclineCall(Session session, ChatId chatId, CancellationToken cancellationToken);
    Task ConfirmRing(Session session, ChatId chatId, RingAck ack, CancellationToken cancellationToken);
    // Obsolete: hanging up now goes through SetParticipation (see ChatAudioUI/LiveSessionUI). Kept as a
    // throwing stub rather than removed, in case a stale client build still calls it.
    Task LeaveCall(Session session, ChatId chatId, CancellationToken cancellationToken);
}
