using ActualChat.Attributes;
using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

[BackendService(nameof(HostRole.LiveBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.LiveBackend))]
public interface ILiveSessionsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    [LegacyName("GetState_Hidden", "2.21.9999")]
    Task<LiveSessionState?> GetState(ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ConversationId?> GetOpenLiveConversationId(ChatId chatId, CancellationToken cancellationToken);
    // The open conversation, null until the session starts. It drops GetState's churn - see
    // LiveSessionsBackend.GetConsolidatedOpenLiveConversation. An open conversation owns [StartLid, +inf).
    [ComputeMethod]
    Task<LiveConversation?> GetOpenLiveConversation(ChatId chatId, CancellationToken cancellationToken);
    // The open conversation's id first, then the closed ones, newest first. A closed one is listed until the
    // saved conversation covering it appears, or it expires.
    [ComputeMethod]
    Task<ApiArray<ConversationId>> ListLiveConversations(ChatId chatId, CancellationToken cancellationToken);
    // A closed conversation never changes; the open one is served here too, but GetOpenLiveConversation is the one to follow.
    // Not named Get: RPC methods are told apart by name and argument count, and Get(ChatId) has both.
    [ComputeMethod]
    [LegacyName("GetLiveConversation_Hidden", "2.21.9999")]
    Task<LiveConversation?> GetLiveConversation(ConversationId conversationId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<LiveSession?> Get(ChatId chatId, CancellationToken cancellationToken);
    // These two consolidate as well - see LiveSessionsBackend.GetConsolidatedParticipants.
    [ComputeMethod]
    Task<ApiArray<AuthorId>> ListParticipants(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<bool> HasRecorder(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<CallState?> GetCallState(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<LiveCall?> GetCall(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<CallInvite>> ListInvites(ChatId chatId, CancellationToken cancellationToken);

    // hasText sits where a caller up to 2.21 passed the chat's summarization setting, so the call keeps its
    // wire identity; what that caller means by it is what the session reported as its transcript then.
    Task OnStreamRegistered(
        ChatId chatId,
        AuthorId authorId,
        long? entryLid,
        bool hasText,
        bool hasVoice,
        CancellationToken cancellationToken);
    Task SetParticipation(
        ChatId chatId,
        AuthorId authorId,
        ParticipationKind kind,
        bool isActive,
        CancellationToken cancellationToken);
    // Returns the rules it replaced, null if there's no session: with every change made under one lock,
    // of two identical requests racing each other only one gets back rules that differ from its own
    Task<SessionRules?> SetRules(ChatId chatId, SessionRules rules, CancellationToken cancellationToken);
    Task MutePeer(ChatId chatId, AuthorId targetAuthorId, bool muted, CancellationToken cancellationToken);
    Task MuteAll(
        ChatId chatId,
        ApiArray<AuthorId> exceptAuthorIds,
        bool muted,
        CancellationToken cancellationToken);
    Task SetHost(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);
    Task SetHandRaised(ChatId chatId, AuthorId authorId, bool isRaised, CancellationToken cancellationToken);
    Task LowerAllHands(ChatId chatId, CancellationToken cancellationToken);
    Task UpdateLiveConversationSummary(
        ChatId chatId,
        LiveConversationSummary summary,
        CancellationToken cancellationToken);
    Task SetLiveConversationContextStart(ChatId chatId, long contextStartLid, CancellationToken cancellationToken);
    Task FinalizeSession(ChatId chatId, CancellationToken cancellationToken);

    // Voice-call ring lifecycle (StartCall invitees empty = every other chat member).
    // callId makes the request a no-op (a refusal, for AcceptCall) once the chat isn't in that call.
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
        CallId callId,
        CancellationToken cancellationToken);
    // Callee methods
    Task AcceptCall(
        ChatId chatId,
        AuthorId inviteeAuthorId,
        string? sessionHash,
        string? clientId,
        CallId callId,
        CancellationToken cancellationToken);
    Task DeclineCall(
        ChatId chatId,
        AuthorId inviteeAuthorId,
        CallId callId,
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

    // Added with the live-conversation rework for 2.21 and older callers; GetState and GetLiveConversation
    // keep their wire names there, so the new ones step aside as _Hidden.
    [ComputeMethod]
    [LegacyName(nameof(GetState), "2.21.9999")]
    [Obsolete("2026.10: Use GetState - this one serves the 2.21 LiveSessionState layout to older callers.")]
    Task<LegacyLiveSessionState?> LegacyGetState(ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    [LegacyName("GetVisibleStartLid", "2.21.9999")]
    [Obsolete("2026.10: Use GetOpenLiveConversationId - this one answers 2.21 and older callers.")]
    Task<long?> LegacyGetVisibleStartLid(ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    [LegacyName(nameof(GetLiveConversation), "2.21.9999")]
    [Obsolete("2026.10: Use GetOpenLiveConversation - this one answers 2.21 and older callers.")]
    Task<Conversation?> LegacyGetLiveConversation(ChatId chatId, CancellationToken cancellationToken);

    // Lays out like the old LiveSessionSummary, but SummaryEndLid arrives as its inclusive EndEntryLid
    [LegacyName("UpdateSummary", "2.21.9999")]
    [Obsolete("2026.10: Use UpdateLiveConversationSummary - this one takes the 2.21 inclusive summary end.")]
    Task LegacyUpdateSummary(
        ChatId chatId,
        LiveConversationSummary summary,
        CancellationToken cancellationToken);

    [LegacyName("SetContextStart", "2.21.9999")]
    [Obsolete("2026.10: Use SetLiveConversationContextStart - this one answers 2.21 and older callers.")]
    Task LegacySetContextStart(ChatId chatId, long contextStartLid, CancellationToken cancellationToken);
}
