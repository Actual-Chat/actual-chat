using ActualChat.Comparison;
using ActualChat.Flows;
using ActualChat.Live;
using ActualChat.Redis;
using ActualLab.Locking;
using ActualLab.Redis;
using ActualLab.Versioning;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming;

/// <summary>
/// Backend for the single live conversation per chat: its in-progress summary block,
/// the participant registry, and open/close driven by live audio/video streams.
/// </summary>
public partial class LiveSessionsBackend : ShardedComputeServiceBase, ILiveSessionsBackend
{

    // Where a started conversation begins: at the entry of whoever spoke first (true), or at the end of the
    // chat when the second peer joined (false), which leaves what the first one said before that outside it.
    internal const bool MustStartFromFirstSpeech = true;
    private static readonly TimeSpan KeyTtl = TimeSpan.FromMinutes(6);
    // How long a closed conversation stays listed, unless the saved conversation covering it appears first
    private static readonly TimeSpan ClosedTtl = TimeSpan.FromHours(1);
    // How long the flow that writes a closed conversation to the saved ones waits after the close
    private static readonly TimeSpan ClosedPersistDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SelfHealDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ClosedRecheckDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ParticipantStaleness = TimeSpan.FromSeconds(90);
    // Safety net: if a closing transcription-on conversation isn't finalized by
    // LiveConversationSummaryFlow within this window (flow not scheduled when global
    // summarization is off, or it threw), the backend vanishes it and sends FINAL itself.
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(90);
    // Grace once nobody is recording/streaming before a phone-mode session winds down.
    private static readonly TimeSpan RecordingCloseGrace = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RingTimeout = Constants.Call.RingTimeout;
    private static readonly TimeSpan RingTtl = Constants.Call.RingTtl;
    // How long past RingTimeout a missed ring can still be answered. The callee's ring stops on time,
    // but an Answer tapped at its very end reaches us only after a cold start and the RPC connect.
    private static readonly TimeSpan AnswerGrace = TimeSpan.FromSeconds(10);
    // An unanswered call's record outlives its ring and the late-answer window, then lapses on its own.
    private static readonly TimeSpan RingingCallTtl = RingTtl + AnswerGrace;
    // How long an in-progress (Dialing/Connecting/Active) call state lingers with no observer before
    // its Redis key lapses; a terminal transition overwrites it sooner.
    private static readonly TimeSpan DialingStateTtl = TimeSpan.FromSeconds(60);
    // How long the caller keeps being shown a resolved call status (accepted / declined / no answer),
    // once the session itself is gone.
    private static readonly TimeSpan ResolvedStateTtl = TimeSpan.FromSeconds(30);
    // How long AcceptCall waits before checking that the invitee genuinely connected (see
    // EnforceCallConnectGrace) - short enough that a stalled connect surfaces fast, long enough to
    // cover the accept-flow reorder's round trip (client starts listening immediately on accept).
    private static readonly TimeSpan CallConnectGrace = TimeSpan.FromSeconds(5);
    // How long a call left with one party waits before closing (see EnforceCallLeaveGrace): a party whose
    // presence dips and returns within it keeps the call, a real hang-up still ends it within seconds.
    private static readonly TimeSpan CallLeaveGrace = TimeSpan.FromSeconds(2);
    // How long CallTailFlow's first pass waits: the realtime transcriber's own post-audio deadline is
    // how long the last utterance's entry can take to appear at all (its text settles later still).
    private static readonly TimeSpan CallTailDelay = Constants.Transcription.CompletionTimeout;
    // How long a call into an ongoing session waits for its parties' clients to stop their media before its
    // entry is written anyway: past it, a client is offline or stuck, and the entry must not wait on it.
    private static readonly TimeSpan CallStreamsEndTimeout = TimeSpan.FromSeconds(10);

    private readonly RedisScope<LiveSessionState> _redisStates;
    private readonly RedisMultiHashMap<LiveConversation> _redisClosedLiveConversations;
    private readonly RedisScope<LiveCall> _redisCalls;
    private readonly RedisScope<CallState> _redisCallStates;
    private readonly RedisMultiHashMap<ParticipationInfo> _redisParticipants;
    private readonly RedisMultiHashMap<CallInvite> _redisInvites;
    private readonly AsyncLockSet<ChatId> _changeLocks = new(LockReentryMode.CheckedFail);
    // Each claimed call's end until its entry is written; see CloseCallSession
    private readonly ConcurrentDictionary<ChatId, Task> _callEndTasks = new();
    private long _lastCallLocalId;

