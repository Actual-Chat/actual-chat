using ActualChat.Search;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

public partial class SearchUI : ScopedWorkerBase<ChatUIHub>, IComputeService, INotifyInitialized
{
    private static readonly SearchScope[] Scopes = [SearchScope.People, SearchScope.Groups, SearchScope.Places, SearchScope.Messages ];
    private Cached _cached = Cached.None;
    private readonly MutableState<string> _text;
    private readonly MutableState<PlaceId> _placeId;
    private readonly MutableState<bool> _isSearchModeOn;

    public IMutableState<string> Text => _text;
    public IMutableState<PlaceId> PlaceId => _placeId;
    private IMutableState<ImmutableHashSet<SearchScope>> ExtendedLimits { get; }
    private ISearch Search => Hub.Search;

    public SearchUI(ChatUIHub uiHub) : base(uiHub)
    {
        var stateFactory = uiHub.StateFactory();
        _text = stateFactory.NewMutable("", StateCategories.Get(GetType(), nameof(Text)));
        _placeId = stateFactory.NewMutable(ActualChat.PlaceId.None, StateCategories.Get(GetType(), nameof(_placeId)));
        _isSearchModeOn = stateFactory.NewMutable(false, StateCategories.Get(GetType(), nameof(IsSearchModeOn)));
        ExtendedLimits = stateFactory
            .NewMutable(ImmutableHashSet<SearchScope>.Empty, StateCategories.Get(GetType(), nameof(ExtendedLimits)));
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod]
    public virtual Task<bool> IsSearchModeOn(CancellationToken cancellationToken)
        => _isSearchModeOn.Use(cancellationToken).AsTask();

    [ComputeMethod] // Synced
    public virtual Task<IReadOnlyList<FoundItem>> GetContactSearchResults()
        => Task.FromResult<IReadOnlyList<FoundItem>>(_cached.FoundItems
            .Where(x => x.SearchResult is ContactSearchResult)
            .ToList());

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

    private sealed record Cached(Criteria Criteria, IReadOnlyList<FoundItem> FoundItems)
    {
        public static readonly Cached None = new (Criteria.None, []);
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
