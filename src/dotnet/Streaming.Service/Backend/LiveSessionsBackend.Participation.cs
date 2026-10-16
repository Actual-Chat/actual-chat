using ActualChat.Live;
using ActualChat.Notifications;
using ActualChat.Queues;

namespace ActualChat.Streaming;

public partial class LiveSessionsBackend
{
    public virtual async Task OnStreamRegistered(
        ChatId chatId,
        AuthorId authorId,
        long? entryLid,
        bool hasText,
        bool hasVoice,
        CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();

        // Best-effort wake trigger: per utterance (before the dedup/early-return below), voice-only,
        // and never allowed to fail stream registration.
        if (hasVoice)
            try {
                await Services.Queues()
                    .Enqueue(new SpeechStartedEvent(chatId, authorId, Clocks.SystemClock.Now), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogError(e, "Failed to enqueue SpeechStartedEvent for chat '{ChatId}'", chatId);
            }

        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);

        var now = Clocks.SystemClock.Now;
        var state = await SafeGetState(chatId).ConfigureAwait(false);
        if (state is null) {
            var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
            var startEntryLid = entryLid
                ?? (await ChatsBackend.GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false)).End;
            state = new LiveSessionState {
                ChatId = chatId,
                FirstSpeechLid = startEntryLid,
                FirstSpeechAt = now,
                AuthorIds = [authorId],
                HostAuthorId = authorId,
                IsTranscriptionOn = chat?.IsSummarized ?? false,
                HasTranscript = hasText,
                Version = VersionGenerator.NextVersion(),
            };
        }
        else {
            var authorIds = state.AuthorIds.Contains(authorId)
                ? state.AuthorIds
                : [..state.AuthorIds, authorId];
            var hasTranscript = state.HasTranscript || hasText;
            if (ReferenceEquals(authorIds, state.AuthorIds)
                && !state.IsQuiet
                && hasTranscript == state.HasTranscript) {
                // Nothing to write, but a re-registering stream is proof of life: keep the key alive.
                await _redisStates.Refresh(chatId.Value).ConfigureAwait(false);
                return;
            }

            var version = VersionGenerator.NextVersion(state.Version);
            state = state with {
                AuthorIds = authorIds,
                HasTranscript = hasTranscript,
                QuietSince = null,
                // A streamer who joins after the start is in the conversation until a summary says who spoke
                OpenConversation = state.OpenConversation is { } liveConversation
                    && (!liveConversation.AuthorIds.Contains(authorId) || !liveConversation.ParticipantIds.Contains(authorId))
                        ? liveConversation with {
                            AuthorIds = liveConversation.AuthorIds.Contains(authorId) ? liveConversation.AuthorIds : [..liveConversation.AuthorIds, authorId],
                            ParticipantIds = liveConversation.ParticipantIds.Contains(authorId)
                                ? liveConversation.ParticipantIds
                                : [..liveConversation.ParticipantIds, authorId],
                            Version = version,
                        }
                        : state.OpenConversation,
                Version = version,
            };
        }