    private IAuthorsBackend AuthorsBackend { get; }
    private IChatsBackend ChatsBackend { get; }
    private IRolesBackend RolesBackend { get; }
    private ILiveAudioBackend LiveAudioBackend { get; }
    private ILiveVideoBackend LiveVideoBackend { get; }
    private VersionGenerator<long> VersionGenerator { get; }
    private ICallsBackend CallsBackend => field ??= Services.GetRequiredService<ICallsBackend>();
    private IConversationsBackend ConversationsBackend
        => field ??= Services.GetRequiredService<IConversationsBackend>();
    private ICommander Commander => field ??= Services.Commander();
    private FlowHub FlowHub => field ??= Services.FlowHub();

    public LiveSessionsBackend(IServiceProvider services)
        : base(services, ShardScheme.LiveBackend)
    {
        AuthorsBackend = services.GetRequiredService<IAuthorsBackend>();
        ChatsBackend = services.GetRequiredService<IChatsBackend>();
        RolesBackend = services.GetRequiredService<IRolesBackend>();
        LiveAudioBackend = services.GetRequiredService<ILiveAudioBackend>();
        LiveVideoBackend = services.GetRequiredService<ILiveVideoBackend>();
        VersionGenerator = services.GetRequiredService<VersionGenerator<long>>();

        var redisDb = services.GetRequiredService<RedisDb<StreamingContext>>();
        _redisStates = new RedisScope<LiveSessionState>(redisDb, "live-session:state-v2", Log) {
            DefaultTtl = KeyTtl,
        };
        _redisClosedLiveConversations = new RedisMultiHashMap<LiveConversation>(redisDb, "live-session:closed", Log) {
            DefaultFieldTtl = ClosedTtl,
            HashTtl = ClosedTtl,
        };
        _redisCalls = new RedisScope<LiveCall>(redisDb, "live-session:call", Log) {
            DefaultTtl = KeyTtl,
        };
        _redisCallStates = new RedisScope<CallState>(redisDb, "live-session:call-state", Log) {
            DefaultTtl = ResolvedStateTtl,
        };
        _redisParticipants = new RedisMultiHashMap<ParticipationInfo>(redisDb, "live-session:participants", Log) {
            HashTtl = KeyTtl,
        };
        _redisInvites = new RedisMultiHashMap<CallInvite>(redisDb, "live-session:invites", Log) {
            HashTtl = KeyTtl,
        };
    }

    // [ComputeMethod]
    public virtual async Task<LiveSessionState?> GetState(ChatId chatId, CancellationToken cancellationToken)
    {
        var redisReadAt = CpuTimestamp.Now;
        var state = await SafeGetState(chatId).ConfigureAwait(false);
        var redisReadMs = (long)redisReadAt.Elapsed.TotalMilliseconds;
        if (redisReadMs > 250)
            Log.LogWarning(
                "GetState: Redis read took {ReadMs}ms for chat #{ChatId}",
                redisReadMs, chatId);
        if (state is null)
            return null;

        // Transcription keeps the longer grace so LiveConversationSummaryFlow can finalize;
        // a phone-mode session winds down on the shorter recording grace.
        var grace = state.IsTranscriptionOn ? CloseTimeout : RecordingCloseGrace;
        if (state is { QuietSince: { } quietSince }
            && Clocks.SystemClock.Now - quietSince > grace) {
            _ = SelfClose(chatId);
            return null;
        }

        // Liveness is streaming-driven: once nobody is recording audio or video, begin the close grace.
        if (!state.IsQuiet && !await IsSessionLive(chatId).ConfigureAwait(false))
            _ = StartClosingGrace(chatId);

        // Required, not merely defensive: IsSessionLive reads raw Redis against a time-based staleness
        // cutoff, so nothing invalidates this on its own. The churn it creates is filtered before it can
        // reach the conversation metadata cache - see GetConsolidatedOpenLiveConversation.
        Computed.GetCurrent().InvalidateSafely(SelfHealDelay);
        return state;
    }

    // [ComputeMethod]
    public virtual Task<ConversationId?> GetOpenLiveConversationId(ChatId chatId, CancellationToken cancellationToken)
        => GetConsolidatedOpenLiveConversationId(chatId, cancellationToken);

    // [ComputeMethod]
    public virtual Task<LiveConversation?> GetOpenLiveConversation(ChatId chatId, CancellationToken cancellationToken)
        => GetConsolidatedOpenLiveConversation(chatId, cancellationToken);

    // [ComputeMethod]
    public virtual Task<ApiArray<ConversationId>> ListLiveConversations(
        ChatId chatId, CancellationToken cancellationToken)
        => GetConsolidatedLiveConversationList(chatId, cancellationToken);

