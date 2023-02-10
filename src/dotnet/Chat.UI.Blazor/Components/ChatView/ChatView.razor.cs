using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Components;

public partial class ChatView : ComponentBase, IVirtualListDataSource<ChatMessageModel>, IDisposable
{
    private const int PageSize = 40;

    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;
    private readonly CancellationTokenSource _disposeToken = new ();
    private readonly TaskSource<Unit> _whenInitializedSource = TaskSource.New<Unit>(true);

    private long? _lastNavigateToEntryId;
    private long? _initialReadEntryLid;

    [Inject] private ILogger<ChatView> Log { get; init; } = null!;
    [Inject] private Session Session { get; init; } = null!;
    [Inject] private IStateFactory StateFactory { get; init; } = null!;
    [Inject] private ChatUI ChatUI { get; init; } = null!;
    [Inject] private ChatPlayers ChatPlayers { get; init; } = null!;
    [Inject] private IChats Chats { get; init; } = null!;
    [Inject] private IAuthors Authors { get; init; } = null!;
    [Inject] private IChatPositions ChatPositions { get; init; } = null!;
    [Inject] private NavigationManager Nav { get; init; } = null!;
    [Inject] private TimeZoneConverter TimeZoneConverter { get; init; } = null!;
    [Inject] private MomentClockSet Clocks { get; init; } = null!;
    [Inject] private UICommander UICommander { get; init; } = null!;

    internal IState<bool> IsViewportAboveUnreadEntryState { get; private set; } = null!;
    internal Task WhenInitialized => _whenInitializedSource.Task;
    private IMutableState<long?> NavigateToEntryLid { get; set; } = null!;
    private IMutableState<ChatViewItemVisibility> ItemVisibility { get; set; } = null!;
    private SyncedStateLease<ChatPosition>? ReadPositionState { get; set; } = null!;

    [CascadingParameter] public Chat Chat { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        Log.LogDebug("Created for chat #{ChatId}", Chat.Id);
        Nav.LocationChanged += OnLocationChanged;
        try {
            NavigateToEntryLid = StateFactory.NewMutable(
                (long?)null,
                StateCategories.Get(GetType(), nameof(NavigateToEntryLid)));
            ItemVisibility = StateFactory.NewMutable(
                ChatViewItemVisibility.Empty,
                StateCategories.Get(GetType(), nameof(ItemVisibility)));
            ReadPositionState = await ChatUI.LeaseReadPositionState(Chat.Id, _disposeToken.Token);
            IsViewportAboveUnreadEntryState = StateFactory.NewComputed(
                new ComputedState<bool>.Options {
                    UpdateDelayer = FixedDelayer.Instant,
                    InitialValue = false,
                    Category = StateCategories.Get(GetType(), nameof(IsViewportAboveUnreadEntryState)),
                },
                ComputeIsViewportAboveUnreadEntry);
            _initialReadEntryLid = ReadPositionState.Value.EntryLid;
        }
        finally {
            _whenInitializedSource.SetResult(Unit.Default);
        }
    }

    public void Dispose()
    {
        Nav.LocationChanged -= OnLocationChanged;
        _disposeToken.Cancel();
        ReadPositionState?.Dispose();
        ReadPositionState = null;
    }

    protected override async Task OnParametersSetAsync()
    {
        await WhenInitialized;
        TryNavigateToEntry();
    }

    public async Task NavigateToUnreadEntry()
    {
        long navigateToEntryLid;
        var readEntryLid = ReadPositionState?.Value.EntryLid ?? 0;
        if (readEntryLid > 0) {
            navigateToEntryLid = readEntryLid;
        }
        else {
            var chatIdRange = await Chats.GetIdRange(Session, Chat.Id, ChatEntryKind.Text, _disposeToken.Token);
            navigateToEntryLid = chatIdRange.ToInclusive().End;
        }

        // Reset to ensure the navigation will happen
        _initialReadEntryLid = navigateToEntryLid;
        NavigateToEntry(navigateToEntryLid);
    }

    public void NavigateToEntry(long entryLid)
    {
        // reset to ensure navigation will happen
        _lastNavigateToEntryId = null;
        NavigateToEntryLid.Value = null;
        NavigateToEntryLid.Value = entryLid;
    }

