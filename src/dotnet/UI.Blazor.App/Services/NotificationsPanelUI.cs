using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Keeps a chat in the notifications panel for a grace period after it stops being unread, so the
/// list doesn't collapse under someone who is reading it.
/// </summary>
public class NotificationsPanelUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    private static readonly ChatListFilter[] Filters =
        [ChatListFilter.Unread, ChatListFilter.UnreadPeople, ChatListFilter.UnreadMentions];

    // Not configurable: candidates live in memory, so a longer window couldn't be honoured anyway.
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

    private readonly Lock _lock = new();
    private readonly Dictionary<Symbol, FilterState> _states = new();
    private readonly MutableState<int> _version;

    private ChatListUI ChatListUI => Hub.ChatListUI;
    private ChatUI ChatUI => Hub.ChatUI;
    private Moment Now => Clocks.SystemClock.Now;

    public NotificationsPanelUI(AppUIHub hub) : base(hub)
    {
        _version = StateFactory.NewMutable(0, StateCategories.Get(GetType(), nameof(_version)));
        foreach (var filter in Filters)
            _states[filter.Id] = new FilterState();
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod]
    public virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> GetExpiring(
        Symbol filterId, CancellationToken cancellationToken = default)
    {
        // Chats that left the unread set recently enough to still be shown. The selected chat is
        // held regardless of its exit time - it must not vanish from under the reader.
        _ = await _version.Use(cancellationToken).ConfigureAwait(false);
        var selectedChatId = await ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
        var now = Now;
        var result = new Dictionary<ChatId, ChatInfo>();
        var nearestExpiresAt = (Moment?)null;
        lock (_lock) {
            var state = _states[filterId];
            var expiredIds = new List<ChatId>();
            foreach (var (chatId, candidate) in state.Candidates) {
                if (chatId == selectedChatId) {
                    result.Add(chatId, candidate.ChatInfo);
                    continue;
                }

                var expiresAt = candidate.ExitedAt + Grace;
                if (expiresAt <= now) {
                    expiredIds.Add(chatId);
                    continue;
                }

                result.Add(chatId, candidate.ChatInfo);
                if (nearestExpiresAt is not { } nearest || expiresAt < nearest)
                    nearestExpiresAt = expiresAt;
            }
            foreach (var chatId in expiredIds)
                state.Candidates.Remove(chatId);
        }

        // Nothing else invalidates a purely time-based exit, so the nearest one schedules its pass.
        if (nearestExpiresAt is { } at)
            Computed.GetCurrent().InvalidateSafely(at - now);

        return result;
    }

    public void DismissAll()
    {
        // Clearing the candidates isn't enough on its own: the dismissal makes those chats read a
        // round-trip later, and the tracker would then see them leaving and re-add them. Dropping
        // the previous snapshot too means it never observes that exit at all.
        var selectedChatId = ChatUI.SelectedChatId.Value;
        lock (_lock) {
            var now = Now;
            foreach (var state in _states.Values) {
                // The chat on screen survives its own dismissal - it may be unread, or already read
                // and holding a place in the list, and either way pulling it out from under the
                // reader is what the grace period exists to prevent.
                var kept = GetKept(state, selectedChatId, now);
                state.Candidates.Clear();
                if (kept is not null && selectedChatId is { } chatId)
                    state.Candidates.Add(chatId, kept);
                state.Previous = ImmutableDictionary<ChatId, ChatInfo>.Empty;
            }
        }

        _version.Value++;
    }

    // Protected methods

    protected override Task OnRun(CancellationToken cancellationToken)
        => Task.WhenAll(Filters.Select(filter => Track(filter, cancellationToken)));

    // Private methods

    private async Task Track(ChatListFilter filter, CancellationToken cancellationToken)
    {
        var computed = await Computed
            .Capture(() => ChatListUI.ListUnordered(null, filter, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var c in computed.Changes(cancellationToken).ConfigureAwait(false)) {
            if (c.HasError)
                continue;

            OnUnreadChanged(filter.Id, c.Value);
        }
    }

    private void OnUnreadChanged(Symbol filterId, IReadOnlyDictionary<ChatId, ChatInfo> current)
    {
        var hasChanges = false;
        lock (_lock) {
            var state = _states[filterId];
            var now = Now;
            foreach (var (chatId, chatInfo) in state.Previous)
                if (!current.ContainsKey(chatId) && state.Candidates.TryAdd(chatId, new Candidate(chatInfo, now)))
                    hasChanges = true;
            // A chat that's unread again shows via the live list, so it must not also be a candidate.
            foreach (var chatId in current.Keys)
                hasChanges |= state.Candidates.Remove(chatId);
            state.Previous = current;
        }

        if (hasChanges)
            _version.Value++;
    }

    private static Candidate? GetKept(FilterState state, ChatId? selectedChatId, Moment now)
    {
        if (selectedChatId is not { } chatId)
            return null;
        if (state.Candidates.TryGetValue(chatId, out var candidate))
            return candidate with { ExitedAt = now };

        return state.Previous.TryGetValue(chatId, out var chatInfo)
            ? new Candidate(chatInfo, now)
            : null;
    }

    // Nested types

    private sealed record Candidate(ChatInfo ChatInfo, Moment ExitedAt);

    private sealed class FilterState
    {
        public Dictionary<ChatId, Candidate> Candidates { get; } = new();
        public IReadOnlyDictionary<ChatId, ChatInfo> Previous { get; set; }
            = ImmutableDictionary<ChatId, ChatInfo>.Empty;
    }
}
