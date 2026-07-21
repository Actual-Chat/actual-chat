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

public sealed record LiveBlockState(
    long FoldBoundaryLid, LiveBlockOverlay? Overlay, bool WasAttending = false, bool IsDissolving = false)
{
    public static readonly LiveBlockState None = new(0, null);
}

/// <summary>
/// Governs how far a live block's fold boundary is allowed to advance (lag + viewport clamp via
/// <see cref="LiveFoldMath"/>), and freezes/materializes the block's render once the viewer leaves
/// or the session closes, so a watched viewport never mutates under the reader.
/// </summary>
public class LiveBlockUI(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub), IComputeService, INotifyInitialized
{
    private readonly Dictionary<ChatId, ChatFoldState> _chatStates = new();
    internal TimeSpan FoldLag = TimeSpan.FromMinutes(3);
    // A too-short (tier-1) session leaves nothing behind, so its block is held briefly on close to
    // fade + collapse out instead of vanishing in one frame.
    internal TimeSpan DissolveDuration = TimeSpan.FromMilliseconds(300);

    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;
    private IChats Chats => Hub.Chats;

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
        var raw = await LiveSessionUI.GetState(chatId, cancellationToken).ConfigureAwait(false);
        var amInLive = raw != null
            && await LiveSessionUI.AmIInLiveConversation(chatId, cancellationToken).ConfigureAwait(false);
        lock (Lock)
            return baseState with {
                Overlay = DeriveOverlay(chatState, baseState.FoldBoundaryLid, raw, amInLive, baseState.IsDissolving),
            };
    }

    public bool TryCollapseOverlay(ConversationId conversationId)
    {
        lock (Lock) {
            foreach (var chatState in _chatStates.Values) {
                if (!chatState.IsClosed || chatState.Template is not { HadSummary: true } t)
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
        ChatFoldState chatState, long foldBoundaryLid, LiveSessionState? raw, bool amInLive, bool isDissolving)
    {
        if (!chatState.WasAttending || chatState.Template is not { } t)
            return null;

        if (raw == null)
            // Close: freeze the block under its live-era render id and hand over to the materialized
            // conversation. Tier-1 (never summarized) has no card - keep the frozen block briefly with
            // IsDissolving so it can fade + collapse out, then drop it and the entries just stay.
            return t.HadSummary
                ? new LiveBlockOverlay(t.LiveRenderId, t.V, FoldRangeOf(t.V, foldBoundaryLid),
                    default, t.BlockEndLid, t.MaterializedId, t.IsExpandedByDefault)
                : isDissolving
                    ? new LiveBlockOverlay(t.LiveRenderId, t.V, FoldRangeOf(t.V, foldBoundaryLid),
                        new Range<long>(t.TailStart, long.MaxValue), t.TailStart, null, false, IsDissolving: true)
                    : null;

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

        var raw = await LiveSessionUI.GetState(chatId, cancellationToken).ConfigureAwait(false);
        var visibility = await Hub.ChatUI.ItemVisibility.Use(cancellationToken).ConfigureAwait(false);
        var isJoined = raw != null
            && await LiveSessionUI.AmIInLiveConversation(chatId, cancellationToken).ConfigureAwait(false);
        return new GovernorInputs(chatId, raw, visibility, isJoined);
    }

    // Private methods

    private async Task<ChatFoldState> GetOrCreateChatState(ChatId chatId, CancellationToken cancellationToken)
    {
        lock (Lock)
            if (_chatStates.TryGetValue(chatId, out var existing))
                return existing;

        // Isolated read: the initial latch must not make the governor's boundary state reactive to
        // the raw live state - only subsequent advances go through the lag + viewport governor. The
        // attending latch is seeded here too - whichever caller creates the state first (this read
        // path or the governor loop) must agree on it, or a join immediately followed by a leave (no
        // governor iteration lands in between) would never mark the viewer as having attended.
        LiveSessionState? raw;
        bool isJoined;
        FrozenTemplate? template = null;
        using (Computed.BeginIsolation()) {
            raw = await LiveSessionUI.GetState(chatId, cancellationToken).ConfigureAwait(false);
            isJoined = raw != null
                && await LiveSessionUI.AmIInLiveConversation(chatId, cancellationToken).ConfigureAwait(false);
            // Seed the template alongside the attending latch: a join immediately followed by a leave
            // (before the governor's first iteration) must still freeze, so WasAttending never outruns
            // the template GetBlockState needs to derive the overlay.
            if (raw is { SessionStartedAt: not null } && isJoined)
                template = await BuildTemplate(chatId, raw, cancellationToken).ConfigureAwait(false);
        }
        var foldEndLid = GetRawFoldEndLid(raw);
        lock (Lock) {
            if (_chatStates.TryGetValue(chatId, out var existing))
                return existing;

            var chatState = new ChatFoldState {
                State = StateFactory.NewMutable(
                    new LiveBlockState(foldEndLid, null, isJoined),
                    StateCategories.Get(GetType(), nameof(GetBlockState), "[*]")),
                LastObservedFoldEndLid = foldEndLid,
                WasAttending = isJoined,
                Template = template,
            };
            _chatStates.Add(chatId, chatState);
            return chatState;
        }
    }

    private async Task<FrozenTemplate> BuildTemplate(ChatId chatId, LiveSessionState raw, CancellationToken cancellationToken)
    {
        Range<long> chatIdRange;
        using (Computed.BeginIsolation())
            chatIdRange = await Chats.GetIdRange(Session, chatId, cancellationToken).ConfigureAwait(false);
        var v = raw.EffectiveVisibleStartLid;
        return new FrozenTemplate(
            v,
            chatIdRange.End,
            raw.EndEntryLid + 1,
            ConversationId.New(chatId, v),
            ConversationId.New(chatId, raw.ContextStartLid > 0 ? raw.ContextStartLid : v),
            raw.IsExpandedByDefault,
            raw.LastSummaryAt.EpochOffsetTicks > 0);
    }

    private static long GetRawFoldEndLid(LiveSessionState? raw)
        => raw is { SessionStartedAt: not null, LastSummaryAt.EpochOffsetTicks: > 0 }
            && raw.EndEntryLid >= raw.EffectiveVisibleStartLid
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
                try {
                    await cInputs.WhenInvalidated(cancellationToken).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException) { }
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                Log.LogError(e, "Fold governor iteration failed");
                await Clocks.CpuClock.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<Moment?> ProcessChat(ChatId chatId, GovernorInputs inputs, CancellationToken cancellationToken)
    {
        var (_, raw, visibility, isJoined) = inputs;
        var chatState = await GetOrCreateChatState(chatId, cancellationToken).ConfigureAwait(false);

        // While the viewer is attending a live session, keep a frozen template ready - the exact
        // descriptor GetBlockState needs to freeze the block the instant they leave or it closes. The
        // template stops refreshing once !isJoined, so its tail start captures the leave moment.
        var template = raw is { SessionStartedAt: not null } && isJoined
            ? await BuildTemplate(chatId, raw, cancellationToken).ConfigureAwait(false)
            : null;

        lock (Lock) {
            chatState.WasAttending |= isJoined;
            if (template != null)
                chatState.Template = template;
            if (raw == null && chatState.WasAttending)
                chatState.IsClosed = true;

            var state = chatState.State.Value with { WasAttending = chatState.WasAttending };
            Moment? wakeAt = null;

            // A tier-1 (never-summarized) close leaves no card behind. Hold the block briefly as
            // "dissolving" so it can fade + collapse out, then release it so the entries drop to plain
            // messages. DissolveEndsAt is latched once; IsDissolving falls false when the window passes.
            var isTierOneClose = raw == null && chatState.WasAttending
                && chatState.Template is { HadSummary: false };
            var isDissolving = false;
            if (isTierOneClose) {
                if (chatState.DissolveEndsAt == default)
                    chatState.DissolveEndsAt = Clocks.ServerClock.Now + DissolveDuration;
                isDissolving = Clocks.ServerClock.Now < chatState.DissolveEndsAt;
                if (isDissolving)
                    wakeAt = chatState.DissolveEndsAt;
            }
            state = state with { IsDissolving = isDissolving };

            if (raw is { SessionStartedAt: not null }) {
                var v = raw.EffectiveVisibleStartLid;
                var foldEndLid = GetRawFoldEndLid(raw);
                if (foldEndLid > chatState.LastObservedFoldEndLid) {
                    chatState.Pending = [..chatState.Pending, new PendingFold(foldEndLid, raw.LastSummaryAt)];
                    chatState.LastObservedFoldEndLid = foldEndLid;
                }

                var minVisibleLid = visibility.ChatId == chatId && !visibility.IsEmpty
                    ? visibility.VisibleMessageLids.Where(lid => lid >= v).DefaultIfEmpty(long.MaxValue).Min()
                    : long.MaxValue;
                var result = LiveFoldMath.Advance(
                    state.FoldBoundaryLid, chatState.Pending, Clocks.ServerClock.Now, FoldLag,
                    minVisibleLid == long.MaxValue ? null : minVisibleLid);
                chatState.Pending = result.Pending;
                wakeAt = result.NextWakeAt;
                state = new LiveBlockState(result.BoundaryLid, null, chatState.WasAttending);
            }

            if (!Equals(chatState.State.Value, state))
                chatState.State.Value = state;
            return wakeAt;
        }
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

    // Nested types

    private sealed class ChatFoldState
    {
        public MutableState<LiveBlockState> State = null!;
        public IReadOnlyList<PendingFold> Pending = [];
        public long LastObservedFoldEndLid;
        public bool WasAttending;
        public bool IsClosed;
        public Moment DissolveEndsAt;
        public FrozenTemplate? Template;
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
        LiveSessionState? Raw,
        ChatViewItemVisibility Visibility,
        bool IsJoined)
    {
        public static readonly GovernorInputs None = new(null, null, ChatViewItemVisibility.Empty, false);
    }
}
