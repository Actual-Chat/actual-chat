using ActualChat.Streaming.Diagnostics;
using ActualChat.Flows;
using ActualChat.Live;
using ActualChat.Notifications;
using ActualChat.Queues;

namespace ActualChat.Streaming;

public partial class LiveSessionsBackend
{
    // Voice-call ring lifecycle

    public virtual async Task<CallId> StartCall(
        ChatId chatId,
        AuthorId callerAuthorId,
        ApiArray<AuthorId> invitees,
        bool hasVideo,
        string? sessionHash,
        string? clientId,
        CancellationToken cancellationToken)
    {
        // Ring each distinct invitee except the caller - once, and never the caller themselves.
        invitees = invitees.Where(id => id != callerAuthorId).Distinct().ToApiArray();
        // Decided before the claims, which carry it - and those are taken before the lock, since they
        // are RPCs to the users' own shards. The lock re-checks the decision.
        var joinedCallId = GetJoinableCallId(await SafeGetCall(chatId).ConfigureAwait(false), callerAuthorId);
        var callId = joinedCallId ?? NewCallId(chatId);
        // A claim the call then fails to justify is ended by the first CallsBackend.GetUserCall that
        // reads it. A one-invitee call names its peer in the claim, so the caller's screens don't have to
        // read the invites to know who they're calling; a group call has no single peer to name.
        var callerPeerId = invitees.Count == 1 ? invitees[0] : null;
        if (!await ClaimUserCall(chatId, callId, callerAuthorId, CallRole.Caller, CallPhase.Dialing,
                callerPeerId, hasVideo, sessionHash, clientId, cancellationToken).ConfigureAwait(false)) {
            CallMeters.RecordStartRefused("caller_busy");
            throw StandardError.Constraint("You're already in a call.");
        }

        var ringing = new List<AuthorId>();
        var busy = new List<AuthorId>();
        foreach (var invitee in invitees) {
            var isFree = await ClaimUserCall(chatId, callId, invitee, CallRole.Callee, CallPhase.Ringing,
                callerAuthorId, hasVideo, null, null, cancellationToken).ConfigureAwait(false);
            (isFree ? ringing : busy).Add(invitee);
        }

        bool isOvertaken;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            var call = await SafeGetCall(chatId).ConfigureAwait(false);
            var isJoining = call?.Id == callId;
            // Another call took the chat since the claims were made: joining it now would leave them
            // naming a call that isn't, and replacing it would end a ring somebody is waiting on.
            // And a call that was there to join may be gone: its id must not name the one placed instead,
            // or the close still running for it would take this one's claims along.
            isOvertaken = !isJoining
                && (joinedCallId is not null || GetJoinableCallId(call, callerAuthorId) is not null
                    || IsUnresolvedDial(call));
            if (!isOvertaken)
                await WriteCall(call, isJoining).ConfigureAwait(false);
        }
        if (isOvertaken) {
            await EndUserCalls(
                    chatId, callId, ringing.Prepend(callerAuthorId), CallOutcome.None, CancellationToken.None)
                .ConfigureAwait(false);
            CallMeters.RecordStartRefused(joinedCallId is not null ? "just_ended" : "chat_busy");
            throw StandardError.Constraint(joinedCallId is not null
                ? "The call has just ended. Please try again."
                : "There's already a call in this chat.");
        }

        Log.LogInformation(
            "StartCall: call #{CallId} by author #{AuthorId}, ringing {RingingCount}, busy {BusyCount}",
            callId, callerAuthorId, ringing.Count, busy.Count);
        CallMeters.RecordStarted(chatId, hasVideo, joinedCallId is not null);
        if (ringing.Count > 0) {
            await Services.Queues()
                .Enqueue(
                    new NotificationsBackend_NotifyCall(callId, callerAuthorId, ringing.ToApiArray(), hasVideo),
                    cancellationToken)
                .ConfigureAwait(false);
            _ = ScheduleRingTimeout(chatId);
        }
        // Nobody can answer: finalize now rather than let the caller dial into a call that will never ring.
        if (ringing.Count == 0 && busy.Count > 0)
            await CloseBusyCall(callId, cancellationToken).ConfigureAwait(false);
        return callId;

