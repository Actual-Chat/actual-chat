using ActualChat.Comparison;
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
    // The signal behind the idle stop-listening / stop-recording timers. Deliberately unrelated to transcription.
    // Consolidated: a stream list change that leaves the bool alone must not be pushed to every listener.
    [ComputeMethod(ConsolidationDelay = 0.5)]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<bool> HasActivity(Session session, ChatId chatId, CancellationToken cancellationToken);
    // Who is speaking right now (VAD-gated, audio only). Consolidated because ILiveAudioStreams.List
    // rebuilds its array per register/unregister while the author set behind it rarely moves;
    // ApiArray compares that array by reference, so the comparer isn't optional.
    [ComputeMethod(ConsolidationDelay = 0.5, ConsolidationComparer = typeof(ApiArrayComparer<AuthorId>))]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<ApiArray<AuthorId>> GetAudioStreamingAuthorIds(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);
    // The "is this a real conversation" signal where transcription is on: stream activity alone
    // can't tell speech from noise that trips VAD. Same reference-equality caveat as ApiArray above.
    [ComputeMethod(ConsolidationDelay = 1, ConsolidationComparer = typeof(ApiMapComparer<AuthorId, int>))]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<ApiMap<AuthorId, int>> GetTranscribedTextLengths(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);
    // Consolidated server-side, so a GetCallState or chat-rules change that leaves the status alone
    // isn't pushed to the caller. Zero delay - this is a ring/accept path.
    [ComputeMethod(ConsolidationDelay = 0)]
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
    Task DismissCallStatus(Session session, ChatId chatId, CancellationToken cancellationToken);
    // Callee methods
    Task AcceptCall(Session session, ChatId chatId, CancellationToken cancellationToken);
    Task DeclineCall(Session session, ChatId chatId, CancellationToken cancellationToken);
    // Either party hangs up an answered call
    Task LeaveCall(Session session, ChatId chatId, CancellationToken cancellationToken);
}
