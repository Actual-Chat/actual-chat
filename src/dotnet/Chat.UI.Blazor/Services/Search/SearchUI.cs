using ActualChat.Contacts;
using ActualChat.Search;
using ActualLab.Interception;

namespace ActualChat.Chat.UI.Blazor.Services;

public partial class SearchUI : ScopedWorkerBase<ChatUIHub>, IComputeService, INotifyInitialized
{
    private static readonly ContactSearchScope[] Scopes = [ContactSearchScope.People, ContactSearchScope.PublicChats, ContactSearchScope.PrivateChats, ];
    private Cached _cached = Cached.None;
    private readonly MutableState<string> _text;

    public IMutableState<string> Text => _text;
    private IMutableState<ImmutableHashSet<ContactSearchScope>> ExtendedLimits { get; }
    private ISearch Search => Hub.Search;
    private ChatListUI ChatListUI => Hub.ChatListUI;

    public SearchUI(ChatUIHub uiHub) : base(uiHub)
    {
        _text = uiHub.StateFactory().NewMutable("", StateCategories.Get(GetType(), nameof(Text)));
        ExtendedLimits = uiHub.StateFactory()
            .NewMutable(ImmutableHashSet<ContactSearchScope>.Empty, StateCategories.Get(GetType(), nameof(ExtendedLimits)));
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod]
    public virtual async Task<bool> IsSearchModeOn(CancellationToken cancellationToken)
    {
        var (criteria, _, _) = await GetCriteria(cancellationToken).ConfigureAwait(false);
        return !criteria.IsNullOrEmpty();
    }

    [ComputeMethod] // Synced
    public virtual async Task<SearchMatch> GetSearchMatch(ChatId chatId)
    {
        await PseudoGetSearchMatch().ConfigureAwait(false);
        return _cached.SearchMatches.GetValueOrDefault(chatId, SearchMatch.Empty);
    }

    [ComputeMethod]
    protected virtual Task<Unit> PseudoGetSearchMatch()
        => ActualLab.Async.TaskExt.UnitTask;

    [ComputeMethod] // Synced
    public virtual Task<IReadOnlyList<FoundContact>> GetContactSearchResults()
        => Task.FromResult(_cached.FoundContacts);

    [ComputeMethod]
    protected virtual async Task<Criteria>
        GetCriteria(CancellationToken cancellationToken)
    {
        var text = await Text.Use(cancellationToken).ConfigureAwait(false);
        if (text.IsNullOrEmpty())
            return Criteria.None;

        var extendedLimits = await ExtendedLimits.Use(cancellationToken).ConfigureAwait(false);

        var chatListView = await ChatListUI.ActiveChatListView.Use(cancellationToken).ConfigureAwait(false);
        return new (text, chatListView?.PlaceId ?? PlaceId.None, extendedLimits);
    }

    public async Task ShowMore(ContactSearchScope scope, CancellationToken cancellationToken = default)
    {
        var current = await ExtendedLimits.Use(cancellationToken).ConfigureAwait(false);
        ExtendedLimits.Value = current.Add(scope);
    }

    public async Task ShowLess(ContactSearchScope scope, CancellationToken cancellationToken = default)
    {
        var current = await ExtendedLimits.Use(cancellationToken).ConfigureAwait(false);
        ExtendedLimits.Value = current.Remove(scope);
    }

    private sealed record Cached(Criteria Criteria, IReadOnlyList<FoundContact> FoundContacts)
    {
        public IReadOnlyDictionary<ChatId, SearchMatch> SearchMatches { get; } =
            FoundContacts.Select(x => x.SearchResult).ToDictionary(x => x.ContactId.ChatId, x => x.SearchMatch);

        public static readonly Cached None = new (Criteria.None, []);
    }

    protected sealed record Criteria(string Text, PlaceId PlaceId, ImmutableHashSet<ContactSearchScope> ExtendedLimits)
    {
        public static readonly Criteria None = new ("", PlaceId.None, []);

        public ContactSearchQuery ToQuery(ContactSearchScope scope)
            => new() {
                Criteria = Text,
                PlaceId = PlaceId == PlaceId.None ? null : PlaceId, // search in places if None
                Kind = scope == ContactSearchScope.People ? ContactKind.User : ContactKind.Chat,
                IsPublic = scope != ContactSearchScope.PrivateChats,
                Limit = ExtendedLimits.Contains(scope) ? Constants.Search.ContactSearchExtendedPageSize : Constants.Search.ContactSearchDefaultPageSize,
            };
    }
}
