using ActualChat.Live;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Frozen render state for a live block once the viewer stops watching it live: which entries stay
/// folded behind the card, which tail stays hidden, and (once closed) which persisted conversation
/// replaces the live render id.
/// </summary>
public sealed record LiveBlockOverlay(
    ConversationId RenderId,
    long CardLid,
    Range<long> FoldRange,
    Range<long> HiddenTailRange,
    long BlockEndLid,
    ConversationId? MaterializedId,
    bool IsExpandedByDefault,
    bool IsDissolving = false);

/// <summary>
/// The live block's fold state. <see cref="FoldBoundaryLid"/> is the governed fold end, already
/// bounded by the viewport top and by both floors when it was advanced - see
/// <see cref="LiveFoldMath.Advance"/> - so consumers use it as-is.
/// </summary>
public sealed record LiveBlockState(
    long FoldBoundaryLid,
    LiveBlockOverlay? Overlay,
    bool WasAttending = false,
    bool IsDissolving = false,
    long RevealedBoundaryLid = long.MaxValue)
{
    public static readonly LiveBlockState None = new(0, null);
}

/// <summary>
/// Governs how far a live block's fold boundary is allowed to advance (monotonic viewport-top
/// tracking via <see cref="LiveFoldMath"/>), and freezes/materializes the block's render once the
/// viewer leaves or the session closes, so a watched viewport never mutates under the reader.
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
    public virtual async Task<LiveBlockState> GetBlockState(ChatId chatId, CancellationToken cancellationToken = default)
    {
        // The freeze overlay is derived here, reactively, from "am I still attending this block" -
        // never latched by the async governor a beat later. That's what keeps a hang-up (or close)
        // from ever flashing a collapsed frame: the moment AmIInLiveConversation flips, this recomputes
        // and the overlay is already present. The governor only advances the fold boundary and keeps
        // the frozen template fresh; it no longer owns whether the overlay exists.
        var chatState = await GetOrCreateChatState(chatId, cancellationToken).ConfigureAwait(false);
        var baseState = await chatState.State.Use(cancellationToken).ConfigureAwait(false);
        var snapshotTask = LiveSessionUI.GetBlockSnapshot(chatId, cancellationToken);
        var raw = await LiveSessionUI.UseSnapshotOrLastKnown(chatId, snapshotTask).ConfigureAwait(false);
        var amInLive = raw != null
            && await LiveSessionUI.AmIInLiveConversation(chatId, cancellationToken).ConfigureAwait(false);
        lock (Lock) {
            var effectiveBoundaryLid = Math.Min(baseState.FoldBoundaryLid, chatState.RevealedBoundaryLid);
            return baseState with {
                Overlay = DeriveOverlay(chatState, effectiveBoundaryLid, raw, amInLive),
                RevealedBoundaryLid = chatState.RevealedBoundaryLid,
            };
        }
    }

    [ComputeMethod]
    public virtual async Task<int> GetSwallowedCount(ChatId chatId, CancellationToken cancellationToken = default)
    {
        // Cached (this is a [ComputeMethod]) but counts real messages via a full read of the swallowed
        // range on each recompute - never approximate with a lid span, since lids have gaps.
        var blockState = await GetBlockState(chatId, cancellationToken).ConfigureAwait(false);
        var raw = await LiveSessionUI.GetBlockSnapshot(chatId, cancellationToken).ConfigureAwait(false);
        if (raw is not { IsLatched: true })
            return 0;
        var v = raw.VisibleStartLid;
        var effectiveBoundary = Math.Min(blockState.FoldBoundaryLid, blockState.RevealedBoundaryLid);
        if (effectiveBoundary <= v)
            return 0;

        var count = 0;
        await foreach (var entry in Chats.ReadReverse(Session, chatId, cancellationToken).ConfigureAwait(false)) {
            if (entry.LocalId >= effectiveBoundary || entry.IsSystemEntry)
                continue;
            if (entry.LocalId < v)
                break;
            count++;
        }
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
            v = t.V;
            effectiveBoundary = Math.Min(chatState.State.Value.FoldBoundaryLid, chatState.RevealedBoundaryLid);
        }
        if (effectiveBoundary <= v)
            return;

        // Walk back RevealBatchSize real messages from just below the current effective boundary; the
        // last one becomes the new revealed boundary (clamped to V when fewer remain).
        var revealed = v;
        using (Computed.BeginIsolation()) {
            var taken = 0;
            await foreach (var entry in Chats.ReadReverse(Session, chatId, cancellationToken).ConfigureAwait(false)) {
                if (entry.LocalId >= effectiveBoundary || entry.IsSystemEntry)
                    continue;
                if (entry.LocalId < v)
                    break;
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
            _ = GetBlockState(chatId, default);
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
            _ = GetBlockState(chatId, default);
    }

    public bool TryCollapseOverlay(ConversationId conversationId)
    {
        lock (Lock) {
            foreach (var chatState in _chatStates.Values) {
                // WasAttending gates the intercept to a still-visible overlay: once dismissed, later
                // toggles on the materialized conversation must reach the ordinary expand/collapse path.
                if (!chatState.IsClosed || !chatState.WasAttending || chatState.Template is not { HadSummary: true } t)
                    continue;
                if (t.LiveRenderId != conversationId && t.MaterializedId != conversationId)
                    continue;

                chatState.WasAttending = false;
                chatState.State.Value = chatState.State.Value with { WasAttending = false };
                Hub.ChatUI.EnsureConversationCollapsed(t.MaterializedId, t.IsExpandedByDefault);
                return true;
            }
        }
        return false;
    }

    private static LiveBlockOverlay? DeriveOverlay(
        ChatFoldState chatState, long foldBoundaryLid, LiveBlockSnapshot? raw, bool amInLive)
    {
        if (!chatState.WasAttending || chatState.Template is not { } t)
            return null;

        if (raw == null)
            // Close: freeze the block under its live-era render id and hand over to the materialized
            // conversation. Tier-1 (never summarized) has no card - it dissolves immediately here (no
            // wait on the governor, or the block would be gone before the animation starts), then the
            // governor flips DissolveDone once the window passes and it drops to plain messages.
            return t.HadSummary
                ? new LiveBlockOverlay(t.LiveRenderId, t.V, FoldRangeOf(t.V, foldBoundaryLid),
                    default, t.BlockEndLid, t.MaterializedId, t.IsExpandedByDefault)
                : chatState.DissolveDone
                    ? null
                    : new LiveBlockOverlay(t.LiveRenderId, t.V, FoldRangeOf(t.V, foldBoundaryLid),
                        new Range<long>(t.TailStart, long.MaxValue), t.TailStart, null, false, IsDissolving: true);

        if (!amInLive)
            // Leave (session still live): freeze what the viewer was rendering; entries that arrive
            // after the leave (past the frozen tail start) stay hidden.
            return new LiveBlockOverlay(t.LiveRenderId, t.V, FoldRangeOf(t.V, foldBoundaryLid),
                new Range<long>(t.TailStart, long.MaxValue), t.TailStart, null, false);

        return null; // still joined and live
    }

    private static Range<long> FoldRangeOf(long v, long boundaryLid)
        => boundaryLid > v ? new Range<long>(v, boundaryLid) : default;

    // Protected/internal methods

    protected override Task OnRun(CancellationToken cancellationToken)
        => RunFoldGovernor(cancellationToken);

    [ComputeMethod]
    protected virtual async Task<GovernorInputs> GetGovernorInputs(CancellationToken cancellationToken)
    {
        var chatId = await Hub.ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return GovernorInputs.None;

        var raw = await LiveSessionUI.GetBlockSnapshot(chatId, cancellationToken).ConfigureAwait(false);
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
        return new GovernorInputs(chatId, raw, visibility, isJoined, streamingTail.FloorLid, tailFloorLid);
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
        LiveBlockSnapshot? raw;
        bool isJoined;
        FrozenTemplate? template = null;
        var floorLid = long.MaxValue;
        using (Computed.BeginIsolation()) {
            raw = await LiveSessionUI.GetBlockSnapshot(chatId, cancellationToken).ConfigureAwait(false);
            isJoined = raw != null
                && await LiveSessionUI.AmIInLiveConversation(chatId, cancellationToken).ConfigureAwait(false);
            // Seed the template alongside the attending latch: a join immediately followed by a leave
            // (before the governor's first iteration) must still freeze, so WasAttending never outruns
            // the template GetBlockState needs to derive the overlay.
            if (raw is { IsLatched: true } && isJoined)
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
                State = StateFactory.NewMutable(
                    new LiveBlockState(foldEndLid, null, isJoined),
                    StateCategories.Get(GetType(), nameof(GetBlockState), "[*]")),
                WasAttending = isJoined,
                Template = template,
            };
            _chatStates.Add(chatId, chatState);
            return chatState;
        }
    }

    private async Task<FrozenTemplate> BuildTemplate(ChatId chatId, LiveBlockSnapshot raw, CancellationToken cancellationToken)
    {
        Range<long> chatIdRange;
        using (Computed.BeginIsolation())
            chatIdRange = await Chats.GetIdRange(Session, chatId, cancellationToken).ConfigureAwait(false);
        var v = raw.VisibleStartLid;
        return new FrozenTemplate(
            v,
            chatIdRange.End,
            raw.EndEntryLid + 1,
            ConversationId.New(chatId, v),
            ConversationId.New(chatId, raw.ContextStartLid > 0 ? raw.ContextStartLid : v),
            raw.IsExpandedByDefault,
            raw.HasSummary);
    }

    private static long GetRawFoldEndLid(LiveBlockSnapshot? raw)
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
        var (_, raw, visibility, isJoined, rawStreamingFloorLid, tailFloorLid) = inputs;
        var chatState = await GetOrCreateChatState(chatId, cancellationToken).ConfigureAwait(false);

        // While the viewer is attending a live session, keep a frozen template ready - the exact
        // descriptor GetBlockState needs to freeze the block the instant they leave or it closes. The
        // template stops refreshing once !isJoined, so its tail start captures the leave moment.
        var template = raw is { IsLatched: true } && isJoined
            ? await BuildTemplate(chatId, raw, cancellationToken).ConfigureAwait(false)
            : null;
        var streamingFloorLid = StreamingFloorOf(raw, rawStreamingFloorLid);

        var clearedReveal = false;
        Moment? wakeAt = null;
        lock (Lock) {
            // A session everyone has left and then restarted is a new conversation as far as the viewer
            // is concerned: they watched the block go quiet. Left latched, the template they froze on
            // leaving keeps its unbounded hidden tail, and every entry the restart produces falls inside
            // it - so the block sits there frozen while people talk into it. IsClosing going false again
            // after it was true is that restart; the identity of the session cannot say so, because
            // resuming keeps the one it was closing.
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
                // The fold boundary only ever advances, so the one the last session left behind would
                // fold the restart's first entries into the card the moment they arrive.
                chatState.State.Value = LiveBlockState.None;
            }

            chatState.WasAttending |= isJoined;
            if (template != null)
                chatState.Template = template;
            if (raw == null && chatState.WasAttending)
                chatState.IsClosed = true;
            else if (raw != null)
                chatState.IsClosed = false;

            var state = chatState.State.Value with { WasAttending = chatState.WasAttending };

            // A tier-1 (never-summarized) close leaves no card behind. DeriveOverlay dissolves the
            // block immediately (synchronously) so the animation actually starts; the governor only
            // arms the window and flips DissolveDone once it passes, dropping the block to plain
            // messages. The state's IsDissolving mirror is just the signal that re-invalidates
            // GetBlockState when the window ends.
            var isTierOneClose = raw == null && chatState.WasAttending
                && chatState.Template is { HadSummary: false };
            if (isTierOneClose) {
                if (chatState.DissolveEndsAt == default)
                    chatState.DissolveEndsAt = Clocks.ServerClock.Now + DissolveDuration;
                if (!chatState.DissolveDone && Clocks.ServerClock.Now >= chatState.DissolveEndsAt)
                    chatState.DissolveDone = true;
                if (!chatState.DissolveDone)
                    wakeAt = chatState.DissolveEndsAt;
            }
            state = state with { IsDissolving = isTierOneClose && !chatState.DissolveDone };

            if (raw is { IsLatched: true }) {
                var v = raw.VisibleStartLid;
                // 0 means no part of the block is visible, which holds the fold where LiveFoldMath left it.
                var minVisibleLid = visibility.ChatId == chatId && !visibility.IsEmpty
                    ? visibility.VisibleMessageLids.Where(lid => lid >= v).DefaultIfEmpty(0).Min()
                    : 0;
                var oldBoundary = state.FoldBoundaryLid;
                var boundaryLid = LiveFoldMath.Advance(
                    oldBoundary, minVisibleLid, streamingFloorLid, tailFloorLid);
                // A reveal is a temporary peek. Latch that the viewport entered the revealed region (above
                // the governed boundary); once the reader scrolls back down so every revealed row is above
                // the viewport again, re-swallow them - the block re-compacts on return to the live tail.
                if (chatState.RevealedBoundaryLid != long.MaxValue && minVisibleLid != 0) {
                    if (minVisibleLid < oldBoundary)
                        chatState.RevealScrolledInto = true;
                    else if (chatState.RevealScrolledInto) {
                        chatState.RevealedBoundaryLid = long.MaxValue;
                        chatState.RevealScrolledInto = false;
                        clearedReveal = true;
                    }
                }
                state = new LiveBlockState(boundaryLid, null, chatState.WasAttending);
            }

            if (!Equals(chatState.State.Value, state))
                chatState.State.Value = state;
        }
        if (clearedReveal)
            using (Invalidation.Begin())
                _ = GetBlockState(chatId, default);
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
                _ = GetBlockState(chatId, default);
    }

    private async Task<long> GetFloorLid(ChatId chatId, LiveBlockSnapshot raw, CancellationToken cancellationToken)
    {
        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var streamingTail = await Hub.ChatUI
            .GetStreamingTail(chatId, ownAuthor?.Id ?? default, cancellationToken)
            .ConfigureAwait(false);
        var tailFloorLid = await GetTailFloorLid(chatId, raw.VisibleStartLid, cancellationToken)
            .ConfigureAwait(false);
        return Math.Min(StreamingFloorOf(raw, streamingTail.FloorLid), tailFloorLid);
    }

    private static long StreamingFloorOf(LiveBlockSnapshot? raw, long rawStreamingFloorLid)
        // A transcript that started before the block latched isn't the block's to hold open.
        => raw is { IsLatched: true } && rawStreamingFloorLid >= raw.VisibleStartLid
            ? rawStreamingFloorLid
            : long.MaxValue;

    // Nested types

    private sealed class ChatFoldState
    {
        public MutableState<LiveBlockState> State = null!;
        public bool WasAttending;
        public bool IsClosed;
        public bool WasQuiet;
        public Moment DissolveEndsAt;
        public bool DissolveDone;
        public FrozenTemplate? Template;
        public long RevealedBoundaryLid = long.MaxValue;
        public bool RevealScrolledInto;
    }

    private sealed record FrozenTemplate(
        long V,
        long TailStart,
        long BlockEndLid,
        ConversationId LiveRenderId,
        ConversationId MaterializedId,
        bool IsExpandedByDefault,
        bool HadSummary);

    protected sealed record GovernorInputs(
        ChatId? ChatId,
        LiveBlockSnapshot? Raw,
        ChatViewItemVisibility Visibility,
        bool IsJoined,
        long StreamingFloorLid = long.MaxValue,
        long TailFloorLid = long.MaxValue)
    {
        public static readonly GovernorInputs None = new(null, null, ChatViewItemVisibility.Empty, false);
    }
}
