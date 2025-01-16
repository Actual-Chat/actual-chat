using ActualChat.Roulette;
using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

public partial class RouletteUI : ScopedWorkerBase<ChatUIHub>, IComputeService, INotifyInitialized
{
    public static readonly ImmutableArray<Country> CountryOptions
        = ImmutableArray<Country>.Empty.Add(Country.NotSpecified).AddRange(Countries.All);

    private readonly TaskCompletionSource _whenLoaded;
    private readonly MutableState<Profile> _selectedProfile;
    private readonly MutableState<Search?> _activeSearch;
    private readonly MutableState<Preferences> _searchCriteria;

    private IRoulette Roulette => Hub.Roulette;
    private IRouletteProfiles RouletteProfiles => Hub.RouletteProfiles;
    private History History => Hub.History;
    private UICommander UICommander => Hub.UICommander();

    public Task WhenLoaded => _whenLoaded.Task;
    public IState<Profile> SelectedProfile => _selectedProfile;
    public IState<Preferences> SearchCriteria => _searchCriteria;
    public IState<Search?> ActiveSearch => _activeSearch;

    //private SearchRequest? _searchRequest;

    public RouletteUI(ChatUIHub hub):base(hub)
    {
        //Hub = hub;
        _whenLoaded = new TaskCompletionSource();
        _selectedProfile = hub.StateFactory().NewMutable(Profile.None);
        _selectedProfile.Updated += OnSelectedProfileChanged;
        _activeSearch = hub.StateFactory().NewMutable<Search?>();
        _searchCriteria = hub.StateFactory().NewMutable(Preferences.Empty);
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    public Task StartChatRoulette()
        => History.NavigateTo("/chat-roulette");

    private void OnSelectedProfileChanged(IState<Profile> arg1, StateEventKind arg2)
    {
       var profile = arg1.Value;
       _ = Commander.Call(new RouletteProfiles_SelectProfile(Session, profile.Id));
    }

    private void DiscardSelectedProfile()
    {
        _selectedProfile.Value = Profile.None;
        // Discard search request and results.
    }

    public void SelectProfile(Profile profile, bool updateSearchCriteria = false)
    {
        if (profile.IsNone)
            throw new ArgumentOutOfRangeException(nameof(profile));

        var lastProfile = _selectedProfile.Value;
        if (profile == lastProfile && !updateSearchCriteria)
            return;

        _selectedProfile.Value = profile;
        if (lastProfile.Id != profile.Id || updateSearchCriteria)
            UpdateSearchCriteria(profile.Preferences.Preferences with {
                Gender = Gender.NotSpecified,
                Country = Country.NotSpecified,
            });
    }

    public void ApplyNewFilter(Preferences filter)
    {
        UpdateSearchCriteria(filter);
        _ = UpdateSearchResult();
    }

    private void UpdateSearchCriteria(Preferences filter)
    {
        //_searchRequest = new SearchRequest(_selectedProfile.Value.Id, filter);
        _searchCriteria.Value = filter;
        _activeSearch.Value = null;
    }

    public async Task UpdateSearchResult()
    {
        _activeSearch.Value = null;
        var selectedProfile = SelectedProfile.Value;
        var searchCriteria = SearchCriteria.Value;
        if (selectedProfile.IsNone || !searchCriteria.IsSufficientForFiltering)
            return;

        var search = new Search(new SearchRequest(selectedProfile.Id, searchCriteria));
        _activeSearch.Value = search;
        var candidates = await Roulette.FindChatCandidates(Session, selectedProfile.Id, search.Request.Criteria, default)
            .ConfigureAwait(false);
        if (_activeSearch.Value != search)
            return;

        _activeSearch.Value = search.Complete(candidates.ToImmutableArray());
    }

    public async Task ReviewState()
    {
        var selectedProfile = _selectedProfile.Value;
        if (!selectedProfile.IsNone) {
            var selectedProfileId = await RouletteProfiles.GetSelectedProfileId(Session, default).ConfigureAwait(false);
            if (selectedProfile.Id != selectedProfileId)
                DiscardSelectedProfile();
            else {
                var profile = await RouletteProfiles.GetOwnProfile(Session, selectedProfile.Id, default)
                    .ConfigureAwait(false);
                if (profile is null)
                    DiscardSelectedProfile();
                else if (selectedProfile != profile)
                    SelectProfile(profile);
            }
        }
        selectedProfile = _selectedProfile.Value;
        if (selectedProfile.IsNone) {
            // var profileId = Hub.AccountUI.OwnAccount.Value.Avatar.Id;
            // var profile = await RouletteProfiles.GetProfile(Session, profileId, default).ConfigureAwait(false);
            var selectedProfileId = await RouletteProfiles.GetSelectedProfileId(Session, default).ConfigureAwait(false);
            if (!selectedProfileId.IsEmpty) {
                var profile = await RouletteProfiles.GetOwnProfile(Session, selectedProfileId, default).ConfigureAwait(false);
                if (profile != null)
                    SelectProfile(profile);
            }
        }
        _ = UpdateSearchResult();
        _whenLoaded.TrySetResult();
    }

    public virtual async Task StartChat(Symbol ownProfileId, Symbol peerProfileId, CancellationToken cancellationToken = default)
    {
        var chatId = await Roulette.GetOrCreateChat(Session, ownProfileId, peerProfileId, cancellationToken)
            .ConfigureAwait(true);
        if (chatId.IsNone) {
            UICommander.ShowError(new Exception("Can't start Roulette chat."));
            return;
        }

        await History.NavigateTo(Links.Chat(chatId));
    }

    public record SearchRequest(Symbol ProfileId, Preferences Criteria);

    public record Search
    {
        public SearchRequest Request { get; }
        public bool IsCompleted { get; }
        public ImmutableArray<ChatCandidate> Results { get; private set; } = ImmutableArray<ChatCandidate>.Empty;

        public Search(SearchRequest request)
            : this(request, null)
        {
        }

        private Search(SearchRequest request, ImmutableArray<ChatCandidate>? results)
        {
            Request = request;
            IsCompleted = results is not null;
            Results = results ?? ImmutableArray<ChatCandidate>.Empty;
        }

        public Search Complete(ImmutableArray<ChatCandidate> results)
        {
            if (IsCompleted)
                throw StandardError.Constraint("Search is already completed");

            return new Search(Request, results);
        }
    }
}