    // [ComputeMethod]
    public virtual Task<LiveConversation?> GetLiveConversation(
        ConversationId conversationId, CancellationToken cancellationToken)
        => GetConsolidatedLiveConversation(conversationId, cancellationToken);

    // [ComputeMethod]
    public virtual async Task<LiveSession?> Get(ChatId chatId, CancellationToken cancellationToken)
    {
        // A call is shown from its first ring: with no session yet it is a Call with no conversation,
        // and placed into a session it overlays that session's own view until it ends.
        var state = await GetState(chatId, cancellationToken).ConfigureAwait(false);
        var call = await GetCall(chatId, cancellationToken).ConfigureAwait(false);
        if (state is { StartedAt: null })
            state = null;
        if (state is null && call is null)
            return null;

        var audio = await LiveAudioBackend.List(chatId, cancellationToken).ConfigureAwait(false);
        var video = await LiveVideoBackend.List(chatId, cancellationToken).ConfigureAwait(false);
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        var host = state is null
            ? call!.CallerId
            : state.HostAuthorId ?? state.AuthorIds[0];
        var cutoff = Clocks.SystemClock.Now - ParticipantStaleness;
        // Stands in as JoinedAt for stream-only members - they have no participation record to date the
        // join with. Never Clocks.Now: that makes every recompute a different LiveSession, so no
        // consumer can consolidate a no-op invalidation away.
        var startedAt = state?.StartedAt ?? call!.StartedAt;

        var byAuthor = new Dictionary<AuthorId, LiveSessionMember>();
        LiveSessionMember For(AuthorId a)
            => byAuthor.TryGetValue(a, out var m) ? m : new() { AuthorId = a, JoinedAt = startedAt };

        if (state is null)
            byAuthor[call!.CallerId] = For(call.CallerId);

        foreach (var s in audio)
            byAuthor[s.AuthorId] = For(s.AuthorId) with { IsMicOpen = true };
        foreach (var v in video) {
            var m = For(v.AuthorId);
            byAuthor[v.AuthorId] = m with {
                HasCamera = m.HasCamera || v.SourceKind == VideoSourceKind.Camera,
                HasScreenShare = m.HasScreenShare || v.SourceKind == VideoSourceKind.ScreenCast,
            };
        }
        foreach (var (authorIdValue, info) in participants) {
            if (info is null)
                continue;
            if (!AuthorId.TryParse(authorIdValue, out var authorId))
                continue;

            var m = For(authorId);
            var isRecorder = !info.IsPlaceholder && IsFreshRecorder(info, cutoff);
            var isListener = info.Kind == ParticipationKind.AudioListen && info.RegisteredAt >= cutoff;
            byAuthor[authorId] = m with {
                IsMicOpen = m.IsMicOpen || isRecorder,
                IsListening = m.IsListening || isListener,
                MicMuted = info.MicMuted,
                HandRaisedAt = info.HandRaisedAt,
                // RegisteredAt is a liveness stamp rewritten by every heartbeat - only the fallback
                // for participation records written before JoinedAt existed.
                JoinedAt = info.JoinedAt == default ? info.RegisteredAt : info.JoinedAt,
            };
        }
        // Owners and Moderators are grouped with the host (they can manage the call too).
        var ownerIds = (await RolesBackend
            .ListSystemRoleAuthorIds(ChatsBackend, chatId, SystemRole.Owner, cancellationToken)
            .ConfigureAwait(false))
            .ToHashSet();
        var moderatorIds = await RolesBackend
            .ListSystemRoleAuthorIds(ChatsBackend, chatId, SystemRole.Moderator, cancellationToken)
            .ConfigureAwait(false);
        ownerIds.UnionWith(moderatorIds);

        var members = byAuthor.Values
            .Select(m => m with {
                Group = m.AuthorId == host || ownerIds.Contains(m.AuthorId) ? MemberGroup.Host
                    : m.IsPresent ? MemberGroup.Other
                    : MemberGroup.Exited,
                // Only present members show a hand: a crashed client's record lingers for ParticipantStaleness
                HandRaisedAt = m.IsPresent ? m.HandRaisedAt : null,
            })
            // Without the tie-breaks the order is byAuthor's insertion order, i.e. Redis HGETALL
            // order - it reshuffles the list on every recompute. AuthorId breaks the JoinedAt tie
            // stream-only members share via the startedAt fallback.
            .OrderBy(m => (int)m.Group)
            .ThenBy(m => m.JoinedAt)
            .ThenBy(m => m.AuthorId.Value, StringComparer.Ordinal)
            .ToList();

        return new LiveSession {
            ChatId = chatId,
            Host = host,
            StartedAt = startedAt,
            Rules = state?.Rules ?? SessionRules.Default,
            Members = members,
            Conversation = state?.OpenConversation?.ToConversation(),
            TranscriptionOn = state?.IsTranscriptionOn ?? false,
            Version = Math.Max(state?.Version ?? 0, call?.Version ?? 0),
            Kind = call is null ? state!.Kind : LiveSessionKind.Call,
        };
    }

