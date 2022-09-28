using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using Google.Api.Gax;

namespace ActualChat.Chat.UI.Blazor.Components;

public partial class ChatView : ComponentBase, IVirtualListDataSource<ChatMessageModel>, IDisposable
{
    private const int PageSize = 40;

    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;
    private readonly CancellationTokenSource _disposeToken = new ();
    private readonly TaskSource<Unit> _whenInitializedSource = TaskSource.New<Unit>(true);

    private long? _lastNavigateToEntryId;
    private long? _initialLastReadEntryId;
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
    private IMutableState<long?> NavigateToEntryId { get; set; } = null!;
    private IMutableState<List<string>> VisibleKeys { get; set; } = null!;
    private SyncedStateLease<long?>? LastReadEntryState { get; set; } = null!;

    [CascadingParameter] public Chat Chat { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        Log.LogDebug("Created for chat #{ChatId}", Chat.Id);
        Nav.LocationChanged += OnLocationChanged;
        try {
            NavigateToEntryId = StateFactory.NewMutable<long?>();
            VisibleKeys = StateFactory.NewMutable(new List<string>());
            _ = BackgroundTask.Run(() => MonitorVisibleKeyChanges(_disposeToken.Token), _disposeToken.Token);

            LastReadEntryState = await ChatUI.LeaseLastReadEntryState(Chat.Id, _disposeToken.Token);
            _initialLastReadEntryId = LastReadEntryState.Value;
        }
        finally {
            _whenInitializedSource.SetResult(Unit.Default);
        }
    }

    public void Dispose()
    {
        Nav.LocationChanged -= OnLocationChanged;
        _disposeToken.Cancel();
        LastReadEntryState?.Dispose();
        LastReadEntryState = null;
    }

    protected override async Task OnParametersSetAsync()
    {
        await WhenInitialized;
        TryNavigateToEntry();
    }

    public async Task NavigateToUnreadEntry()
    {
        long navigateToEntryId;
        var lastReadEntryId = LastReadEntryState?.Value;
        if (lastReadEntryId is { } entryId)
            navigateToEntryId = entryId;
        else {
            var chatIdRange = await Chats.GetIdRange(Session, Chat.Id, ChatEntryType.Text, _disposeToken.Token);
            navigateToEntryId = chatIdRange.ToInclusive().End;
        }

        // Reset to ensure the navigation will happen
        _initialLastReadEntryId = navigateToEntryId;
        NavigateToEntry(navigateToEntryId);
    }

    public void NavigateToEntry(long navigateToEntryId)
    {
        // reset to ensure navigation will happen
        _lastNavigateToEntryId = null;
        NavigateToEntryId.Value = null;
        NavigateToEntryId.Value = navigateToEntryId;
    }

    public void TryNavigateToEntry()
    {
        // ignore location changed events if already disposed
        if (_disposeToken.IsCancellationRequested)
            return;

        var uri = new Uri(Nav.Uri);
        var entryIdString = uri.Fragment.TrimStart('#');
        if (long.TryParse(entryIdString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryId) && entryId > 0)
            NavigateToEntry(entryId);
    }

    // Private methods

