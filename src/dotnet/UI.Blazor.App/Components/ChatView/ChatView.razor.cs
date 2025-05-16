using ActualChat.UI.Blazor.App.Events;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Diagnostics;
using ActualChat.Kvas;
using ActualChat.Pooling;
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
    private SharedResourcePool<Symbol, SyncedState<ReadPosition>>.Lease _readPositionLease = null!;
    private MutableState<ChatViewItemVisibility> _itemVisibility = null!;
    private MutableState<long> _shownReadEntryLid = null!;
    private MutableState<Navigation?> _nextNavigation = null!;
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

    [field: AllowNull] [field: MaybeNull]
    private ILogger Log => field ??= Hub.LogFor(GetType());

    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);

    public IState<ReadPosition> ReadPosition => _readPositionLease.Resource;
    public IState<long> ShownReadEntryLid => _shownReadEntryLid;

    public IState<ChatViewItemVisibility> ItemVisibility => _itemVisibility;
    public Task WhenInitialized => _whenInitializedSource.Task;

    [Parameter] [EditorRequired]
    public ChatId ChatId { get; set; } = ChatId.None;

    [CascadingParameter] public RegionVisibility RegionVisibility { get; set; } = null!;

    public ChatView(ChatUIHub hub)
    {
        Hub = hub;
        _disposeTokenSource = new CancellationTokenSource();
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
            _readPositionLease = await ChatUI.LeaseReadPositionState(ChatId, DisposeToken);
            _shownReadEntryLid.Value = _readPositionLease.Resource.Value.EntryLid;
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
                _readPositionLease.DisposeSilently();
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
        _readPositionLease.DisposeSilently();
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

    public override string ToString()
    {
        var chatId = ChatId;
        return chatId.IsNone ? "ChatView" : $"ChatView #{chatId}";
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
        if (itemVisibility.IsIdenticalTo(lastItemVisibility)
            && !ReferenceEquals(lastItemVisibility, ChatViewItemVisibility.Empty))
            return;

        _itemVisibility.Value = itemVisibility;
        if (itemVisibility.IsEmpty || !WhenInitialized.IsCompletedSuccessfully)
            return;

        UpdateReadPosition(itemVisibility.MaxEntryLid);
        ChatUI.ItemVisibility.Value = itemVisibility;
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
        if (chatId.IsNone)
            return;

        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return;

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
        var chatId = ChatId;
        if (chatId.IsNone)
            return VirtualListData<ChatMessage>.None;

        var startedAt = CpuTimestamp.Now;
        await WhenInitialized;

        var isChatViewVisible = RegionVisibility.IsVisible;
        if (!isChatViewVisible.Value) {
            // Chat is invisible now, let's suspend & await for it to become visible
            using (Computed.BeginIsolation())
                await isChatViewVisible.Computed.When(x => x, cancellationToken);
            _shownReadEntryLid.Value = ReadPosition.Value.EntryLid;
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

        activity?.SetTag("AC." + nameof(ChatId), chatId);

        // Handling NavigateTo + default navigation
        var itemVisibility = ItemVisibility.Value;
        var isFirstRender = renderedData.IsNone && query.IsNone;
        var readEntryLid = itemVisibility.IsEndAnchorVisible
            ? long.MaxValue // We are at the end of the chat so avoid displaying new messages line
            : ReadPosition.Value.EntryLid;
        var hasReadEntry = readEntryLid != 0 && readEntryLid != long.MaxValue;
        var nav = await _nextNavigation.Use(cancellationToken)
            ?? (isFirstRender && hasReadEntry ? new Navigation(readEntryLid, false) : null);
        if (ReferenceEquals(nav, renderedData.NavigationState)) // Handles null case as well
            nav = null;

        var mustScrollToEntry = nav != null && !ItemVisibility.Value.IsFullyVisible(nav.EntryLid);
        Computed<Range<long>> cChatIdRange;
        using (Computed.BeginIsolation())
            cChatIdRange = await Computed.Capture(
                () => Chats.GetIdRange(Session, chatId, ChatEntryKind.Text, cancellationToken),
                cancellationToken);
        var chatIdRange = cChatIdRange.Value;
        var dataQuery = GetChatDataQuery(query,
            renderedData,
            nav,
            chatIdRange);
        // var originalLoadBefore = dataQuery.LoadBefore;
        // var originalLoadAfter = dataQuery.LoadAfter;
        if (dataQuery.IdTileStart + dataQuery.Offset + dataQuery.Limit + ChatUI.HalfLoadLimit >= chatIdRange.End)
            await cChatIdRange.Use(cancellationToken); // Add dependency on chatIdRange

        activity?.SetTag("AC." + "IdRange", chatIdRange.Format());
        activity?.SetTag("AC." + "ReadEntryLid", readEntryLid);
        activity?.SetTag("AC." + "DataQuery", dataQuery.Format());
        DebugLog?.LogDebug("GetData: #{ChatId} -> {IdRangeToLoad}", chatId, dataQuery.Format());

        var tryUpdateShownReadEntryLid = true;

        rebuildTiles: // Building actual virtual list tiles

        var hasVeryFirstItem = dataQuery.HasVeryFirstItem;
        var hasVeryLastItem = dataQuery.HasVeryLastItem;
        // var hasAllItems = hasVeryFirstItem && hasVeryLastItem;
        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return VirtualListData<ChatMessage>.None;

        var items = await ChatUI.GetChatItems(chatId, dataQuery, readEntryLid, cancellationToken).ConfigureAwait(false);
        // var itemCount = items.Sum(item => item is IVirtualListGroup group ? group.Count : 1);
        // var isQueryFulfilled = query.ExpectedCount > itemCount / 2;
        // var isLoadLimitReached = itemCount >= ChatUI.LoadLimit;
        if (items.Count == 0) {
            var isEmpty = await ChatUI.IsEmpty(chatId, cancellationToken);
            if (isEmpty)
                return new VirtualListData<ChatMessage>([ChatMessage.Welcome(ChatId, chat.IsAiSearchChat())]) {
                    HasVeryFirstItem = true,
                    HasVeryLastItem = true,
                    ScrollToKey = null,
                    NavigationState = nav ?? renderedData.NavigationState,
                    ItemVisibilityState = ItemVisibility.Value,
                };
        }
        // else if (!isQueryFulfilled && !isLoadLimitReached && !hasAllItems) {
        //     // We need to load more data to fulfill the query
        //     var expandedDataQuery = await ChatUI
        //         .ExpandDataQuery(chatId,
        //             dataQuery,
        //             originalLoadBefore,
        //             originalLoadAfter,
        //             items,
        //             cancellationToken)
        //         .ConfigureAwait(false);
        //     if (expandedDataQuery != null) {
        //         dataQuery = expandedDataQuery;
        //         goto rebuildTiles;
        //     }
        // }

        if (tryUpdateShownReadEntryLid && TryUpdateShownReadEntryLid(items, ref readEntryLid)) {
            tryUpdateShownReadEntryLid = false;
            goto rebuildTiles;
        }

        // Locating navigation entry
        string? navKey = null;
        if (nav != null) {
            var navChatMessage = items
                .SelectMany(item => item is IVirtualListGroup<ChatMessage> group ? group.Items : [item])
                .LastOrDefault(x => x.Id <= nav.EntryLid);
            navKey = navChatMessage?.Key;
            if (navChatMessage == null)
                Log.LogWarning("GetData: entry not found in the loaded set: #{EntryLid}", nav.EntryLid);
            else if (nav.MustHighlight)
                // TODO(AK): Implement highlighting of conversations
                ChatUI.HighlightEntry(
                    new ChatEntryId(chatId, ChatEntryKind.Text, navChatMessage.Id, AssumeValid.Option),
                    false);
        }
        if (items.Count != 0) {
            hasVeryFirstItem = hasVeryFirstItem || chatIdRange.Start >= items[0].Id;
            hasVeryLastItem = hasVeryLastItem || chatIdRange.End - 1 <= items[^1].Id;
        }
        var result = new VirtualListData<ChatMessage>(items) {
            Index = renderedData.Index + 1,
            EstimatedCount = (int?)(chatIdRange.End - chatIdRange.Start),
            HasVeryFirstItem = hasVeryFirstItem,
            HasVeryLastItem = hasVeryLastItem,
            ScrollToKey = navKey != null && mustScrollToEntry ? navKey : null,
            NavigationState = nav ?? renderedData.NavigationState,
            ItemVisibilityState = ItemVisibility.Value,
        };

        // do not return new instance if data is the same to prevent re-renders
        return !mustScrollToEntry && result.IsSimilarTo(renderedData)
            ? renderedData
            : result;
    }

    private ChatDataQuery GetChatDataQuery(
        VirtualListDataQuery query,
        VirtualListData<ChatMessage> oldData,
        Navigation? scrollAnchor,
        Range<long> chatIdRange)
    {
        // DebugLog?.LogDebug("GetChatDataQuery: {ChatId}, {Query}, {OldData}, {Anchor}, {IdRange}", ChatId, query, oldData.KeyRange.Format(), scrollAnchor, chatIdRange.Format());
        // var firstLayer = ChatUI.IdTileStack.Layers[0];
        var secondLayer = ChatUI.IdTileStack.Layers[1];
        // var minTileSize = ChatUI.IdTileStack.MinTileSize;
        // var chatIdRangeEndPlus = chatIdRange.End + minTileSize;
        var firstItem = oldData.FirstItem;
        // var lastItem = oldData.LastItem;
        var keyRange = query.IsNone ? default : query.KeyRange.ToLongRange(true);
        var dataQuery = (!query.IsNone, firstItem != null) switch {
            // Align the query params with the second tile layer

            // No query, no data -> initial load
            (false, false) => new ChatDataQuery(
                secondLayer.GetTile(chatIdRange.End - 1).Start,
                -ChatUI.HalfLoadLimit,
                ChatUI.LoadLimit),

            // No query, there is old data, and we are at the end of the list
            (false, true) when oldData.HasVeryLastItem
                => new ChatDataQuery(
                    secondLayer.GetTile(chatIdRange.End - 1).Start,
                    -ChatUI.HalfLoadLimit,
                    ChatUI.LoadLimit),

            // No query, but there is old data -> retaining it
            (false, true) => new ChatDataQuery(
                secondLayer.GetTile(firstItem!.Id).Start,
                0,
                ChatUI.LoadLimit),

            // Query is there, so data is irrelevant
            _ => new ChatDataQuery(
                secondLayer.GetTile(keyRange.Start).Start,
                (int)secondLayer.GetTile(query.MoveRange.Start).Start,
                (int)secondLayer.GetTile(keyRange.Move(query.MoveRange).Size()).Start),
        };

        // If we are scrolling somewhere within idRange, let's extend the range to scrollAnchor & nearby entries.
        if (scrollAnchor != null && chatIdRange.Contains(scrollAnchor.EntryLid))
            dataQuery = new ChatDataQuery(
                secondLayer.GetTile(scrollAnchor.EntryLid).Start,
                -ChatUI.HalfLoadLimit,
                ChatUI.LoadLimit);

        var hasVeryFirstItem = dataQuery.IdTileStart + dataQuery.Offset <= chatIdRange.Start;
        var hasVeryLastItem = dataQuery.IdTileStart + dataQuery.Offset + dataQuery.Limit >= chatIdRange.End;

        return dataQuery with {
            HasVeryFirstItem = hasVeryFirstItem,
            HasVeryLastItem = hasVeryLastItem,
        };
        //
        // var hasVeryFirstItem = range.Start <= chatIdRange.Start;
        // var hasVeryLastItem = range.End >= chatIdRange.End;

        // var range = (!query.IsNone, firstItem != null) switch {
        //     // No query, no data -> initial load
        //     (false, false) => new Range<long>(
        //         secondLayer.GetTile(chatIdRange.End - ChatUI.LoadLimit).Start,
        //         secondLayer.GetTile(chatIdRangeEndPlus).End).IntersectWith(new Range<long>(chatIdRange.Start,
        //         long.MaxValue)),
        //
        //     // No query, there is old data, and we are at the end of the list
        //     (false, true) when oldData.HasVeryLastItem
        //         => new Range<long>(Math.Max(firstItem!.Id, itemVisibility.MinEntryLid - ChatUI.HalfLoadLimit), lastItem!.Id + 1)
        //             .ExpandToTiles(firstLayer),
        //
        //     // No query, but there is old data -> retaining it
        //     (false, true) => new Range<long>(firstItem!.Id, lastItem!.Id),
        //
        //     // Query is there, so data is irrelevant
        //     _ => keyRange.Move(query.MoveRange),
        // };
        //
        // // Check whether the range is out of the chatIdRange
        // if (chatIdRange.IntersectWith(range).IsEmpty) {
        //     if (range.Start > chatIdRange.End)
        //         range = new Range<long>(chatIdRange.End - ChatUI.LoadLimit, chatIdRangeEndPlus);
        //     else if (range.End < chatIdRange.Start)
        //         range = new Range<long>(chatIdRange.Start, chatIdRange.Start + ChatUI.LoadLimit);
        // }
        //
        // // If we are scrolling somewhere within idRange, let's extend the range to scrollAnchor & nearby entries.
        // if (scrollAnchor is { } vScrollAnchor && chatIdRange.Contains(vScrollAnchor.EntryLid)) {
        //     var scrollAnchorRange = new Range<long>(
        //         vScrollAnchor.EntryLid - ChatUI.LoadLimit,
        //         vScrollAnchor.EntryLid + ChatUI.HalfLoadLimit);
        //     range = scrollAnchorRange.Overlaps(range)
        //         ? range.MinMaxWith(scrollAnchorRange)
        //         : scrollAnchorRange;
        // }
        // range = range.MoveEnd(1); // tiles excludes the end element
        //
        // // Fix queryRange start
        // if (range.Start < chatIdRange.Start)
        //     range = new Range<long>(chatIdRange.Start, range.End);
        // // Fix queryRange end + subscribe to the next new tile
        // if (range.End >= chatIdRange.End - minTileSize)
        //     range = new Range<long>(range.Start, chatIdRangeEndPlus);
        //
        // // Expand queryRange to tile boundaries
        // range = range.ExpandToTiles(ChatUI.IdTileStack.FirstLayer);
        // var hasVeryFirstItem = range.Start <= chatIdRange.Start;
        // var hasVeryLastItem = range.End >= chatIdRange.End;
        // return (query.IsNone
        //     ? new ChatDataQuery(range)
        //     : FromKeyRange()) with {
        //     HasVeryFirstItem = hasVeryFirstItem,
        //     HasVeryLastItem = hasVeryLastItem,
        // };
        //
        // ChatDataQuery FromKeyRange()
        // {
        //     var intersected = range.IntersectWith(keyRange);
        //     if (intersected.IsEmpty)
        //         return new ChatDataQuery(range);
        //
        //     var loadBefore = (int)(intersected.Start - range.Start).Clamp(0, int.MaxValue);
        //     var loadAfter = (int)(range.End - intersected.End).Clamp(0, int.MaxValue);
        //     return new ChatDataQuery(intersected, loadBefore, loadAfter);
        // }
    }

    // Helpers

    private long UpdateReadPosition(long readEntryLid)
    {
        var readPosition = _readPositionLease.Resource;
        readEntryLid = Math.Max(readPosition.Value.EntryLid, readEntryLid);
        if (readPosition.Value.EntryLid < readEntryLid)
            readPosition.Value = new ReadPosition(ChatId, readEntryLid);
        return readEntryLid;
    }

    private bool TryUpdateShownReadEntryLid(IReadOnlyList<ChatMessage> items, ref long readEntryLid)
    {
        var itemVisibility = ItemVisibility.Value;
        if (items.Count == 0)
            return false; // Not loaded yet or wrong load range

        if (itemVisibility.IsEmpty || !itemVisibility.IsEndAnchorVisible) {
            DebugLog?.LogDebug("TryUpdateShownReadEntryLid: no item visibility or end anchor is not visible");
            return false; // No item visibility or we aren't at the end of the list
        }

        if (readEntryLid == long.MaxValue) {
            DebugLog?.LogDebug("TryUpdateShownReadEntryLid: read position is at the end");
            return false; // We are at the end of the chat view
        }

        var shownReadEntryLid = _shownReadEntryLid.Value;
        var newMessagesLine = items
            .SkipWhile(i => i.Id < shownReadEntryLid)
            .FirstOrDefault(i => i.ReplacementKind == ChatMessageReplacementKind.NewMessagesLine);
        var hasNewMessagesLine = newMessagesLine != null;
        if (!hasNewMessagesLine) {
            DebugLog?.LogDebug("TryUpdateShownReadEntryLid: no new messages line");
            return false; // No new messages line
        }

        // We see end anchor, when the new message appears so we can update shownReadEntryLid
        var lastEntryLid = items[^1].Id;
        var maxVisibleEntryLid = itemVisibility.MaxEntryLid;
        var newShownReadEntryLid = UpdateReadPosition(Math.Max(lastEntryLid, maxVisibleEntryLid));
        if (newShownReadEntryLid == shownReadEntryLid) {
            DebugLog?.LogDebug("TryUpdateShownReadEntryLid: read position is unchanged");
            return false;
        }

        _shownReadEntryLid.Value = newShownReadEntryLid;
        readEntryLid = newShownReadEntryLid;
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
        if (chatId.IsNone || chatId.Kind == ChatKind.Peer)
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
