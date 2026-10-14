using ActualChat.Streaming.Diagnostics;
using ActualChat.Live;
using ActualChat.Queues;

namespace ActualChat.Streaming;

public partial class LiveSessionsBackend
{
    // Private methods

    // The session is live only while someone is streaming - recording audio or video (a Record
    // participant; OnStreamRegistered registers every streamer as one). Pure listeners/watchers
    // don't keep it alive. A call answered in it is the exception: it ends with a hang-up, not with
    // silence, so its own parties present - listening counts - hold the session too.
    private async Task<bool> IsSessionLive(ChatId chatId)
    {
        if (await HasFreshRecorder(chatId).ConfigureAwait(false))
            return true;
        if (await SafeGetCall(chatId).ConfigureAwait(false) is not { IsAnswered: true } call)
            return false;

        var partyIds = await GetCallPartyIds(chatId, call).ConfigureAwait(false);
        return await CountFreshCallParties(chatId, partyIds).ConfigureAwait(false) >= MinCallParties(chatId);
    }

    // As IsSessionLive's call rule, but it also holds for CallConnectGrace after the answer, before the one
    // who answered has had the time to connect: GetCall's self-heal ends a call this reports dead.
    private async Task<bool> IsCallAlive(ChatId chatId, LiveCall call)
    {
        if (Clocks.SystemClock.Now - call.AnsweredAt < CallConnectGrace)
            return true;

        var partyIds = await GetCallPartyIds(chatId, call).ConfigureAwait(false);
        return await CountFreshCallParties(chatId, partyIds).ConfigureAwait(false) >= MinCallParties(chatId);
    }

    // The caller and whoever answered: someone else present in the session the call rang into isn't one.
    private async Task<HashSet<AuthorId>> GetCallPartyIds(ChatId chatId, LiveCall call)
    {
        var invites = await SafeGetInvites(chatId).ConfigureAwait(false);
        return [
            .. invites.Values
                .Where(i => i is { Status: CallInviteStatus.Accepted or CallInviteStatus.Active })
                .Select(i => i!.InviteeId),
            call.CallerId,
        ];
    }

    private async Task<int> CountFreshCallParties(ChatId chatId, HashSet<AuthorId> partyIds)
    {
        var freshIds = await GetFreshParticipantIds(chatId).ConfigureAwait(false);
        return freshIds.Count(partyIds.Contains);
    }

    private async Task<bool> IsCallAbandoned(ChatId chatId, LiveCall call)
    {
        // No invite is still ringing or answerable late, and the call has too few present parties to go on:
        // nobody else can join it any more, so it's abandoned - the whole thing should close.
        var invites = await SafeGetInvites(chatId).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        if (invites.Values.Any(i => i is { Status: CallInviteStatus.Ringing } || IsInAnswerGrace(i, now)))
            return false;
        if (!call.IsAnswered)
            return true;

        var partyIds = await GetCallPartyIds(chatId, call).ConfigureAwait(false);
        return await CountFreshCallParties(chatId, partyIds).ConfigureAwait(false) < MinCallParties(chatId);
    }

    // Caller must hold the change lock + Computed.BeginIsolation().
    // Returns true iff this call just transitioned the session into IsQuiet.
    private async Task<bool> EvaluateLiveness(ChatId chatId)
    {
        var state = await SafeGetState(chatId).ConfigureAwait(false);
        if (state is null)
            return false;

        // A streamer (recording audio or video) keeps the session alive; it closes once nobody is
        // streaming, even if listeners/watchers remain - except in a connected call, see IsSessionLive.
        var isActive = await IsSessionLive(chatId).ConfigureAwait(false);
        if (isActive == !state.IsQuiet)
            return false; // already active+open or inactive+closing

        state = isActive
            ? state with {
                QuietSince = null,
                Version = VersionGenerator.NextVersion(state.Version),
            }
            : state with {
                QuietSince = Clocks.SystemClock.Now,
                Version = VersionGenerator.NextVersion(state.Version),
            };
        await _redisStates.Set(chatId.Value, state).ConfigureAwait(false);
        InvalidateState(chatId);
        return !isActive;
    }

