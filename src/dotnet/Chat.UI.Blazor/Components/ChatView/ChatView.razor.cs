using System.Text.RegularExpressions;
using ActualChat.Chat.UI.Blazor.Events;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Components;

public partial class ChatView : ComponentBase, IVirtualListDataSource<ChatMessageModel>, IDisposable
{
    private const int PageSize = 40;

    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;
    private readonly CancellationTokenSource _disposeTokenSource = new();
    private readonly TaskCompletionSource _whenInitializedSource = TaskCompletionSourceExt.New();
    private readonly Suspender _getDataSuspender = new();

    private long? _lastNavigateToEntryId;
    private long? _initialReadEntryLid;
    private bool _itemVisibilityUpdateHasReceived;
    private bool _doNotShowNewMessagesSeparator;
    private IMutableState<ChatViewItemVisibility> _itemVisibility = null!;

    [Inject] private ILogger<ChatView> Log { get; init; } = null!;
    [Inject] private Session Session { get; init; } = null!;
    [Inject] private ChatUI ChatUI { get; init; } = null!;
    [Inject] private ChatPlayers ChatPlayers { get; init; } = null!;
    [Inject] private IChats Chats { get; init; } = null!;
    [Inject] private IAuthors Authors { get; init; } = null!;
    [Inject] private IChatPositions ChatPositions { get; init; } = null!;
    [Inject] private NavigationManager Nav { get; init; } = null!;
    [Inject] private History History { get; init; } = null!;
    [Inject] private TimeZoneConverter TimeZoneConverter { get; init; } = null!;
    [Inject] private MomentClockSet Clocks { get; init; } = null!;
    [Inject] private UICommander UICommander { get; init; } = null!;
    [Inject] private IStateFactory StateFactory { get; init; } = null!;

    private IMutableState<long?> NavigateToEntryLid { get; set; } = null!;
    private SyncedStateLease<ReadPosition>? ReadPositionState { get; set; } = null!;
    private Dispatcher Dispatcher => History.Dispatcher;
    private CancellationToken DisposeToken => _disposeTokenSource.Token;

    public IState<bool> IsViewportAboveUnreadEntry { get; private set; } = null!;
    public IState<ChatViewItemVisibility> ItemVisibility => _itemVisibility;
    public Task WhenInitialized => _whenInitializedSource.Task;

    [Parameter, EditorRequired] public Chat Chat { get; set; } = null!;
    [CascadingParameter] public RegionVisibility RegionVisibility { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        Log.LogDebug("Created for chat #{ChatId}", Chat.Id);
        Nav.LocationChanged += OnLocationChanged;
        try {
            NavigateToEntryLid = StateFactory.NewMutable(
                (long?)null,
                StateCategories.Get(GetType(), nameof(NavigateToEntryLid)));
            _itemVisibility = StateFactory.NewMutable(
                ChatViewItemVisibility.Empty,
                StateCategories.Get(GetType(), nameof(ItemVisibility)));
            ReadPositionState = await ChatUI.LeaseReadPositionState(Chat.Id, DisposeToken);
            IsViewportAboveUnreadEntry = StateFactory.NewComputed(
                new ComputedState<bool>.Options {
                    UpdateDelayer = FixedDelayer.Instant,
                    InitialValue = false,
                    Category = StateCategories.Get(GetType(), nameof(IsViewportAboveUnreadEntry)),
                },
                ComputeIsViewportAboveUnreadEntry);
            _initialReadEntryLid = ReadPositionState.Value.EntryLid;

            RegionVisibility.IsVisible.Updated += OnRegionVisibilityChanged;
        }
        finally {
            _whenInitializedSource.SetResult();
        }
    }

