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

    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;
    private IChats Chats => Hub.Chats;
    private IAuthors Authors => Hub.Authors;

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod]
    public virtual async Task<LiveBlock?> GetBlock(ChatId chatId, CancellationToken cancellationToken = default)
    {
        // Derive lifecycle changes directly from their sources: waiting for the governor can flash
        // a collapsed frame between leaving/closing and its next iteration.
        var chatState = await GetOrCreateChatState(chatId, cancellationToken).ConfigureAwait(false);
        _ = await chatState.FoldEndLid.Use(cancellationToken).ConfigureAwait(false);
        var blockStateTask = LiveSessionUI.GetBlockState(chatId, cancellationToken);
        var raw = await LiveSessionUI.UseBlockStateOrLastKnown(chatId, blockStateTask).ConfigureAwait(false);
        // The projections consolidate independently; the conversation can arrive before the block state.
        var conversationTask = raw is { IsLatched: true }
            ? null
            : LiveSessionUI.GetConversation(chatId, cancellationToken);
        var conversation = conversationTask == null
            ? null
            : await LiveSessionUI.UseConversationOrLastKnown(chatId, conversationTask).ConfigureAwait(false);
        var amInLive = (raw != null || conversation != null)
            && await LiveSessionUI.AmIInLiveConversation(chatId, cancellationToken).ConfigureAwait(false);
        bool mustLatchAttendance;
        lock (Lock)
            mustLatchAttendance = amInLive && raw is { IsLatched: true } && !chatState.WasAttending;
        var template = mustLatchAttendance
            ? await BuildTemplate(chatId, raw!, cancellationToken).ConfigureAwait(false)
            : null;
        lock (Lock) {
            // This compute deliberately latches attendance and its descriptor idempotently, so a join
            // followed by an immediate leave/close cannot outrun the governor.
            if (template != null && !chatState.WasAttending) {
                chatState.Template = template;
                chatState.WasAttending = true;
            }
            var block = DeriveBlock(chatId, chatState, raw);
            if (block != null || conversation == null)
                return block;

            return new OpenLiveBlock(conversation.Id, chatState.WasAttending || amInLive,
                Math.Min(chatState.FoldEndLid.Value, chatState.RevealedBoundaryLid));
        }
    }

    [ComputeMethod]
    public virtual async Task<int> GetSwallowedCount(ChatId chatId, CancellationToken cancellationToken = default)
    {
        // Cached (this is a [ComputeMethod]) but counts real messages via a full read of the swallowed
        // range on each recompute - never approximate with a lid span, since lids have gaps.
        var block = await GetBlock(chatId, cancellationToken).ConfigureAwait(false);
        if (block is not OpenLiveBlock || block.FoldRange.IsEmpty)
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
            v = t.ConversationId.StartEntryLid;
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
                // WasAttending gates the intercept to a still-visible block: once dismissed, later
                // toggles on the materialized conversation must reach the ordinary expand/collapse path.
                if (!chatState.IsClosed || !chatState.WasAttending || chatState.Template is not { HadSummary: true } t)
                    continue;
                if (t.ConversationId != conversationId && t.MaterializedId != conversationId)
                    continue;

                chatState.WasAttending = false;
                dismissed = t;
                break;
            }
        }
        if (dismissed == null)
            return false;

        using (Invalidation.Begin())
            _ = GetBlock(conversationId.ChatId, default);
        if (dismissed.ConversationId != dismissed.MaterializedId)
            Hub.ChatUI.SuppressAutoExpansion(dismissed.ConversationId);
        Hub.ChatUI.EnsureConversationCollapsed(dismissed.MaterializedId, dismissed.IsExpandedByDefault);
        return true;
    }

    private static LiveBlock? DeriveBlock(ChatId chatId, ChatFoldState chatState, LiveBlockState? raw)
    {
        var foldEndLid = Math.Min(chatState.FoldEndLid.Value, chatState.RevealedBoundaryLid);
        if (raw != null)
            return raw.IsLatched
                ? new OpenLiveBlock(ConversationId.New(chatId, raw.VisibleStartLid), chatState.WasAttending, foldEndLid)
                : null;
        if (!chatState.WasAttending || chatState.Template is not { } t)
            return null;

        return t.HadSummary
            ? new ClosedLiveBlock(t.ConversationId, foldEndLid, t.SummaryEndLid, t.MaterializedId)
            : chatState.DissolveDone
                ? null
                : new ClosedLiveBlock(t.ConversationId, foldEndLid, t.ChatEndLid, null, t.DissolvingConversation);
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

        var raw = await LiveSessionUI.GetBlockState(chatId, cancellationToken).ConfigureAwait(false);
        var visibility = await Hub.ChatUI.ItemVisibility.Use(cancellationToken).ConfigureAwait(false);
        var isJoined = raw != null
            && await LiveSessionUI.AmIInLiveConversation(chatId, cancellationToken).ConfigureAwait(false);
        // Reactive, not polled: GetStreamingTail consolidates transcript-rate churn away and expires
        // itself when the last grace period lapses, so the loop wakes exactly when the floor moves.
        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var streamingTail = await Hub.ChatUI
            .GetStreamingTail(chatId, ownAuthor?.Id ?? default, cancellationToken)
            .ConfigureAwait(false);
        var tailFloorLid = raw is { IsLatched: true }
            ? await GetTailFloorLid(chatId, raw.VisibleStartLid, cancellationToken).ConfigureAwait(false)
            : long.MaxValue;
        // Resolved from the same latch and states the tile builder resolves expansion from, so the governor
        // and the render cannot disagree on what "collapsed" is. The snapshot already carries the id and the
        // default; the conversation record would only add a churnier dependency.
        var isBlockExpanded = false;
        if (raw is { IsLatched: true }) {
            await Hub.ChatUI.ConversationExpansionOverrides.Use(cancellationToken).ConfigureAwait(false);
            await Hub.ChatUI.AutoExpandedConversations.Use(cancellationToken).ConfigureAwait(false);
            isBlockExpanded = Hub.ChatUI.IsConversationExpanded(
                ConversationId.New(chatId, raw.VisibleStartLid), raw.IsExpandedByDefault);
        }
        return new GovernorInputs(
            chatId, raw, visibility, isJoined, isBlockExpanded, streamingTail.FloorLid, tailFloorLid);
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

        // Isolated read: the initial latch must not make the governor's boundary state reactive to
        // the raw live state - only subsequent advances go through the viewport governor. The
        // attending latch is seeded here too - whichever caller creates the state first (this read
        // path or the governor loop) must agree on it, or a join immediately followed by a leave (no
        // governor iteration lands in between) would never mark the viewer as having attended.
        LiveBlockState? raw;
        bool isJoined;
        LiveBlockTemplate? template = null;
        var floorLid = long.MaxValue;
        using (Computed.BeginIsolation()) {
            raw = await LiveSessionUI.GetBlockState(chatId, cancellationToken).ConfigureAwait(false);
            isJoined = raw != null
                && await LiveSessionUI.AmIInLiveConversation(chatId, cancellationToken).ConfigureAwait(false);
            // Attendance must retain its descriptor even before the governor runs. Other viewers
            // need the template too: RevealMore uses its start when walking back through entries.
            if (raw is { IsLatched: true })
                template = await BuildTemplate(chatId, raw, cancellationToken).ConfigureAwait(false);
            // The seed is the one fold end no advance produced, so the floors have to bound it here
            // instead: the governed value only ever grows, and a seed above them could never be
            // walked back.
            if (raw is { IsLatched: true })
                floorLid = await GetFloorLid(chatId, raw, cancellationToken).ConfigureAwait(false);
        }
        var foldEndLid = Math.Min(GetRawFoldEndLid(raw), floorLid);
        lock (Lock) {
            if (_chatStates.TryGetValue(chatId, out var existing))
                return existing;

            var chatState = new ChatFoldState {
                FoldEndLid = StateFactory.NewMutable(
                    foldEndLid,
                    StateCategories.Get(GetType(), nameof(GetBlock), "[*]")),
                WasAttending = isJoined,
                Template = template,
            };
            _chatStates.Add(chatId, chatState);
            return chatState;
        }
    }

    private async Task<LiveBlockTemplate> BuildTemplate(
        ChatId chatId,
        LiveBlockState raw,
        CancellationToken cancellationToken)
    {
        Range<long> chatIdRange;
        Conversation? conversation;
        using (Computed.BeginIsolation()) {
            var rangeTask = Chats.GetIdRange(Session, chatId, cancellationToken);
            var conversationTask = raw.HasSummary ? null : LiveSessionUI.GetConversation(chatId, cancellationToken);
            chatIdRange = await rangeTask.ConfigureAwait(false);
            conversation = conversationTask == null ? null : await conversationTask.ConfigureAwait(false);
        }
        var v = raw.VisibleStartLid;
        var renderId = ConversationId.New(chatId, v);
        // A close may reach the conversation read before its snapshot; keep the descriptor already shown.
        if (conversation == null && !raw.HasSummary)
            lock (Lock)
                if (_chatStates.TryGetValue(chatId, out var state) && state.Template?.ConversationId == renderId)
                    conversation = state.Template.DissolvingConversation;

        return new LiveBlockTemplate(
            chatIdRange.End,
            raw.EndEntryLid + 1,
            renderId,
            ConversationId.New(chatId, raw.ContextStartLid > 0 ? raw.ContextStartLid : v),
            raw.IsExpandedByDefault,
            raw.HasSummary,
            conversation?.Id == renderId
                ? conversation with { EndEntryLid = Math.Max(v, chatIdRange.End - 1) }
                : null);
    }

    private static long GetRawFoldEndLid(LiveBlockState? raw)
        => raw is { IsLatched: true, HasSummary: true }
            && raw.EndEntryLid >= raw.VisibleStartLid
            ? raw.EndEntryLid + 1
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
        var (_, raw, visibility, isJoined, isBlockExpanded, rawStreamingFloorLid, tailFloorLid) = inputs;
        var chatState = await GetOrCreateChatState(chatId, cancellationToken).ConfigureAwait(false);

        // Keep tracking the descriptor after leaving; only session closure stops the refresh.
        var template = raw is { IsLatched: true }
            ? await BuildTemplate(chatId, raw, cancellationToken).ConfigureAwait(false)
            : null;
        var streamingFloorLid = StreamingFloorOf(raw, rawStreamingFloorLid);

        bool mustInvalidate;
        Moment? wakeAt = null;
        lock (Lock) {
            var oldBlock = DeriveBlock(chatId, chatState, raw);
            var foldEndLid = chatState.FoldEndLid.Value;
            // Resuming keeps the session identity, so detect the restart from IsClosing and discard
            // the previous attendance/fold state before it hides messages from the new call.
            if (raw is { IsClosing: true })
                chatState.WasQuiet = true;
            else if (raw != null && chatState.WasQuiet) {
                chatState.WasQuiet = false;
                chatState.WasAttending = false;
                chatState.Template = null;
                chatState.RevealedBoundaryLid = long.MaxValue;
                chatState.RevealScrolledInto = false;
                chatState.IsClosed = false;
                chatState.DissolveEndsAt = default;
                chatState.DissolveDone = false;
                chatState.WasBlockExpanded = false;
                chatState.StaleVisibility = null;
                // The fold boundary only ever advances, so the one the last session left behind would
                // fold the restart's first entries into the card the moment they arrive.
                foldEndLid = 0;
            }

            chatState.WasAttending |= isJoined;
            if (template != null)
                chatState.Template = template;
            if (raw == null && chatState.WasAttending)
                chatState.IsClosed = true;
            else if (raw != null)
                chatState.IsClosed = false;

            // DeriveBlock exposes dissolve immediately; the governor owns its expiry.
            var isTierOneClose = raw == null && chatState.WasAttending
                && chatState.Template is { HadSummary: false };
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

            if (raw is { IsLatched: true }) {
                var v = raw.VisibleStartLid;
                // 0 means no part of the block is visible, which holds the fold where LiveFoldMath left it.
                // Collapsed counts as invisible: the card stands in for the rows, so the typed rows showing
                // below it say nothing about what the reader scrolled past - fed to the fold, they would
                // swallow every spoken row by the time the block is expanded again. The report in hand at
                // the moment of expansion is that same collapsed-render report, so it is held until the
                // expanded render replaces it.
                if (isBlockExpanded && !chatState.WasBlockExpanded)
                    chatState.StaleVisibility = visibility;
                chatState.WasBlockExpanded = isBlockExpanded;
                var isVisibilityUsable = isBlockExpanded
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
                && !Equals(oldBlock, DeriveBlock(chatId, chatState, raw));
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

    private async Task<long> GetFloorLid(ChatId chatId, LiveBlockState raw, CancellationToken cancellationToken)
    {
        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var streamingTail = await Hub.ChatUI
            .GetStreamingTail(chatId, ownAuthor?.Id ?? default, cancellationToken)
            .ConfigureAwait(false);
        var tailFloorLid = await GetTailFloorLid(chatId, raw.VisibleStartLid, cancellationToken)
            .ConfigureAwait(false);
        return Math.Min(StreamingFloorOf(raw, streamingTail.FloorLid), tailFloorLid);
    }

    private static long StreamingFloorOf(LiveBlockState? raw, long rawStreamingFloorLid)
        // A transcript that started before the block latched isn't the block's to hold open.
        => raw is { IsLatched: true } && rawStreamingFloorLid >= raw.VisibleStartLid
            ? rawStreamingFloorLid
            : long.MaxValue;

    // Nested types

    private sealed class ChatFoldState
    {
        public MutableState<long> FoldEndLid = null!;
        public bool WasAttending;
        public bool IsClosed;
        public bool WasQuiet;
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
        long SummaryEndLid,
        ConversationId ConversationId,
        ConversationId MaterializedId,
        bool IsExpandedByDefault,
        bool HadSummary,
        Conversation? DissolvingConversation);

    protected sealed record GovernorInputs(
        ChatId? ChatId,
        LiveBlockState? Raw,
        ChatViewItemVisibility Visibility,
        bool IsJoined,
        bool IsBlockExpanded,
        long StreamingFloorLid = long.MaxValue,
        long TailFloorLid = long.MaxValue)
    {
        public static readonly GovernorInputs None = new(null, null, ChatViewItemVisibility.Empty, false, false);
    }
}
