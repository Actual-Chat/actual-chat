using ActualChat.UI.Blazor.App.Events;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Diagnostics;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using ActualLab.Diagnostics;

namespace ActualChat.UI.Blazor.App.Components;

public partial class ChatView : ComponentBase, IVirtualListDataSource<ChatMessage>, IDisposable
{
    public static readonly TimeSpan FastUpdateRecency = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan FastUpdateDelay = TimeSpan.FromMilliseconds(20);
    public static readonly TimeSpan SlowUpdateDelay = TimeSpan.FromMilliseconds(100);

    private readonly CancellationTokenSource _disposeTokenSource;
    private readonly TaskCompletionSource _whenInitializedSource = TaskCompletionSourceExt.New();

    private Task _updateReadStateTask = null!;
    private SyncedStateLease<ReadPosition> _readPosition = null!;
    private MutableState<ChatViewItemVisibility> _itemVisibility = null!;
    private MutableState<long> _shownReadEntryLid = null!;
    private MutableState<Navigation?> _nextNavigation = null!;
    private Range<long> _lastIdRangeToLoad;
    private ChatContext _chatContext = null!;

    private ChatUIHub Hub { get; set; }
    private Session Session => Hub.Session();
    private ICommander Commander => Hub.Commander();
    private ChatUI ChatUI => Hub.ChatUI;
    private IChats Chats => Hub.Chats;
    private Media.IMediaLinkPreviews MediaLinkPreviews => Hub.MediaLinkPreviews;
    private IAuthors Authors => Hub.Authors;
    private NavigationManager Nav => Hub.Nav;
    private History History => Hub.History;
    private DateTimeConverter DateTimeConverter => Hub.DateTimeConverter;
    private StateFactory StateFactory => Hub.StateFactory();
    private Dispatcher Dispatcher => Hub.Dispatcher;
    private CancellationToken DisposeToken { get; }

    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= Hub.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);

    public IState<ReadPosition> ReadPosition => _readPosition;
    public IState<long> ShownReadEntryLid => _shownReadEntryLid;

    public IState<ChatViewItemVisibility> ItemVisibility => _itemVisibility;
    public Task WhenInitialized => _whenInitializedSource.Task;

    [Parameter, EditorRequired] public ChatId ChatId { get; set; } = ChatId.None;
    [CascadingParameter] public RegionVisibility RegionVisibility { get; set; } = null!;

    public ChatView(ChatUIHub hub)
    {
        Hub = hub;
        _disposeTokenSource = new ();
        DisposeToken = _disposeTokenSource.Token;
    }

    protected override async Task OnInitializedAsync()
    {
        Log.LogDebug("Created for chat #{ChatId}", ChatId);
        Nav.LocationChanged += OnLocationChanged;
        try {
            var type = GetType();
            _itemVisibility = StateFactory.NewMutable(
                ChatViewItemVisibility.Empty,
                StateCategories.Get(type, nameof(ItemVisibility)));
            _nextNavigation = StateFactory.NewMutable(
                (Navigation?)null,
                StateCategories.Get(type, nameof(_nextNavigation)));
            _shownReadEntryLid = StateFactory.NewMutable(
                0L,
                StateCategories.Get(type, nameof(ShownReadEntryLid)));
            _readPosition = await ChatUI.LeaseReadPositionState(ChatId, DisposeToken);
            _shownReadEntryLid.Value = _readPosition.Value.EntryLid;
            _whenInitializedSource.TrySetResult();
            _updateReadStateTask = AsyncChain.From(UpdateReadState)
                .Log(LogLevel.Debug, Log)
                .RetryForever(RetryDelaySeq.Exp(0.5, 3), Log)
                .RunIsolated(DisposeToken);
            UpdateGroupChatUsageList();
        }
        catch {
            _whenInitializedSource.TrySetCanceled();
        }
        finally {
            // Async part of this method may run after Dispose,
            // so Dispose won't see a new value of ReadPositionState
            if (_disposeTokenSource.IsCancellationRequested)
                _readPosition.DisposeSilently();
        }
    }

    protected override void OnParametersSet()
    {
        if (_disposeTokenSource.IsCancellationRequested)
            return;

        _chatContext = new ChatContext(Hub, ChatId);
    }

    protected override Task OnParametersSetAsync()
        => NavigateToUrlFragment();

    public void Dispose()
    {
        if (_disposeTokenSource.IsCancellationRequested)
            return;

        _disposeTokenSource.CancelAndDisposeSilently();
        _whenInitializedSource.TrySetCanceled();
        _readPosition.DisposeSilently();
        Nav.LocationChanged -= OnLocationChanged;
    }

    public async Task NavigateToNext(long entryLid, bool highlight, bool updateReadPosition = false)
    {
        var navEntry = await GetFirstEntry(entryLid, DisposeToken).ConfigureAwait(false);
        if (navEntry == null) {
            Log.LogWarning("NavigateToNext: entry not found: #{EntryLid}", entryLid);
            return;
        }
        if (navEntry.LocalId == entryLid) {
            var nextEntry = await GetFirstEntry(entryLid + 1, DisposeToken).ConfigureAwait(false);
            navEntry = nextEntry ?? navEntry;
        }
        await NavigateTo(navEntry.LocalId, highlight, updateReadPosition).ConfigureAwait(false);
    }

    public async Task NavigateTo(long entryLid, bool highlight, bool updateReadPosition = false)
    {
        await WhenInitialized;
        if (updateReadPosition)
            _shownReadEntryLid.Value = UpdateReadPosition(entryLid);
        _nextNavigation.Value = new Navigation(entryLid, highlight);
    }

    // Event handlers

    private async Task NavigateToUrlFragment()
    {
        await WhenInitialized;
        // Ignore location changed events if already disposed
        if (DisposeToken.IsCancellationRequested)
            return;

        var sUri = History.Uri;
        var localUrl = new LocalUrl(sUri);
        if (!localUrl.IsChat(out _, out long entryId) || entryId <= 0)
            return;

        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        _ = ForegroundTask.Run(async () => {
                try {
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                    var uri = localUrl.ToAbsolute(Hub.UrlMapper()).ToUri();
                    var uriWithoutMsgId = uri.DropQueryItem(Links.ChatEntryLidQueryParameterName).PathAndQuery;
                    _ = History.NavigateTo(uriWithoutMsgId, true);
                }
                finally {
                    cts.CancelAndDisposeSilently();
                }
            },
            CancellationToken.None);
        History.CancelWhen(cts, x => !OrdinalEquals(x.Url, sUri));
        await NavigateTo(entryId, true);
    }

    private void OnItemVisibilityChanged(VirtualListItemVisibility virtualListItemVisibility)
    {
        var identity = virtualListItemVisibility.ListIdentity;
        if (!OrdinalEquals(identity, ChatId.Value)) {
            Log.LogWarning(
                $"{nameof(OnItemVisibilityChanged)} received wrong identity {{Identity}} while expecting {{ActualIdentity}}",
                identity,
                ChatId.Value);
            return;
        }

        var lastItemVisibility = ItemVisibility.Value;
        var itemVisibility = new ChatViewItemVisibility(virtualListItemVisibility);
        if (itemVisibility.IsIdenticalTo(lastItemVisibility) && !ReferenceEquals(lastItemVisibility, ChatViewItemVisibility.Empty))
            return;

        _itemVisibility.Value = itemVisibility;
        if (itemVisibility.IsEmpty || !WhenInitialized.IsCompletedSuccessfully)
            return;

        UpdateReadPosition(itemVisibility.MaxEntryLid);
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
        => _ = NavigateToUrlFragment();

    private Task OnNavigateToChatEntry(NavigateToChatEntryEvent @event, CancellationToken cancellationToken)
    {
        if (@event.ChatEntryId.ChatId == ChatId)
            _ = NavigateTo(@event.ChatEntryId.LocalId, @event.MustHighlight);
        return Task.CompletedTask;
    }

    // AsyncChains

    private async Task UpdateReadState(CancellationToken cancellationToken)
    {
        var chatId = ChatId;
        var entryReader = new ChatEntryReader(Chats, Session, chatId, ChatEntryKind.Text);
        var author = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var authorId = author?.Id ?? AuthorId.None;
        var chatIdRange = await Chats
            .GetIdRange(Session, chatId, ChatEntryKind.Text, cancellationToken)
            .ConfigureAwait(false);

        // Getting very last chat entry
        var chatNews = await Chats.GetNews(Session, chatId, cancellationToken).ConfigureAwait(false);
        var chatIdGap = new Range<long>(chatNews.TextEntryIdRange.End, chatIdRange.End);
        var lastEntry = await entryReader.ReadReverse(chatIdGap, cancellationToken)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        lastEntry ??= chatNews.LastTextEntry;
        var lastEntryLid = lastEntry?.LocalId ?? 0;

        // Observing new entries
        var entries = entryReader.Observe(chatIdRange.End, cancellationToken);
        await foreach (var entry in entries.ConfigureAwait(false)) {
            if (entry.AuthorId != authorId) {
                lastEntryLid = entry.LocalId;
                continue;
            }

            var shownReadEntryLid = _shownReadEntryLid.Value;
            var lastEntryWasShownAsRead = lastEntryLid == shownReadEntryLid;
            lastEntryLid = entry.LocalId;
            if (lastEntryWasShownAsRead) {
                _shownReadEntryLid.Value = lastEntryLid;
                UpdateReadPosition(lastEntryLid);
            }
            if (entry.IsStreaming || entry.AudioEntryLid.HasValue)
                continue;

            await NavigateTo(lastEntryLid, false).ConfigureAwait(false);
        }
    }

    // GetData & related methods

    // The following 5 cases should be handled by this method:
    // - Return data for the first-time request to the last read position if exists, or to the end of the chat messages
    // - Return updated data on invalidation of requested tiles
    // - Return new messages in addition to already rendered messages - monitoring last tiles if rendered near the end
    // - Return last chat messages when the author has submitted a new message - monitoring dedicated state
    // - Return messages around an anchor message we are navigating to
    // If the message data is the same it should return same instances of data tiles to reduce re-rendering
    async Task<VirtualListData<ChatMessage>> IVirtualListDataSource<ChatMessage>.GetData(
        VirtualListDataQuery query,
        VirtualListData<ChatMessage> renderedData,
        CancellationToken cancellationToken)
    {
        var startedAt = CpuTimestamp.Now;
        await WhenInitialized;

        var isChatViewVisible = RegionVisibility.IsVisible;
        if (!isChatViewVisible.Value) {
            // Chat is invisible now, let's suspend & await for it to become visible
            using (Computed.BeginIsolation())
                await isChatViewVisible.When(x => x, cancellationToken);
            _shownReadEntryLid.Value = _readPosition.Value.EntryLid;
        }

        // Update delay: we want to collect as many dependencies as possible here,
        // but don't want to delay rapid updates.
        // We don't need delays when data is being requested by the client code - e.g. when query isn't None
        if (query.IsNone && renderedData.Index > 0) {
            var lastComputedAt = renderedData.IsNone ? startedAt : renderedData.ComputedAt;
            var isFastUpdate = startedAt - lastComputedAt <= FastUpdateRecency;
            var delay = startedAt + (isFastUpdate ? FastUpdateDelay : SlowUpdateDelay) - CpuTimestamp.Now;
            if (delay > TimeSpan.FromMilliseconds(10)) {
                await Task.Delay(delay, cancellationToken);
                DebugLog?.LogDebug("GetData: delayed for {Delay}", delay);
            }
        }

        // ReSharper disable once ExplicitCallerInfoArgument
        using var activity = AppUIInstruments.ActivitySource.StartActivity(GetType(), "GetVirtualListData");

        var chatId = ChatId;
        activity?.SetTag("AC." + nameof(ChatId), chatId);

        // Handling NavigateTo + default navigation
        var isFirstRender = renderedData.IsNone && query.IsNone;
        var readEntryLid = _readPosition.Value.EntryLid;
        var nav = await _nextNavigation.Use(cancellationToken)
            ?? (isFirstRender && readEntryLid != 0 ? new Navigation(readEntryLid, false) : null);
        if (ReferenceEquals(nav, renderedData.NavigationState)) // Handles null case as well
            nav = null;

        var itemVisibility = ItemVisibility.Value;
        var mustScrollToEntry = nav != null && !itemVisibility.IsFullyVisible(nav.EntryLid);
        Computed<Range<long>> cChatIdRange;
        using (Computed.BeginIsolation()) {
            cChatIdRange = await Computed.Capture(
                () => Chats.GetIdRange(Session, chatId, ChatEntryKind.Text, cancellationToken),
                cancellationToken);
        }
        var chatIdRange = cChatIdRange.Value;
        var idRangeToLoad = GetIdRangeToLoad(query, renderedData, nav, chatIdRange, _itemVisibility.Value);
        var hasVeryFirstItem = idRangeToLoad.Start <= chatIdRange.Start;
        var hasVeryLastItem = idRangeToLoad.End >= chatIdRange.End;
        var hasAllItems = hasVeryFirstItem && hasVeryLastItem;
        if (idRangeToLoad.End + ChatUI.HalfLoadLimit >= chatIdRange.End)
            await cChatIdRange.Use(cancellationToken); // Add dependency on chatIdRange

        activity?.SetTag("AC." + "IdRange", chatIdRange.Format());
        activity?.SetTag("AC." + "ReadEntryLid", readEntryLid);
        activity?.SetTag("AC." + "IdRangeToLoad", idRangeToLoad.Format());
        DebugLog?.LogDebug("GetData: #{ChatId} -> {IdRangeToLoad}", chatId, idRangeToLoad.Format());

        // Prefetching new tiles
        var lastIdRangeToLoad = _lastIdRangeToLoad;
        _lastIdRangeToLoad = idRangeToLoad;
        var newIdRanges = idRangeToLoad.Subtract(lastIdRangeToLoad);
        _ = ChatUI.PrefetchTiles(chatId, newIdRanges.Item1, cancellationToken);
        _ = ChatUI.PrefetchTiles(chatId, newIdRanges.Item2, cancellationToken);

        var idTiles = GetIdTilesToLoad(idRangeToLoad, chatIdRange);
        var tryUpdateShownReadEntryLid = true;

        rebuildTiles: // Building actual virtual list tiles

        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return VirtualListData<ChatMessage>.None;

        var isBot = chat.IsAiSearchChat();
        var prevMessage = hasVeryFirstItem ? ChatMessage.Welcome(chatId, isBot) : null;
        var shownReadyEntryLid = _shownReadEntryLid.Value;
        var renderedTiles = renderedData.Tiles.ToDictionary(t => t.Key, StringComparer.Ordinal);
        var tiles = new List<VirtualListTile<ChatMessage>>();
        foreach (var idTile in idTiles) {
            var lastReadEntryLid = shownReadyEntryLid;
            if (lastReadEntryLid < idTile.Range.Start)
                lastReadEntryLid = 0;
            else if (shownReadyEntryLid >= idTile.Range.End - 1)
                lastReadEntryLid = long.MaxValue;
            var tile = await ChatUI.GetTile(
                chatId,
                chat.Rules.Author?.Id ?? AuthorId.None,
                idTile.Range,
                prevMessage,
                lastReadEntryLid,
                cancellationToken);
            if (tile.Items.Count == 0)
                continue;

            if (renderedTiles.TryGetValue(tile.Key, out var renderedTile)) {
                var tileToAdd = ReferenceEquals(tile, renderedTile) || renderedTile.Items.SequenceEqual(tile.Items)
                    ? renderedTile
                    : tile;
                tiles.Add(tileToAdd);
            }
            else
                tiles.Add(tile);
            prevMessage = tile.Items[^1];
#if false
        // Uncomment for debugging:
        DebugLog?.LogDebug("Tile: #{IdRange}, {IsUnread}, {LastReadEntryLid}",
            idTile.Range.Format(), isUnread, lastReadEntryLidArg);
        foreach (var item in tile.Items)
            DebugLog?.LogDebug("- {Key}: {ReplacementKind}", item.Key, item.ReplacementKind);
#endif
        }
        if (tiles.Count == 0) {
            var isEmpty = await ChatUI.IsEmpty(chatId, cancellationToken);
            if (isEmpty)
                return new VirtualListData<ChatMessage>([
                    new VirtualListTile<ChatMessage>(default(Range<long>), new [] { ChatMessage.Welcome(ChatId, isBot) }),
                ]) {
                    HasVeryFirstItem = true,
                    HasVeryLastItem = true,
                    ScrollToKey = null,
                    NavigationState = nav ?? renderedData.NavigationState,
                    ItemVisibilityState = itemVisibility,
                };
        }
        else if (query.ExpectedCount > tiles.SelectMany(t => t.Items).Count() / 2 && !hasAllItems) {
            var startOffset = (int)(query.MoveRange.Start - ChatUI.SecondTileSize);
            var endOffset = (int)(query.MoveRange.End + ChatUI.SecondTileSize);
            var extendedQuery = new VirtualListDataQuery(query.KeyRange, query.VirtualRange, new Range<int>(startOffset, endOffset)) {
                ExpectedCount = query.ExpectedCount,
            };
            return await ((IVirtualListDataSource<ChatMessage>)this)
                .GetData(extendedQuery, renderedData, cancellationToken)
                .ConfigureAwait(false);
        }

        if (tryUpdateShownReadEntryLid
            && !ReferenceEquals(itemVisibility, renderedData.ItemVisibilityState)
            && TryUpdateShownReadEntryLid(tiles, itemVisibility)) {
            tryUpdateShownReadEntryLid = false;
            goto rebuildTiles;
        }

        // Locating navigation entry
        var navEntry = (ChatEntry?)null;
        if (nav != null) {
            navEntry = tiles
                .SkipWhile(t => t.Items[^1].Entry.LocalId < nav.EntryLid)
                .SelectMany(t => t.Items)
                .FirstOrDefault(x => x.Entry.LocalId == nav.EntryLid && !x.IsReplacement)?.Entry;
            if (navEntry == null)
                Log.LogWarning("GetData: entry not found in the loaded set: #{EntryLid}", nav.EntryLid);
            else if (nav.MustHighlight)
                ChatUI.HighlightEntry(navEntry.Id, navigate: false);
        }
        var result = new VirtualListData<ChatMessage>(tiles) {
            Index = renderedData.Index + 1,
            HasVeryFirstItem = hasVeryFirstItem,
            HasVeryLastItem = hasVeryLastItem,
            ScrollToKey = navEntry != null && mustScrollToEntry ? navEntry.LocalId.Format() : null,
            NavigationState = nav ?? renderedData.NavigationState,
            ItemVisibilityState = itemVisibility,
        };

        // do not return new instance if data is the same to prevent re-renders
        return !mustScrollToEntry && result.IsSimilarTo(renderedData)
            ? renderedData
            : result;
    }

    private Tile<long>[] GetIdTilesToLoad(Range<long> idRangeToLoad, Range<long> chatIdRange)
    {
        DebugLog?.LogDebug("GetIdTilesToLoad: {IdRangeToLoad} {ChatIdRange}", idRangeToLoad, chatIdRange);
        idRangeToLoad = new Range<long>(Math.Max(chatIdRange.Start, idRangeToLoad.Start), idRangeToLoad.End);
        var firstLayer = ChatUI.IdTileStack.FirstLayer;
        var secondLayer = ChatUI.IdTileStack.Layers[1];
        var tiles = ArrayBuffer<Tile<long>>.Lease(true);
        try {
            // hot range assumes high probability of changes - so close to the end of the chat messages
            var hotRangeTiles = firstLayer.GetCoveringTiles(new Range<long>(chatIdRange.End - secondLayer.TileSize, chatIdRange.End + firstLayer.TileSize));
            var hotRange = new Range<long>(hotRangeTiles[0].Range.Start, hotRangeTiles[^1].Range.End);
            if (!idRangeToLoad.Overlaps(hotRange)) // idRangeToLoad has already been extended to cover ids beyond existing chat id range
                hotRange = default;

            var coldRange = hotRange.IsEmpty
                ? idRangeToLoad
                : new Range<long>(secondLayer.GetTile(idRangeToLoad.Start).Start, hotRange.Start);

            // load second layer stack to improve reuse if large tiles during scroll
            tiles.AddRange(secondLayer.GetCoveringTiles(coldRange));
            var lastColdRange = tiles.Count > 0
                ? tiles[^1].Range
                : default;
            tiles.AddRange(firstLayer.GetCoveringTiles(hotRange).SkipWhile(hr => hr.Range.Overlaps(lastColdRange)));
            var result = tiles.ToArray();
            // if (result.DistinctBy(x => x.Range).Count() != result.Length)
            //     Debugger.Break();
            return result;
        }
        finally {
            tiles.Release();
        }
    }

    private Range<long> GetIdRangeToLoad(
        VirtualListDataQuery query,
        VirtualListData<ChatMessage> oldData,
        Navigation? scrollAnchor,
        Range<long> chatIdRange,
        ChatViewItemVisibility itemVisibility)
    {
        var firstLayer = ChatUI.IdTileStack.Layers[0];
        var secondLayer = ChatUI.IdTileStack.Layers[1];
        var minTileSize = ChatUI.IdTileStack.MinTileSize;
        var chatIdRangeEndPlus = chatIdRange.End + minTileSize;
        var firstItem = oldData.FirstItem;
        var lastItem = oldData.LastItem;
        var range = (!query.IsNone, firstItem != null) switch {
            // No query, no data -> initial load
            (false, false) => new Range<long>(
                secondLayer.GetTile(chatIdRange.End - ChatUI.LoadLimit).Start,
                secondLayer.GetTile(chatIdRangeEndPlus).End).IntersectWith(new Range<long>(chatIdRange.Start, long.MaxValue)),

            // No query, but there is old data + we know visible items
            // KEEP THIS case, otherwise virtual list will grow indefinitely!
            (false, true) when !itemVisibility.IsEmpty
                => new Range<long>(itemVisibility.MinEntryLid, itemVisibility.MaxEntryLid)
                    .Expand(ChatUI.SecondTileSize)
                    .ExpandToTiles(firstLayer),

            // No query, but there is old data -> retaining it
            (false, true) => new Range<long>(firstItem!.Entry.LocalId, lastItem!.Entry.LocalId),

            // Query is there, so data is irrelevant
            _ => query.KeyRange.ToLongRange(true).Move(query.MoveRange),
        };

        // If we are scrolling somewhere, let's extend the range to scrollAnchor & nearby entries.
        if (scrollAnchor is { } vScrollAnchor) {
            var scrollAnchorRange = new Range<long>(
                vScrollAnchor.EntryLid - ChatUI.HalfLoadLimit,
                vScrollAnchor.EntryLid + ChatUI.HalfLoadLimit);
            range = scrollAnchorRange.Overlaps(range)
                ? range.MinMaxWith(scrollAnchorRange)
                : scrollAnchorRange;
        }
        range = range.MoveEnd(1); // tiles excludes the end element

        // Fix queryRange start
        if (range.Start < chatIdRange.Start)
            range = new Range<long>(chatIdRange.Start, range.End);
        // Fix queryRange end + subscribe to the next new tile
        if (range.End >= chatIdRange.End - minTileSize)
            range = new Range<long>(range.Start, chatIdRangeEndPlus);

        // Expand queryRange to tile boundaries
        range = range.ExpandToTiles(ChatUI.IdTileStack.FirstLayer);
        return range;
    }

    // Helpers

    private long UpdateReadPosition(long readEntryLid)
    {
        readEntryLid = Math.Max(_readPosition.Value.EntryLid, readEntryLid);
        if (_readPosition.Value.EntryLid < readEntryLid)
            _readPosition.Value = new ReadPosition(ChatId, readEntryLid);
        return readEntryLid;
    }

    private bool TryUpdateShownReadEntryLid(List<VirtualListTile<ChatMessage>> tiles, ChatViewItemVisibility itemVisibility)
    {
        if (tiles.Count == 0)
            return false; // Not loaded yet or wrong load range

        if (itemVisibility.IsEmpty || !itemVisibility.IsEndAnchorVisible)
            return false; // No item visibility or we aren't at the end of the list

        var shownReadEntryLid = _shownReadEntryLid.Value;
        if (shownReadEntryLid > itemVisibility.MinEntryLid - ChatUI.LoadLimit)
            return false; // The marker is visible or near the viewport

        var newShownReadEntryLid = UpdateReadPosition(itemVisibility.MaxEntryLid);
        if (newShownReadEntryLid == shownReadEntryLid)
            return false;

        _shownReadEntryLid.Value = newShownReadEntryLid;
        return true;
    }

    private async ValueTask<ChatEntry?> GetFirstEntry(long minEntryLid, CancellationToken cancellationToken)
    {
        var chatId = ChatId;
        var entryReader = new ChatEntryReader(Chats, Session, chatId, ChatEntryKind.Text);
        var chatIdRange = await Chats
            .GetIdRange(Session, chatId, ChatEntryKind.Text, cancellationToken)
            .ConfigureAwait(false);
        var range = new Range<long>(minEntryLid, minEntryLid + (20 * ChatUI.IdTileStack.MinTileSize))
            .IntersectWith(chatIdRange);
        return await entryReader.GetFirst(range, cancellationToken).ConfigureAwait(false);
    }

    private void UpdateGroupChatUsageList()
    {
        var chatId = ChatId;
        if (chatId.Kind == ChatKind.Peer)
            return;

        _ = BackgroundTask.Run(async () => {
                var chat = await Chats.Get(Session, chatId, DisposeToken);
                if (chat == null)
                    return;

                var isAiSearchChat = chat.IsAiSearchChat();
                if (isAiSearchChat)
                    return;

                var command = new ChatUsages_RegisterUsage(Session, ChatUsageListKind.ViewedGroupChats, chatId);
                await Commander.Call(command, DisposeToken);
            },
            ex => Log.LogDebug(ex, "Failed to register view group chat"),
            DisposeToken);
    }

    // Nested types

    private sealed record Navigation(
        long EntryLid,
        bool MustHighlight)
    {
        // This record relies on referential equality
        public bool Equals(Navigation? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
}