    public void TryNavigateToEntry()
    {
        // ignore location changed events if already disposed
        if (_disposeToken.IsCancellationRequested)
            return;

        var entryIdString = Nav.Uri.ToUri().Fragment.TrimStart('#');
        if (long.TryParse(entryIdString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryId) && entryId > 0) {
            var uriWithoutEntryId = new UriBuilder(Nav.Uri) {Fragment = ""}.ToString();
            Nav.ExecuteUnlessLocationChanged(TimeSpan.FromSeconds(3),
                () => Nav.NavigateTo(uriWithoutEntryId, false, true)
            );
            NavigateToEntry(entryId);
        }
    }

    // Private methods

    async Task<VirtualListData<ChatMessageModel>> IVirtualListDataSource<ChatMessageModel>.GetData(
        VirtualListDataQuery query,
        VirtualListData<ChatMessageModel> oldData,
        CancellationToken cancellationToken)
    {
        await WhenInitialized;

        var chat = Chat;
        var chatId = chat.Id;
        var author = await Authors.GetOwn(Session, chatId, cancellationToken);
        var authorId = author?.Id ?? Symbol.Empty;
        var chatIdRange = await Chats.GetIdRange(Session, chatId, ChatEntryKind.Text, cancellationToken);
        var readEntryLid = ReadPositionState?.Value.EntryLid ?? 0;

        // NOTE(AY): Commented this out:
        // - It triggers backward updates due to latency / eventual consistency
        // - Even though such updates to state are possible, they are ignored
        //   in ChatPositionBackend, but generate many extra commands.
        /*
        if (ReadPositionState != null && readEntryLid > 0 && readEntryLid >= chatIdRange.End) {
            // Looks like an error, let's reset last read position to the last entry Id
            readEntryLid = Math.Max(0, chatIdRange.End - 1);
            ReadPositionState.Value = new ChatPosition(readEntryLid);
        }
        */

        var entryLid = readEntryLid;
        var mustScrollToEntry = query.IsNone && entryLid != 0;

        // Get the last tile to check whether the Author has submitted a new entry
        var lastIdTile = IdTileStack.Layers[0].GetTile(chatIdRange.ToInclusive().End);
        var lastTile = await Chats.GetTile(Session,
            chatId,
            ChatEntryKind.Text,
            lastIdTile.Range,
            cancellationToken);
        foreach (var entry in lastTile.Entries) {
            if (entry.AuthorId != authorId || entry.LocalId <= _initialReadEntryLid)
                continue;

            // Scroll only on text entries
            if (entry.IsStreaming || entry.AudioEntryId != null)
                continue;

            // Scroll to the latest Author entry - e.g.m when author submits the new one
            _initialReadEntryLid = entry.LocalId;
            entryLid = entry.LocalId;
            mustScrollToEntry = true;
        }

        var isHighlighted = false;
        // Handle NavigateToEntry
        var navigateToEntryId = await NavigateToEntryLid.Use(cancellationToken);
        if (navigateToEntryId.HasValue && navigateToEntryId != _lastNavigateToEntryId) {
            isHighlighted = true;
            _lastNavigateToEntryId = navigateToEntryId;
            entryLid = navigateToEntryId.Value;
            if (!ItemVisibility.Value.IsFullyVisible(navigateToEntryId.Value))
                mustScrollToEntry = true;
        }
        var scrollToKey = mustScrollToEntry
            ? entryLid.Format()
            : null;

        // If we are scrolling somewhere - let's load the date near the entryId
        var idRangeToLoad = GetIdRangeToLoad(query, mustScrollToEntry ? entryLid : 0, chatIdRange);

        var hasVeryFirstItem = idRangeToLoad.Start <= chatIdRange.Start;
        var hasVeryLastItem = idRangeToLoad.End + 1 >= chatIdRange.End;

        var idTiles = IdTileStack.GetOptimalCoveringTiles(idRangeToLoad);
        var chatTiles = await idTiles
            .Select(idTile => Chats.GetTile(Session, chatId, ChatEntryKind.Text, idTile.Range, cancellationToken))
            .Collect();

        var chatEntries = chatTiles
            .SelectMany(chatTile => chatTile.Entries)
            .Where(e => e.Kind == ChatEntryKind.Text)
            .ToList();

        // do not render -new- section if we see the end anchor to avoid rerender
        var unreadEntryLidStarts = ItemVisibility.Value.IsEndAnchorVisible
            ? int.MaxValue
            : _initialReadEntryLid;

        var chatMessages = ChatMessageModel.FromEntries(
            chatEntries,
            oldData.Items,
            unreadEntryLidStarts,
            hasVeryFirstItem,
            hasVeryLastItem,
            TimeZoneConverter);

        var result = VirtualListData.New(
            new VirtualListDataQuery(idRangeToLoad.AsStringRange()),
            chatMessages,
            hasVeryFirstItem,
            hasVeryLastItem,
            scrollToKey);

        if (isHighlighted) {
            // highlight entry when it has already been loaded
            var highlightedEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, entryLid, AssumeValid.Option);
            ChatUI.HighlightEntry(highlightedEntryId, navigate: false);
        }

