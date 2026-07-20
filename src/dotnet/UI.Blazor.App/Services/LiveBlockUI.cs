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
    bool IsExpandedByDefault);

public sealed record LiveBlockState(long FoldBoundaryLid, LiveBlockOverlay? Overlay)
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

    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;
    private IChats Chats => Hub.Chats;

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod]
    public virtual async Task<LiveBlockState> GetBlockState(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var chatState = await GetOrCreateChatState(chatId, cancellationToken).ConfigureAwait(false);
        return await chatState.State.Use(cancellationToken).ConfigureAwait(false);
    }

    public bool TryCollapseOverlay(ConversationId conversationId)
    {
        lock (Lock) {
            foreach (var chatState in _chatStates.Values) {
                var overlay = chatState.State.Value.Overlay;
                if (overlay is not { MaterializedId: { } materializedId })
                    continue;
                if (overlay.RenderId != conversationId && materializedId != conversationId)
                    continue;

                chatState.State.Value = chatState.State.Value with { Overlay = null };
                Hub.ChatUI.EnsureConversationCollapsed(materializedId, overlay.IsExpandedByDefault);
                return true;
            }
        }
        return false;
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

        // Isolated read: the initial latch must not make GetBlockState reactive to the raw live
        // state - only subsequent advances go through the lag + viewport governor.
        LiveSessionState? raw;
        using (Computed.BeginIsolation())
            raw = await LiveSessionUI.GetState(chatId, cancellationToken).ConfigureAwait(false);
        var foldEndLid = GetRawFoldEndLid(raw);
        lock (Lock) {
            if (_chatStates.TryGetValue(chatId, out var existing))
                return existing;

            var chatState = new ChatFoldState {
                State = StateFactory.NewMutable(
                    new LiveBlockState(foldEndLid, null),
                    StateCategories.Get(GetType(), nameof(GetBlockState), "[*]")),
                LastObservedFoldEndLid = foldEndLid,
                LastRaw = raw,
            };
            _chatStates.Add(chatId, chatState);
            return chatState;
        }
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

        // The leaver freeze needs the chat's end lid, read outside the lock (isolated, non-reactive).
        Range<long> chatIdRange = default;
        var isLeaving = raw is { SessionStartedAt: not null } && chatState.WasJoined && !isJoined
            && chatState.State.Value.Overlay == null;
        if (isLeaving)
            using (Computed.BeginIsolation())
                chatIdRange = await Chats.GetIdRange(Session, chatId, cancellationToken).ConfigureAwait(false);

        lock (Lock) {
            var state = chatState.State.Value;
            var overlay = state.Overlay;
            Moment? wakeAt = null;

            if (raw is { SessionStartedAt: not null }) {
                var v = raw.EffectiveVisibleStartLid;

                var foldEndLid = GetRawFoldEndLid(raw);
                if (foldEndLid > chatState.LastObservedFoldEndLid) {
                    chatState.Pending = [..chatState.Pending, new PendingFold(foldEndLid, raw.LastSummaryAt)];
                    chatState.LastObservedFoldEndLid = foldEndLid;
                }

                if (isLeaving)
                    overlay = new LiveBlockOverlay(
                        ConversationId.New(chatId, v), v,
                        FoldRangeOf(v, state.FoldBoundaryLid),
                        new Range<long>(chatIdRange.End, long.MaxValue),
                        chatIdRange.End, null, false);
                else if (overlay is { MaterializedId: null } && isJoined)
                    overlay = null; // Rejoined - the live tail is expected to reappear.

                var minVisibleLid = visibility.ChatId == chatId && !visibility.IsEmpty
                    ? visibility.VisibleMessageLids.Where(lid => lid >= v).DefaultIfEmpty(long.MaxValue).Min()
                    : long.MaxValue;
                var result = LiveFoldMath.Advance(
                    state.FoldBoundaryLid, chatState.Pending, Clocks.ServerClock.Now, FoldLag,
                    minVisibleLid == long.MaxValue ? null : minVisibleLid);
                chatState.Pending = result.Pending;
                wakeAt = result.NextWakeAt;
                if (overlay is { MaterializedId: null })
                    overlay = overlay with { FoldRange = FoldRangeOf(v, result.BoundaryLid) };
                state = new LiveBlockState(result.BoundaryLid, overlay);
            }
            else if (chatState.LastRaw is { SessionStartedAt: not null } lastRaw) {
                // Session closed - freeze whatever this viewer was rendering; no live-viewport collapse.
                if (lastRaw.LastSummaryAt.EpochOffsetTicks <= 0) {
                    // Tier-1 close (never summarized): nothing materializes, so a kept overlay would
                    // hide entry V behind a card that no longer renders - drop it entirely.
                    state = state with { Overlay = null };
                    if (!Equals(chatState.State.Value, state))
                        chatState.State.Value = state;
                    chatState.LastRaw = raw;
                    chatState.WasJoined = isJoined;
                    return null;
                }

                var v = lastRaw.EffectiveVisibleStartLid;
                var blockEndLid = lastRaw.EndEntryLid + 1;
                var materializedId = ConversationId.New(chatId,
                    lastRaw.ContextStartLid > 0 ? lastRaw.ContextStartLid : v);
                overlay = overlay is { MaterializedId: null } leaverOverlay
                    ? leaverOverlay with {
                        HiddenTailRange = leaverOverlay.HiddenTailRange.Start < blockEndLid
                            ? new Range<long>(leaverOverlay.HiddenTailRange.Start, blockEndLid)
                            : default,
                        BlockEndLid = blockEndLid,
                        MaterializedId = materializedId,
                        IsExpandedByDefault = lastRaw.IsExpandedByDefault,
                    }
                    : overlay ?? new LiveBlockOverlay(
                        ConversationId.New(chatId, v), v,
                        chatState.WasJoined
                            ? FoldRangeOf(v, state.FoldBoundaryLid)
                            : new Range<long>(v, blockEndLid),
                        default, blockEndLid, materializedId, lastRaw.IsExpandedByDefault);
                state = state with { Overlay = overlay };
            }

            if (!Equals(chatState.State.Value, state))
                chatState.State.Value = state;
            chatState.LastRaw = raw;
            chatState.WasJoined = isJoined;
            return wakeAt;
        }

        static Range<long> FoldRangeOf(long v, long boundaryLid)
            => boundaryLid > v ? new Range<long>(v, boundaryLid) : default;
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
        public LiveSessionState? LastRaw;
        public bool WasJoined;
    }

    protected sealed record GovernorInputs(
        ChatId? ChatId,
        LiveSessionState? Raw,
        ChatViewItemVisibility Visibility,
        bool IsJoined)
    {
        public static readonly GovernorInputs None = new(null, null, ChatViewItemVisibility.Empty, false);
    }
}
