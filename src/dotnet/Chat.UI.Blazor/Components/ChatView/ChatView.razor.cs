using System.Text.RegularExpressions;
using ActualChat.Chat.UI.Blazor.Events;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using Stl.Diagnostics;

namespace ActualChat.Chat.UI.Blazor.Components;

public partial class ChatView : ComponentBase, IVirtualListDataSource<ChatMessage>, IDisposable
{
    public static readonly TileStack<long> IdTileStack = Constants.Chat.ViewIdTileStack;
    public static readonly long HalfMinLoadLimit = 2 * IdTileStack.Layers[1].TileSize; // 40
    public static readonly long MinLoadLimit = 4 * IdTileStack.Layers[1].TileSize; // 80
    public static readonly TimeSpan FastUpdateRecency = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan FastUpdateDelay = TimeSpan.FromMilliseconds(20);
    public static readonly TimeSpan SlowUpdateDelay = TimeSpan.FromMilliseconds(100);

    private readonly CancellationTokenSource _disposeTokenSource;
    private readonly TaskCompletionSource _whenInitializedSource = TaskCompletionSourceExt.New();

    private Task _updateReadStateTask = null!;
    private NavigationInfo? _lastNavigation;
    private IMutableState<ChatViewItemVisibility> _itemVisibility = null!;
    private IComputedState<bool> _canScrollToUnread = null!;
    private Range<long> _lastIdRangeToLoad;
    private ILogger? _log;

