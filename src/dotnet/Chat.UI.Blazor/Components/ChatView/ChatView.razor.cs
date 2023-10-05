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
    private long _lastReadEntryLid;
    private bool _itemVisibilityUpdateReceived;
    private bool _suppressNewMessagesEntry;
    private IMutableState<ChatViewItemVisibility> _itemVisibility = null!;
    private IComputedState<bool>? _isViewportAboveUnreadEntry = null;
    private ILogger? _log;

    private IServiceProvider Services => ChatContext.Services;
    private Session Session => ChatContext.Session;
    private Chat Chat => ChatContext.Chat;
    private ChatUI ChatUI => ChatContext.ChatUI;
    private IChats Chats => ChatContext.Chats;
    private IAuthors Authors => ChatContext.Authors;
    private NavigationManager Nav => ChatContext.Nav;
    private History History => ChatContext.History;
    private TimeZoneConverter TimeZoneConverter => ChatContext.TimeZoneConverter;
    private IStateFactory StateFactory => ChatContext.StateFactory;
    private Dispatcher Dispatcher => ChatContext.Dispatcher;
    private CancellationToken DisposeToken => _disposeTokenSource.Token;
    private ILogger Log => _log ??= Services.LogFor(GetType());

    private IMutableState<long?> NavigateToEntryLid { get; set; } = null!;
    private SyncedStateLease<ReadPosition> ReadPositionState { get; set; } = null!;

    public IState<bool> IsViewportAboveUnreadEntry => _isViewportAboveUnreadEntry!;
    public IState<ChatViewItemVisibility> ItemVisibility => _itemVisibility;
    public Task WhenInitialized => _whenInitializedSource.Task;

    [Parameter, EditorRequired] public ChatContext ChatContext { get; set; } = null!;
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
            _isViewportAboveUnreadEntry = StateFactory.NewComputed(
                new ComputedState<bool>.Options {
                    UpdateDelayer = FixedDelayer.Instant,
                    InitialValue = false,
                    Category = StateCategories.Get(GetType(), nameof(IsViewportAboveUnreadEntry)),
                },
                ComputeIsViewportAboveUnreadEntry);
            ReadPositionState = await ChatUI.LeaseReadPositionState(Chat.Id, DisposeToken);
            _lastReadEntryLid = ReadPositionState.Value.EntryLid;
            if (_whenInitializedSource.TrySetResult())
                RegionVisibility.IsVisible.Updated += OnRegionVisibilityChanged;
        }
        catch {
            _whenInitializedSource.TrySetCanceled();
        }
        finally {
            // Async part of this method may run after Dispose,
            // so Dispose won't see a new value of ReadPositionState
            if (_disposeTokenSource.IsCancellationRequested)
                ReadPositionState.DisposeSilently();
        }
    }

    public void Dispose()
    {
        if (_disposeTokenSource.IsCancellationRequested)
            return;

        _disposeTokenSource.Cancel();
        _whenInitializedSource.TrySetCanceled();
        RegionVisibility.IsVisible.Updated -= OnRegionVisibilityChanged;
        Nav.LocationChanged -= OnLocationChanged;
        _getDataSuspender.IsSuspended = false;
        _isViewportAboveUnreadEntry?.Dispose();
        ReadPositionState.DisposeSilently();
    }

    protected override async Task OnParametersSetAsync()
    {
        await WhenInitialized;
        TryNavigateToEntry();
    }

    public async Task NavigateToUnreadEntry()
    {
        await WhenInitialized;
        long navigateToEntryLid;
        var readEntryLid = ReadPositionState.Value.EntryLid;
        if (readEntryLid > 0)
            navigateToEntryLid = readEntryLid;
        else {
            var chatIdRange = await Chats.GetIdRange(Session, Chat.Id, ChatEntryKind.Text, DisposeToken);
            navigateToEntryLid = chatIdRange.ToInclusive().End;
        }

        // Reset to ensure the navigation will happen
        _lastReadEntryLid = navigateToEntryLid;
        NavigateToEntry(navigateToEntryLid);
    }

    public void NavigateToEntry(long entryLid)
    {
        // Reset to ensure navigation will happen
        _lastNavigateToEntryId = null;
        NavigateToEntryLid.Value = entryLid;
        NavigateToEntryLid.Invalidate();
    }

    public void TryNavigateToEntry()
    {
        // Ignore location changed events if already disposed
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

        using var activity = BlazorUITrace.StartActivity("ChatView.GetVirtualListData");

        var chat = Chat;
        var chatId = chat.Id;
        activity?.SetTag("AC." + nameof(ChatId), chatId);

        var authorTask = Authors.GetOwn(Session, chatId, cancellationToken);
        var chatIdRangeTask = Chats.GetIdRange(Session, chatId, ChatEntryKind.Text, cancellationToken);
        var author = await authorTask;
        var authorId = author?.Id ?? Symbol.Empty;
        var chatIdRange = await chatIdRangeTask;
        var readEntryLid = ReadPositionState.Value.EntryLid;

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
        var mustScrollToEntry = oldData.IsNone && entryLid != 0;

        // Get the last tile to check whether the Author has submitted a new entry
        var lastIdTile = IdTileStack.Layers[0].GetTile(chatIdRange.ToInclusive().End);
        var lastTile = await Chats.GetTile(Session,
            chatId,
            ChatEntryKind.Text,
            lastIdTile.Range,
            cancellationToken);
        foreach (var entry in lastTile.Entries) {
            if (entry.AuthorId != authorId || entry.LocalId <= _lastReadEntryLid)
                continue;

            // Scroll only on text entries
            if (entry.IsStreaming || entry.AudioEntryId != null)
                continue;

            // Scroll to the latest Author entry - e.g.m when author submits the new one
            _lastReadEntryLid = entry.LocalId;
            entryLid = entry.LocalId;
            mustScrollToEntry = true;
        }

        var isNavigatingToEntry = false;
        // Handle NavigateToEntry
        var navigateToEntryId = await NavigateToEntryLid.Use(cancellationToken);
        if (navigateToEntryId.HasValue && navigateToEntryId != _lastNavigateToEntryId) {
            isNavigatingToEntry = true;
            _lastNavigateToEntryId = navigateToEntryId;
            entryLid = navigateToEntryId.Value;
            if (!ItemVisibility.Value.IsFullyVisible(navigateToEntryId.Value))
                mustScrollToEntry = true;
        }

        // If we are scrolling somewhere - let's load the date near the entryId
        var idRangeToLoad = GetIdRangeToLoad(query, oldData, mustScrollToEntry ? entryLid : 0, chatIdRange);

        activity?.SetTag("AC." + "IdRange", chatIdRange.AsOneLineString());
        activity?.SetTag("AC." + "ReadEntryLid", readEntryLid);
        activity?.SetTag("AC." + "IdRangeToLoad", idRangeToLoad.AsOneLineString());

        var hasVeryFirstItem = idRangeToLoad.Start <= chatIdRange.Start;
        var hasVeryLastItem = idRangeToLoad.End + 1 >= chatIdRange.End;

        // get tiles from the smallest tile layer
        var idTiles = IdTileStack.FirstLayer.GetOptimalCoveringTiles(idRangeToLoad);
        var chatTiles = await idTiles
            .Select(idTile => Chats.GetTile(Session, chatId, ChatEntryKind.Text, idTile.Range, cancellationToken))
            .Collect();

        var entries = chatTiles
            .SelectMany(chatTile => chatTile.Entries)
            .Where(e => e.Kind == ChatEntryKind.Text)
            .ToList();

        var scrollToKey = mustScrollToEntry
            ? GetScrollToKey(entries, entryLid)
            : null;

        // Do not show '-new-' separator after view is scrolled to the end anchor.
        if (!_suppressNewMessagesEntry && _itemVisibilityUpdateReceived) {
            if (ShouldSuppressNewMessagesEntry(ItemVisibility.Value, lastTile))
                _suppressNewMessagesEntry = true;
        }

        if (entries.Count == 0) {
            var isEmpty = await ChatUI.IsEmpty(chatId, cancellationToken);
            if (isEmpty)
                return new VirtualListData<ChatMessageModel>(ChatMessageModel.FromEmpty(Chat.Id)) {
                    HasVeryFirstItem = true,
                    HasVeryLastItem = true,
                    ScrollToKey = null,
                    RequestedStartExpansion = null,
                    RequestedEndExpansion = null,
                };
        }

        var messages = ChatMessageModel.FromEntries(
            entries,
            oldData.Items,
            _suppressNewMessagesEntry ? long.MaxValue : _lastReadEntryLid,
            hasVeryFirstItem,
            TimeZoneConverter);

        var areSameMessages = !oldData.IsNone
            && messages.Count == oldData.Items.Count
            && messages
                .Zip(oldData.Items)
                .All(pair => ReferenceEquals(pair.First, pair.Second));

        var result = areSameMessages && OrdinalEquals(scrollToKey, oldData.ScrollToKey) && !isNavigatingToEntry
            ? oldData
            : new VirtualListData<ChatMessageModel>(messages) {
                HasVeryFirstItem = hasVeryFirstItem,
                HasVeryLastItem = hasVeryLastItem,
                ScrollToKey = scrollToKey,
                RequestedStartExpansion = query.IsNone
                    ? null
                    : query.ExpandStartBy,
                RequestedEndExpansion = query.IsNone
                    ? null
                    : query.ExpandEndBy,
            };

        var visibility = ItemVisibility.Value;
        // Keep most recent entry as read if end anchor is visible
        if (visibility != ChatViewItemVisibility.Empty
            && visibility.IsEndAnchorVisible
            && hasVeryLastItem
            && entries.Count > 0) {
            var lastEntryLid = entries[^1].Id.LocalId;
            if (lastEntryLid > readEntryLid)
                ReadPositionState.Value = new ReadPosition(chatId,  lastEntryLid);
            else if (readEntryLid >= chatIdRange.End)
                ReadPositionState.Value = new ReadPosition(chatId,chatIdRange.End - 1);
        }

        if (isNavigatingToEntry) {
            // highlight entry when it has already been loaded
            var highlightedEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, entryLid, AssumeValid.Option);
            ChatUI.HighlightEntry(highlightedEntryId, navigate: false);
        }

        return result;
    }

    private Range<long> GetIdRangeToLoad(
        VirtualListDataQuery query,
        VirtualListData<ChatMessageModel> oldData,
        long scrollToEntryLid,
        Range<long> chatIdRange)
    {
        var queryRange = (query.IsNone, oldData.Items.Count == 0) switch {
            (true, true) => new Range<long>(chatIdRange.End - (2 * PageSize), chatIdRange.End),
            (true, false) => new Range<long>(oldData.Items[0].Entry.LocalId, oldData.Items[^1].Entry.LocalId),
            _ => query.KeyRange
                .AsLongRange()
                .Expand(new Range<long>(query.ExpandStartBy, query.ExpandEndBy)),
        };

        // Last read position might point to already deleted entries, OR it might be corrupted!
        var scrollToEntryRange = scrollToEntryLid > 0 && chatIdRange.Contains(scrollToEntryLid)
            ? new Range<long>(
                scrollToEntryLid - PageSize,
                scrollToEntryLid + (2 * PageSize))
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
        var extendedRange = (closeToTheStart: isCloseToTheStart, closeToTheEnd: isCloseToTheEnd) switch {
            (true, true) => chatIdRange.Expand(1), // extend to mitigate outdated id range
            (_, true) => new Range<long>(queryRange.Start, chatIdRange.End + 2),
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

        _itemVisibilityUpdateReceived = true;
        var lastItemVisibility = ItemVisibility.Value;
        var itemVisibility = new ChatViewItemVisibility(virtualListItemVisibility);
        if (itemVisibility.ContentEquals(lastItemVisibility))
            return;

        _itemVisibility.Value = itemVisibility;
        if (itemVisibility.IsEmpty || !WhenInitialized.IsCompletedSuccessfully)
            return;

        if (ReadPositionState.Value.EntryLid < itemVisibility.MaxEntryLid)
            ReadPositionState.Value = new ReadPosition(Chat.Id, itemVisibility.MaxEntryLid);
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
        if (!WhenInitialized.IsCompleted)
            await WhenInitialized;

        var readEntryLid = (await ReadPositionState.Use(cancellationToken)).EntryLid;
        var itemVisibility = await ItemVisibility.Use(cancellationToken);
        var chatInfo = await ChatUI.Get(Chat.Id, cancellationToken).ConfigureAwait(true);
        var lastEntryLid = chatInfo?.News.LastTextEntry?.Id ?? ChatEntryId.None;
        if (lastEntryLid.IsNone)
            return false;

        return lastEntryLid.LocalId > readEntryLid
            && readEntryLid > 0
            && itemVisibility.MaxEntryLid > 0
            && itemVisibility.MaxEntryLid < readEntryLid;
    }

    private bool ShouldSuppressNewMessagesEntry(ChatViewItemVisibility itemVisibility, ChatTile lastTile)
    {
        if (!itemVisibility.IsEndAnchorVisible)
            return false;

        var mustShow = _lastReadEntryLid != 0 && itemVisibility.VisibleEntryLids.Contains(_lastReadEntryLid);
        // If user still sees '-new-' separator while they has reached the end anchor, keep separator displayed.
        if (mustShow)
            return false;

        var lastVisibleEntryLid = itemVisibility.MaxEntryLid;
        var lastEntryLid = !lastTile.IsEmpty ? lastTile.Entries.Max(c => c.Id.LocalId) : -1;
        return lastEntryLid < 0 || lastVisibleEntryLid >= lastEntryLid;
    }

    private void OnRegionVisibilityChanged(IState<bool> state, StateEventKind eventKind)
        => _ = Dispatcher.InvokeAsync(() => {
            if (_disposeTokenSource.IsCancellationRequested || !WhenInitialized.IsCompletedSuccessfully)
                return;

            var isVisible = RegionVisibility.IsVisible.Value;
            if (isVisible) {
                var readEntryLid = ReadPositionState.Value.EntryLid;
                if (readEntryLid > _lastReadEntryLid)
                    _suppressNewMessagesEntry = false;
                _lastReadEntryLid = readEntryLid;
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