    // [ComputeMethod]
    public virtual Task<ApiArray<AuthorId>> ListParticipants(ChatId chatId, CancellationToken cancellationToken)
        => GetConsolidatedParticipants(chatId, cancellationToken);

    // [ComputeMethod]
    public virtual Task<bool> HasRecorder(ChatId chatId, CancellationToken cancellationToken)
        => GetConsolidatedHasRecorder(chatId, cancellationToken);

    // [ComputeMethod]
    public virtual async Task<CallState?> GetCallState(ChatId chatId, CancellationToken cancellationToken)
    {
        // Captured before the awaits below — see GetState.
        var computed = Computed.GetCurrent();
        // Depend on GetCall so CallState and the call invalidate together over RPC instead of drifting
        // independently - this used to race (9e0b87186c): Accepted could land before the call did.
        await GetCall(chatId, cancellationToken).ConfigureAwait(false);

        var callState = await SafeGetCallState(chatId).ConfigureAwait(false);
        if (callState is null)
            return null;

        // Nothing invalidates a Redis TTL expiry, so age the observed value out alongside the key.
        var expiresIn = callState.ChangedAt + CallStateTtl(callState.Status) - Clocks.SystemClock.Now;
        if (expiresIn <= TimeSpan.Zero)
            return null;

        computed.Invalidate(expiresIn);
        return callState;
    }

