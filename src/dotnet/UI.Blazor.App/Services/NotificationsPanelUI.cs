namespace ActualChat.UI.Blazor.App.Services;

public class NotificationsPanelUI : UIServiceBase<AppUIHub>, IComputeService
{
    private static readonly ChatListFilter[] Filters =
        [ChatListFilter.Unread, ChatListFilter.UnreadDMs, ChatListFilter.UnreadMentions];

    private readonly object _lock = new();
    private readonly MutableState<ImmutableDictionary<Symbol, ImmutableHashSet<ChatId>>> _sessionSets;
    private CancellationTokenSource? _sessionCts;

    private ChatListUI ChatListUI => Hub.ChatListUI;

    public bool IsActive { get; private set; }

    public NotificationsPanelUI(AppUIHub hub) : base(hub)
        => _sessionSets = StateFactory.NewMutable(
            ImmutableDictionary<Symbol, ImmutableHashSet<ChatId>>.Empty,
            StateCategories.Get(GetType(), nameof(_sessionSets)));

    [ComputeMethod]
    public virtual async Task<ImmutableHashSet<ChatId>> GetSessionSet(Symbol filterId, CancellationToken cancellationToken = default)
    {
        var sets = await _sessionSets.Use(cancellationToken).ConfigureAwait(false);
        return sets.GetValueOrDefault(filterId, ImmutableHashSet<ChatId>.Empty);
    }

    public void Open()
    {
        lock (_lock) {
            if (IsActive)
                return;
            IsActive = true;
            _sessionCts = Hub.StopToken.CreateLinkedTokenSource();
            var token = _sessionCts.Token;
            foreach (var filter in Filters)
                _ = Accumulate(filter, token);
        }
    }

    public void Close()
    {
        lock (_lock) {
            if (!IsActive)
                return;
            IsActive = false;
            _sessionCts.CancelAndDisposeSilently();
            _sessionCts = null;
            _sessionSets.Value = ImmutableDictionary<Symbol, ImmutableHashSet<ChatId>>.Empty;
        }
    }

    // Private methods

    private async Task Accumulate(ChatListFilter filter, CancellationToken cancellationToken)
    {
        try {
            var computed = await Computed
                .Capture(() => ChatListUI.ListUnordered(null, filter, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            while (!cancellationToken.IsCancellationRequested) {
                AddIds(filter.Id, computed.Value.Keys);
                await computed.WhenInvalidated(cancellationToken).ConfigureAwait(false);
                computed = await computed.Update(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) {
            // Session closed
        }
    }

    private void AddIds(Symbol filterId, IEnumerable<ChatId> ids)
    {
        lock (_lock) {
            if (!IsActive)
                return;
            var sets = _sessionSets.Value;
            var current = sets.GetValueOrDefault(filterId, ImmutableHashSet<ChatId>.Empty);
            var updated = current.Union(ids);
            if (updated.Count == current.Count)
                return;
            _sessionSets.Value = sets.SetItem(filterId, updated);
        }
    }
}
