using ActualChat.Notifications;
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
    private readonly MutableState<NotificationId?> _selectedNotificationId;
    private readonly MutableState<Symbol> _selectedTabId;
    private NotificationId? _tabAppliedFor;

    private ChatListUI ChatListUI => Hub.ChatListUI;
    private ChatUI ChatUI => Hub.ChatUI;
    private Moment Now => Clocks.SystemClock.Now;

    public IState<NotificationId?> SelectedNotificationId
        // The notification the URL names (nid) while the panel is the UI shown; null otherwise
        => _selectedNotificationId;
    public IState<Symbol> SelectedTabId
        // The tab the user last had open, kept here so the panel gets it back when it's re-created
        => _selectedTabId;

    public NotificationsPanelUI(AppUIHub hub) : base(hub)
    {
        _version = StateFactory.NewMutable(0, StateCategories.Get(GetType(), nameof(_version)));
        _selectedNotificationId = StateFactory.NewMutable(
            (NotificationId?)null, StateCategories.Get(GetType(), nameof(SelectedNotificationId)));
        _selectedTabId = StateFactory.NewMutable(
            ChatListFilter.Unread.Id, StateCategories.Get(GetType(), nameof(SelectedTabId)));
        foreach (var filter in Filters)
            _states[filter.Id] = new FilterState();
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    public static IReadOnlyList<Symbol> ListTabIds(NotificationId notificationId)
    {
        // The tabs a notification can appear on, the most specific first and All - which lists every
        // unread chat - after them.
        var tabIds = new List<Symbol>(4);
        var isChatLevel = notificationId.Kind is NotificationKind.Message or NotificationKind.Reply;
        if (notificationId.Kind is NotificationKind.Mention or NotificationKind.Attention)
            tabIds.Add(ChatListFilter.UnreadMentions.Id);
        else if (notificationId.Kind == NotificationKind.Reaction)
            tabIds.Add(NotificationsUI.ReactionsFilterId);
        if (notificationId.TryGetChatTarget(out var chatId, out _) && chatId.Kind == ChatKind.Peer)
            tabIds.Add(ChatListFilter.UnreadPeople.Id);
        tabIds.Add(ChatListFilter.Unread.Id);
        // A chat-level id is also what a row without a bound notification links to, whatever its tab
        if (isChatLevel)
            tabIds.Add(ChatListFilter.UnreadMentions.Id);
        return tabIds;
    }

    public static Symbol ChooseTabId(NotificationId notificationId, Symbol lastTabId, Func<Symbol, bool> isAvailable)
    {
        // The tab the user was on stays when the notification is on it too, so opening one doesn't
        // flip the panel around; otherwise the closest match, and All when none is shown.
        var tabIds = ListTabIds(notificationId);
        if (tabIds.Contains(lastTabId) && isAvailable(lastTabId))
            return lastTabId;

        foreach (var tabId in tabIds)
            if (isAvailable(tabId))
                return tabId;

        return ChatListFilter.Unread.Id;
    }

    public void SelectNotification(NotificationId? notificationId)
    {
        if (_selectedNotificationId.Value != notificationId)
            _tabAppliedFor = null;
        _selectedNotificationId.Value = notificationId;
    }

    public bool IsTabApplied(NotificationId notificationId)
        // The panel moves to a notification's tab once, whichever instance of it is showing: leaving
        // the tab by hand sticks, also across the panel being re-created
        => _tabAppliedFor == notificationId;

    public void MarkTabApplied(NotificationId notificationId)
        => _tabAppliedFor = notificationId;

    public void SelectTab(Symbol tabId)
        => _selectedTabId.Value = tabId;

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