        return result;
    }

    private Range<long> GetIdRangeToLoad(VirtualListDataQuery query, long scrollToEntryLid, Range<long> chatIdRange)
    {
        var queryRange = query.IsNone
            ? new Range<long>(
                chatIdRange.End - (2 * PageSize),
                chatIdRange.End)
            : query.KeyRange
                .AsLongRange()
                .Expand(new Range<long>((long)query.ExpandStartBy, (long)query.ExpandEndBy));
        var scrollToEntryRange = scrollToEntryLid > 0
            ? new Range<long>(
                scrollToEntryLid - PageSize,
                scrollToEntryLid + PageSize)
            : queryRange;

        // Union (queryRange, scrollToEntryRange) if they overlap, otherwise pick scrollToEntryRange
        queryRange = scrollToEntryRange.Overlaps(queryRange)
            ? queryRange.MinMaxWith(scrollToEntryRange)
            : scrollToEntryRange;

        // Clamp queryRange by chatIdRange
        queryRange = queryRange.Clamp(chatIdRange);

        // Extend requested range if it's close to chat Id range
        var isCloseToTheEnd = queryRange.End >= chatIdRange.End - (PageSize / 2);
        var isCloseToTheStart = queryRange.Start <= chatIdRange.Start + (PageSize / 2);
        var extendedRange = (closeToTheStart: isCloseToTheStart, closeToTheEnd: isCloseToTheEnd) switch
        {
            (true, true) => chatIdRange.Expand(1), // extend to mitigate outdated id range
            (_, true) => new Range<long>(queryRange.Start, chatIdRange.End).Expand(1),
            (true, _) => new Range<long>(chatIdRange.Start, queryRange.End).Expand(1),
            _ => queryRange,
        };
        return extendedRange;
    }

    // Event handlers

    private void OnItemVisibilityChanged(VirtualListItemVisibility virtualListItemVisibility)
    {
        var lastItemVisibility = ItemVisibility.Value;
        var itemVisibility = new ChatViewItemVisibility(virtualListItemVisibility);
        if (itemVisibility.ContentEquals(lastItemVisibility))
            return;

        ItemVisibility.Value = itemVisibility;
        if (lastItemVisibility.IsEndAnchorVisible != itemVisibility.IsEndAnchorVisible)
            StateHasChanged(); // To re-render NavigateToEnd

        var readPositionState = ReadPositionState;
        if (itemVisibility.IsEmpty || readPositionState == null)
            return;

        if (readPositionState.Value.EntryLid < itemVisibility.MaxEntryLid)
            readPositionState.Value = new ChatPosition(itemVisibility.MaxEntryLid);
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
        => TryNavigateToEntry();

    private Task OnNavigateToChatEntry(NavigateToChatEntryEvent @event, CancellationToken cancellationToken)
    {
        if (@event.ChatEntryId.ChatId == Chat.Id)
            NavigateToEntry(@event.ChatEntryId.LocalId);
        return Task.CompletedTask;
    }

    private async Task<bool> ComputeIsViewportAboveUnreadEntry(IComputedState<bool> state, CancellationToken cancellationToken)
    {
        var readPositionState = ReadPositionState;
        var chatPosition = readPositionState != null ? await readPositionState.Use(cancellationToken) : null;
        var readEntryLid = chatPosition?.EntryLid ?? 0;
        var visibility = await ItemVisibility.Use(cancellationToken);
        return readEntryLid > 0 && visibility.MaxEntryLid > 0 && visibility.MaxEntryLid < readEntryLid;
    }
}