    private IServiceProvider Services => ChatContext.Services;
    private Session Session => ChatContext.Session;
    private Chat Chat => ChatContext.Chat;
    private ChatUI ChatUI => ChatContext.ChatUI;
    private IChats Chats => ChatContext.Chats;
    private Media.IMediaLinkPreviews MediaLinkPreviews => ChatContext.MediaLinkPreviews;
    private IAuthors Authors => ChatContext.Authors;
    private NavigationManager Nav => ChatContext.Nav;
    private History History => ChatContext.History;
    private TimeZoneConverter TimeZoneConverter => ChatContext.TimeZoneConverter;
    private IStateFactory StateFactory => ChatContext.StateFactory();
    private Dispatcher Dispatcher => ChatContext.Dispatcher;
    private CancellationToken DisposeToken { get; }
    private ILogger Log => _log ??= Services.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);

    private IMutableState<NavigationInfo?> NextNavigation { get; set; } = null!;
    private IMutableState<long> ShownReadEntryLid { get; set; } = null!;
    private SyncedStateLease<ReadPosition> ReadPosition { get; set; } = null!;

    public IState<bool> CanScrollToUnread => _canScrollToUnread;
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
            NextNavigation = StateFactory.NewMutable(
                (NavigationInfo?)null,
                StateCategories.Get(type, nameof(NextNavigation)));
            ShownReadEntryLid = StateFactory.NewMutable(
                0L,
                StateCategories.Get(type, nameof(ShownReadEntryLid)));
            _itemVisibility = StateFactory.NewMutable(
                ChatViewItemVisibility.Empty,
                StateCategories.Get(type, nameof(ItemVisibility)));
            _canScrollToUnread = StateFactory.NewComputed(
                new ComputedState<bool>.Options {
                    UpdateDelayer = FixedDelayer.Instant,
                    InitialValue = false,
                    Category = StateCategories.Get(type, nameof(CanScrollToUnread)),
                },
                ComputeCanScrollToUnread);
            ReadPosition = await ChatUI.LeaseReadPositionState(Chat.Id, DisposeToken);
            ShownReadEntryLid.Value = ReadPosition.Value.EntryLid;
            _whenInitializedSource.TrySetResult();
            _updateReadStateTask = AsyncChainExt.From(UpdateReadState)
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
                ReadPosition.DisposeSilently();
        }
    }

    public void Dispose()
    {
        if (_disposeTokenSource.IsCancellationRequested)
            return;

        _disposeTokenSource.CancelAndDisposeSilently();
        _whenInitializedSource.TrySetCanceled();
        _canScrollToUnread.DisposeSilently();
        ReadPosition.DisposeSilently();
        Nav.LocationChanged -= OnLocationChanged;
    }

    protected override async Task OnParametersSetAsync()
    {
        await WhenInitialized;
        TryNavigateToUrlFragment();
    }

    public async Task NavigateToUnreadOrEnd()
    {
        await WhenInitialized;
        if (CanScrollToUnread.Value) {
            NavigateTo(true, ShownReadEntryLid.Value, true);
            return;
        }

        var entryLid = await GetLastTextEntryLid(DisposeToken);
        if (entryLid <= 0)
            return;

        ShownReadEntryLid.Value = UpdateReadPosition(entryLid);
        var entryId = new ChatEntryId(Chat.Id, ChatEntryKind.Text, entryLid, AssumeValid.Option);
        ChatUI.HighlightEntry(entryId, navigate: true);
    }

    public void NavigateTo(bool isManual, long entryLid, bool mustPositionAfter = false)
        => NextNavigation.Value = new NavigationInfo(isManual, entryLid, mustPositionAfter);

    public void TryNavigateToUrlFragment()
    {
        // Ignore location changed events if already disposed
        if (DisposeToken.IsCancellationRequested)
            return;

        var uri = History.Uri;
        var fragment = new LocalUrl(uri).ToAbsolute(History.UrlMapper).ToUri().Fragment.TrimStart('#');
        if (NumberExt.TryParsePositiveLong(fragment, out var entryId) && entryId > 0) {
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
            NavigateTo(true, entryId);
        }
    }

    // Private methods

    // Following 5 cases should be handled by this method:
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
            ShownReadEntryLid.Value = ReadPosition.Value.EntryLid;
        }
        // Create a dependency to make sure GetData is called when the chat becomes invisible again
        await isChatViewVisible.Use(cancellationToken);

        // Update delay: we want to collect as many dependencies as possible here,
        // but don't want to delay rapid updates.
        {
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
        var readEntryLid = ReadPosition.Value.EntryLid;
        var navigation = await NextNavigation.Use(cancellationToken)
            ?? (isFirstRender && readEntryLid != 0 ? new NavigationInfo(false, readEntryLid) : null);
        if (navigation == _lastNavigation) // Handles null case as well
            navigation = null;

        var mustScrollToEntry = navigation != null && !ItemVisibility.Value.IsFullyVisible(navigation.EntryLid);
        Computed<Range<long>> cChatIdRange;
        using (Computed.SuspendDependencyCapture()) {
            cChatIdRange = await Computed.Capture(
                () => Chats.GetIdRange(Session, chatId, ChatEntryKind.Text, cancellationToken),
                cancellationToken);
        }
        var chatIdRange = cChatIdRange.Value;
        var idRangeToLoad = GetIdRangeToLoad(query, renderedData, navigation, chatIdRange);
        var hasVeryFirstItem = idRangeToLoad.Start <= chatIdRange.Start;
        var hasVeryLastItem = idRangeToLoad.End >= chatIdRange.End;
        if (idRangeToLoad.End + HalfMinLoadLimit >= chatIdRange.End)
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
        var shownReadyEntryLid = ShownReadEntryLid.Value;
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
                };
        }
        if (tryUpdateShownReadEntryLid
            && renderedData.VisibilitySnapshot != ItemVisibility.Snapshot
            && TryUpdateShownReadEntryLid(tiles)) {
            tryUpdateShownReadEntryLid = false;
            goto rebuildTiles;
        }

        var scrollToKey = (string?)null;
        var highlightEntryLid = navigation is { IsManual: true }
            ? (long?)navigation.EntryLid
            : null;
        if (mustScrollToEntry && navigation != null) {
            var entryLid = navigation.EntryLid;
            var criteria = (Func<ChatMessage, bool>)(navigation.MustPositionAfter
                ? m => m.Entry.LocalId <= entryLid || m.IsReplacement
                : m => m.Entry.LocalId < entryLid || m.IsReplacement);
            var message = tiles
                .SkipWhile(t => criteria.Invoke(t.Items[^1]))
                .SelectMany(t => t.Items)
                .SkipWhile(criteria)
                .FirstOrDefault();
            if (message is not null) {
                scrollToKey = message.Entry.LocalId.Format();
                if (highlightEntryLid.HasValue)
                    highlightEntryLid = message.Entry.LocalId;
            }
            else
                Log.LogWarning("Failed to find entry to scroll to #{EntryLid}", entryLid);
        }

        var result = new VirtualListData<ChatMessage>(tiles) {
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
        if (highlightEntryLid.HasValue) {
            // highlight entry when it has already been loaded
            var entryLid = new ChatEntryId(chatId, ChatEntryKind.Text, highlightEntryLid.Value, AssumeValid.Option);
            ChatUI.HighlightEntry(entryLid, navigate: false);
        }
        return result;
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
        if (itemVisibility.IsIdenticalTo(lastItemVisibility))
            return;

        _itemVisibility.Value = itemVisibility;
        if (itemVisibility.IsEmpty || !WhenInitialized.IsCompletedSuccessfully)
            return;

        UpdateReadPosition(itemVisibility.MaxEntryLid);
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
        => TryNavigateToUrlFragment();

    private Task OnNavigateToChatEntry(NavigateToChatEntryEvent @event, CancellationToken cancellationToken)
    {
        if (@event.ChatEntryId.ChatId == Chat.Id)
            NavigateTo(true, @event.ChatEntryId.LocalId);
        return Task.CompletedTask;
    }

    // Private methods

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
        NavigationInfo? scrollAnchor,
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
                firstLayer.GetTile(chatIdRange.End - MinLoadLimit).Start,
                chatIdRangeEndPlus),