    private async Task StartClosingGrace(ChatId chatId)
    {
        bool startedClosing;
        try {
            using var _ = Computed.BeginIsolation();
            using var lockHolder = await _changeLocks.Lock(chatId, CancellationToken.None).ConfigureAwait(false);
            startedClosing = await EvaluateLiveness(chatId).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "StartClosingGrace failed for chat #{ChatId}", chatId);
            return;
        }
        // Outside the lock: wake the summary flow so it finalizes the just-closing session at once.
        if (startedClosing)
            await WakeSummaryFlow(chatId).ConfigureAwait(false);
    }

    // Immediate close: the last participant left explicitly, so wind the call down now rather than
    // marking it closing and waiting out the grace. Re-checks under no lock that nobody rejoined first.
    private async Task CloseNow(ChatId chatId)
    {
        try {
            var state = await SafeGetState(chatId).ConfigureAwait(false);
            if (state is null)
                return;
            if (await IsSessionLive(chatId).ConfigureAwait(false))
                return; // someone is (still) streaming - not empty after all
            if (state is { IsTranscriptionOn: true, StartedAt: not null, Kind: LiveSessionKind.Ambient }) {
                // Hand the close to LiveConversationSummaryFlow: it runs the final summary pass, decides the
                // tier, materializes, then calls FinalizeSession. StartClosingGrace marks IsQuiet; the 90s
                // SelfClose stays the backstop if the flow never finalizes.
                await StartClosingGrace(chatId).ConfigureAwait(false);
                return;
            }

            await CloseSession(state, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "CloseNow failed for chat #{ChatId}", chatId);
        }
    }

    // Call teardown, the one funnel every end of a call reaches. Unlike CloseNow it doesn't require anyone
    // to have left - a call with a single party is already over. mustRecheckParties is for an end decided
    // by headcount: it counts again under the lock, and a party back by then keeps the call. callId names
    // the call the end was decided for: the next call to the chat is not its to end.
    private async Task EndCall(
        ChatId chatId, CallEndReason reason, bool mustRecheckParties = false, CallId? callId = null)
    {
        try {
            LiveCall? call;
            List<CallInvite> invites;
            HashSet<AuthorId> partyIds;
            // Dropping the call's key is the atomic claim that picks one closer out of the several that can
            // decide a call is over at once. It shares _changeLocks with every write of the key - outside it
            // a straddling read-modify-write puts the key back and the call is recorded twice - and the call
            // is re-read, as the callers' snapshots predate it.
            using (Computed.BeginIsolation())
            using (await _changeLocks.Lock(chatId, CancellationToken.None).ConfigureAwait(false)) {
                call = await SafeGetCall(chatId).ConfigureAwait(false);
                if (call is null || (callId is not null && call.Id != callId))
                    return;

                partyIds = await GetCallPartyIds(chatId, call).ConfigureAwait(false);
                if (mustRecheckParties && call.IsAnswered
                    && await CountFreshCallParties(chatId, partyIds).ConfigureAwait(false) >= MinCallParties(chatId))
                    return;

                var inviteMap = await SafeGetInvites(chatId).ConfigureAwait(false);
                invites = inviteMap.Values.SkipNullItems().ToList();
                if (!await _redisCalls.Remove(chatId.Value).ConfigureAwait(false))
                    return;
            }

            var inviteeIds = invites.Select(i => i.InviteeId).ToList();
            var endedAt = Clocks.SystemClock.Now;
            // An answered call is Ended whichever button ended it - CancelCall is also how a caller hangs
            // up - so the outcome recorded during the ring only decides a call that was never answered.
            var outcome = call.IsAnswered ? CallOutcome.Ended : call.Outcome;
            CallMeters.RecordEnded(call, outcome, reason, endedAt, invites, partyIds.Count);
            // The one line per call that has every number the meters aggregate away.
            Log.LogInformation(
                "Call #{CallId} ended: {Outcome} ({Reason}), {ChatKind} chat, video={HasVideo}, "
                + "invitees={InviteeCount}, parties={PartyCount}, answered in {AnswerDelay}, lasted {Duration}",
                call.Id, outcome, reason, chatId.Kind, call.HasVideo,
                invites.Count, partyIds.Count,
                (call.AnsweredAt - call.StartedAt)?.ToShortString() ?? "-",
                (endedAt - (call.AnsweredAt ?? call.StartedAt)).ToShortString());
            try {
                // Everything below runs with CancellationToken.None for the same reason the teardown in the
                // finally does: the claim above is once-or-never, so a token revoked mid-way would leave the
                // call with no trace at all, and nothing retries it.
                if (inviteeIds.Count > 0)
                    await DismissRing(call.Id, inviteeIds, CancellationToken.None).ConfigureAwait(false);
                await EndUserCalls(
                        chatId, call.Id, inviteeIds.Prepend(call.CallerId).Distinct(), outcome, CancellationToken.None)
                    .ConfigureAwait(false);
                if (chatId.Kind == ChatKind.Peer) {
                    // A call that rang into an ongoing session has no card of its own to gather its last words
                    // in, so its entry, visible there, has to come after them - see WriteCallEntryAfterTail.
                    var isInOngoingSession = call.IsAnswered
                        && await SafeGetState(chatId).ConfigureAwait(false) is { IsCall: false };
                    if (isInOngoingSession)
                        _ = WriteCallEntryAfterTail(call, inviteeIds, partyIds, outcome, endedAt);
                    else
                        await WriteCallEntry(call, inviteeIds, outcome, endedAt, CancellationToken.None)
                            .ConfigureAwait(false);
                }
            }
            finally {
                // Having won the claim, this is the call's only closer: nothing retries it, so a failed ring
                // dismissal or entry must not also cost the teardown and the invalidation that tells clients
                // the call is over.
                try {
                    await CleanUpAfterCall(chatId, call.Id).ConfigureAwait(false);
                }
                catch (Exception e) {
                    // Swallowed so a teardown failure doesn't replace whatever the body threw.
                    Log.LogWarning(e, "EndCall: teardown failed for chat #{ChatId}", chatId);
                }
            }
            // The session goes on without the call, by the rule every session closes by: once nobody records.
            // The parties' clients stop their media on the call's end - in a peer chat both of them - so that
            // is usually at once. A session the call started closes as the call's card.
            if (await SafeGetState(chatId).ConfigureAwait(false) is { IsCall: true })
                await CloseCallSession(chatId).ConfigureAwait(false);
            else
                await StartClosingGrace(chatId).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "EndCall failed for chat #{ChatId}", chatId);
        }
    }

    // Caller must hold the change lock + Computed.BeginIsolation().
    private async Task MaterializeCallSession(LiveSessionState state)
    {
        // A call's session is materialized when it ends, and unlike a transcript session it has no title to
        // gate on - the card is the point.
        if (state.OpenConversation is not { } liveConversation)
            return;

        // Only a summary advances SummaryEndLid, and a call may never have had one, so the range could end at -
        // or before - the start it was given at the answer, taking the card and the entry it must contain
        // out of the chat. The chat's last id covers the call entry, which the call's end wrote before this.
        var lastLid = (await ChatsBackend
            .GetLidRange(state.ChatId, false, CancellationToken.None)
            .ConfigureAwait(false)).End - 1;
        // EndsAt defaults to SummarizedAt, which may predate the end by a resummarization delay,
        // so it needs the session's real end.
        var conversation = liveConversation.ToMaterializedConversation() with {
            EndEntryLid = Math.Max(liveConversation.SummaryLastLid, lastLid),
            EndsAt = Clocks.SystemClock.Now,
        };
        var materialize = new ConversationBackend_Materialize(conversation);
        // Null when the card was declined because its chat is being removed:
        // there is no card for the tail flow to grow, but the session still has to close.
        var card = await Commander.Call(materialize, true, CancellationToken.None).ConfigureAwait(false);
        if (card is null)
            Log.LogWarning("Call card {ConversationId} in chat #{ChatId} was declined", conversation.Id, state.ChatId);
        else
            await WakeCallTailFlow(conversation.Id).ConfigureAwait(false);
        await EnqueueSessionEnded(state).ConfigureAwait(false);
    }

    private async Task CloseCallSession(ChatId chatId)
    {
        // A call's session outlives its call and closes as an ambient one would - once nobody records - but
        // its block stays the call's card, which needs no summary to be kept.
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, CancellationToken.None).ConfigureAwait(false);
        if (await SafeGetState(chatId).ConfigureAwait(false) is not { IsCall: true } state)
            return;
        if (await SafeGetCall(chatId).ConfigureAwait(false) is not null)
            return;
        if (await IsSessionLive(chatId).ConfigureAwait(false))
            return;

        await MaterializeCallSession(state).ConfigureAwait(false);
        await RemoveSession(chatId).ConfigureAwait(false);
    }

    // Backstop close: the grace elapsed without an explicit leave (a crashed/stale client). Vanishes
    // the session and sends FINAL itself.
    private async Task SelfClose(ChatId chatId)
    {
        try {
            var state = await SafeGetState(chatId).ConfigureAwait(false);
            if (state is not { IsQuiet: true })
                return;

            await CloseSession(state, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "SelfClose failed for chat #{ChatId}", chatId);
        }
    }

    private async Task CloseSession(LiveSessionState state, CancellationToken cancellationToken)
    {
        // A call still in a call's session ends first, as only its teardown records it; the session then
        // closes as the call's card.
        if (state.IsCall) {
            await EndCall(state.ChatId, CallEndReason.SessionClosed, mustRecheckParties: true).ConfigureAwait(false);
            await CloseCallSession(state.ChatId).ConfigureAwait(false);
            return;
        }

        // A session that never started (solo) leaves its ordinary split-flow conversations behind;
        // only a started session keeps its summary. There is no completion notification - the
        // in-chat block already carries the summary in place, so an "ended" banner adds nothing.
        // An empty title (phone-mode, or below the summary threshold) has nothing to keep and just vanishes.
        // The whole close is one locked step that re-checks liveness: callers found the session empty without
        // the lock, and a speaker who came back since has reopened this same session. Dropping it would take
        // their fresh recorder along.
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(state.ChatId, cancellationToken).ConfigureAwait(false);
        if (await SafeGetState(state.ChatId).ConfigureAwait(false) is not { } freshState)
            return; // Another closer got here first
        if (await IsSessionLive(state.ChatId).ConfigureAwait(false))
            return;

        state = freshState;
        // The closed conversation is written and its flow woken before the session state goes: a failure or a
        // crash in between leaves the session in place for the next close, which overwrites the same entry.
        // Nothing is saved here - the flow writes the saved conversation from the closed one's exact boundaries.
        var closed = state.OpenConversation is { } liveConversation && !liveConversation.Title.IsNullOrEmpty()
            ? await CloseConversation(liveConversation).ConfigureAwait(false)
            : null;
        if (closed is not null)
            await WakeClosedLiveConversationFlow(closed.Id).ConfigureAwait(false);
        await EnqueueSessionEnded(state).ConfigureAwait(false);
        await RemoveSession(state.ChatId).ConfigureAwait(false);
        if (closed is not null)
            InvalidateClosed(closed);
    }

    private async Task EnqueueSessionEnded(LiveSessionState state)
    {
        // Best-effort, like SpeechStartedEvent: a lost event costs the participants one counted
        // session, never the close. Runs before Close, which drops the participant map it reads.
        if (state.StartedAt is not { } startedAt)
            return;

        try {
            var participants = await SafeGetHashMap(state.ChatId).ConfigureAwait(false);
            var members = new Dictionary<AuthorId, Moment>();
            foreach (var authorId in state.AuthorIds)
                members[authorId] = startedAt;
            foreach (var (authorIdValue, info) in participants) {
                if (info is null || !AuthorId.TryParse(authorIdValue, out var authorId))
                    continue;

                var joinedAt = info.JoinedAt == default ? info.RegisteredAt : info.JoinedAt;
                members[authorId] = members.TryGetValue(authorId, out var known) && known < joinedAt ? known : joinedAt;
            }
            var ended = new LiveSessionEndedEvent(
                state.ChatId,
                startedAt,
                Clocks.SystemClock.Now,
                state.Kind,
                members.Select(kv => new LiveSessionEndedMember(kv.Key, kv.Value)).ToApiArray());
            await Services.Queues().Enqueue(ended, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            StreamingMeters.LiveSessionEndedDropped.Add(1);
            Log.LogError(e, "Failed to enqueue LiveSessionEndedEvent for chat '{ChatId}'", state.ChatId);
        }
    }

    private Task WriteCallEntryAfterTail(
        LiveCall call,
        IReadOnlyList<AuthorId> invitees,
        HashSet<AuthorId> partyIds,
        CallOutcome outcome,
        Moment endedAt)
        // A transcript gets its entry on its first non-empty result, so the call's last words get theirs
        // only once the parties' clients have stopped their media and the transcriber has caught up.
        // In memory, like the ring timers: a restart in between leaves the call without its entry.
        => BackgroundTask.Run(async () => {
            var chatId = call.ChatId;
            using (var cts = new CancellationTokenSource(CallStreamsEndTimeout)) {
                var cStreams = await Computed
                    .Capture(() => LiveAudioBackend.List(chatId, cts.Token), cts.Token)
                    .ConfigureAwait(false);
                await cStreams
                    .When(streams => !streams.Any(s => partyIds.Contains(s.AuthorId) && s.BeginsAt < endedAt),
                        cts.Token)
                    .SilentAwait(false);
            }
            await Task.Delay(CallTailDelay).ConfigureAwait(false);
            await WriteCallEntry(call, invitees, outcome, endedAt, CancellationToken.None).ConfigureAwait(false);
        }, Log, $"Writing the entry of call #{call.Id} failed");

    private async Task WriteCallEntry(
        LiveCall call,
        IReadOnlyList<AuthorId> invitees,
        CallOutcome outcome,
        Moment endedAt,
        CancellationToken cancellationToken)
    {
        if (outcome == CallOutcome.None)
            return;

        var chatId = call.ChatId;
        var caller = await AuthorsBackend
            .Get(chatId, call.CallerId, RequestedAuthorKind.Full, cancellationToken)
            .ConfigureAwait(false);
        var command = new ChatsBackend_ChangeEntry(
            ChatEntryId.New(chatId, 0),
            null,
            Change.Create(new ChatEntryDiff {
                Kind = ChatEntryKind.Call,
                AuthorId = Constants.User.Walle.GetWalleAuthorId(chatId),
                // An answered call's span is its talk time: it is the entry's own Duration, all a call
                // placed into an ongoing session has to show it by - there is no call card around it.
                BeginsAt = call.AnsweredAt ?? endedAt,
                EndsAt = call.IsAnswered ? endedAt : null,
                CallerId = call.CallerId,
                CallerName = caller?.Avatar.Name.NullIfEmpty() ?? MentionMarkup.NotAvailableName,
                Outcome = outcome,
                InviteeIds = invitees.ToApiArray(),
                HasVideo = call.HasVideo,
            }));
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task CleanUpAfterCall(ChatId chatId, CallId callId)
    {
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, CancellationToken.None).ConfigureAwait(false);
        // The ended call's key went first, and the chat has been free since: a call placed meanwhile
        // wrote invites of its own, which aren't this teardown's to drop.
        var nextCall = await SafeGetCall(chatId).ConfigureAwait(false);
        if (nextCall is not null && nextCall.Id != callId)
            Log.LogInformation(
                "EndCall: call #{CallId} is over, keeping the invites of #{NextCallId}", callId, nextCall.Id);
        else
            await _redisInvites.RemoveHashMap(chatId.Value).ConfigureAwait(false);
        InvalidateCall(chatId);
        InvalidateState(chatId);
        InvalidateHasRecorder(chatId);
        InvalidateListParticipants(chatId);
    }

    // Caller must hold the change lock + Computed.BeginIsolation().
    private async Task RemoveSession(ChatId chatId)
    {
        // Everything derived from the dropped participant map is invalidated with it: HasRecorder
        // otherwise self-heals on a delay, reading a stale streamer as a talker after the session.
        await _redisStates.Remove(chatId.Value).ConfigureAwait(false);
        await _redisParticipants.RemoveHashMap(chatId.Value).ConfigureAwait(false);
        // A call still ringing into this session outlives it: the answer will start a session of its own.
        if (await SafeGetCall(chatId).ConfigureAwait(false) is null)
            await _redisInvites.RemoveHashMap(chatId.Value).ConfigureAwait(false);
        InvalidateState(chatId);
        InvalidateHasRecorder(chatId);
        InvalidateListParticipants(chatId);
    }
}
