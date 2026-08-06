using ActualChat.Search;
using ActualChat.UI.Blazor.App.Events;
using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Manages unified search across people, groups, places, and messages with navigation support.
/// </summary>
public partial class SearchUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized, IDisposable
{
    private static readonly SearchScope[] Scopes = [SearchScope.People, SearchScope.Groups, SearchScope.Places, SearchScope.Messages ];

    private readonly MutableState<string> _text;
    private readonly MutableState<PlaceId?> _placeId;
    private readonly MutableState<bool> _isSearchModeOn;
    private readonly MutableState<bool> _isShowRecentOn;
    private readonly MutableState<bool> _isResultsNavigationOn;
    private readonly MutableState<bool> _isGlobalSearchOn;
    private readonly MutableState<SearchLocationFilter> _locationFilter;
    private readonly MutableState<SearchTypeFilter> _typeFilter;
    private readonly ComputedState<FoundItem?> _selectedItem;
    private Cached _cached = Cached.None;

    public MutableState<string> Text => _text;
    public MutableState<PlaceId?> PlaceId => _placeId;
    public IState<bool> IsSearchModeOn => _isSearchModeOn;
    public IState<bool> IsShowRecentOn => _isShowRecentOn;
    public IState<bool> IsResultsNavigationOn => _isResultsNavigationOn;
    public IState<bool> IsGlobalSearchOn => _isGlobalSearchOn;
    public IState<SearchLocationFilter> LocationFilter => _locationFilter;
    public IState<SearchTypeFilter> TypeFilter => _typeFilter;
    public IState<FoundItem?> SelectedItem => _selectedItem;

    private MutableState<ImmutableHashSet<SearchScope>> ExtendedLimits { get; }
    public MutableState<ImmutableHashSet<SearchResultGroupKey>> CollapsedGroups { get; }

    private ISearch Search => Hub.Search;
    private LocalSearchUI LocalSearch => field ??= Services.GetRequiredService<LocalSearchUI>();
    private NavbarUI NavbarUI => Hub.NavbarUI;
    private HighlightUI HighlightUI => Hub.HighlightUI;