    // [ComputeMethod]
    public virtual async Task<LiveCall?> GetCall(ChatId chatId, CancellationToken cancellationToken)
    {
        // Not SafeGetCall: a failed read is no answer, while "no call" ends every claim on it - and the call
        // on the clients that run it.
        var call = await _redisCalls.Get(chatId.Value).ConfigureAwait(false);
        if (call is null)
            return null;

        // Rings lapse and parties drop without a write that would invalidate this, so while the call is
        // observed it re-checks itself; the ring timer and the record's own TTL cover the unobserved case.
        if (await HasStaleRinging(chatId).ConfigureAwait(false))
            _ = ExpireRings(chatId);
        else if (!call.IsAnswered && !await HasFreshRing(chatId).ConfigureAwait(false))
            _ = ExpireRings(chatId);
        if (call.IsAnswered) {
            _ = SyncCallParticipantActivity(chatId, call, CancellationToken.None);
            // A crashed client never reports leaving: this is what ends the call it was the other side of.
            if (!await IsCallAlive(chatId, call).ConfigureAwait(false))
                _ = EndCall(chatId, CallEndReason.Lost, mustRecheckParties: true, call.Id);
        }
        Computed.GetCurrent().InvalidateSafely(SelfHealDelay);
        return call;
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<CallInvite>> ListInvites(ChatId chatId, CancellationToken cancellationToken)
    {
        // Invites outlast their call until its teardown drops them; without the call they're nobody's.
        var call = await GetCall(chatId, cancellationToken).ConfigureAwait(false);
        if (call is null)
            return [];

        // Not SafeGetInvites, as in GetCall: a callee's claim missing its invite is ended.
        var invites = await _redisInvites.GetHashMap(chatId.Value).ConfigureAwait(false);
        return invites.Values.SkipNullItems().OrderBy(i => i.RingingAt).ToApiArray();
    }

    // Protected/internal methods

    [ComputeMethod(ConsolidationDelay = 0)]
    protected virtual async Task<ConversationId?> GetConsolidatedOpenLiveConversationId(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        // Protected on purpose: ConsolidationDelay applies only to methods computed locally, and an
        // RPC-visible one is served from a RemoteComputed on the caller's side instead.
        var state = await GetState(chatId, cancellationToken).ConfigureAwait(false);
        return state?.OpenConversation?.Id;
    }

    [ComputeMethod(ConsolidationDelay = 0)]
    protected virtual async Task<LiveConversation?> GetConsolidatedLiveConversation(
        ConversationId conversationId,
        CancellationToken cancellationToken)
    {
        var chatId = conversationId.ChatId;
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var liveConversation = await GetConsolidatedOpenLiveConversation(chatId, cancellationToken).ConfigureAwait(false);
        if (liveConversation?.Id == conversationId)
            return liveConversation;

        var closed = await GetClosedLiveConversation(chatId, conversationId).ConfigureAwait(false);
        if (closed is not null)
            // A closed conversation never changes, but it expires
            Computed.GetCurrent().Invalidate(ClosedRecheckDelay);
        return closed;
    }

    [ComputeMethod(ConsolidationDelay = 0, ConsolidationComparer = typeof(ApiArrayComparer<ConversationId>))]
    protected virtual async Task<ApiArray<ConversationId>> GetConsolidatedLiveConversationList(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var openId = await GetConsolidatedOpenLiveConversationId(chatId, cancellationToken).ConfigureAwait(false);
        var closed = await ListClosedLiveConversations(chatId, cancellationToken).ConfigureAwait(false);
        var ids = closed.Where(c => c.Id != openId).Select(c => c.Id);
        if (closed.Count > 0)
            // Re-read so an expired or covered conversation drops without an explicit signal
            Computed.GetCurrent().Invalidate(SelfHealDelay);
        return (openId is null ? ids : ids.Prepend(openId)).ToApiArray();
    }

    [ComputeMethod(ConsolidationDelay = 0)]
    protected virtual async Task<LiveConversation?> GetConsolidatedOpenLiveConversation(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        // LiveConversation compares by content, so a GetState recompute that leaves the conversation
        // as it was is absorbed here.
        var state = await GetState(chatId, cancellationToken).ConfigureAwait(false);
        return state?.OpenConversation;
    }

    [ComputeMethod(ConsolidationDelay = 0.2, ConsolidationComparer = typeof(ApiArrayComparer<AuthorId>))]
    protected virtual async Task<ApiArray<AuthorId>> GetConsolidatedParticipants(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var authorIds = await GetFreshParticipantIds(chatId).ConfigureAwait(false);
        if (authorIds.Count > 0)
            // Re-check so a stale (left) participant drops without an explicit off signal.
            Computed.GetCurrent().Invalidate(SelfHealDelay);
        return authorIds;
    }

    [ComputeMethod(ConsolidationDelay = 0.2)]
    protected virtual async Task<bool> GetConsolidatedHasRecorder(ChatId chatId, CancellationToken cancellationToken)
    {
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var cutoff = Clocks.SystemClock.Now - ParticipantStaleness;
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        var hasRecorder = participants.Values.Any(p => IsFreshRecorder(p, cutoff));
        if (hasRecorder)
            // Re-check so a stale (crashed) recorder drops without an explicit off signal.
            Computed.GetCurrent().Invalidate(SelfHealDelay);
        return hasRecorder;
    }

    // Private methods

    private async Task<LiveSessionState?> SafeGetState(ChatId chatId)
    {
        try {
            return await _redisStates.Get(chatId.Value).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read live conversation from Redis for chat #{ChatId}", chatId);
            return null;
        }
    }

    private void InvalidateState(ChatId chatId)
    {
        using (Invalidation.Begin()) {
            _ = GetState(chatId, default);
            _ = Get(chatId, default);
        }
    }

    private void InvalidateCall(ChatId chatId)
    {
        using (Invalidation.Begin()) {
            _ = GetCall(chatId, default);
            _ = Get(chatId, default);
        }
    }

    // An invite write alone; InvalidateCall reaches the invites through GetCall.
    private void InvalidateInvites(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = ListInvites(chatId, default);
    }

    private void InvalidateGet(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = Get(chatId, default);
    }

    private void InvalidateHasRecorder(ChatId chatId)
    {
        // The consolidating methods, not the public ones: IHasInvalidationTarget redirects to the
        // consolidation source, while invalidating a plain derived computed can't reach it.
        using (Invalidation.Begin())
            _ = GetConsolidatedHasRecorder(chatId, default);
    }

    private void InvalidateListParticipants(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = GetConsolidatedParticipants(chatId, default);
    }

    // Nested types

    [DataContract, MessagePackObject]
    public sealed partial record ParticipationInfo(
        [property: DataMember(Order = 0), Key(0)] ParticipationKind Kind,
        [property: DataMember(Order = 1), Key(1)] Moment RegisteredAt,
        [property: DataMember(Order = 2), Key(2)] bool MicMuted = false,
        [property: DataMember(Order = 3), Key(3)] Moment JoinedAt = default,
        [property: DataMember(Order = 4), Key(4)] Moment? HandRaisedAt = null,
        // Registered by the server for a call's caller at the answer, before their client joins: fresh for the
        // call's checks, but no presence to show until the client's own SetParticipation replaces it.
        [property: DataMember(Order = 5), Key(5)] bool IsPlaceholder = false);
}
