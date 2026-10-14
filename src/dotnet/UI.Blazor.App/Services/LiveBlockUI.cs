using ActualChat.Live;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Governs how far a live block's fold boundary is allowed to advance (monotonic viewport-top
/// tracking via <see cref="LiveFoldMath"/>), and retains attended blocks after session closure.
/// </summary>
public class LiveBlockUI(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub), IComputeService, INotifyInitialized
{
    private readonly Dictionary<ChatId, ChatFoldState> _chatStates = new();
    // A too-short (tier-1) session leaves nothing behind, so its block is held briefly on close to
    // fade + collapse out instead of vanishing in one frame.
    internal TimeSpan DissolveDuration = TimeSpan.FromMilliseconds(300);

    private IChats Chats => Hub.Chats;
    private IAuthors Authors => Hub.Authors;
    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod]
    public virtual async Task<LiveBlock?> GetBlock(ChatId chatId, CancellationToken cancellationToken = default)
    {
        // Derive lifecycle changes directly from their sources: waiting for the governor can flash
        // a collapsed frame between leaving/closing and its next iteration.
        var chatState = await GetOrCreateChatState(chatId, cancellationToken).ConfigureAwait(false);
        _ = await chatState.FoldEndLid.Use(cancellationToken).ConfigureAwait(false);
        var liveConversationTask = LiveSessionUI.GetOpenConversation(chatId, cancellationToken);
        var liveConversation = await LiveSessionUI.UseOpenConversationOrLastKnown(chatId, liveConversationTask).ConfigureAwait(false);
        // The server remembers who joined, so attendance survives leaving, reloading and another device
        var hasAttended = liveConversation != null && await HasAttended(chatId, liveConversation, cancellationToken).ConfigureAwait(false);
        ConversationId? closedId = null;
        bool mustBuildTemplate;
        lock (Lock) {
            // A join followed by an immediate leave/close must not outrun the governor, so the descriptor
            // an attended block retains is built here too, idempotently.
            mustBuildTemplate = hasAttended
                && (chatState.Template is not { HasAttended: true } known || known.Conversation.Id != liveConversation!.Id);
            if (liveConversation == null
                && chatState is { IsDismissed: false, Template: { HasAttended: true, HadSummary: true } closingTemplate })
                closedId = closingTemplate.Conversation.Id;
        }
        // The server knows where the closed conversation really ends; the summary's end is what the
        // client saw last while it was live.
        var closed = closedId == null
            ? null
            : await LiveSessionUI.GetClosed(closedId, cancellationToken).ConfigureAwait(false);
        var template = mustBuildTemplate
            ? await BuildTemplate(chatId, liveConversation!, true, cancellationToken).ConfigureAwait(false)
            : null;
        lock (Lock) {
            if (template != null)
                chatState.Template = template;
            // A block shown as closed must dismiss on the very next toggle, not after the governor's pass
            if (liveConversation == null && chatState.Template is { HasAttended: true })
                chatState.IsClosed = true;
            return DeriveBlock(chatState, liveConversation, hasAttended, closed);
        }
    }

    [ComputeMethod]
    public virtual async Task<int> GetSwallowedCount(ChatId chatId, CancellationToken cancellationToken = default)
    {
        // Cached (this is a [ComputeMethod]) but counts real messages via a full read of the swallowed
        // range on each recompute - never approximate with a lid span, since lids have gaps.
        var block = await GetBlock(chatId, cancellationToken).ConfigureAwait(false);
        if (block is not { IsOpen: true } || block.FoldRange.IsEmpty)
            return 0;

        // Bounded to the fold itself: read from the chat end, this would re-walk the whole live tail and
        // depend on every tile of it, for every participant, on every new entry.
        var count = 0;
        var reader = Chats.NewEntryReader(Session, chatId);
        var foldRange = block.FoldRange;
        await foreach (var entry in reader.ReadReverse(foldRange, cancellationToken).ConfigureAwait(false))
            if (!entry.IsSystemEntry)
                count++;
        return count;
    }

    public async Task RevealMore(ChatId chatId, CancellationToken cancellationToken = default)
    {
        ChatFoldState chatState;
        long v, effectiveBoundary;
        lock (Lock) {
            if (!_chatStates.TryGetValue(chatId, out var s) || s.Template is not { } t)
                return;
            chatState = s;
            v = t.Conversation.StartLid;
            effectiveBoundary = Math.Min(chatState.FoldEndLid.Value, chatState.RevealedBoundaryLid);
        }
        if (effectiveBoundary <= v)
            return;

        // Walk back RevealBatchSize real messages from just below the current effective boundary; the
        // last one becomes the new revealed boundary (clamped to V when fewer remain).
        var revealed = v;
        using (Computed.BeginIsolation()) {
            var taken = 0;
            var reader = Chats.NewEntryReader(Session, chatId);
            var foldRange = new Range<long>(v, effectiveBoundary);
            await foreach (var entry in reader.ReadReverse(foldRange, cancellationToken).ConfigureAwait(false)) {
                if (entry.IsSystemEntry)
                    continue;

                revealed = entry.LocalId;
                if (++taken >= LiveFoldMath.RevealBatchSize)
                    break;
            }
        }

        lock (Lock) {
            chatState.RevealedBoundaryLid = Math.Min(chatState.RevealedBoundaryLid, revealed);
            chatState.RevealScrolledInto = false;
        }
        using (Invalidation.Begin())
            _ = GetBlock(chatId, default);
    }

    public void ResetReveal(ChatId chatId)
    {
        lock (Lock) {
            if (!_chatStates.TryGetValue(chatId, out var chatState) || chatState.RevealedBoundaryLid == long.MaxValue)
                return;
            chatState.RevealedBoundaryLid = long.MaxValue;
            chatState.RevealScrolledInto = false;
        }
        using (Invalidation.Begin())
            _ = GetBlock(chatId, default);
    }

    public bool TryDismissClosedBlock(ConversationId conversationId)
    {
        LiveBlockTemplate? dismissed = null;
        lock (Lock) {
            foreach (var chatState in _chatStates.Values) {
                // IsDismissed gates the intercept to a still-visible block: once dismissed, later
                // toggles on the materialized conversation must reach the ordinary expand/collapse path.
                if (!chatState.IsClosed || chatState.IsDismissed || chatState.Template is not { HadSummary: true } t)
                    continue;
                if (t.Conversation.Id != conversationId && t.Conversation.MaterializedId != conversationId)
                    continue;

                chatState.IsDismissed = true;
                dismissed = t;
                break;
            }
        }
        if (dismissed == null)
            return false;

        using (Invalidation.Begin())
            _ = GetBlock(conversationId.ChatId, default);
        if (dismissed.Conversation.Id != dismissed.Conversation.MaterializedId)
            Hub.ChatUI.SuppressAutoExpansion(dismissed.Conversation.Id);
        Hub.ChatUI.EnsureConversationCollapsed(dismissed.Conversation.MaterializedId, dismissed.Conversation.IsExpandedByDefault);
        return true;
    }

    private static LiveBlock? DeriveBlock(
        ChatFoldState chatState, LiveConversation? liveConversation, bool hasAttended, LiveConversation? closed = null)
    {
        var foldEndLid = Math.Min(chatState.FoldEndLid.Value, chatState.RevealedBoundaryLid);
        if (liveConversation != null)
            return new LiveBlock(liveConversation.Id, hasAttended, foldEndLid);
        if (chatState.IsDismissed || chatState.Template is not { HasAttended: true } t)
            return null;

        return t.HadSummary
            ? new LiveBlock(t.Conversation.Id, true, foldEndLid, closed?.EndLid ?? t.Conversation.SummaryEndLid, t.Conversation.MaterializedId)
            : chatState.DissolveDone
                ? null
                : new LiveBlock(t.Conversation.Id, true, foldEndLid, t.ChatEndLid, null, t.DissolvingConversation);
    }

    private async Task<bool> HasAttended(ChatId chatId, LiveConversation conversation, CancellationToken cancellationToken)
    {
        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        return conversation.IsParticipant(ownAuthor?.Id)
            || await LiveSessionUI.AmIInLiveConversation(chatId, cancellationToken).ConfigureAwait(false);
    }

    // Protected/internal methods

    protected override Task OnRun(CancellationToken cancellationToken)
        => RunFoldGovernor(cancellationToken);

    [ComputeMethod]
    protected virtual async Task<GovernorInputs> GetGovernorInputs(CancellationToken cancellationToken)
    {
        var chatId = await Hub.ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return GovernorInputs.None;

        var liveConversation = await LiveSessionUI.GetOpenConversation(chatId, cancellationToken).ConfigureAwait(false);
        var visibility = await Hub.ChatUI.ItemVisibility.Use(cancellationToken).ConfigureAwait(false);
        // Reactive, not polled: GetStreamingTail consolidates transcript-rate churn away and expires
        // itself when the last grace period lapses, so the loop wakes exactly when the floor moves.
        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var hasAttended = liveConversation != null && await HasAttended(chatId, liveConversation, cancellationToken).ConfigureAwait(false);
        var streamingTail = await Hub.ChatUI
            .GetStreamingTail(chatId, ownAuthor?.Id ?? default, cancellationToken)
            .ConfigureAwait(false);
        var tailFloorLid = liveConversation != null
            ? await GetTailFloorLid(chatId, liveConversation.StartLid, cancellationToken).ConfigureAwait(false)
            : long.MaxValue;
        // Resolved from the same latch and states the tile builder resolves expansion from, so the governor
        // and the render cannot disagree on what "collapsed" is. The snapshot already carries the id and the
        // default; the conversation record would only add a churnier dependency.
        var isBlockExpanded = false;
        if (liveConversation != null) {
            await Hub.ChatUI.ConversationExpansionOverrides.Use(cancellationToken).ConfigureAwait(false);
            await Hub.ChatUI.AutoExpandedConversations.Use(cancellationToken).ConfigureAwait(false);
            isBlockExpanded = Hub.ChatUI.IsConversationExpanded(
                ConversationId.New(chatId, liveConversation.StartLid), liveConversation.IsExpandedByDefault);
        }
        var hasTranscript = liveConversation != null
            && await LiveSessionUI.HasTranscript(chatId, cancellationToken).ConfigureAwait(false);
        return new GovernorInputs(
            chatId, liveConversation, visibility, hasAttended, isBlockExpanded, hasTranscript,
            streamingTail.FloorLid, tailFloorLid);
    }

    [ComputeMethod(ConsolidationDelay = 1)]
    protected virtual async Task<long> GetTailFloorLid(
        ChatId chatId,
        long visibleStartLid,
        CancellationToken cancellationToken)
    {
        // Consolidated, and unlike GetStreamingTail with a delay: the scan re-runs whenever a tail entry
        // changes, but the lid it yields moves only when one is added or removed, and the fold it feeds
        // rebuilds the whole message list. Nothing waits on this floor, so debouncing it is free.
        var floorLid = visibleStartLid;
        var count = 0;
        await foreach (var entry in Chats.ReadReverse(Session, chatId, cancellationToken).ConfigureAwait(false)) {
            if (entry.IsSystemEntry)
                continue;
            if (entry.LocalId < visibleStartLid)
                break;

            floorLid = entry.LocalId;
            if (++count >= LiveFoldMath.MinTailEntryCount)
                break;
        }

        return count < LiveFoldMath.MinTailEntryCount ? visibleStartLid : floorLid;
    }

    // Private methods

    private async Task<ChatFoldState> GetOrCreateChatState(ChatId chatId, CancellationToken cancellationToken)
    {
        lock (Lock)
            if (_chatStates.TryGetValue(chatId, out var existing))
                return existing;

        // Isolated read: the initial seed must not make the governor's boundary state reactive to
        // the raw live state - only subsequent advances go through the viewport governor.
        LiveConversation? liveConversation;
        LiveBlockTemplate? template = null;
        var floorLid = long.MaxValue;
        using (Computed.BeginIsolation()) {
            liveConversation = await LiveSessionUI.GetOpenConversation(chatId, cancellationToken).ConfigureAwait(false);
            // The template is built even before the governor runs, for a viewer who isn't attending too:
            // RevealMore uses its start when walking back through entries.
            if (liveConversation != null) {
                var hasAttended = await HasAttended(chatId, liveConversation, cancellationToken).ConfigureAwait(false);
                template = await BuildTemplate(chatId, liveConversation, hasAttended, cancellationToken).ConfigureAwait(false);
            }
            // The seed is the one fold end no advance produced, so the floors have to bound it here
            // instead: the governed value only ever grows, and a seed above them could never be
            // walked back.
            if (liveConversation != null)
                floorLid = await GetFloorLid(chatId, liveConversation, cancellationToken).ConfigureAwait(false);
        }
        var foldEndLid = Math.Min(GetRawFoldEndLid(liveConversation), floorLid);
        lock (Lock) {
            if (_chatStates.TryGetValue(chatId, out var existing))
                return existing;

            var chatState = new ChatFoldState {
                FoldEndLid = StateFactory.NewMutable(
                    foldEndLid,
                    StateCategories.Get(GetType(), nameof(GetBlock), "[*]")),
                Template = template,
            };
            _chatStates.Add(chatId, chatState);
            return chatState;
        }
    }

    private async Task<LiveBlockTemplate> BuildTemplate(
        ChatId chatId,
        LiveConversation liveConversation,
        bool hasAttended,
        CancellationToken cancellationToken)
    {
        Range<long> chatIdRange;
        using (Computed.BeginIsolation())
            chatIdRange = await Chats.GetIdRange(Session, chatId, cancellationToken).ConfigureAwait(false);
        var conversation = liveConversation.HasSummary ? null : liveConversation.ToConversation();
        var v = liveConversation.StartLid;
        var renderId = ConversationId.New(chatId, v);
        // A close may reach the conversation read before its snapshot; keep the descriptor already shown.
        if (conversation == null && !liveConversation.HasSummary)
            lock (Lock)
                if (_chatStates.TryGetValue(chatId, out var state) && state.Template?.Conversation.Id == renderId)
                    conversation = state.Template.DissolvingConversation;

        return new LiveBlockTemplate(
            chatIdRange.End,
            liveConversation,
            hasAttended,
            conversation?.Id == renderId
                ? conversation with { EndEntryLid = Math.Max(v, chatIdRange.End - 1) }
                : null);
    }

    private static long GetRawFoldEndLid(LiveConversation? liveConversation)
        => liveConversation is { HasSummary: true }
            && liveConversation.SummaryEndLid > liveConversation.StartLid
            ? liveConversation.SummaryEndLid
            : 0;

    private async Task RunFoldGovernor(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            Moment? wakeAt = null;
            try {
                var cInputs = await Computed
                    .Capture(() => GetGovernorInputs(cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
                var inputs = cInputs.Value;
                if (inputs.ChatId is { } chatId) {
                    wakeAt = await ProcessChat(chatId, inputs, cancellationToken).ConfigureAwait(false);
                    CleanupOtherChats(chatId);
                }
                var timeout = wakeAt is { } w
                    ? (w - Clocks.ServerClock.Now).Positive()
                    : TimeSpan.FromHours(1);
                using var waitCts = cancellationToken.CreateLinkedTokenSource();
                try {
                    await cInputs.WhenInvalidated(waitCts.Token)
                        .WaitAsync(timeout, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException) { }
                finally {
                    // Releases the invalidation handler the timeout path would otherwise leave
                    // registered on cInputs and on cancellationToken.
                    waitCts.CancelAndDisposeSilently();
                }
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                Log.LogError(e, "Fold governor iteration failed");
                await Clocks.CpuClock.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<Moment?> ProcessChat(ChatId chatId, GovernorInputs inputs, CancellationToken cancellationToken)
    {
        var (_, liveConversation, visibility, hasAttended, isBlockExpanded, hasTranscript, rawStreamingFloorLid, tailFloorLid)
            = inputs;
        var chatState = await GetOrCreateChatState(chatId, cancellationToken).ConfigureAwait(false);

        // Keep tracking the descriptor after leaving; only session closure stops the refresh.
        var template = liveConversation != null
            ? await BuildTemplate(chatId, liveConversation, hasAttended, cancellationToken).ConfigureAwait(false)
            : null;
        var streamingFloorLid = StreamingFloorOf(liveConversation, rawStreamingFloorLid);

        bool mustInvalidate;
        Moment? wakeAt = null;
        lock (Lock) {
            var oldBlock = DeriveBlock(chatState, liveConversation, hasAttended);
            var foldEndLid = chatState.FoldEndLid.Value;
            if (template != null)
                chatState.Template = template;
            if (liveConversation == null && chatState.Template is { HasAttended: true })
                chatState.IsClosed = true;
            else if (liveConversation != null) {
                chatState.IsClosed = false;
                chatState.IsDismissed = false;
                chatState.DissolveEndsAt = default;
                chatState.DissolveDone = false;
            }

            // DeriveBlock exposes dissolve immediately; the governor owns its expiry.
            var isTierOneClose = liveConversation == null
                && chatState is { IsDismissed: false, Template: { HasAttended: true, HadSummary: false } };
            if (isTierOneClose) {
                if (chatState.DissolveEndsAt == default)
                    chatState.DissolveEndsAt = Clocks.ServerClock.Now + DissolveDuration;
                if (!chatState.DissolveDone && Clocks.ServerClock.Now >= chatState.DissolveEndsAt) {
                    chatState.DissolveDone = true;
                    chatState.Template = chatState.Template! with { DissolvingConversation = null };
                }
                if (!chatState.DissolveDone)
                    wakeAt = chatState.DissolveEndsAt;
            }

            if (liveConversation != null) {
                var v = liveConversation.StartLid;
                // 0 means no part of the block is visible, which holds the fold where LiveFoldMath left it.
                // Collapsed counts as invisible: the card stands in for the rows, so the typed rows showing
                // below it say nothing about what the reader scrolled past - fed to the fold, they would
                // swallow every spoken row by the time the block is expanded again. The report in hand at
                // the moment of expansion is that same collapsed-render report, so it is held until the
                // expanded render replaces it. Not the report in hand here: the expanded render's first one
                // can land before this pass, and the view re-reports only on a change.
                if (isBlockExpanded && !chatState.WasBlockExpanded)
                    chatState.StaleVisibility = Hub.ChatUI.GetItemVisibilityAtExpansionChange(
                        ConversationId.New(chatId, v));
                chatState.WasBlockExpanded = isBlockExpanded;
                // Without a transcript the card has neither a preview nor "show more" to stand in for what
                // it would swallow, so the rows stay: what a voice-only session hides, nothing brings back.
                var isVisibilityUsable = isBlockExpanded
                    && hasTranscript
                    && !ReferenceEquals(visibility, chatState.StaleVisibility)
                    && visibility.ChatId == chatId
                    && !visibility.IsEmpty;
                var minVisibleLid = isVisibilityUsable
                    ? visibility.VisibleMessageLids.Where(lid => lid >= v).DefaultIfEmpty(0).Min()
                    : 0;
                var oldBoundary = foldEndLid;
                foldEndLid = LiveFoldMath.Advance(
                    oldBoundary, minVisibleLid, streamingFloorLid, tailFloorLid);
                // A reveal is a temporary peek. Latch that the reader scrolled up into the revealed region
                // (above the governed boundary); once they scroll back down so every revealed row is above
                // the viewport again, re-swallow them - the block re-compacts on return to the live tail.
                // Pinned to the end, the revealed rows are on screen only until the next message pushes
                // them up, and that is not the reader scrolling in - counting it would re-swallow a reveal
                // made at the live tail within seconds, with the pill back as if nothing had been shown.
                if (chatState.RevealedBoundaryLid != long.MaxValue && minVisibleLid != 0) {
                    if (minVisibleLid < oldBoundary && !visibility.IsPinnedToEnd)
                        chatState.RevealScrolledInto = true;
                    else if (chatState.RevealScrolledInto && minVisibleLid >= oldBoundary) {
                        chatState.RevealedBoundaryLid = long.MaxValue;
                        chatState.RevealScrolledInto = false;
                    }
                }
            }

            // Publish the fold last, so its notification sees all private changes. Unchanged folds
            // need explicit invalidation for reveal or lifecycle changes.
            mustInvalidate = foldEndLid == chatState.FoldEndLid.Value
                && !Equals(oldBlock, DeriveBlock(chatState, liveConversation, hasAttended));
            chatState.FoldEndLid.Value = foldEndLid;
        }
        if (mustInvalidate)
            using (Invalidation.Begin())
                _ = GetBlock(chatId, default);
        return wakeAt;
    }

    private void CleanupOtherChats(ChatId selectedChatId)
    {
        List<ChatId>? removed = null;
        lock (Lock)
            foreach (var chatId in _chatStates.Keys.Where(id => id != selectedChatId).ToList()) {
                _chatStates.Remove(chatId);
                (removed ??= []).Add(chatId);
            }
        if (removed == null)
            return;

        using (Invalidation.Begin())
            foreach (var chatId in removed)
                _ = GetBlock(chatId, default);
    }

    private async Task<long> GetFloorLid(ChatId chatId, LiveConversation liveConversation, CancellationToken cancellationToken)
    {
        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var streamingTail = await Hub.ChatUI
            .GetStreamingTail(chatId, ownAuthor?.Id ?? default, cancellationToken)
            .ConfigureAwait(false);
        var tailFloorLid = await GetTailFloorLid(chatId, liveConversation.StartLid, cancellationToken)
            .ConfigureAwait(false);
        return Math.Min(StreamingFloorOf(liveConversation, streamingTail.FloorLid), tailFloorLid);
    }

    private static long StreamingFloorOf(LiveConversation? liveConversation, long rawStreamingFloorLid)
        // A transcript that started before the block started isn't the block's to hold open.
        => liveConversation != null && rawStreamingFloorLid >= liveConversation.StartLid
            ? rawStreamingFloorLid
            : long.MaxValue;

    // Nested types

    private sealed class ChatFoldState
    {
        public MutableState<long> FoldEndLid = null!;
        public bool IsClosed;
        public bool IsDismissed;
        public Moment DissolveEndsAt;
        public bool DissolveDone;
        public LiveBlockTemplate? Template;
        public long RevealedBoundaryLid = long.MaxValue;
        public bool RevealScrolledInto;
        public bool WasBlockExpanded;
        public ChatViewItemVisibility? StaleVisibility;
    }

    private sealed record LiveBlockTemplate(
        long ChatEndLid,
        LiveConversation Conversation,
        bool HasAttended,
        Conversation? DissolvingConversation)
    {
        public bool HadSummary => Conversation.HasSummary;
    }

    protected sealed record GovernorInputs(
        ChatId? ChatId,
        LiveConversation? Conversation,
        ChatViewItemVisibility Visibility,
        bool HasAttended,
        bool IsBlockExpanded,
        bool HasTranscript,
        long StreamingFloorLid = long.MaxValue,
        long TailFloorLid = long.MaxValue)
    {
        public static readonly GovernorInputs None = new(
            null, null, ChatViewItemVisibility.Empty, false, false, false);
    }
}