    public SearchUI(AppUIHub hub) : base(hub)
    {
        var stateFactory = hub.StateFactory;
        _text = stateFactory.NewMutable("", StateCategories.Get(GetType(), nameof(Text)));
        _placeId = stateFactory.NewMutable((PlaceId?)null, StateCategories.Get(GetType(), nameof(_placeId)));
        _isSearchModeOn = stateFactory.NewMutable(false, StateCategories.Get(GetType(), nameof(IsSearchModeOn)));
        _isShowRecentOn = stateFactory.NewMutable(false, StateCategories.Get(GetType(), nameof(IsShowRecentOn)));
        _isResultsNavigationOn = stateFactory.NewMutable(false, StateCategories.Get(GetType(), nameof(IsResultsNavigationOn)));
        _isGlobalSearchOn = stateFactory.NewMutable(false, StateCategories.Get(GetType(), nameof(IsGlobalSearchOn)));
        _locationFilter = stateFactory.NewMutable(SearchLocationFilter.Anywhere, StateCategories.Get(GetType(), nameof(LocationFilter)));
        _typeFilter = stateFactory.NewMutable(SearchTypeFilter.Anything, StateCategories.Get(GetType(), nameof(TypeFilter)));
        _selectedItem = stateFactory.NewComputed((FoundItem?)null, _ => Task.FromResult(_cached.Selected), StateCategories.Get(GetType(), nameof(SelectedItem)));
        ExtendedLimits = stateFactory
            .NewMutable(ImmutableHashSet<SearchScope>.Empty, StateCategories.Get(GetType(), nameof(ExtendedLimits)));
        CollapsedGroups = stateFactory
            .NewMutable(ImmutableHashSet<SearchResultGroupKey>.Empty, StateCategories.Get(GetType(), nameof(CollapsedGroups)));
        NavbarUI.SelectedGroupChanged += NavbarUIOnSelectedGroupChanged;
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    void IDisposable.Dispose()
        => NavbarUI.SelectedGroupChanged -= NavbarUIOnSelectedGroupChanged;

    [ComputeMethod] // Synced
    public virtual Task<IReadOnlyList<FoundItem>> GetSearchResults()
        => Task.FromResult(_cached.FoundItems);

    [ComputeMethod]
    protected virtual async Task<Criteria> GetCriteria(CancellationToken cancellationToken)
    {
        var text = await Text.Use(cancellationToken).ConfigureAwait(false);
        if (text.IsNullOrEmpty())
            return Criteria.None;

        var extendedLimits = await ExtendedLimits.Use(cancellationToken).ConfigureAwait(false);
        var placeId = await _placeId.Use(cancellationToken).ConfigureAwait(false);
        var isGlobalSearchOn = await _isGlobalSearchOn.Use(cancellationToken).ConfigureAwait(false);
        var locationFilter = await _locationFilter.Use(cancellationToken).ConfigureAwait(false);
        var typeFilter = await _typeFilter.Use(cancellationToken).ConfigureAwait(false);
        ChatId? chatId = null;
        if (locationFilter == SearchLocationFilter.Chat)
            chatId = await Hub.ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
        // Effective query PlaceId reflects the LocationFilter — Anywhere/Chat ignore the ambient place.
        var effectivePlaceId = locationFilter == SearchLocationFilter.Place ? placeId : null;
        return new (text, effectivePlaceId, chatId, extendedLimits, isGlobalSearchOn, locationFilter, typeFilter);
    }

    [ComputeMethod]
    public virtual async Task<bool> IsExpanded(SearchScope scope)
    {
        var expandedScopes = await ExtendedLimits.Use(StopToken).ConfigureAwait(false);
        return expandedScopes.Contains(scope);
    }

    public void SearchFor(string text)
    {
        var wasOpen = _isShowRecentOn.Value || _isSearchModeOn.Value;
        PanelsUI.Left.SetIsVisible(true);
        ShowRecent(true);
        _placeId.Value = NavbarUI.IsPlaceSelected(out var placeId) ? placeId : null;
        if (!wasOpen)
            ResetFilters();
        _typeFilter.Value = SearchTypeFilter.Messages;
        _text.Value = text;
        // The left panel's search input deliberately doesn't track Text changes,
        // so an externally set text must be pushed into it explicitly.
        _ = UIEventHub.Publish(new SearchTextSetEvent(text));
    }

    public void Clear()
    {
        if (Text.Value.IsNullOrEmpty())
            return;

        Text.Value = "";
        PlaceId.Value = null;
        _isGlobalSearchOn.Value = false;
        _ = UIEventHub.Publish(new SearchClearedEvent());
    }

    public async Task ShowMore(SearchScope scope, CancellationToken cancellationToken = default)
    {
        var current = await ExtendedLimits.Use(cancellationToken).ConfigureAwait(false);
        ExtendedLimits.Value = current.Add(scope);
    }

    public async Task ShowLess(SearchScope chatKind, CancellationToken cancellationToken = default)
    {
        var current = await ExtendedLimits.Use(cancellationToken).ConfigureAwait(false);
        ExtendedLimits.Value = current.Remove(chatKind);
    }

    [ComputeMethod]
    public virtual async Task<bool> IsGroupCollapsed(SearchResultGroupKey key)
    {
        var collapsed = await CollapsedGroups.Use(StopToken).ConfigureAwait(false);
        return collapsed.Contains(key);
    }

    public async Task ToggleGroupCollapse(SearchResultGroupKey key, CancellationToken cancellationToken = default)
    {
        var current = await CollapsedGroups.Use(cancellationToken).ConfigureAwait(false);
        var withoutKey = current.Remove(key);
        CollapsedGroups.Value = ReferenceEquals(withoutKey, current) ? current.Add(key) : withoutKey;
    }

    public void ShowGlobalResults()
        => _isGlobalSearchOn.Value = true;

    public void SetLocationFilter(SearchLocationFilter value)
        => _locationFilter.Value = value;

    public void SetTypeFilter(SearchTypeFilter value)
        => _typeFilter.Value = value;

    public void ResetFilters()
    {
        _locationFilter.Value = GetDefaultLocationFilter();
        _typeFilter.Value = SearchTypeFilter.Anything;
        CollapsedGroups.Value = ImmutableHashSet<SearchResultGroupKey>.Empty;
    }

    private SearchLocationFilter GetDefaultLocationFilter()
        => Hub.ChatUI.SelectedChatId.ValueOrDefault is PlaceChatId
            ? SearchLocationFilter.Place
            : SearchLocationFilter.Anywhere;

    public void ShowRecent(bool isOn)
        => _isShowRecentOn.Set(isOn);

    public Task Select(FoundItem foundItem, bool mustNavigate = false)
    {
        if (!_cached.TrySelect(foundItem))
            return Task.CompletedTask;

        _isResultsNavigationOn.Value = true;
        _selectedItem.Invalidate();
        return mustNavigate ? NavigateTo(foundItem) : Task.CompletedTask;
    }

    public Task SelectPrevious()
    {
        var selected = _cached.SelectPrevious();
        _selectedItem.Invalidate();
        return NavigateTo(selected);
    }

    public Task SelectNext()
    {
        var selected = _cached.SelectNext();
        _selectedItem.Invalidate();
        return NavigateTo(selected);
    }

    private Task NavigateTo(FoundItem? foundItem)
        => foundItem is not null ? History.NavigateTo(foundItem.Link) : Task.CompletedTask;

    private void NavbarUIOnSelectedGroupChanged(object? sender, NavbarGroupChangedEventArgs e)
    {
        if (!e.IsUserAction)
            return;

        var newPlaceId = NavbarUI.IsPlaceSelected(out var placeId) ? placeId : null;
        PlaceId.Value = newPlaceId;
        if (newPlaceId is null && _locationFilter.Value == SearchLocationFilter.Place)
            _locationFilter.Value = SearchLocationFilter.Anywhere;
    }

    // Nested types

    private sealed class Cached(List<FoundItem> foundItems)
    {
        private int _activeIndex = -1;
        public IReadOnlyList<FoundItem> FoundItems { get; } = foundItems;
        public static readonly Cached None = new ([]);

        public FoundItem? Selected => _activeIndex >= 0 ? FoundItems[_activeIndex] : null;

        public bool TrySelect(FoundItem foundItem)
        {
            var i = foundItems.IndexOf(foundItem);
            if (i < 0)
                return false;

            _activeIndex = i;
            return true;
        }

        public FoundItem? SelectPrevious()
        {
            _activeIndex = foundItems.PreviousIndexOrLast(_activeIndex);
            return foundItems.GetValueOrDefault(_activeIndex);
        }

        public FoundItem? SelectNext()
        {
            _activeIndex = foundItems.NextIndexOrFirst(_activeIndex);
            return foundItems.GetValueOrDefault(_activeIndex);
        }
    }

    protected sealed record Criteria(
        string Text,
        PlaceId? PlaceId,
        ChatId? ChatId,
        ImmutableHashSet<SearchScope> ExtendedLimits,
        bool IsGlobalSearchOn,
        SearchLocationFilter LocationFilter,
        SearchTypeFilter TypeFilter)
    {
        public static readonly Criteria None = new ("", null, null, [], false, SearchLocationFilter.Anywhere, SearchTypeFilter.Anything);

        // We request DisplayLimit + 1 so we can detect whether more results exist beyond what we render.
        public int DisplayLimit(SearchScope scope) => ExtendedLimits.Contains(scope)
            ? Constants.Search.ExtendedPageSize
            : Constants.Search.DefaultPageSize;

        public ContactSearchQuery ToContactQuery(SubgroupKey key)
            => new () {
                Criteria = Text,
                PlaceId = PlaceId, // search everywhere (chats and places) if Null
                Scope = key.Scope,
                Limit = DisplayLimit(key.Scope) + 1,
                Own = key.Own,
            };

        public EntrySearchQuery ToEntryQuery(SubgroupKey key)
            => new () {
                Criteria = Text,
                PlaceId = PlaceId,
                ChatId = LocationFilter == SearchLocationFilter.Chat ? ChatId : null,
                Limit = DisplayLimit(key.Scope) + 1,
            };
    }

    protected sealed record SubgroupKey(SearchScope Scope, bool Own);
}