    async Task<VirtualListData<ChatMessageModel>> IVirtualListDataSource<ChatMessageModel>.GetData(
        VirtualListDataQuery query,
        VirtualListData<ChatMessageModel> oldData,
        CancellationToken cancellationToken)
    {
        await WhenInitialized;

        var chat = Chat;
        var chatId = chat.Id.Value;
        var author = await ChatAuthors.Get(Session, chatId, cancellationToken);
        var authorId = author?.Id ?? Symbol.Empty;
        var chatIdRange = await Chats.GetIdRange(Session, chatId, ChatEntryType.Text, cancellationToken);
        var lastReadEntryId = LastReadEntryState?.Value ?? 0;
        if (LastReadEntryState != null && lastReadEntryId >= chatIdRange.End) {
            // looks like an error, let's reset last read position to the las entry id
            lastReadEntryId = chatIdRange.End - 1;
            LastReadEntryState.Value = lastReadEntryId;
        }
        var entryId = lastReadEntryId;
        var mustScrollToEntry = query.IsNone && entryId != 0;

        // get latest tile to check whether the Author has submitted new entry
        var lastIdTile = IdTileStack.Layers[0].GetTile(chatIdRange.ToInclusive().End);
        var lastTile = await Chats.GetTile(Session,
            chatId,
            ChatEntryType.Text,
            lastIdTile.Range,
            cancellationToken);
        foreach (var entry in lastTile.Entries) {
            if (entry.AuthorId != authorId || entry.Id <= _initialLastReadEntryId)
                continue;

            // scroll to the latest Author entry - e.g.m when author submits the new one
            _initialLastReadEntryId = entry.Id;
            entryId = entry.Id;
            mustScrollToEntry = true;
        }

        var isHighlighted = false;
        // handle NavigateToEntry
        var navigateToEntryId = await NavigateToEntryId.Use(cancellationToken);
        if (!mustScrollToEntry)
            if (navigateToEntryId.HasValue && navigateToEntryId != _lastNavigateToEntryId) {
                isHighlighted = true;
                _lastNavigateToEntryId = navigateToEntryId;
                entryId = navigateToEntryId.Value;
                if (!_fullyVisibleEntryIds.Contains(navigateToEntryId.Value))
                    mustScrollToEntry = true;
            }
        // if we are scrolling somewhere - let's load date near the entryId
        var queryRange = mustScrollToEntry
            ? new Range<long>(
                entryId - PageSize,
                entryId + PageSize)
            : query.IsNone
                ? new Range<long>(
                    chatIdRange.End - (2*PageSize),
                    chatIdRange.End)
                : query.InclusiveRange
                    .AsLongRange()
                    .ToExclusive()
                    .Expand(new Range<long>((long)query.ExpandStartBy, (long)query.ExpandEndBy));

        var adjustedRange = queryRange.Clamp(chatIdRange);
        var idTiles = IdTileStack.GetOptimalCoveringTiles(adjustedRange);
        var chatTiles = await idTiles
            .Select(idTile => Chats.GetTile(Session, chatId, ChatEntryType.Text, idTile.Range, cancellationToken))
            .Collect();

        var chatEntries = chatTiles
            .SelectMany(chatTile => chatTile.Entries)
            .Where(e => e.Type == ChatEntryType.Text)
            .ToList();

        var hasVeryFirstItem = adjustedRange.Start <= chatIdRange.Start;
        var hasVeryLastItem = adjustedRange.End + 1 >= chatIdRange.End;
        var chatMessages = ChatMessageModel.FromEntries(
            chatEntries,
            oldData.Items,
            _initialLastReadEntryId,
            hasVeryFirstItem,
            hasVeryLastItem,
            TimeZoneConverter);
        var scrollToKey = mustScrollToEntry
            ? entryId.ToString(CultureInfo.InvariantCulture)
            : null;
        var result = VirtualListData.New(
            new VirtualListDataQuery(adjustedRange.AsStringRange()),
            chatMessages,
            hasVeryFirstItem,
            hasVeryLastItem,
            scrollToKey);

        if (isHighlighted)
            // highlight entry when it has already been loaded
            ChatUI.HighlightedChatEntryId.Value = entryId;

        return result;
    }

    private async Task MonitorVisibleKeyChanges(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
            try {
                await VisibleKeys.Computed.WhenInvalidated(cancellationToken);
                var visibleKeys = await VisibleKeys.Use(cancellationToken);
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
                await InvokeAsync(() => { _fullyVisibleEntryIds = visibleEntryIds; });

                if (LastReadEntryState?.Value >= maxVisibleEntryId)
                    continue;

                if (LastReadEntryState != null)
                    LastReadEntryState.Value = maxVisibleEntryId;
            }
            catch (Exception e) when(e is not OperationCanceledException) {
                Log.LogWarning(e,
                    "Error monitoring visible key changes, LastReadEntryId = {LastReadEntryId}",
                    LastReadEntryState?.Value);
            }
    }

    // Event handlers

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
        => TryNavigateToEntry();

    private Task OnNavigateToChatEntry(NavigateToChatEntryEvent navigation, CancellationToken cancellationToken)
    {
        NavigateToEntry(navigation.ChatEntryId);
        return Task.CompletedTask;
    }
}