        // Caller must hold the change lock.
        async Task WriteCall(LiveCall? call, bool isJoining) {
            var now = Clocks.SystemClock.Now;
            // The chat's session, if it has one, is left alone: the call joins it only when answered.
            if (!isJoining) {
                call = new LiveCall {
                    Id = callId,
                    CallerId = callerAuthorId,
                    HasVideo = hasVideo,
                    StartedAt = now,
                    Version = VersionGenerator.NextVersion(),
                };
                await _redisCalls.Set(chatId.Value, call, RingingCallTtl).ConfigureAwait(false);
                await _redisInvites.RemoveHashMap(chatId.Value).ConfigureAwait(false);
            }
            // Dialing status only while unanswered; inviting more people into a connected call isn't "calling".
            await SetCallState(chatId, call!.IsAnswered ? null : NewCallState(call, CallStatus.Dialing))
                .ConfigureAwait(false);
            foreach (var invitee in ringing)
                await _redisInvites.Set(chatId.Value, invitee.Value,
                        new CallInvite {
                            InviteeId = invitee,
                            Status = CallInviteStatus.Ringing,
                            RingingAt = now,
                            CallId = callId,
                        },
                        RingTtl)
                    .ConfigureAwait(false);
            // An invitee already in another call is never rung: the invite is closed here, and they are
            // left out of the notification batch below, so no device of theirs is even pushed.
            foreach (var invitee in busy)
                await _redisInvites.Set(chatId.Value, invitee.Value,
                        new CallInvite {
                            InviteeId = invitee,
                            Status = CallInviteStatus.Busy,
                            RingingAt = now,
                            RespondedAt = now,
                            Ack = RingAck.Busy,
                            AckAt = now,
                            CallId = callId,
                        },
                        RingTtl)
                    .ConfigureAwait(false);
            InvalidateCall(chatId);
        }
    }

    public virtual async Task AcceptCall(
        ChatId chatId,
        AuthorId inviteeAuthorId,
        string? sessionHash,
        string? clientId,
        CallId callId,
        CancellationToken cancellationToken)
    {
        var isReclaimed = await ClaimAnswer(
            chatId, inviteeAuthorId, sessionHash, clientId, callId, cancellationToken).ConfigureAwait(false);
        (bool IsAccepted, bool JustConnected) accepted;
        try {
            accepted = await AcceptInvite(chatId, inviteeAuthorId, callId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            if (e is not OperationCanceledException)
                CallMeters.AcceptRefused.Add(1);
            // A late answer the call refused: to this callee it stays the ring they missed.
            if (isReclaimed)
                await EndUserCall(chatId, callId, inviteeAuthorId, CallOutcome.NoAnswer, CancellationToken.None)
                    .ConfigureAwait(false);
            throw;
        }
        if (accepted.IsAccepted)
            await DismissRing(callId, [inviteeAuthorId], cancellationToken).ConfigureAwait(false);
        if (accepted.JustConnected)
            _ = ScheduleCallPartiesCheck(
                chatId, nameof(EnforceCallConnectGrace), CallConnectGrace, CallEndReason.ConnectTimeout);
    }

    public virtual async Task DeclineCall(
        ChatId chatId,
        AuthorId inviteeAuthorId,
        CallId callId,
        CancellationToken cancellationToken)
    {
        var abandoned = false;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            var call = await SafeGetCall(chatId).ConfigureAwait(false);
            if (IsAnotherCall(call, callId, inviteeAuthorId, nameof(DeclineCall)))
                return;

            var invite = await SafeGetInvite(chatId, inviteeAuthorId).ConfigureAwait(false);
            if (!EnsureValidTransition(chatId, inviteeAuthorId, nameof(DeclineCall),
                    invite?.Status ?? CallInviteStatus.New, invite is { Status: CallInviteStatus.Ringing }))
                return;

            await _redisInvites.Set(chatId.Value, inviteeAuthorId.Value,
                    invite! with { Status = CallInviteStatus.Declined, RespondedAt = Clocks.SystemClock.Now })
                .ConfigureAwait(false);
            abandoned = await IsCallAbandoned(chatId, call).ConfigureAwait(false);
            // Gated on abandoned: a decline while another invitee still rings isn't the call's final story.
            if (abandoned)
                await RecomputeCallStatus(chatId, call, cancellationToken).ConfigureAwait(false);
            // Unlike CallStatus, the outcome is recorded on every decline: first-writer-wins already
            // covers a later accept or cancel rewriting the story.
            await SetOutcome(chatId, call, CallOutcome.Declined).ConfigureAwait(false);
            InvalidateCall(chatId);
        }
        await EndUserCall(chatId, callId, inviteeAuthorId, CallOutcome.Declined, cancellationToken)
            .ConfigureAwait(false);
        await DismissRing(callId, [inviteeAuthorId], cancellationToken).ConfigureAwait(false);
        if (abandoned)
            await EndCall(chatId, CallEndReason.Declined, callId: callId).ConfigureAwait(false);
    }

    public virtual async Task ConfirmRing(
        ChatId chatId, AuthorId inviteeAuthorId, RingAck ack, CancellationToken cancellationToken)
    {
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            var invite = await SafeGetInvite(chatId, inviteeAuthorId).ConfigureAwait(false);
            if (!EnsureValidTransition(chatId, inviteeAuthorId, nameof(ConfirmRing),
                    invite?.Status ?? CallInviteStatus.New, invite is { Status: CallInviteStatus.Ringing }))
                return;

            await _redisInvites.Set(chatId.Value, inviteeAuthorId.Value,
                    invite! with { Ack = ack, AckAt = Clocks.SystemClock.Now })
                .ConfigureAwait(false);
            InvalidateInvites(chatId);
        }
    }

    public virtual async Task CancelCall(
        ChatId chatId,
        AuthorId callerAuthorId,
        CallId callId,
        CancellationToken cancellationToken)
    {
        // The caller hangs up. Unanswered, that cancels the call; answered, it is a party leaving it.
        var ringing = new List<AuthorId>();
        int? callPartiesLeft = null;
        var outcome = CallOutcome.Canceled;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            var call = await SafeGetCall(chatId).ConfigureAwait(false);
            if (IsAnotherCall(call, callId, callerAuthorId, nameof(CancelCall)))
                return;

            // A call is its caller's to cancel; an invitee's way out is DeclineCall.
            if (call.CallerId != callerAuthorId) {
                Log.LogWarning(
                    "{SignalName} rejected for author #{AuthorId}: call #{CallId} was placed by #{CallerId}",
                    nameof(CancelCall), callerAuthorId, callId, call.CallerId);
                return;
            }

            // The client cancels while its slot still reads Dialing, which can outlast the answer by a
            // round trip: the caller is then leaving a connected call, as a hang-up from it would (#4984).
            if (call.IsAnswered) {
                outcome = CallOutcome.Ended;
                await _redisParticipants.Remove(chatId.Value, callerAuthorId.Value).ConfigureAwait(false);
                callPartiesLeft = await CountCallPartiesLeft(chatId, callerAuthorId, cancellationToken)
                    .ConfigureAwait(false);
                InvalidateListParticipants(chatId);
                InvalidateHasRecorder(chatId);
            }
            else {
                var now = Clocks.SystemClock.Now;
                foreach (var info in (await SafeGetInvites(chatId).ConfigureAwait(false)).Values) {
                    if (info is not { Status: CallInviteStatus.Ringing })
                        continue;

                    ringing.Add(info.InviteeId);
                    await _redisInvites.Set(chatId.Value, info.InviteeId.Value,
                            info with { Status = CallInviteStatus.Missed, RespondedAt = now })
                        .ConfigureAwait(false);
                }
                // Hanging up myself needs no status, and it must beat a decline that just landed.
                await SetCallState(chatId, null).ConfigureAwait(false);
                await SetOutcome(chatId, call, CallOutcome.Canceled).ConfigureAwait(false);
            }
            InvalidateCall(chatId);
        }
        await EndUserCalls(chatId, callId, ringing.Prepend(callerAuthorId), outcome, cancellationToken)
            .ConfigureAwait(false);
        if (ringing.Count > 0)
            await DismissRing(callId, ringing, cancellationToken).ConfigureAwait(false);
        if (callPartiesLeft is { } partiesLeft)
            await OnCallPartyLeft(chatId, partiesLeft).ConfigureAwait(false);
        else
            await EndCall(chatId, CallEndReason.Canceled, callId: callId).ConfigureAwait(false);
    }

    // Private methods

    private Task WakeCallTailFlow(ConversationId conversationId)
        // The last utterance's entry can be created after this close, since it's created on the first
        // non-empty transcript - the flow grows the conversation over whatever lands and re-sizes it.
        => FlowHub.NewResumeEvent(new FlowId(LiveFlows.CallTailFlowName, conversationId.Value))
            .WithDelay(Clocks.SystemClock.Now + CallTailDelay, TimeSpan.Zero)
            .Schedule(CancellationToken.None);

    // Caller must hold the change lock. Null when the leaver is no party to an answered call.
    private async Task<int?> CountCallPartiesLeft(
        ChatId chatId, AuthorId leaverId, CancellationToken cancellationToken)
    {
        // Ambient sessions have no two-party invariant: solo dictation is legitimate, and someone else in
        // the session the call was placed into leaving it isn't the call's business.
        var call = await SafeGetCall(chatId).ConfigureAwait(false);
        if (call is not { IsAnswered: true })
            return null;

        var partyIds = await GetCallPartyIds(chatId, call).ConfigureAwait(false);
        if (!partyIds.Contains(leaverId))
            return null;

        var count = await CountFreshCallParties(chatId, partyIds).ConfigureAwait(false);
        if (count >= MinCallParties(chatId) && await SafeGetState(chatId).ConfigureAwait(false) is { IsCall: true } state
            && state.HostAuthorId == leaverId)
            await ReassignHost(chatId, state, cancellationToken).ConfigureAwait(false);
        return count;
    }

    // Outside the change lock: EndCall takes it.
    private async Task OnCallPartyLeft(ChatId chatId, int partiesLeft)
    {
        // Below MinCallParties a call is over: what's left of it gets CallLeaveGrace to come back, and
        // none left closes now.
        if (partiesLeft == 0)
            await EndCall(chatId, CallEndReason.HungUp, mustRecheckParties: true).ConfigureAwait(false);
        else if (partiesLeft < MinCallParties(chatId))
            _ = ScheduleCallPartiesCheck(
                chatId, nameof(EnforceCallLeaveGrace), CallLeaveGrace, CallEndReason.PartyLeft);
    }

    private static int MinCallParties(ChatId chatId)
        // A peer call is its two parties, so either one leaving ends it. Elsewhere a call goes on while anyone
        // is in it and ends once the last one leaves: nobody stays on a call that ended under them unseen.
        => chatId.Kind == ChatKind.Peer ? 2 : 1;

    private async Task ReassignHost(ChatId chatId, LiveSessionState state, CancellationToken cancellationToken)
    {
        // Without this the host slot would keep pointing at someone who already left, and in a call
        // with no Owner present nobody could take it over.
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        var candidateIds = participants.Keys
            .Select(x => AuthorId.TryParse(x, out var id) ? id : null)
            .SkipNullItems()
            .ToList();
        if (candidateIds.Count == 0)
            return;

        var ownerIds = (await RolesBackend
            .ListSystemRoleAuthorIds(ChatsBackend, chatId, SystemRole.Owner, cancellationToken)
            .ConfigureAwait(false))
            .ToHashSet();
        var newHost = candidateIds.FirstOrDefault(ownerIds.Contains) ?? candidateIds[0];
        await _redisStates
            .Set(chatId.Value, state with {
                HostAuthorId = newHost,
                Version = VersionGenerator.NextVersion(state.Version),
            })
            .ConfigureAwait(false);
        InvalidateState(chatId);
    }

    private Task ScheduleCallPartiesCheck(ChatId chatId, string checkName, TimeSpan grace, CallEndReason reason)
        // Logged as the pair to EnforceCallParties' verdict: the two lines bracket the window a client has
        // to get (or get back) its presence up, which is what a slow accept or a presence dip loses.
        => BackgroundTask.Run(async () => {
            // The check is for the call the chat is in now: by the time it runs, that one may be over
            // and the next one connecting - with a grace of its own.
            var callId = (await SafeGetCall(chatId).ConfigureAwait(false))?.Id;
            Log.LogInformation(
                "{CheckName}: call #{CallId} - checking back in {Grace}",
                checkName, callId?.Value ?? chatId.Value, grace.ToShortString());
            await Task.Delay(grace).ConfigureAwait(false);
            await EnforceCallParties(chatId, checkName, grace, reason, callId).ConfigureAwait(false);
        }, Log, $"{checkName} check failed for chat #{chatId}");

    private Task ScheduleRingTimeout(ChatId chatId)
        // GetCall's self-heal alone notices a lapsed ring only on its next period, up to SelfHealDelay late
        // (#4755). The margin keeps a timer that fires a hair early from finding the ring still fresh.
        => BackgroundTask.Run(async () => {
            await Task.Delay(RingTimeout + TimeSpan.FromSeconds(0.5)).ConfigureAwait(false);
            await ExpireRings(chatId).ConfigureAwait(false);
        }, Log, $"Ring timeout check failed for chat #{chatId}");

    private Task ScheduleAnswerGraceEnd(ChatId chatId)
        // A missed ring holds the call open for a late answer, and GetCall's self-heal that would close it
        // otherwise is SelfHealDelay away - the caller would keep dialing that much longer.
        => BackgroundTask.Run(async () => {
            await Task.Delay(AnswerGrace).ConfigureAwait(false);
            await ExpireRings(chatId).ConfigureAwait(false);
        }, Log, $"Answer-grace check failed for chat #{chatId}");

    // AcceptCall schedules this once, fire-and-forget, CallConnectGrace after the first answer.
    // Internal so a test can drive it directly, without a real wait - mirrors ExpireRings.
    internal Task EnforceCallConnectGrace(ChatId chatId)
        => EnforceCallParties(
            chatId, nameof(EnforceCallConnectGrace), CallConnectGrace, CallEndReason.ConnectTimeout);

    // SetParticipation schedules this, CallLeaveGrace after a leave that left the call one party.
    // Internal so a test can drive it directly, without a real wait.
    internal Task EnforceCallLeaveGrace(ChatId chatId)
        => EnforceCallParties(chatId, nameof(EnforceCallLeaveGrace), CallLeaveGrace, CallEndReason.PartyLeft);

    private async Task EnforceCallParties(
        ChatId chatId,
        string checkName,
        TimeSpan grace,
        CallEndReason reason,
        CallId? callId = null)
    {
        try {
            var shouldClose = false;
            using (Computed.BeginIsolation())
            using (await _changeLocks.Lock(chatId, CancellationToken.None).ConfigureAwait(false)) {
                var call = await SafeGetCall(chatId).ConfigureAwait(false);
                if (callId is not null && call is not null && call.Id != callId)
                    Log.LogInformation(
                        "{CheckName}: call #{CallId} is over, chat #{ChatId} is in #{CurrentCallId} - nothing to check",
                        checkName, callId, chatId, call.Id);
                else if (call is { IsAnswered: true }) {
                    callId = call.Id;
                    var partyIds = await GetCallPartyIds(chatId, call).ConfigureAwait(false);
                    var partyCount = await CountFreshCallParties(chatId, partyIds).ConfigureAwait(false);
                    if (partyCount < MinCallParties(chatId))
                        shouldClose = true;
                    else
                        await RecomputeCallStatus(chatId, call, CancellationToken.None).ConfigureAwait(false);

                    // The verdict this method exists for, and the one thing that tells a closed call
                    // apart from one a party left: a client registers its presence through its
                    // participation sync, so the count here is what the race actually turns on.
                    Log.LogInformation(
                        "{CheckName}: chat #{ChatId} - {PartyCount} call part(ies) present after {Grace}, {Verdict}",
                        checkName, chatId, partyCount, grace.ToShortString(),
                        shouldClose ? "closing the call" : "keeping it");
                }
                else
                    Log.LogInformation(
                        "{CheckName}: chat #{ChatId} - no answered call to check", checkName, chatId);
            }
            if (shouldClose)
                await EndCall(chatId, reason, mustRecheckParties: true, callId).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "{CheckName} failed for chat #{ChatId}", checkName, chatId);
        }
    }

    // Drives CallInviteStatus.Active/Ended (and the caller-side equivalent on CallState) from genuine
    // presence, by the same freshness rule Ambient sessions already use - never from a
    // raw participant count, and never written inline from SetParticipation, which has no way to detect
    // a silent crash. Internal so a test can drive it directly, without a real self-heal wait.
    internal async Task SyncCallParticipantActivity(
        ChatId chatId, LiveCall call, CancellationToken cancellationToken)
    {
        if (!call.IsAnswered)
            return;

        try {
            using (Computed.BeginIsolation())
            using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
                // Re-read under the lock instead of trusting the pre-lock `call` parameter: a
                // concurrent CancelCall/DeclineCall/EndCall may have already torn the call down, and
                // RecomputeCallStatus below must not resurrect a status for a call that no longer exists.
                var freshCall = await SafeGetCall(chatId).ConfigureAwait(false);
                if (freshCall is not { IsAnswered: true } || freshCall.Id != call.Id)
                    return;

                // Read once the lock is held (mirrors EnforceCallConnectGrace), not before: GetCall's
                // self-heal fires a new Sync on every tick, so several can be queued on this same chat's
                // lock at once - reading freshness before the lock would let a stale snapshot from an
                // earlier, slower tick win the write race and revert a just-applied Ended back to Active.
                var freshAuthorIds = (await GetFreshParticipantIds(chatId).ConfigureAwait(false)).ToHashSet();
                var invites = await SafeGetInvites(chatId).ConfigureAwait(false);
                var callerId = freshCall.CallerId;
                // Gate the "become Active" transitions on how many of THIS call's own roster (the
                // caller plus its own invitees) are fresh - not the chat-wide fresh count, which an
                // unrelated bystander (an already-started Ambient session, or someone else's unrelated
                // stream in the same chat) could satisfy on its own and flip CallerActiveAt before
                // anyone in this call has actually answered.
                var callRosterFreshCount = (freshAuthorIds.Contains(callerId) ? 1 : 0)
                    + invites.Values.Count(i => i is not null && freshAuthorIds.Contains(i.InviteeId));

                var changed = await SyncCallerActivity(
                        chatId, freshCall, callerId, freshAuthorIds, callRosterFreshCount)
                    .ConfigureAwait(false);
                changed |= await SyncInviteeActivity(chatId, invites, freshAuthorIds, callRosterFreshCount)
                    .ConfigureAwait(false);
                if (changed)
                    await RecomputeCallStatus(chatId, freshCall, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "SyncCallParticipantActivity failed for chat #{ChatId}", chatId);
        }
    }

    // Caller must hold the change lock.
    private async Task<bool> SyncCallerActivity(
        ChatId chatId,
        LiveCall call,
        AuthorId callerId,
        HashSet<AuthorId> freshAuthorIds,
        int callRosterFreshCount)
    {
        var callState = await SafeGetCallState(chatId).ConfigureAwait(false);
        var isFresh = freshAuthorIds.Contains(callerId);
        var wasActive = callState is { CallerActiveAt: not null, CallerEndedAt: null };
        // CallerEndedAt is a ratchet too, same as the invitee side's Declined/Missed/Ended check below -
        // once the caller has been marked ended, a later fresh reading must not flip them back to Active.
        var isTerminal = callState is { CanceledAt: not null } or { CallerEndedAt: not null };

        // Gated on >= 2 fresh participants from THIS CALL's own roster (see callRosterFreshCount above),
        // not the caller alone: the caller is fresh from the moment the call is answered (AcceptInvite
        // registers them), so without this Derive would see a lone active party and report the whole
        // call Ended before the invitee even connected.
        if (isFresh && callRosterFreshCount >= 2 && !wasActive && !isTerminal) {
            await SetCallState(chatId, (callState ?? NewCallState(call, CallStatus.Dialing)) with {
                CallerActiveAt = callState?.CallerActiveAt ?? Clocks.SystemClock.Now,
            }).ConfigureAwait(false);
            return true;
        }
        if (!isFresh && wasActive) {
            await SetCallState(chatId, callState! with { CallerEndedAt = Clocks.SystemClock.Now })
                .ConfigureAwait(false);
            return true;
        }
        if (isFresh && isTerminal)
            Log.LogWarning(
                "SyncCallParticipantActivity: caller #{AuthorId} of chat #{ChatId} reactivated after "
                + "a terminal status - ignored", callerId, chatId);
        return false;
    }

    // Caller must hold the change lock.
    private async Task<bool> SyncInviteeActivity(
        ChatId chatId,
        Dictionary<string, CallInvite?> invites,
        HashSet<AuthorId> freshAuthorIds,
        int callRosterFreshCount)
    {
        var changed = false;
        foreach (var (authorIdValue, invite) in invites) {
            if (invite is null || !AuthorId.TryParse(authorIdValue, out var authorId))
                continue;

            var isFresh = freshAuthorIds.Contains(authorId);
            var now = Clocks.SystemClock.Now;
            switch (invite.Status) {
                // The roster count keeps a lone fresh invitee from turning Active on its own; only an answered
                // call syncs, so one present in the session the call rang into isn't answered for them.
                case CallInviteStatus.Ringing when isFresh && callRosterFreshCount >= 2:
                case CallInviteStatus.Accepted when isFresh && callRosterFreshCount >= 2:
                    await _redisInvites.Set(chatId.Value, authorIdValue,
                            invite with { Status = CallInviteStatus.Active, ActiveAt = now })
                        .ConfigureAwait(false);
                    changed = true;
                    break;
                case CallInviteStatus.Active when !isFresh:
                    await _redisInvites.Set(chatId.Value, authorIdValue,
                            invite with { Status = CallInviteStatus.Ended, EndedAt = now })
                        .ConfigureAwait(false);
                    changed = true;
                    break;
                case CallInviteStatus.Declined or CallInviteStatus.Missed or CallInviteStatus.Ended when isFresh:
                    Log.LogWarning(
                        "SyncCallParticipantActivity: invitee #{AuthorId} of chat #{ChatId} reactivated "
                        + "after status {Status} - ignored", authorId, chatId, invite.Status);
                    break;
            }
        }
        if (changed)
            InvalidateInvites(chatId);
        return changed;
    }

    // The user's call lives on the user's shard, so each of these is an RPC - never call them
    // while holding a chat's change lock.
    private async Task<bool> ClaimUserCall(
        ChatId chatId,
        CallId callId,
        AuthorId authorId,
        CallRole role,
        CallPhase phase,
        AuthorId? peerId,
        bool hasVideo,
        string? sessionHash,
        string? clientId,
        CancellationToken cancellationToken)
    {
        if (await GetUserId(chatId, authorId, cancellationToken).ConfigureAwait(false) is not { } userId)
            return true; // No user behind this author: nothing to arbitrate, so never block the call

        var call = new UserCallClaim {
            ChatId = chatId,
            AuthorId = authorId,
            Role = role,
            Phase = phase,
            PeerId = peerId,
            HasVideo = hasVideo,
            SessionHash = sessionHash,
            ClientId = clientId,
            CallId = callId,
        };
        return await CallsBackend.TryClaim(userId, call, cancellationToken).ConfigureAwait(false);
    }

    // Reports whether this answer was taken, and whether it was the first one; neither for an answer
    // that was already given.
    private async Task<(bool IsAccepted, bool JustConnected)> AcceptInvite(
        ChatId chatId, AuthorId inviteeAuthorId, CallId callId, CancellationToken cancellationToken)
    {
        var justConnected = false;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            var call = await SafeGetCall(chatId).ConfigureAwait(false);
            // The ring this answer was tapped on is gone, and what rings here now is another call.
            if (IsAnotherCall(call, callId, inviteeAuthorId, nameof(AcceptCall)))
                throw StandardError.Constraint("There's no ring left to accept in this chat.");

            var invite = await SafeGetInvite(chatId, inviteeAuthorId).ConfigureAwait(false);
            // Answering a call that is already ours is the same answer, not an error: an RPC resend
            // after a reconnect, a second tap, and SyncInviteeActivity's own Ringing -> Active promotion
            // all land here. The caller tears its call down on a throw, so this has to stay idempotent.
            if (invite is { Status: CallInviteStatus.Accepted or CallInviteStatus.Active })
                return (false, false);

            var now = Clocks.SystemClock.Now;
            var isLate = IsAnswerableMissedRing(invite, call, now);
            // Runs under the change lock, so it's the answer - a client-side check races the call it
            // reads. And a call that already has an outcome is over, whatever its invites still say.
            var isValid = (invite is { Status: CallInviteStatus.Ringing } || isLate)
                && call.Outcome is CallOutcome.None or CallOutcome.Declined;
            if (!EnsureValidTransition(chatId, inviteeAuthorId, nameof(AcceptCall),
                    invite?.Status ?? CallInviteStatus.New, isValid))
                throw StandardError.Constraint("There's no ring left to accept in this chat.");

            if (isLate)
                Log.LogInformation(
                    "AcceptCall: call #{CallId}, author #{AuthorId} answered a missed ring {Delay} after it began",
                    callId, inviteeAuthorId, (now - invite!.RingingAt).ToShortString());
            await _redisInvites.Set(chatId.Value, inviteeAuthorId.Value,
                    invite! with { Status = CallInviteStatus.Accepted, RespondedAt = now })
                .ConfigureAwait(false);

            if (!call.IsAnswered) {
                call = call with { AnsweredAt = now, Version = VersionGenerator.NextVersion(call.Version) };
                await _redisCalls.Set(chatId.Value, call).ConfigureAwait(false);
                await JoinSession(call, inviteeAuthorId, cancellationToken).ConfigureAwait(false);
                // The answer is the caller's "accepted" moment - a brief confirmation before this fades.
                await RecomputeCallStatus(chatId, call, cancellationToken).ConfigureAwait(false);
                justConnected = true;
            }
            // Both claims turn Active on their own: CallsBackend reads the phase off the invite and the
            // call this has just written.
            InvalidateCall(chatId);
            return (true, justConnected);
        }
    }

    // Caller must hold the change lock. The first answer gives the call its session: a new one the call
    // started, or the one already in the chat, which the call then leaves as it was found.
    private async Task JoinSession(LiveCall call, AuthorId inviteeAuthorId, CancellationToken cancellationToken)
    {
        var chatId = call.ChatId;
        if (await SafeGetState(chatId).ConfigureAwait(false) is null) {
            var lidRangeEnd = (await ChatsBackend
                .GetLidRange(chatId, false, cancellationToken)
                .ConfigureAwait(false)).End;
            var version = VersionGenerator.NextVersion();
            var state = new LiveSessionState {
                ChatId = chatId,
                FirstSpeechLid = lidRangeEnd,
                // The ring, not the answer: the call card's span is set from the answer at its close.
                FirstSpeechAt = call.StartedAt,
                OpenConversation = new LiveConversation {
                    Id = ConversationId.New(chatId, lidRangeEnd),
                    SummaryEndLid = lidRangeEnd,
                    StartedAt = call.AnsweredAt ?? Clocks.SystemClock.Now,
                    AuthorIds = [call.CallerId, inviteeAuthorId],
                    ParticipantIds = [call.CallerId, inviteeAuthorId],
                    CallerId = call.CallerId,
                    Version = version,
                },
                AuthorIds = [call.CallerId, inviteeAuthorId],
                HostAuthorId = call.CallerId,
                CallerId = call.CallerId,
                Kind = LiveSessionKind.Call,
                Version = version,
            };
            await _redisStates.Set(chatId.Value, state).ConfigureAwait(false);
            // The caller is present from the answer; the invitee only once their client listens or records,
            // which is what lets EnforceCallConnectGrace tell "accepted" from "connected". The caller's own
            // client hasn't heard of the answer yet, so to the members it isn't there until it joins.
            await EnsureParticipant(chatId, call.CallerId, isPlaceholder: true).ConfigureAwait(false);
            InvalidateState(chatId);
        }
    }

    // Taken before the answer is written. A ring's claim names no client, so every client of the callee
    // sees it; the answer makes it the answering client's alone (#4929). A missed ring's claim is gone
    // (ExpireRings released it), so a late answer takes it anew - and a callee who took another call
    // meanwhile can't also connect this one. Reports whether it was taken anew, for the caller to
    // release it if the answer is refused after all.
    private async Task<bool> ClaimAnswer(
        ChatId chatId,
        AuthorId inviteeAuthorId,
        string? sessionHash,
        string? clientId,
        CallId callId,
        CancellationToken cancellationToken)
    {
        var call = await SafeGetCall(chatId).ConfigureAwait(false);
        // Refused for good under the change lock, in AcceptInvite; here it only must not claim anything.
        if (call?.Id != callId)
            return false;

        var invite = await SafeGetInvite(chatId, inviteeAuthorId).ConfigureAwait(false);
        var isMissed = invite is { Status: CallInviteStatus.Missed };
        var isRingToOwn = invite is { Status: CallInviteStatus.Ringing } && sessionHash is not null;
        if (!isMissed && !isRingToOwn)
            return false;

        if (!await ClaimUserCall(chatId, callId, inviteeAuthorId, CallRole.Callee, CallPhase.Active,
                call.CallerId, call.HasVideo, sessionHash, clientId, cancellationToken)
                .ConfigureAwait(false))
            throw StandardError.Constraint("You're already in another call.");

        return isMissed;
    }

    private async Task EndUserCall(
        ChatId chatId,
        CallId? callId,
        AuthorId authorId,
        CallOutcome outcome,
        CancellationToken cancellationToken)
    {
        // Frees the user, and tells the client running the call that it is over - the one signal that
        // client stops the call's media on.
        // Only a call takes claims, and every call has an id: a session with none is an ambient one.
        if (callId is null)
            return;

        if (await GetUserId(chatId, authorId, cancellationToken).ConfigureAwait(false) is { } userId)
            await CallsBackend.EndCall(userId, callId, outcome, cancellationToken).ConfigureAwait(false);
    }

    private async Task EndUserCalls(
        ChatId chatId,
        CallId? callId,
        IEnumerable<AuthorId> authorIds,
        CallOutcome outcome,
        CancellationToken cancellationToken)
    {
        foreach (var authorId in authorIds)
            await EndUserCall(chatId, callId, authorId, outcome, cancellationToken).ConfigureAwait(false);
    }

    // A request is for the call it names only: by the time it arrives the chat may be in the next one,
    // or in none.
    private bool IsAnotherCall(
        [NotNullWhen(false)] LiveCall? call,
        CallId callId,
        AuthorId authorId,
        string signalName)
    {
        if (call?.Id == callId)
            return false;

        Log.LogWarning(
            "{SignalName} rejected for author #{AuthorId}: it names call #{CallId}, the chat is in #{CurrentCallId}",
            signalName, authorId, callId, call?.Id.Value ?? "none");
        return true;
    }

    // The call a StartCall joins instead of placing a new one: an answered one - or the caller's own,
    // still dialing: an RPC resent after a reconnect must find the call it placed.
    private static CallId? GetJoinableCallId(LiveCall? call, AuthorId callerAuthorId)
    {
        var isOwnDial = IsUnresolvedDial(call) && call!.CallerId == callerAuthorId;
        return call is { IsAnswered: true } || isOwnDial ? call!.Id : null;
    }

    private static bool IsUnresolvedDial(LiveCall? call)
        => call is { IsResolved: false };

    private CallId NewCallId(ChatId chatId)
    {
        // The start time in ms: it reads in a log, and sorts. Bumped past the last one issued here, so
        // two calls never share it.
        var nowMs = (long)Clocks.SystemClock.Now.EpochOffset.TotalMilliseconds;
        long last, next;
        do {
            last = Interlocked.Read(ref _lastCallLocalId);
            next = Math.Max(nowMs, last + 1);
        } while (Interlocked.CompareExchange(ref _lastCallLocalId, next, last) != last);
        return CallId.New(chatId, next.ToString());
    }

    private async Task<UserId?> GetUserId(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
    {
        var author = await AuthorsBackend
            .Get(chatId, authorId, RequestedAuthorKind.Full, cancellationToken)
            .ConfigureAwait(false);
        // A guest's id is a user id all the same: they can be in a call, so they can be busy in one.
        return author?.UserId is { } userId && !userId.Value.IsNullOrEmpty() ? userId : null;
    }

    // Every invitee was busy, so this call can never ring: tell the caller and close it, instead of
    // leaving them dialing until the ring times out.
    private async Task CloseBusyCall(CallId callId, CancellationToken cancellationToken)
    {
        var chatId = callId.ChatId;
        AuthorId callerId;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            var call = await SafeGetCall(chatId).ConfigureAwait(false);
            if (call is not { IsAnswered: false } || call.Id != callId)
                return;

            callerId = call.CallerId;
            await SetCallState(chatId, NewCallState(call, CallStatus.Busy)).ConfigureAwait(false);
            await SetOutcome(chatId, call, CallOutcome.Busy).ConfigureAwait(false);
        }

        await EndUserCall(chatId, callId, callerId, CallOutcome.Busy, cancellationToken).ConfigureAwait(false);
        await EndCall(chatId, CallEndReason.Busy, callId: callId).ConfigureAwait(false);
    }

    private async Task<LiveCall?> SafeGetCall(ChatId chatId)
    {
        try {
            return await _redisCalls.Get(chatId.Value).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read the call from Redis for chat #{ChatId}", chatId);
            return null;
        }
    }

    private async Task<CallState?> SafeGetCallState(ChatId chatId)
    {
        try {
            return await _redisCallStates.Get(chatId.Value).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read call state from Redis for chat #{ChatId}", chatId);
            return null;
        }
    }

    private async Task SetCallState(ChatId chatId, CallState? callState)
    {
        try {
            if (callState is null)
                await _redisCallStates.Remove(chatId.Value).ConfigureAwait(false);
            else
                await _redisCallStates.Set(chatId.Value, callState, CallStateTtl(callState.Status)).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to write call state to Redis for chat #{ChatId}", chatId);
            return;
        }

        using (Invalidation.Begin())
            _ = GetCallState(chatId, default);
    }

    // Internal (rather than private) to be accessible from tests: every real caller of this method also
    // unconditionally closes the call in the same operation once its outcome is the abandoning one, so the
    // guard below can't be pinned by observing Redis state through the close - it has to be called directly.
    internal async Task SetOutcome(ChatId chatId, LiveCall call, CallOutcome outcome)
    {
        // First writer wins: the earliest terminal response decides the outcome, and nothing later
        // may overwrite it - including a decline outranking another invitee's ring later timing out
        // into NoAnswer, and any outcome at all once the call was answered. Callers hold _changeLocks,
        // so this read and write are atomic.
        if (call.IsResolved)
            return;

        await _redisCalls.Set(chatId.Value, call with {
            Outcome = outcome,
            Version = VersionGenerator.NextVersion(call.Version),
        }, RingingCallTtl).ConfigureAwait(false);
        InvalidateCall(chatId);
    }

    private CallState NewCallState(LiveCall call, CallStatus status, CallState? previous = null)
        => new() {
            CallerId = call.CallerId,
            Status = status,
            ChangedAt = Clocks.SystemClock.Now,
            CallerActiveAt = previous?.CallerActiveAt,
            CallerEndedAt = previous?.CallerEndedAt,
            CanceledAt = previous?.CanceledAt,
            CallId = call.Id,
        };

    internal static CallStatus Derive(CallState? callState, IReadOnlyCollection<CallInvite?> invites)
    {
        var everActive = callState?.CallerActiveAt is not null
            || invites.Any(i => i?.ActiveAt is not null);
        var activeCount = (callState?.CallerActiveAt is not null && callState.CallerEndedAt is null
                ? 1
                : 0)
            + invites.Count(i => i is { Status: CallInviteStatus.Active });

        if (activeCount >= 2)
            return CallStatus.Active;
        if (everActive)
            return CallStatus.Ended;
        if (callState?.CanceledAt is not null)
            return CallStatus.Canceled;
        if (invites.Any(i => i is { Status: CallInviteStatus.Accepted }))
            return CallStatus.Connecting;
        if (invites.Any(i => i is { Status: CallInviteStatus.Declined }))
            return CallStatus.Declined;
        if (invites.Count > 0 && invites.All(i => i is { Status: CallInviteStatus.Missed }))
            return CallStatus.NoAnswer;
        return CallStatus.Dialing;
    }

    private async Task RecomputeCallStatus(
        ChatId chatId, LiveCall call, CancellationToken cancellationToken)
    {
        var callState = await SafeGetCallState(chatId).ConfigureAwait(false);
        var invites = (await SafeGetInvites(chatId).ConfigureAwait(false)).Values;
        var status = Derive(callState, invites);
        if (callState is null && status == CallStatus.Dialing)
            return;

        await SetCallState(chatId, NewCallState(call, status, callState)).ConfigureAwait(false);
    }

    private bool EnsureValidTransition<TStatus>(
        ChatId chatId, AuthorId authorId, string signalName,
        TStatus currentStatus, bool isValid)
        where TStatus : Enum
    {
        if (isValid)
            return true;

        Log.LogWarning(
            "{SignalName} rejected for chat #{ChatId}, author #{AuthorId}: not valid from status {CurrentStatus}",
            signalName, chatId, authorId, currentStatus);
        return false;
    }

    private static TimeSpan CallStateTtl(CallStatus status)
        => status is CallStatus.Dialing or CallStatus.Connecting or CallStatus.Active
            ? DialingStateTtl
            : ResolvedStateTtl;

    private async Task<CallInvite?> SafeGetInvite(ChatId chatId, AuthorId inviteeAuthorId)
    {
        try {
            return await _redisInvites.Get(chatId.Value, inviteeAuthorId.Value).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read call invites from Redis for chat #{ChatId}", chatId);
            return null;
        }
    }

    private async Task<Dictionary<string, CallInvite?>> SafeGetInvites(ChatId chatId)
    {
        try {
            return await _redisInvites.GetHashMap(chatId.Value).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read call invites from Redis for chat #{ChatId}", chatId);
            return [];
        }
    }

    private async Task<bool> HasStaleRinging(ChatId chatId)
    {
        var cutoff = Clocks.SystemClock.Now - RingTimeout;
        var invites = await SafeGetInvites(chatId).ConfigureAwait(false);
        return invites.Values.Any(i => i is { Status: CallInviteStatus.Ringing } && i.RingingAt <= cutoff);
    }

    private async Task<bool> HasFreshRing(ChatId chatId)
    {
        var cutoff = Clocks.SystemClock.Now - RingTimeout;
        var invites = await SafeGetInvites(chatId).ConfigureAwait(false);
        return invites.Values.Any(i => i is { Status: CallInviteStatus.Ringing } && i.RingingAt > cutoff);
    }

    private static bool IsAnswerableMissedRing(CallInvite? invite, LiveCall call, Moment now)
        // A cancel marks the rings Missed too, and a resolved call has nobody left to answer.
        => IsInAnswerGrace(invite, now)
            && call is { IsAnswered: false, Outcome: CallOutcome.None or CallOutcome.Declined };

    private static bool IsInAnswerGrace(CallInvite? invite, Moment now)
        => invite is { Status: CallInviteStatus.Missed } && now - invite.RingingAt < RingTimeout + AnswerGrace;

    private Task DismissRing(CallId callId, IReadOnlyList<AuthorId> invitees, CancellationToken cancellationToken)
        => Services.Queues().Enqueue(new NotificationsBackend_CancelCall(callId, invitees), cancellationToken);

    // An unanswered invitee rang past RingTimeout: mark Missed and stop the ring (the caller still
    // sees "missed" and can hang up); the call closes as NoAnswer only once AnswerGrace is over too.
    // Fired by the ring timer and by GetCall's self-heal.
    // A dialing call is finalized here even when no invite is left to expire — a ring can vanish via
    // its RingTtl before this catches it, and the call must still reach an outcome rather than linger.
    // Internal rather than private so a test can drive it directly, without a real ring timeout.
    internal async Task ExpireRings(ChatId chatId)
    {
        try {
            LiveCall? call;
            var expired = new List<AuthorId>();
            var wasDialing = false;
            using (Computed.BeginIsolation())
            using (await _changeLocks.Lock(chatId, CancellationToken.None).ConfigureAwait(false)) {
                call = await SafeGetCall(chatId).ConfigureAwait(false);
                if (call is null)
                    return;

                wasDialing = !call.IsAnswered;
                var now = Clocks.SystemClock.Now;
                var cutoff = now - RingTimeout;
                foreach (var info in (await SafeGetInvites(chatId).ConfigureAwait(false)).Values) {
                    if (info is not { Status: CallInviteStatus.Ringing } || info.RingingAt > cutoff)
                        continue;

                    expired.Add(info.InviteeId);
                    await _redisInvites.Set(chatId.Value, info.InviteeId.Value,
                            info with { Status = CallInviteStatus.Missed, RespondedAt = now })
                        .ConfigureAwait(false);
                }
                if (expired.Count > 0)
                    InvalidateCall(chatId);
            }
            await EndUserCalls(chatId, call.Id, expired, CallOutcome.NoAnswer, CancellationToken.None)
                .ConfigureAwait(false);
            if (expired.Count > 0)
                await DismissRing(call.Id, expired, CancellationToken.None).ConfigureAwait(false);
            if (expired.Count > 0)
                _ = ScheduleAnswerGraceEnd(chatId);
            if ((expired.Count > 0 || wasDialing) && await IsCallAbandoned(chatId, call).ConfigureAwait(false)) {
                // The lock is retaken here because the outcome must be decided from a fresh read:
                // IsCallAbandoned above reads Redis without the lock, so it can catch AcceptCall's own
                // locked section mid-flight and come back true for a call answered a moment later.
                // shouldClose is decided from this same fresh, locked read, so a call that got there
                // before us keeps running instead of being torn down right after the client sees it connect.
                var shouldClose = false;
                using (Computed.BeginIsolation())
                using (await _changeLocks.Lock(chatId, CancellationToken.None).ConfigureAwait(false)) {
                    // Only a still-dialing call gets a "no answer" - a connected one that emptied out just closes.
                    var freshCall = await SafeGetCall(chatId).ConfigureAwait(false);
                    if (freshCall is { IsAnswered: false } current && current.Id == call.Id
                        && await SafeGetCallState(chatId).ConfigureAwait(false)
                            is null or { Status: CallStatus.Dialing }) {
                        shouldClose = true;
                        await RecomputeCallStatus(chatId, current, CancellationToken.None).ConfigureAwait(false);
                        // Derive falls back to Dialing when there are no invite facts to work with
                        // (e.g. a zero-invitee StartCall, or every invite already vanished via its own
                        // RingTtl) - this is the call being closed as abandoned, so that generic
                        // fallback must not leave CallState stuck at Dialing past the call's own close.
                        var recomputed = await SafeGetCallState(chatId).ConfigureAwait(false);
                        if (recomputed is { Status: CallStatus.Dialing })
                            await SetCallState(chatId, NewCallState(current, CallStatus.NoAnswer, recomputed))
                                .ConfigureAwait(false);
                        await SetOutcome(chatId, current, CallOutcome.NoAnswer).ConfigureAwait(false);
                    }
                    else if (freshCall is not null)
                        // The unlocked check above read this call as abandoned, but it wasn't - flag it
                        // so a near-miss here is visible if this class of race ever fires for real.
                        Log.LogWarning(
                            "ExpireRings: abandon check for chat #{ChatId} was stale - "
                            + "call #{CallId} is already answered or replaced, not closing",
                            chatId, freshCall.Id);
                }
                // Outside the lock: EndCall takes the same non-reentrant lock.
                if (shouldClose)
                    await EndCall(chatId, CallEndReason.NoAnswer, callId: call.Id).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "ExpireRings failed for chat #{ChatId}", chatId);
        }
    }
}
