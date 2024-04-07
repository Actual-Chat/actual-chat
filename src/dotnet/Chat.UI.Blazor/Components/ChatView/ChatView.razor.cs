using System.Text.RegularExpressions;
using ActualChat.Chat.UI.Blazor.Events;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using ActualLab.Diagnostics;

namespace ActualChat.Chat.UI.Blazor.Components;

public partial class ChatView : ComponentBase, IVirtualListDataSource<ChatMessage>, IDisposable
{
    public static readonly TileStack<long> IdTileStack = Constants.Chat.ViewIdTileStack;
    public static readonly long HalfLoadLimit = 2 * IdTileStack.Layers[1].TileSize; // 40
    public static readonly long LoadLimit = 4 * IdTileStack.Layers[1].TileSize; // 80
    public static readonly TimeSpan FastUpdateRecency = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan FastUpdateDelay = TimeSpan.FromMilliseconds(20);
    public static readonly TimeSpan SlowUpdateDelay = TimeSpan.FromMilliseconds(100);

    private readonly CancellationTokenSource _disposeTokenSource;
    private readonly TaskCompletionSource _whenInitializedSource = TaskCompletionSourceExt.New();

    private Task _updateReadStateTask = null!;
    private SyncedStateLease<ReadPosition> _readPosition = null!;
    private IMutableState<ChatViewItemVisibility> _itemVisibility = null!;
    private IMutableState<long> _shownReadEntryLid = null!;
    private IMutableState<Navigation?> _nextNavigation = null!;
    private Range<long> _lastIdRangeToLoad;
    private ChatUIHub? _hub;
    private ILogger? _log;

