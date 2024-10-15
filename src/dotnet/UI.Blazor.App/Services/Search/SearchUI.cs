using ActualChat.MLSearch;
using ActualChat.Search;
using ActualChat.UI.Blazor.App.Events;
using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

public partial class SearchUI : ScopedWorkerBase<ChatUIHub>, IComputeService, INotifyInitialized, IDisposable
{
    private static readonly SearchScope[] Scopes = [SearchScope.People, SearchScope.Groups, SearchScope.Places, SearchScope.Messages ];
    private Cached _cached = Cached.None;
    private readonly MutableState<string> _text;
    private readonly MutableState<PlaceId> _placeId;
    private readonly MutableState<bool> _isSearchModeOn;
    private readonly MutableState<bool> _isShowRecentOn;
    private readonly MutableState<bool> _isResultsNavigationOn;
    private readonly ComputedState<FoundItem?> _selectedItem;

    public IMutableState<string> Text => _text;
    public IMutableState<PlaceId> PlaceId => _placeId;
    public IState<bool> IsSearchModeOn => _isSearchModeOn;
    public IState<bool> IsShowRecentOn => _isShowRecentOn;
    public IState<bool> IsResultsNavigationOn => _isResultsNavigationOn;
    public IState<FoundItem?> SelectedItem => _selectedItem;
    private IMutableState<ImmutableHashSet<SearchScope>> ExtendedLimits { get; }
    private History History => Hub.History;
    private ISearch Search => Hub.Search;
    private BrowserInfo BrowserInfo => Hub.BrowserInfo;
    private NavbarUI NavbarUI => Hub.NavbarUI;
    private PanelsUI PanelsUI => Hub.PanelsUI;
    private UIEventHub UIEventHub => Hub.UIEventHub();
    private UICommander UICommander => Hub.UICommander();

    public SearchUI(ChatUIHub uiHub) : base(uiHub)
    {
        var stateFactory = uiHub.StateFactory();
        _text = stateFactory.NewMutable("", StateCategories.Get(GetType(), nameof(Text)));
        _placeId = stateFactory.NewMutable(ActualChat.PlaceId.None, StateCategories.Get(GetType(), nameof(_placeId)));
        _isSearchModeOn = stateFactory.NewMutable(false, StateCategories.Get(GetType(), nameof(IsSearchModeOn)));
        _isShowRecentOn = stateFactory.NewMutable(false, StateCategories.Get(GetType(), nameof(IsShowRecentOn)));
        _isResultsNavigationOn = stateFactory.NewMutable(false, StateCategories.Get(GetType(), nameof(IsResultsNavigationOn)));
        _selectedItem = stateFactory.NewComputed<FoundItem?>((FoundItem?)null, _ => Task.FromResult(_cached.Selected), StateCategories.Get(GetType(), nameof(SelectedItem)));
        ExtendedLimits = stateFactory
            .NewMutable(ImmutableHashSet<SearchScope>.Empty, StateCategories.Get(GetType(), nameof(ExtendedLimits)));
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
        return new (text, placeId, extendedLimits);
    }

    [ComputeMethod]
    public virtual async Task<bool> IsExpanded(SearchScope scope)
    {
        var expandedScopes = await ExtendedLimits.Use(StopToken).ConfigureAwait(false);
        return expandedScopes.Contains(scope);
    }

    public void Clear()
    {
        if (Text.Value.IsNullOrEmpty())
            return;

        Text.Value = "";
        PlaceId.Value = ActualChat.PlaceId.None;
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

    public void ShowRecent(bool isOn)
        => _isShowRecentOn.Set(isOn);

    public async Task LaunchAISearch()
    {
        var chatIdOpt = await CreateSearchChat().ConfigureAwait(true); // Continue on Blazor Context
        if (!chatIdOpt.HasValue)
            return;

        var searchChatId = chatIdOpt.Value;
        NavbarUI.SelectGroup(NavbarGroupIds.Chats, false);
        if (BrowserInfo.ScreenSize.Value.IsNarrow())
            PanelsUI.HidePanels();
        await History.NavigateTo(Links.Chat(searchChatId)).ConfigureAwait(false);
    }

    private async Task<ChatId?> CreateSearchChat() {
        var createSearchChatCommand = new MLSearch_CreateChat(Session, "Search", default);
        var (searchChat, error) = await UICommander.Run(createSearchChatCommand).ConfigureAwait(false);
        if (error != null)
            return null;

        return searchChat.Id;
    }

    public Task Select(FoundItem foundItem, bool mustNavigate = false)
    {
        if (_cached.TrySelect(foundItem))
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

        PlaceId.Value = NavbarUI.IsPlaceSelected(out var placeId) ? placeId : ActualChat.PlaceId.None;
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

    protected sealed record Criteria(string Text, PlaceId PlaceId, ImmutableHashSet<SearchScope> ExtendedLimits)
    {
        public static readonly Criteria None = new ("", PlaceId.None, []);

        public ContactSearchQuery ToContactQuery(SubgroupKey key)
            => new () {
                Criteria = Text,
                PlaceId = PlaceId == PlaceId.None ? null : PlaceId, // search in places if None
                Scope = key.Scope,
                Limit = ExtendedLimits.Contains(key.Scope)
                    ? Constants.Search.ExtendedPageSize
                    : Constants.Search.DefaultPageSize,
                Own = key.Own,
            };

        public EntrySearchQuery ToEntryQuery(SubgroupKey key)
            => new () {
                Criteria = Text,
                PlaceId = PlaceId == PlaceId.None ? null : PlaceId, // search in places if None
                Limit = ExtendedLimits.Contains(key.Scope)
                    ? Constants.Search.ExtendedPageSize
                    : Constants.Search.DefaultPageSize,
            };
    }

    protected sealed record SubgroupKey(SearchScope Scope, bool Own);
}