#if false
            // No query, but there is old data + we're close to the end
            (false, true) when Math.Abs(lastItem!.Entry.LocalId - chatIdRange.End) <= minTileSize
                => new Range<long>(
                    firstLayer.GetTile(
                        oldData.GetNthItem((int)(2 * MinLoadLimit), true)?.Entry.LocalId // Chopping head
                        ?? firstItem!.Entry.LocalId
                    ).Start,
                    chatIdRangeEndPlus),
#endif
            // No query, but there is old data -> retaining it
            (false, true) => new Range<long>(firstItem!.Entry.LocalId, lastItem!.Entry.LocalId),
            // Query is there, so data is irrelevant
            _ => query.KeyRange.ToLongRange().Expand(new Range<long>(query.ExpandStartBy, query.ExpandEndBy)),
        };

        // If we are scrolling somewhere, let's extend the range to scrollAnchor & nearby entries.
        if (scrollAnchor is { } vScrollAnchor) {
            var scrollAnchorRange = new Range<long>(
                vScrollAnchor.EntryLid - HalfMinLoadLimit,
                vScrollAnchor.EntryLid + HalfMinLoadLimit);
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
            await IdTileStack.FirstLayer
                .GetCoveringTiles(idRange)
                .Select(x => Chats.GetTile(Session, chatId, ChatEntryKind.Text, x.Range, cancellationToken))
                .Collect()
                .ConfigureAwait(false);
        }, CancellationToken.None);
    }

    private long UpdateReadPosition(long readEntryLid)
    {
        readEntryLid = Math.Max(ReadPosition.Value.EntryLid, readEntryLid);
        if (ReadPosition.Value.EntryLid < readEntryLid)
            ReadPosition.Value = new ReadPosition(Chat.Id, readEntryLid);
        return readEntryLid;
    }

    private bool TryUpdateShownReadEntryLid(List<VirtualListTile<ChatMessage>> tiles)
    {
        if (tiles.Count == 0)
            return false; // Not loaded yet or wrong load range

        var v = ItemVisibility.Value;
        if (v.IsEmpty || !v.IsEndAnchorVisible)
            return false; // No item visibility or we aren't at the end of the list

        var shownReadEntryLid = ShownReadEntryLid.Value;
        if (shownReadEntryLid > v.MinEntryLid - MinLoadLimit)
            return false; // The marker is visible or near the viewport

        var newShownReadEntryLid = UpdateReadPosition(v.MaxEntryLid);
        if (newShownReadEntryLid == shownReadEntryLid)
            return false;

        ShownReadEntryLid.Value = newShownReadEntryLid;
        return true;
    }

    private async Task<bool> ComputeCanScrollToUnread(IComputedState<bool> state, CancellationToken cancellationToken)
    {
        // It's ok to use ConfigureAwait(false) is fine here
        var lastTextEntryLid = await GetLastTextEntryLid(cancellationToken);
        if (lastTextEntryLid <= 0) // Chat is empty
            return false;

        var itemVisibility = await ItemVisibility.Use(cancellationToken).ConfigureAwait(false);
        if (itemVisibility.IsEmpty) // No visibility is computed yet
            return false;

        var readEntryLid = await ShownReadEntryLid.Use(cancellationToken).ConfigureAwait(false);
        if (readEntryLid <= 0) // Nothing is read yet
            return false;

        return itemVisibility.MaxEntryLid < readEntryLid;
    }

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
        var lastEntryLid = lastEntry?.Id.LocalId ?? 0;

        // Observing new entries
        var entries = entryReader.Observe(chatIdRange.End, cancellationToken);
        await foreach (var entry in entries.ConfigureAwait(false)) {
            if (entry.AuthorId != authorId) {
                lastEntryLid = entry.LocalId;
                continue;
            }

            var shownReadEntryLid = ShownReadEntryLid.Value;
            var lastEntryWasShownAsRead = lastEntryLid == shownReadEntryLid;
            lastEntryLid = entry.LocalId;
            if (lastEntryWasShownAsRead) {
                ShownReadEntryLid.Value = lastEntryLid;
                UpdateReadPosition(lastEntryLid);
            }
            if (entry.IsStreaming || entry.AudioEntryId.HasValue)
                continue;

            NavigateTo(false, lastEntryLid);
        }
    }

    private async ValueTask<long> GetLastTextEntryLid(CancellationToken cancellationToken)
    {
        var chatNews = await Chats.GetNews(Session, Chat.Id, cancellationToken).ConfigureAwait(false);
        return chatNews.LastTextEntry?.Id.LocalId ?? 0;
    }

    // Nested types

    private sealed record NavigationInfo(
        bool IsManual,
        long EntryLid,
        bool MustPositionAfter = false);
}