    private ChatUIHub Hub => _hub ??= ChatContext.Hub;
    private Session Session => Hub.Session();
    private Chat Chat => ChatContext.Chat;
    private ChatUI ChatUI => Hub.ChatUI;
    private IChats Chats => Hub.Chats;
    private Media.IMediaLinkPreviews MediaLinkPreviews => Hub.MediaLinkPreviews;
    private IAuthors Authors => Hub.Authors;
    private NavigationManager Nav => Hub.Nav;
    private History History => Hub.History;
    private DateTimeConverter DateTimeConverter => Hub.DateTimeConverter;
    private IStateFactory StateFactory => Hub.StateFactory();
    private Dispatcher Dispatcher => Hub.Dispatcher;
    private CancellationToken DisposeToken { get; }
    private ILogger Log => _log ??= Hub.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);

    public IState<ReadPosition> ReadPosition => _readPosition;
    public IState<long> ShownReadEntryLid => _shownReadEntryLid;

    public IState<ChatViewItemVisibility> ItemVisibility => _itemVisibility;
    public Task WhenInitialized => _whenInitializedSource.Task;

    [Parameter, EditorRequired] public ChatContext ChatContext { get; set; } = null!;
    [CascadingParameter] public RegionVisibility RegionVisibility { get; set; } = null!;

    public ChatView()
    {
        _disposeTokenSource = new ();
        DisposeToken = _disposeTokenSource.Token;
    }

    protected override async Task OnInitializedAsync()
    {
        Log.LogDebug("Created for chat #{ChatId}", Chat.Id);
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
            _readPosition = await ChatUI.LeaseReadPositionState(Chat.Id, DisposeToken);
            _shownReadEntryLid.Value = _readPosition.Value.EntryLid;
            _whenInitializedSource.TrySetResult();
            _updateReadStateTask = AsyncChain.From(UpdateReadState)
                .Log(LogLevel.Debug, Log)
                .RetryForever(RetryDelaySeq.Exp(0.5, 3), Log)
                .RunIsolated(DisposeToken);
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

    public void Dispose()
    {
        if (_disposeTokenSource.IsCancellationRequested)
            return;

        _disposeTokenSource.CancelAndDisposeSilently();
        _whenInitializedSource.TrySetCanceled();
        _readPosition.DisposeSilently();
        Nav.LocationChanged -= OnLocationChanged;
    }

    protected override Task OnParametersSetAsync()
        => NavigateToUrlFragment();

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

    public async Task NavigateToUrlFragment()
    {
        await WhenInitialized;
        // Ignore location changed events if already disposed
        if (DisposeToken.IsCancellationRequested)
            return;

        var uri = History.Uri;
        var fragment = new LocalUrl(uri).ToAbsolute(Hub.UrlMapper()).ToUri().Fragment.TrimStart('#');
        if (!NumberExt.TryParsePositiveLong(fragment, out var entryId) || entryId <= 0)
            return;

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
            },
            CancellationToken.None);
        History.CancelWhen(cts, x => !OrdinalEquals(x.Uri, uri));
        await NavigateTo(entryId, true);
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
        if (@event.ChatEntryId.ChatId == Chat.Id)
            _ = NavigateTo(@event.ChatEntryId.LocalId, @event.MustHighlight);
        return Task.CompletedTask;
    }

    // AsyncChains

    private async Task UpdateReadState(CancellationToken cancellationToken)
    {
        var chatId = Chat.Id;
        var entryReader = ChatContext.NewEntryReader(ChatEntryKind.Text);
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
            if (entry.IsStreaming || entry.AudioEntryId.HasValue)
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
        IComputedState<VirtualListData<ChatMessage>> state,
        VirtualListDataQuery query,
        VirtualListData<ChatMessage> renderedData,
        CancellationToken cancellationToken)
    {
        var startedAt = CpuTimestamp.Now;
        await WhenInitialized;

        var isChatViewVisible = RegionVisibility.IsVisible;
        if (!isChatViewVisible.Value) {
            // Chat is invisible now, let's suspend & await for it to become visible
            using (Computed.SuspendDependencyCapture())
                await isChatViewVisible.When(x => x, cancellationToken);
            _shownReadEntryLid.Value = _readPosition.Value.EntryLid;
        }
        // Create a dependency to make sure GetData is called when the chat becomes invisible again
        await isChatViewVisible.Use(cancellationToken);

        // Update delay: we want to collect as many dependencies as possible here,
        // but don't want to delay rapid updates.
        // We don't need delays when data is being requested by the client code - e.g. when query isn't None
        if (query.IsNone && renderedData.Index > 2) {
            var lastData = state.ValueOrDefault ?? VirtualListData<ChatMessage>.None;
            var lastComputedAt = lastData.IsNone ? startedAt : lastData.ComputedAt;
            var isFastUpdate = startedAt - lastComputedAt <= FastUpdateRecency;
            var delay = startedAt + (isFastUpdate ? FastUpdateDelay : SlowUpdateDelay) - CpuTimestamp.Now;
            if (delay > TimeSpan.FromMilliseconds(10))
                await Task.Delay(delay, cancellationToken);
        }

        // ReSharper disable once ExplicitCallerInfoArgument
        using var activity = BlazorUITrace.StartActivity("ChatView.GetVirtualListData");

        var chat = Chat;
        var chatId = chat.Id;
        activity?.SetTag("AC." + nameof(ChatId), chatId);

        // Handling NavigateTo + default navigation
        var isFirstRender = renderedData.IsNone;
        var readEntryLid = _readPosition.Value.EntryLid;
        var nav = await _nextNavigation.Use(cancellationToken)
            ?? (isFirstRender && readEntryLid != 0 ? new Navigation(readEntryLid, false) : null);
        if (ReferenceEquals(nav, renderedData.NavigationState)) // Handles null case as well
            nav = null;

        var itemVisibility = ItemVisibility.Value;
        var mustScrollToEntry = nav != null && !itemVisibility.IsFullyVisible(nav.EntryLid);
        Computed<Range<long>> cChatIdRange;
        using (Computed.SuspendDependencyCapture()) {
            cChatIdRange = await Computed.Capture(
                () => Chats.GetIdRange(Session, chatId, ChatEntryKind.Text, cancellationToken),
                cancellationToken);
        }
        var chatIdRange = cChatIdRange.Value;
        var idRangeToLoad = GetIdRangeToLoad(query, renderedData, nav, chatIdRange);
        var hasVeryFirstItem = idRangeToLoad.Start <= chatIdRange.Start;
        var hasVeryLastItem = idRangeToLoad.End >= chatIdRange.End;
        if (idRangeToLoad.End + HalfLoadLimit >= chatIdRange.End)
            await cChatIdRange.Use(cancellationToken); // Add dependency on chatIdRange

        activity?.SetTag("AC." + "IdRange", chatIdRange.Format());
        activity?.SetTag("AC." + "ReadEntryLid", readEntryLid);
        activity?.SetTag("AC." + "IdRangeToLoad", idRangeToLoad.Format());
        // DebugLog?.LogDebug("GetData: #{ChatId} -> {IdRangeToLoad}", chatId, idRangeToLoad.Format());

        // Prefetching new tiles
        var lastIdRangeToLoad = _lastIdRangeToLoad;
        _lastIdRangeToLoad = idRangeToLoad;
        var newIdRanges = idRangeToLoad.Subtract(lastIdRangeToLoad);
        using (var __ = ExecutionContextExt.TrySuppressFlow()) {
            // We don't want dependencies to be captured for prefetch calls
            _ = PrefetchTiles(chatId, newIdRanges.Item1, cancellationToken);
            _ = PrefetchTiles(chatId, newIdRanges.Item2, cancellationToken);
        }

        var idTiles = GetIdTilesToLoad(idRangeToLoad, chatIdRange);
        var tryUpdateShownReadEntryLid = true;

        rebuildTiles: // Building actual virtual list tiles

        var prevMessage = hasVeryFirstItem ? ChatMessage.Welcome(chatId) : null;
        var shownReadyEntryLid = _shownReadEntryLid.Value;
        var tiles = new List<VirtualListTile<ChatMessage>>();
        foreach (var idTile in idTiles) {
            var lastReadEntryLid = shownReadyEntryLid;
            if (lastReadEntryLid < idTile.Range.Start)
                lastReadEntryLid = 0;
            else if (shownReadyEntryLid >= idTile.Range.End - 1)
                lastReadEntryLid = long.MaxValue;
            var tile = await ChatUI.GetTile(chatId,
                idTile.Range,
                prevMessage,
                lastReadEntryLid,
                cancellationToken);
            if (tile.Items.Count == 0)
                continue;

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
                return new VirtualListData<ChatMessage>(new [] {
                    new VirtualListTile<ChatMessage>(default(Range<long>), new [] { ChatMessage.Welcome(Chat.Id) }),
                }) {
                    HasVeryFirstItem = true,
                    HasVeryLastItem = true,
                    ScrollToKey = null,
                    RequestedStartExpansion = null,
                    RequestedEndExpansion = null,
                    NavigationState = nav ?? renderedData.NavigationState,
                    ItemVisibilityState = itemVisibility,
                };
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
            RequestedStartExpansion = query.IsNone
                ? null
                : query.ExpandStartBy,
            RequestedEndExpansion = query.IsNone
                ? null
                : query.ExpandEndBy,
            NavigationState = nav ?? renderedData.NavigationState,
            ItemVisibilityState = itemVisibility,
        };
        return result;
    }

    private static Tile<long>[] GetIdTilesToLoad(Range<long> idRangeToLoad, Range<long> chatIdRange)
    {
        var firstLayer = IdTileStack.FirstLayer;
        var secondLayer = IdTileStack.Layers[1];
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
            // DebugLog?.LogDebug("GetIdTilesToLoad: slow {SlowRange}, fast {FastRange}", slowRange.Format(), fastRange.Format());
            // if (result.DistinctBy(x => x.Range).Count() != result.Length)
            //     Debugger.Break();
            return result;
        }
        finally {
            tiles.Release();
        }
    }

    private static Range<long> GetIdRangeToLoad(
        VirtualListDataQuery query,
        VirtualListData<ChatMessage> oldData,
        Navigation? scrollAnchor,
        Range<long> chatIdRange)
    {
        var firstLayer = IdTileStack.Layers[0];
        var minTileSize = IdTileStack.MinTileSize;
        var chatIdRangeEndPlus = chatIdRange.End + minTileSize;
        var firstItem = oldData.FirstItem;
        var lastItem = oldData.LastItem;
        var range = (!query.IsNone, firstItem != null) switch {
            // No query, no data -> initial load
            (false, false) => new Range<long>(
                firstLayer.GetTile(chatIdRange.End - LoadLimit).Start,
                chatIdRangeEndPlus),
            // No query, but there is old data + we're close to the end
            // KEEP THIS case, otherwise virtual list will grow indefinitely!
            (false, true) when Math.Abs(lastItem!.Entry.LocalId - chatIdRange.End) <= minTileSize
                => new Range<long>(
                    firstLayer.GetTile(
                        oldData.GetNthItem( (int)LoadLimit, true)?.Entry.LocalId // Chopping head
                        ?? firstItem!.Entry.LocalId
                    ).Start,
                    chatIdRangeEndPlus),
            // No query, but there is old data -> retaining it
            (false, true) => new Range<long>(firstItem!.Entry.LocalId, lastItem!.Entry.LocalId),
            // Query is there, so data is irrelevant
            _ => query.KeyRange.ToLongRange().Expand(new Range<long>(query.ExpandStartBy, query.ExpandEndBy)),
        };

        // If we are scrolling somewhere, let's extend the range to scrollAnchor & nearby entries.
        if (scrollAnchor is { } vScrollAnchor) {
            var scrollAnchorRange = new Range<long>(
                vScrollAnchor.EntryLid - HalfLoadLimit,
                vScrollAnchor.EntryLid + HalfLoadLimit);
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
        range = range.ExpandToTiles(IdTileStack.FirstLayer);
        return range;
    }

    private Task PrefetchTiles(ChatId chatId, Range<long> idRange, CancellationToken cancellationToken)
    {
        if (idRange.IsEmptyOrNegative)
            return Task.CompletedTask;

        return Task.Run(async () => {
            var tiles = await IdTileStack.FirstLayer
                .GetCoveringTiles(idRange)
                .Select(x => Chats.GetTile(Session, chatId, ChatEntryKind.Text, x.Range, cancellationToken))
                .Collect()
                .ConfigureAwait(false);

            // prefetch authors
            await tiles
                .SelectMany(t => t.Entries)
                .Select(e => e.AuthorId)
                .Distinct()
                .Select(authorId => Authors.Get(Session, chatId, authorId, cancellationToken))
                .Collect();

        }, CancellationToken.None);
    }

    // Helpers

    private long UpdateReadPosition(long readEntryLid)
    {
        readEntryLid = Math.Max(_readPosition.Value.EntryLid, readEntryLid);
        if (_readPosition.Value.EntryLid < readEntryLid)
            _readPosition.Value = new ReadPosition(Chat.Id, readEntryLid);
        return readEntryLid;
    }

    private bool TryUpdateShownReadEntryLid(List<VirtualListTile<ChatMessage>> tiles, ChatViewItemVisibility itemVisibility)
    {
        if (tiles.Count == 0)
            return false; // Not loaded yet or wrong load range

        if (itemVisibility.IsEmpty || !itemVisibility.IsEndAnchorVisible)
            return false; // No item visibility or we aren't at the end of the list

        var shownReadEntryLid = _shownReadEntryLid.Value;
        if (shownReadEntryLid > itemVisibility.MinEntryLid - LoadLimit)
            return false; // The marker is visible or near the viewport

        var newShownReadEntryLid = UpdateReadPosition(itemVisibility.MaxEntryLid);
        if (newShownReadEntryLid == shownReadEntryLid)
            return false;

        _shownReadEntryLid.Value = newShownReadEntryLid;
        return true;
    }

    private async ValueTask<ChatEntry?> GetFirstEntry(long minEntryLid, CancellationToken cancellationToken)
    {
        var entryReader = ChatContext.NewEntryReader(ChatEntryKind.Text);
        var chatIdRange = await Chats
            .GetIdRange(Session, Chat.Id, ChatEntryKind.Text, cancellationToken)
            .ConfigureAwait(false);
        var range = new Range<long>(minEntryLid, minEntryLid + 20 * IdTileStack.MinTileSize)
            .IntersectWith(chatIdRange);
        return await entryReader.GetFirst(range, cancellationToken).ConfigureAwait(false);
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
