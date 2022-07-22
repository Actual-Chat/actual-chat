using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Components;

public partial class ChatView : ComponentBase, IAsyncDisposable
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;
    private readonly CancellationTokenSource _disposeToken = new ();
    private readonly TaskSource<Unit> _whenInitializedSource = TaskSource.New<Unit>(true);

    private Symbol _currentAuthorId;
    private long _lastNavigateToEntryId;
    private long _initialLastReadEntryId;
    private HashSet<long> _fullyVisibleEntryIds = new ();

    [Inject] private ILogger<ChatView> Log { get; init; } = null!;
    [Inject] private Session Session { get; init; } = null!;
    [Inject] private IStateFactory StateFactory { get; init; } = null!;
    [Inject] private IAuth Auth { get; init; } = null!;
    [Inject] private ChatUI ChatUI { get; init; } = null!;
    [Inject] private ChatPlayers ChatPlayers { get; init; } = null!;
    [Inject] private IChats Chats { get; init; } = null!;
    [Inject] private IChatAuthors ChatAuthors { get; init; } = null!;
    [Inject] private IChatReadPositions ChatReadPositions { get; init; } = null!;
    [Inject] private NavigationManager Nav { get; init; } = null!;
    [Inject] private TimeZoneConverter TimeZoneConverter { get; init; } = null!;
    [Inject] private MomentClockSet Clocks { get; init; } = null!;
    [Inject] private UICommander UICommander { get; init; } = null!;

    private Task WhenInitialized => _whenInitializedSource.Task;
    private IMutableState<long> NavigateToEntryId { get; set; } = null!;
    private IMutableState<List<string>> VisibleKeys { get; set; } = null!;
    private IPersistentState<long> LastReadEntryId { get; set; } = null!;

    [CascadingParameter] public Chat Chat { get; set; } = null!;

    public async ValueTask DisposeAsync()
    {
        Nav.LocationChanged -= OnLocationChanged;
        _disposeToken.Cancel();
        await LastReadEntryId.DisposeSilentlyAsync().ConfigureAwait(false);
    }

    protected override async Task OnParametersSetAsync()
    {
        await WhenInitialized.ConfigureAwait(false);

        TryNavigateToEntryIfExists();
    }

    public async Task NavigateToUnreadEntry()
    {
        long navigateToEntryId;
        var lastReadEntryId = LastReadEntryId?.Value ?? 0;
        if (lastReadEntryId > 0)
            navigateToEntryId = lastReadEntryId;
        else {
            var chatIdRange = await Chats.GetIdRange(Session, Chat.Id, ChatEntryType.Text, _disposeToken.Token)
                .ConfigureAwait(true);
            navigateToEntryId = chatIdRange.ToInclusive().End;
        }

        // reset to ensure navigation will happen
        _initialLastReadEntryId = navigateToEntryId;
        NavigateToEntry(navigateToEntryId);
    }

    protected override async Task OnInitializedAsync()
    {
        Log.LogDebug("Created for chat #{ChatId}", Chat.Id);
        Nav.LocationChanged += OnLocationChanged;
        try {
            NavigateToEntryId = StateFactory.NewMutable(0L);
            VisibleKeys = StateFactory.NewMutable(new List<string>());
            _ = BackgroundTask.Run(() => MonitorVisibleKeyChanges(_disposeToken.Token), _disposeToken.Token);

            var currentAuthor = await ChatAuthors.Get(Session, Chat.Id, _disposeToken.Token)
                .ConfigureAwait(true);
            _currentAuthorId = currentAuthor?.Id ?? Symbol.Empty;

            LastReadEntryId = await ChatUI.GetLastReadEntryId(Chat.Id, _disposeToken.Token).ConfigureAwait(false);
            _initialLastReadEntryId = LastReadEntryId.Value;
        }
        finally {
            await TimeZoneConverter.WhenInitialized.ConfigureAwait(true);
            _whenInitializedSource.SetResult(Unit.Default);
        }
    }

    protected override bool ShouldRender()
        => WhenInitialized.IsCompleted;

    private async Task MonitorVisibleKeyChanges(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
            try {
                await VisibleKeys.Computed.WhenInvalidated(cancellationToken).ConfigureAwait(true);
                var visibleKeys = await VisibleKeys.Use(cancellationToken).ConfigureAwait(true);
                if (visibleKeys.Count == 0)
                    continue;

                var visibleEntryIds = visibleKeys
                    .Select(key =>
                        long.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryId)
                            ? (long?)entryId
                            : null)
                    .Where(entryId => entryId.HasValue)
                    .Select(entryId => entryId!.Value)
                    .ToHashSet();

                var maxVisibleEntryId = visibleEntryIds.Max();
                var minVisibleEntryId = visibleEntryIds.Min();
                visibleEntryIds.Remove(maxVisibleEntryId);
                visibleEntryIds.Remove(minVisibleEntryId);
                await InvokeAsync(() => { _fullyVisibleEntryIds = visibleEntryIds; }).ConfigureAwait(false);

                if (LastReadEntryId?.Value >= maxVisibleEntryId)
                    continue;

                if (LastReadEntryId != null)
                    LastReadEntryId.Value = maxVisibleEntryId;
            }
            catch (Exception e) when(e is not OperationCanceledException) {
                Log.LogWarning(e,
                    "Error monitoring visible key changes, LastVisibleEntryId = {LastVisibleEntryId}",
                    LastReadEntryId!.Value);
            }
    }

    private async Task<VirtualListData<ChatMessageModel>> GetMessages(
        VirtualListDataQuery query,
        CancellationToken cancellationToken)
    {
        await WhenInitialized.ConfigureAwait(true);

        var chat = Chat;
        var chatId = chat.Id.Value;
        var chatIdRange = await Chats.GetIdRange(Session, chatId, ChatEntryType.Text, cancellationToken)
            .ConfigureAwait(true);
        var lastReadEntryId = LastReadEntryId?.Value ?? 0;
        var entryId = lastReadEntryId;
        var mustScrollToEntry = query.IsNone && entryId != 0;

        var lastIdTile = IdTileStack.Layers[0].GetTile(chatIdRange.ToInclusive().End);
        var lastTile = await Chats.GetTile(Session,
            chatId,
            ChatEntryType.Text,
            lastIdTile.Range,
            cancellationToken).ConfigureAwait(true);
        foreach (var entry in lastTile.Entries) {
            if (entry.AuthorId != _currentAuthorId || entry.Id <= _initialLastReadEntryId)
                continue;

            _initialLastReadEntryId = entry.Id;
            entryId = entry.Id;
            mustScrollToEntry = true;
        }

        var isHighlighted = false;
        var navigateToEntryId = await NavigateToEntryId.Use(cancellationToken).ConfigureAwait(true);
        if (!mustScrollToEntry) {
            if (navigateToEntryId != _lastNavigateToEntryId && !_fullyVisibleEntryIds.Contains(navigateToEntryId)) {
                isHighlighted = true;
                _lastNavigateToEntryId = navigateToEntryId;
                entryId = navigateToEntryId;
                mustScrollToEntry = true;
            }
            else if (query.ScrollToKey != null) {
                entryId = long.Parse(query.ScrollToKey, NumberStyles.Number, CultureInfo.InvariantCulture);
                mustScrollToEntry = true;
            }
        }
        var queryRange = mustScrollToEntry
            ? IdTileStack.Layers[1].GetTile(entryId).Range.Expand(IdTileStack.Layers[1].TileSize)
            : query.IsNone
                ? new Range<long>(
                    chatIdRange.End - (3 * IdTileStack.Layers[1].TileSize),
                    chatIdRange.End)
                : query.InclusiveRange
                    .AsLongRange()
                    .ToExclusive()
                    .Expand(new Range<long>((long)query.ExpandStartBy, (long)query.ExpandEndBy));

        var adjustedRange = queryRange.Clamp(chatIdRange);
        var idTiles = IdTileStack.GetOptimalCoveringTiles(adjustedRange);
        var chatTiles = await idTiles
            .Select(idTile => Chats.GetTile(Session, chatId, ChatEntryType.Text, idTile.Range, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        var chatEntries = chatTiles
            .SelectMany(chatTile => chatTile.Entries)
            .Where(e => e.Type == ChatEntryType.Text)
            .ToList();

        var chatMessages = ChatMessageModel.FromEntries(
            chatEntries,
            _initialLastReadEntryId,
            TimeZoneConverter);
        var scrollToKey = mustScrollToEntry
            ? entryId.ToString(CultureInfo.InvariantCulture)
            : null;
        var result = VirtualListData.New(
            OrdinalEquals(query.ScrollToKey, scrollToKey)
                ? query
                : new VirtualListDataQuery(adjustedRange.AsStringRange()) { ScrollToKey = scrollToKey },
            chatMessages,
            adjustedRange.Start <= chatIdRange.Start,
            adjustedRange.End + 1 >= chatIdRange.End,
            scrollToKey);

        if (isHighlighted)
            // highlight entry when it has already been loaded
            ChatUI.HighlightedChatEntryId.Value = entryId;

        return result;
    }

    public void NavigateToEntry(long navigateToEntryId)
    {
        // reset to ensure navigation will happen
        _lastNavigateToEntryId = 0;
        NavigateToEntryId.Value = navigateToEntryId;
        NavigateToEntryId.Invalidate();
    }

    private Task OnNavigateToChatEntry(NavigateToChatEntry navigation, CancellationToken cancellationToken)
    {
        NavigateToEntry(navigation.ChatEntryId);
        return Task.CompletedTask;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
        => TryNavigateToEntryIfExists();

    private void TryNavigateToEntryIfExists()
    {
        // ignore location changed events if already disposed
        if (_disposeToken.IsCancellationRequested)
            return;

        var currentUri = new Uri(Nav.Uri);
        var entryIdString = currentUri.Fragment.TrimStart('#');
        if (long.TryParse(entryIdString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryId) && entryId > 0)
            NavigateToEntry(entryId);
    }
}