    public void Dispose()
    {
        RegionVisibility.IsVisible.Updated -= OnRegionVisibilityChanged;
        Nav.LocationChanged -= OnLocationChanged;
        _disposeTokenSource.Cancel();
        _getDataSuspender.IsSuspended = false;
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
            var chatIdRange = await Chats.GetIdRange(Session, Chat.Id, ChatEntryKind.Text, DisposeToken);
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
        if (DisposeToken.IsCancellationRequested)
            return;

        var uri = History.Uri;
        var fragment = new LocalUrl(uri).ToAbsolute(History.UrlMapper).ToUri().Fragment.TrimStart('#');
        if (long.TryParse(fragment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryId) && entryId > 0) {
            var uriWithoutFragment = Regex.Replace(uri, "#.*$", "");
            var cts = new CancellationTokenSource();
            var cancellationToken = cts.Token;
            _ = ForegroundTask.Run(async () => {
                try {
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                    _ = History.NavigateTo(uriWithoutFragment, true);
                }
                finally {
                    cts.CancelAndDisposeSilently();
                }
            }, CancellationToken.None);
            History.CancelWhen(cts, x => !OrdinalEquals(x.Uri, uri));
            NavigateToEntry(entryId);
        }
    }

    // Private methods

    async Task<VirtualListData<ChatMessageModel>> IVirtualListDataSource<ChatMessageModel>.GetData(
        VirtualListDataQuery query,
        VirtualListData<ChatMessageModel> oldData,
        CancellationToken cancellationToken)
    {
        await WhenInitialized; // No need for .ConfigureAwait(false) here

        // NOTE(AY): The old logic relying on _getDataSuspender was returning oldData,
        // which should never happen, coz it doesn't create any dependencies.
        await _getDataSuspender.WhenResumed();  // No need for .ConfigureAwait(false) here

        var chat = Chat;
        var chatId = chat.Id;

        var authorTask = Authors.GetOwn(Session, chatId, cancellationToken);
        var chatIdRangeTask = Chats.GetIdRange(Session, chatId, ChatEntryKind.Text, cancellationToken);
        var author = await authorTask;
        var authorId = author?.Id ?? Symbol.Empty;
        var chatIdRange = await chatIdRangeTask;
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

        var scrollToKey = mustScrollToEntry
            ? GetScrollToKey(chatEntries, entryLid)
            : null;

        // Do not show '-new-' separator after view is scrolled to the end anchor.
        if (!_doNotShowNewMessagesSeparator && _itemVisibilityUpdateHasReceived) {
            if (ShouldHideNewMessagesSeparator(ItemVisibility.Value, lastTile))
                _doNotShowNewMessagesSeparator = true;
        }
        var unreadEntryLidStarts = _doNotShowNewMessagesSeparator
            ? int.MaxValue
            : _initialReadEntryLid;

        var chatMessages = ChatMessageModel.FromEntries(
            chatEntries,
            oldData.Items,
            unreadEntryLidStarts,
            hasVeryFirstItem,
            TimeZoneConverter);

        var result = VirtualListData.New(
            new VirtualListDataQuery(idRangeToLoad.AsStringRange()),
            chatMessages,
            hasVeryFirstItem,
            hasVeryLastItem,
            scrollToKey);

        var visibility = ItemVisibility.Value;
        // Keep most recent entry as read if end anchor is visible
        if (visibility != ChatViewItemVisibility.Empty
            && visibility.IsEndAnchorVisible
            && hasVeryLastItem
            && chatEntries.Count > 0) {
            var lastEntryId = chatEntries[^1].Id.LocalId;
            if (ReadPositionState != null) {
                if (lastEntryId > readEntryLid)
                    ReadPositionState.Value = new ReadPosition(chatId,  lastEntryId);
                else if (readEntryLid >= chatIdRange.End)
                    ReadPositionState.Value = new ReadPosition(chatId,chatIdRange.End - 1);
            }
        }

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
        var isClientRequest = query.VirtualRange.HasValue;
        var extendedRange = (closeToTheStart: isCloseToTheStart, closeToTheEnd: isCloseToTheEnd) switch {
            (true, true) => chatIdRange.Expand(1), // extend to mitigate outdated id range
            (_, true) when isClientRequest => new Range<long>(queryRange.Start, chatIdRange.End + 2),
            (_, true) => new Range<long>(chatIdRange.End - (2 * PageSize), chatIdRange.End + 2),
            (true, _) => new Range<long>(chatIdRange.Start, queryRange.End),
            _ => queryRange,
        };
        return extendedRange;
    }

