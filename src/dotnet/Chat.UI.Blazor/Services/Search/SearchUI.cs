using ActualChat.Search;
using ActualLab.Interception;

namespace ActualChat.Chat.UI.Blazor.Services;

public partial class SearchUI : ScopedWorkerBase<ChatUIHub>, IComputeService, INotifyInitialized
{
    private Cached _contactSearchResults = Cached.None;
    private readonly MutableState<string> _text;

    public IMutableState<string> Text => _text;
    private ISearch Search => Hub.Search;
    private ChatListUI ChatListUI => Hub.ChatListUI;

    public SearchUI(ChatUIHub uiHub) : base(uiHub)
        => _text = uiHub.StateFactory().NewMutable("", StateCategories.Get(GetType(), nameof(Text)));

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod]
    public virtual async Task<bool> IsSearchModeOn(CancellationToken cancellationToken)
    {
        var (criteria, _) = await GetCriteria(cancellationToken).ConfigureAwait(false);
        return !criteria.IsNullOrEmpty();
    }

    [ComputeMethod] // Synced
    public virtual async Task<SearchMatch> GetSearchMatch(ChatId chatId)
    {
        await PseudoGetSearchMatch().ConfigureAwait(false);
        return _contactSearchResults.SearchMatches.GetValueOrDefault(chatId, SearchMatch.Empty);
    }

    [ComputeMethod]
    protected virtual Task<Unit> PseudoGetSearchMatch()
        => ActualLab.Async.TaskExt.UnitTask;

    [ComputeMethod] // Synced
    public virtual Task<IReadOnlyList<ContactSearchResult>> GetContactSearchResults()
        => Task.FromResult(_contactSearchResults.Results);

    [ComputeMethod]
    protected virtual async Task<(string Criteria, PlaceId placeId)> GetCriteria(CancellationToken cancellationToken)
    {
        var text = await Text.Use(cancellationToken).ConfigureAwait(false);
        if (text.IsNullOrEmpty())
            return ("", PlaceId.None);

        var chatListView = await ChatListUI.ActiveChatListView.Use(cancellationToken).ConfigureAwait(false);
        return chatListView is null ? (text, PlaceId.None) : (text, chatListView.PlaceId);
    }

    private sealed record Cached(IReadOnlyList<ContactSearchResult> Results)
    {
        public IReadOnlyDictionary<ChatId, SearchMatch> SearchMatches { get; } =
            Results.ToDictionary(x => x.ContactId.ChatId, x => x.SearchMatch);
        public static readonly Cached None = new ([]);
    }
}
