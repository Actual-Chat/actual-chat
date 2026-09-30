using ActualChat.Attributes;
using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

[BackendService(nameof(HostRole.LiveBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.LiveBackend))]
public interface ILiveSessionsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<LiveSessionState?> GetState(ChatId chatId, CancellationToken cancellationToken);

    // The two below drop GetState's churn - see LiveSessionsBackend.GetConsolidatedVisibleStartLid.
    // A latched session owns [lid, +inf) - it always runs to the chat's tail, so there is no end to return.
    [ComputeMethod]
    Task<long?> GetVisibleStartLid(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Conversation?> GetLiveConversation(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<LiveSession?> Get(ChatId chatId, CancellationToken cancellationToken);
    // These two consolidate as well - see LiveSessionsBackend.GetConsolidatedParticipants.
    [ComputeMethod]
    Task<ApiArray<AuthorId>> ListParticipants(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<bool> HasRecorder(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<CallState?> GetCallState(ChatId chatId, CancellationToken cancellationToken);

    Task OnStreamRegistered(
        ChatId chatId,
        AuthorId authorId,
        long? entryLid,
        bool transcriptionOn,
        bool hasVoice,
        CancellationToken cancellationToken);
    Task SetParticipation(
        ChatId chatId,
        AuthorId authorId,
        ParticipationKind kind,
        bool isActive,
        CancellationToken cancellationToken);
    Task SetRules(ChatId chatId, SessionRules rules, CancellationToken cancellationToken);
    Task MutePeer(ChatId chatId, AuthorId targetAuthorId, bool muted, CancellationToken cancellationToken);
    Task MuteAll(
        ChatId chatId,
        ApiArray<AuthorId> exceptAuthorIds,
        bool muted,
        CancellationToken cancellationToken);
    Task SetHost(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);
    Task SetHandRaised(ChatId chatId, AuthorId authorId, bool isRaised, CancellationToken cancellationToken);
    Task LowerAllHands(ChatId chatId, CancellationToken cancellationToken);
    Task UpdateSummary(
        ChatId chatId,
        LiveSessionSummary summary,
        CancellationToken cancellationToken);
    Task SetContextStart(ChatId chatId, long contextStartLid, CancellationToken cancellationToken);
    Task FinalizeSession(ChatId chatId, CancellationToken cancellationToken);

    // Voice-call ring lifecycle (StartCall invitees empty = every other chat member).
    // A null callId means "whatever call the chat is in" - what a client asks for before it is told
    // the id; a given one makes the request a no-op (a refusal, for AcceptCall) once the chat is in
    // another call.
    // Caller methods
    Task<CallId> StartCall(
        ChatId chatId,
        AuthorId callerAuthorId,
        ApiArray<AuthorId> invitees,
        bool hasVideo,
        string? sessionHash,
        string? clientId,
        CancellationToken cancellationToken);
    Task CancelCall(
        ChatId chatId,
        AuthorId callerAuthorId,
        CallId? callId,
        CancellationToken cancellationToken);
    // Callee methods
    Task AcceptCall(
        ChatId chatId,
        AuthorId inviteeAuthorId,
        string? sessionHash,
        string? clientId,
        CallId? callId,
        CancellationToken cancellationToken);
    Task DeclineCall(
        ChatId chatId,
        AuthorId inviteeAuthorId,
        CallId? callId,
        CancellationToken cancellationToken);
    Task ConfirmRing(ChatId chatId, AuthorId inviteeAuthorId, RingAck ack, CancellationToken cancellationToken);

    // Legacy methods

    [LegacyName(nameof(OnStreamRegistered), "2.15.9999")]
    Task LegacyOnStreamRegistered(
        ChatId chatId,
        AuthorId authorId,
        long? entryLid,
        bool transcriptionOn,
        CancellationToken cancellationToken);
}