        if (state.OpenConversation is null && state.AuthorIds.Count >= 2) {
            var startLid = MustStartFromFirstSpeech
                ? state.FirstSpeechLid
                : (await ChatsBackend.GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false)).End;
            var version = VersionGenerator.NextVersion(state.Version);
            state = state with {
                OpenConversation = new LiveConversation {
                    Id = ConversationId.New(chatId, startLid),
                    SummaryEndLid = startLid,
                    StartedAt = now,
                    AuthorIds = state.AuthorIds,
                    // Whoever is in by now, listeners included, is part of it from the start
                    ParticipantIds = [
                        ..state.AuthorIds.Concat(await GetFreshParticipantIds(chatId).ConfigureAwait(false))
                            .Distinct(),
                    ],
                    CallerId = state.CallerId,
                    Version = version,
                },
                Version = version,
            };
            // Calls announce themselves by ringing, not a conversation banner; only ambient sessions banner.
            if (state.Kind == LiveSessionKind.Ambient)
                await EnqueueLiveNotification(
                    state, ConversationNotificationPhase.Started, "Voice chat started", cancellationToken)
                    .ConfigureAwait(false);
        }

        await _redisStates.Set(chatId.Value, state).ConfigureAwait(false);
        // Register the streamer as a participant so per-peer mute flags have a home
        // (grouping uses live-stream state, so a stale entry never misgroups an active streamer).
        await EnsureParticipant(chatId, authorId).ConfigureAwait(false);
        InvalidateState(chatId);
    }

    public virtual async Task SetParticipation(
        ChatId chatId,
        AuthorId authorId,
        ParticipationKind kind,
        bool isActive,
        CancellationToken cancellationToken)
    {
        bool emptiedByLeave;
        int? callPartiesLeft = null;
        var startedClosing = false;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            if (isActive) {
                // Preserve mute flags, the raised hand and the original join time across heartbeats / kind
                // changes: RegisteredAt is refreshed by every heartbeat, so it can't double as JoinedAt.
                var now = Clocks.SystemClock.Now;
                var existing = await SafeGetParticipant(chatId, authorId).ConfigureAwait(false);
                // A placeholder's join is the server's guess at the answer: the client joins only now.
                var info = existing is null
                    ? new ParticipationInfo(kind, now, JoinedAt: now)
                    : existing with {
                        Kind = kind,
                        RegisteredAt = now,
                        JoinedAt = existing.JoinedAt != default && !existing.IsPlaceholder ? existing.JoinedAt : now,
                        IsPlaceholder = false,
                    };
                await _redisParticipants.Set(chatId.Value, authorId.Value, info).ConfigureAwait(false);
                // The participants hash refreshes its own TTL on write, but the session state key only
                // does so on Set - which a steady-state session never reaches. Without this the state
                // silently expires mid-call and the next stream rebuilds it as a brand-new session.
                await _redisStates.Refresh(chatId.Value).ConfigureAwait(false);
                await AddParticipantToConversation(chatId, authorId).ConfigureAwait(false);
                // An answered call lives as long as its session does; a ringing one keeps its own short TTL.
                // Its invites go with it: they name the call's parties, and a steady call never rewrites them.
                var call = await SafeGetCall(chatId).ConfigureAwait(false);
                if (call is { IsAnswered: true }) {
                    await _redisCalls.Refresh(chatId.Value).ConfigureAwait(false);
                    await _redisInvites.Touch(chatId.Value).ConfigureAwait(false);
                }
            }
            else {
                // Kind-guarded: a stream ending must only clear the registration it itself owns. Two
                // independent streams for the same author (e.g. a recorder stopping while a separate
                // listening stream stays open) would otherwise let the ending one delete the record the
                // still-open one relies on - _participants holds one record per author, not per kind.
                // Nor is such an ending a departure: the author is still here under the newer kind, so
                // the leave handling below (which closes a session nobody streams in) must not run.
                var existing = await SafeGetParticipant(chatId, authorId).ConfigureAwait(false);
                if (existing is { } info && info.Kind != kind)
                    return;

                if (existing is not null)
                    await _redisParticipants.Remove(chatId.Value, authorId.Value).ConfigureAwait(false);
            }
            InvalidateListParticipants(chatId);
            InvalidateHasRecorder(chatId);
            InvalidateGet(chatId);
            // Whether the departure was an explicit hang-up or a connection that just died - both reach
            // this the same way. Dialing keeps its own ExpireRings path.
            if (!isActive)
                callPartiesLeft = await CountCallPartiesLeft(chatId, authorId, cancellationToken)
                    .ConfigureAwait(false);
            // A join/heartbeat, or a leave with someone still streaming, just re-evaluates liveness; the
            // grace there is the safety net for crashed/stale clients. A leave that stops the last stream
            // closes it outright below - no waiting on the grace or on a UI observer. EvaluateLiveness only
            // marks a still-populated session closing (recoverable if a recorder returns), so a transient
            // not-live blip never tears down a live recording - unlike an unconditional CloseNow here would.
            // A call leave skips all of this: it would close or mark closing the call CallLeaveGrace may keep.
            var isCallLeave = callPartiesLeft is 0 or 1;
            emptiedByLeave = !isActive && !isCallLeave && !await IsSessionLive(chatId).ConfigureAwait(false);
            if (!emptiedByLeave && !isCallLeave)
                startedClosing = await EvaluateLiveness(chatId).ConfigureAwait(false);
        }
        if (callPartiesLeft is { } partiesLeft and (0 or 1))
            await OnCallPartyLeft(chatId, partiesLeft).ConfigureAwait(false);
        else if (emptiedByLeave)
            await CloseNow(chatId).ConfigureAwait(false);
        else if (startedClosing)
            // The last recorder stayed on as a listener: no stream left to trip CloseNow, but the session is
            // no longer live. Wake the summary flow to finalize the just-closing session now, rather than
            // leaving the block and Call tab up until the 90s SelfClose backstop fires.
            await WakeSummaryFlow(chatId).ConfigureAwait(false);
    }

    public virtual async Task<SessionRules?> SetRules(
        ChatId chatId, SessionRules rules, CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);

        var state = await SafeGetState(chatId).ConfigureAwait(false);
        if (state is null)
            return null;

        var oldRules = state.Rules;
        state = state with { Rules = rules, Version = VersionGenerator.NextVersion(state.Version) };
        await _redisStates.Set(chatId.Value, state).ConfigureAwait(false);
        InvalidateState(chatId);
        return oldRules;
    }

    public virtual async Task MutePeer(
        ChatId chatId, AuthorId targetAuthorId, bool muted, CancellationToken cancellationToken)
    {
        await EnsureParticipant(chatId, targetAuthorId).ConfigureAwait(false);
        var existing = await SafeGetParticipant(chatId, targetAuthorId).ConfigureAwait(false);
        if (existing is null)
            return;

        await _redisParticipants
            .Set(chatId.Value, targetAuthorId.Value, existing with { MicMuted = muted })
            .ConfigureAwait(false);
        InvalidateGet(chatId);
    }

    public virtual async Task MuteAll(
        ChatId chatId,
        ApiArray<AuthorId> exceptAuthorIds,
        bool muted,
        CancellationToken cancellationToken)
    {
        // Soft "mute all except the actor": sets MicMuted on every other participant.
        // This is peer-revocable — a muted peer can re-record to unmute themselves.
        var exceptAuthorIdValues = exceptAuthorIds.Select(x => x.Value).ToHashSet();
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        foreach (var (authorIdValue, info) in participants) {
            if (info is null || info.MicMuted == muted)
                continue;
            if (exceptAuthorIdValues.Contains(authorIdValue))
                continue;

            await _redisParticipants.Set(chatId.Value, authorIdValue, info with { MicMuted = muted }).ConfigureAwait(false);
        }
        InvalidateGet(chatId);
    }

    public virtual async Task SetHost(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);

        var state = await SafeGetState(chatId).ConfigureAwait(false);
        if (state is null || state.HostAuthorId == authorId)
            return;

        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        if (!participants.ContainsKey(authorId.Value))
            throw StandardError.Constraint("Only a call participant can become the call host.");

        state = state with {
            HostAuthorId = authorId,
            Version = VersionGenerator.NextVersion(state.Version),
        };
        await _redisStates.Set(chatId.Value, state).ConfigureAwait(false);
        InvalidateState(chatId);
    }

    public virtual async Task<bool> SetHandRaised(
        ChatId chatId,
        AuthorId authorId,
        bool isRaised,
        CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);

        var existing = await SafeGetParticipant(chatId, authorId).ConfigureAwait(false);
        if (existing is null || (existing.HandRaisedAt is not null) == isRaised)
            return false;

        if (isRaised) {
            // A hand is a LiveSession member's state, so it exists only where Get returns a session
            var state = await SafeGetState(chatId).ConfigureAwait(false);
            if (state is not { StartedAt: not null })
                return false;
        }

        var info = existing with { HandRaisedAt = isRaised ? Clocks.SystemClock.Now : null };
        await _redisParticipants.Set(chatId.Value, authorId.Value, info).ConfigureAwait(false);
        InvalidateGet(chatId);
        return true;
    }

    public virtual async Task<ApiArray<AuthorId>> LowerAllHands(ChatId chatId, CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);

        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        var lowered = new List<AuthorId>();
        foreach (var (authorIdValue, info) in participants) {
            if (info?.HandRaisedAt is null)
                continue;

            await _redisParticipants
                .Set(chatId.Value, authorIdValue, info with { HandRaisedAt = null })
                .ConfigureAwait(false);
            lowered.Add(AuthorId.Parse(authorIdValue));
        }
        InvalidateGet(chatId);
        return lowered.ToApiArray();
    }

    // Legacy methods

    public Task LegacyOnStreamRegistered(
        ChatId chatId,
        AuthorId authorId,
        long? entryLid,
        bool transcriptionOn,
        CancellationToken cancellationToken)
        => OnStreamRegistered(chatId, authorId, entryLid, transcriptionOn, true, cancellationToken);

    // [ComputeMethod]
    [Obsolete("2026.10: Use GetState - this one serves the 2.21 LiveSessionState layout to older callers.")]
    public virtual async Task<LegacyLiveSessionState?> LegacyGetState(
        ChatId chatId, CancellationToken cancellationToken)
    {
        var state = await GetState(chatId, cancellationToken).ConfigureAwait(false);
        return LegacyLiveSessionState.From(state);
    }

    // [ComputeMethod]
    [Obsolete("2026.10: Use GetOpenLiveConversationId - this one answers 2.21 and older callers.")]
    public virtual async Task<long?> LegacyGetVisibleStartLid(ChatId chatId, CancellationToken cancellationToken)
    {
        var id = await GetOpenLiveConversationId(chatId, cancellationToken).ConfigureAwait(false);
        return id?.StartEntryLid;
    }

    // [ComputeMethod]
    [Obsolete("2026.10: Use GetOpenLiveConversation - this one answers 2.21 and older callers.")]
    public virtual async Task<Conversation?> LegacyGetLiveConversation(
        ChatId chatId, CancellationToken cancellationToken)
    {
        var liveConversation = await GetOpenLiveConversation(chatId, cancellationToken).ConfigureAwait(false);
        return liveConversation?.ToConversation();
    }

    [Obsolete("2026.10: Use UpdateLiveConversationSummary - this one takes the 2.21 inclusive summary end.")]
    public Task LegacyUpdateSummary(
        ChatId chatId,
        LiveConversationSummary summary,
        CancellationToken cancellationToken)
        => UpdateLiveConversationSummary(
            chatId, summary with { SummaryEndLid = summary.SummaryEndLid + 1 }, cancellationToken);

    [Obsolete("2026.10: Use SetLiveConversationContextStart - this one answers 2.21 and older callers.")]
    public Task LegacySetContextStart(ChatId chatId, long contextStartLid, CancellationToken cancellationToken)
        => SetLiveConversationContextStart(chatId, contextStartLid, cancellationToken);

    // Private methods

    private async Task<ParticipationInfo?> SafeGetParticipant(ChatId chatId, AuthorId authorId)
    {
        try {
            return await _redisParticipants.Get(chatId.Value, authorId.Value).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read participants from Redis for chat #{ChatId}", chatId);
            return null;
        }
    }

    // Caller must hold the change lock. The conversation keeps everyone who ever joined it, so a viewer
    // who left, or reloads, is still known to have been there.
    private async Task AddParticipantToConversation(ChatId chatId, AuthorId authorId)
    {
        var state = await SafeGetState(chatId).ConfigureAwait(false);
        if (state?.OpenConversation is not { } liveConversation || liveConversation.ParticipantIds.Contains(authorId))
            return;

        var version = VersionGenerator.NextVersion(state.Version);
        state = state with {
            OpenConversation = liveConversation with { ParticipantIds = [..liveConversation.ParticipantIds, authorId], Version = version },
            Version = version,
        };
        await _redisStates.Set(chatId.Value, state).ConfigureAwait(false);
        InvalidateState(chatId);
    }

    private async Task EnsureParticipant(ChatId chatId, AuthorId authorId, bool isPlaceholder = false)
    {
        var existing = await SafeGetParticipant(chatId, authorId).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        // Preserve the client's real kind — a trailing utterance must not flip a now-listening author back to Record.
        var info = existing is null
            ? new ParticipationInfo(ParticipationKind.Record, now, JoinedAt: now, IsPlaceholder: isPlaceholder)
            : existing with { RegisteredAt = now };
        await _redisParticipants.Set(chatId.Value, authorId.Value, info).ConfigureAwait(false);
        InvalidateListParticipants(chatId);
        InvalidateHasRecorder(chatId);
    }

    private async Task<Dictionary<string, ParticipationInfo?>> SafeGetHashMap(ChatId chatId)
    {
        try {
            return await _redisParticipants.GetHashMap(chatId.Value).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read participants from Redis for chat #{ChatId}", chatId);
            return [];
        }
    }

    private static bool IsFreshRecorder(ParticipationInfo? info, Moment cutoff)
        => info is { Kind: ParticipationKind.Record } && info.RegisteredAt >= cutoff;

    private static bool IsFreshParticipant(ParticipationInfo? info, Moment cutoff)
        => info is not null && info.RegisteredAt >= cutoff;

    private async Task<ApiArray<AuthorId>> GetFreshParticipantIds(ChatId chatId)
    {
        // For decisions taken under the change lock: GetConsolidatedParticipants keeps serving the old
        // roster for ConsolidationDelay after a change, so a hang-up read through it still counts two.
        var cutoff = Clocks.SystemClock.Now - ParticipantStaleness;
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        return participants
            .Where(kv => IsFreshParticipant(kv.Value, cutoff))
            .Select(kv => AuthorId.TryParse(kv.Key, out var id) ? id : null)
            .SkipNullItems()
            .ToApiArray();
    }

    private async Task<bool> HasParticipant(ChatId chatId)
    {
        var cutoff = Clocks.SystemClock.Now - ParticipantStaleness;
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        return participants.Values.Any(p => IsFreshParticipant(p, cutoff));
    }

    private async Task<bool> HasFreshRecorder(ChatId chatId)
    {
        var cutoff = Clocks.SystemClock.Now - ParticipantStaleness;
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        return participants.Values.Any(p => IsFreshRecorder(p, cutoff));
    }
}
