using ActualChat.Flows;
using ActualChat.Live;
using ActualChat.Notifications;
using ActualChat.Queues;

namespace ActualChat.Streaming;

public partial class LiveSessionsBackend
{
    public virtual async Task UpdateLiveConversationSummary(
        ChatId chatId,
        LiveConversationSummary summary,
        CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);

        var state = await SafeGetState(chatId).ConfigureAwait(false);
        if (state?.OpenConversation is not { } liveConversation)
            return;

        // The summary flow re-runs on a fixed schedule, so most calls carry an unchanged summary. Bailing
        // out here keeps SummarizedAt/Version from making every run look like a change and invalidating
        // the whole live view for nothing.
        var isUnchanged = liveConversation.Title == summary.Title
            && liveConversation.Description == summary.Description
            && liveConversation.Summary == summary.Summary
            && liveConversation.SummaryEndLid == summary.SummaryEndLid
            && liveConversation.SummarizedEntryCount == summary.SummarizedEntryCount
            && liveConversation.IsExpandedByDefault == summary.IsExpandedByDefault
            && (summary.AuthorIds.Count == 0 || liveConversation.AuthorIds.SequenceEqual(summary.AuthorIds));
        if (isUnchanged)
            return;

        // The first non-empty title promotes the live banner to "Voice chat: {title}" (TITLED fires once).
        var isFirstTitle = liveConversation.Title.IsNullOrEmpty() && !summary.Title.IsNullOrEmpty();
        var version = VersionGenerator.NextVersion(state.Version);
        state = state with {
            OpenConversation = liveConversation with {
                Title = summary.Title,
                Description = summary.Description,
                Summary = summary.Summary,
                SummaryEndLid = summary.SummaryEndLid,
                SummarizedEntryCount = summary.SummarizedEntryCount,
                AuthorIds = summary.AuthorIds.Count > 0 ? summary.AuthorIds : liveConversation.AuthorIds,
                IsExpandedByDefault = summary.IsExpandedByDefault,
                SummarizedAt = Clocks.SystemClock.Now,
                Version = version,
            },
            Version = version,
        };
        await _redisStates.Set(chatId.Value, state).ConfigureAwait(false);
        if (isFirstTitle)
            await EnqueueLiveNotification(
                state, ConversationNotificationPhase.Titled, $"Voice chat: {summary.Title}", cancellationToken)
                .ConfigureAwait(false);
        InvalidateState(chatId);
    }

    public virtual async Task SetLiveConversationContextStart(
        ChatId chatId, long contextStartLid,
        CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);

        var state = await SafeGetState(chatId).ConfigureAwait(false);
        if (state?.OpenConversation is not { ContextStartLid: 0 } liveConversation)
            return;

        var version = VersionGenerator.NextVersion(state.Version);
        state = state with {
            OpenConversation = liveConversation with { ContextStartLid = contextStartLid, Version = version },
            Version = version,
        };
        await _redisStates.Set(chatId.Value, state).ConfigureAwait(false);
        InvalidateState(chatId);
    }

    public virtual async Task FinalizeSession(ChatId chatId, CancellationToken cancellationToken)
    {
        var state = await SafeGetState(chatId).ConfigureAwait(false);
        if (state is null)
            return;
        if (await IsSessionLive(chatId).ConfigureAwait(false))
            return; // someone is streaming again - keep the session live

        await CloseSession(state, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private Task WakeSummaryFlow(ChatId chatId)
        // Runs the flow's final pass at once rather than on its next throttled resume, which is what
        // makes a closing block finalize promptly. The zero quantum keeps the Uuid unquantized: under
        // the flow's own 15s DelayQuanta this wake would share a Uuid with that slot's throttled
        // resume and be dropped as a duplicate - not delayed to it, dropped. FlowId is built by name
        // so the streaming backend needn't reference the Chat.Service flow type.
        => FlowHub.NewResumeEvent(new FlowId(LiveFlows.SummaryFlowName, chatId.Value))
            .WithDelay(TimeSpan.Zero, TimeSpan.Zero)
            .Schedule(CancellationToken.None);

    private Task WakeClosedLiveConversationFlow(ConversationId conversationId)
        // Built by name, like the flows above: the streaming backend doesn't reference Chat.Service
        => FlowHub.NewResumeEvent(new FlowId(LiveFlows.ClosedLiveConversationFlowName, conversationId.Value))
            .WithDelay(Clocks.SystemClock.Now + ClosedPersistDelay, TimeSpan.Zero)
            .Schedule(CancellationToken.None);

    private async Task<LiveConversation> CloseConversation(LiveConversation conversation)
    {
        var chatLidRange = await ChatsBackend
            .GetLidRange(conversation.ChatId, false, CancellationToken.None)
            .ConfigureAwait(false);
        conversation = conversation with {
            EndLid = Math.Max(chatLidRange.End, conversation.SummaryEndLid),
            EndedAt = Clocks.SystemClock.Now,
            Version = VersionGenerator.NextVersion(conversation.Version),
        };
        await _redisClosedLiveConversations
            .Set(conversation.ChatId.Value, conversation.Id.Value, conversation)
            .ConfigureAwait(false);
        return conversation;
    }

    private async Task<IReadOnlyList<LiveConversation>> ListClosedLiveConversations(
        ChatId chatId, CancellationToken cancellationToken)
    {
        var map = await _redisClosedLiveConversations.GetHashMap(chatId.Value).ConfigureAwait(false);
        var result = new List<LiveConversation>();
        foreach (var closed in map.Values.SkipNullItems().OrderByDescending(c => c.StartLid))
            if (!await IsCovered(closed, cancellationToken).ConfigureAwait(false))
                result.Add(closed);
        return result;
    }

    private Task<LiveConversation?> GetClosedLiveConversation(ChatId chatId, ConversationId conversationId)
        => _redisClosedLiveConversations.Get(chatId.Value, conversationId.Value);

    // Saved under the closed conversation's materialized id and reaching its end. The id alone isn't enough:
    // sessions that follow each other share a context start, so a saved conversation of the earlier one
    // carries the later one's id while stopping short of it.
    private async Task<bool> IsCovered(LiveConversation closed, CancellationToken cancellationToken)
    {
        var saved = await ConversationsBackend.Get(closed.MaterializedId, cancellationToken).ConfigureAwait(false);
        return saved is not null && saved.EndEntryLid >= closed.EndLid - 1;
    }

    private Task EnqueueLiveNotification(
        LiveSessionState state,
        ConversationNotificationPhase phase,
        string content,
        CancellationToken cancellationToken)
    {
        // Peer (1:1) chats get no live-session banner - the sole recipient is the other participant,
        // who is already inside the very conversation the session belongs to.
        if (state.ChatId.Kind == ChatKind.Peer)
            return Task.CompletedTask;

        if (state.OpenConversation is not { } liveConversation)
            return Task.CompletedTask;

        var authorIds = liveConversation.AuthorIds.Count > 0 ? liveConversation.AuthorIds : state.AuthorIds;
        return Services.Queues()
            .Enqueue(
                new NotificationsBackend_NotifyConversation(liveConversation.Id, phase, content, liveConversation.SummaryLastLid, authorIds),
                cancellationToken);
    }

    // The state going away doesn't move GetConsolidatedLiveConversationList (it may well have been null already: the backstop
    // close reads the state as null before it writes anything), so the closed conversation is announced here
    private void InvalidateClosed(LiveConversation closed)
    {
        using (Invalidation.Begin()) {
            _ = GetConsolidatedLiveConversationList(closed.ChatId, default);
            _ = GetConsolidatedLiveConversation(closed.Id, default);
        }
    }
}