    private string? GetScrollToKey(List<ChatEntry> chatEntries, long scrollToEntryLid)
    {
        var scrollToEntry = chatEntries.FindLast(x => x.LocalId <= scrollToEntryLid)
            ?? chatEntries.Find(x => x.LocalId > scrollToEntryLid);
        if (scrollToEntry is not null)
            return scrollToEntry.LocalId.Format();

        Log.LogWarning("Failed to find entry to scroll to #{EntryLid}", scrollToEntryLid);
        return null;
    }

    // Event handlers

    private void OnItemVisibilityChanged(VirtualListItemVisibility virtualListItemVisibility)
    {
        var identity = virtualListItemVisibility.ListIdentity;
        if (!OrdinalEquals(identity, Chat.Id.Value)) {
            Log.LogWarning(
                $"{nameof(OnItemVisibilityChanged)} received wrong identity {{Identity}} while expecting {{ActualIdentity}}",
                identity,
                Chat.Id.Value);
            return;
        }

        _itemVisibilityUpdateHasReceived = true;
        var lastItemVisibility = ItemVisibility.Value;
        var itemVisibility = new ChatViewItemVisibility(virtualListItemVisibility);
        if (itemVisibility.ContentEquals(lastItemVisibility))
            return;

        _itemVisibility.Value = itemVisibility;
        var readPositionState = ReadPositionState;
        if (itemVisibility.IsEmpty || readPositionState == null)
            return;

        if (readPositionState.Value.EntryLid < itemVisibility.MaxEntryLid)
            readPositionState.Value = new ReadPosition(Chat.Id, itemVisibility.MaxEntryLid);
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
        var chatInfo = await ChatUI.Get(Chat.Id, cancellationToken).ConfigureAwait(true);
        var lastEntryId = chatInfo?.News.LastTextEntry?.Id ?? ChatEntryId.None;
        if (lastEntryId.IsNone)
            return false;

        return lastEntryId.LocalId > readEntryLid
            && readEntryLid > 0
            && visibility.MaxEntryLid > 0
            && visibility.MaxEntryLid < readEntryLid;
    }

    private bool ShouldHideNewMessagesSeparator(ChatViewItemVisibility itemVisibility, ChatTile lastTile)
    {
        if (!itemVisibility.IsEndAnchorVisible)
            return false;

        var newMessagesSeparatorIsVisible = _initialReadEntryLid.HasValue
            && itemVisibility.VisibleEntryLids.Contains(_initialReadEntryLid.Value);
        // If user still sees '-new-' separator while they has reached the end anchor, keep separator displayed.
        if (newMessagesSeparatorIsVisible)
            return false;

        var lastVisibleEntryLid = itemVisibility.MaxEntryLid;
        var lastEntryId = !lastTile.IsEmpty ? lastTile.Entries.Max(c => c.Id.LocalId) : -1;
        if (lastEntryId >= 0 && lastVisibleEntryLid < lastEntryId)
            return false;

        return true;
    }

    private void OnRegionVisibilityChanged(IState<bool> state, StateEventKind eventKind)
        => _ = Dispatcher.InvokeAsync(() => {
            if (_disposeTokenSource.IsCancellationRequested)
                return;

            var isVisible = RegionVisibility.IsVisible.Value;
            if (isVisible) {
                var readPosition = ReadPositionState!.Value.EntryLid;
                if (readPosition > _initialReadEntryLid)
                    _doNotShowNewMessagesSeparator = false;
                _initialReadEntryLid = readPosition;
            }
            UpdateInvisibleDelayer();
        });

    private void UpdateInvisibleDelayer()
    {
        var isVisible = RegionVisibility.IsVisible.Value;
        if (!DisposeToken.IsCancellationRequested)
            _getDataSuspender.IsSuspended = !isVisible;
    }
}
