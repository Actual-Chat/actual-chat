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

    private static readonly TimeSpan BlockSplitPauseDuration = TimeSpan.FromSeconds(120);
    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;
    private readonly CancellationTokenSource _disposeTokenSource = new();
    private readonly TaskCompletionSource _whenInitializedSource = TaskCompletionSourceExt.New();
    private readonly Suspender _getDataSuspender = new();

    private Task _syncLastAuthorEntryLifState = null!;
    private NavigationAnchor? _lastNavigationAnchor;
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
    private Media.IMediaLinkPreviews MediaLinkPreviews => ChatContext.MediaLinkPreviews;
    private IAuthors Authors => ChatContext.Authors;
    private NavigationManager Nav => ChatContext.Nav;
    private History History => ChatContext.History;
    private TimeZoneConverter TimeZoneConverter => ChatContext.TimeZoneConverter;
    private IStateFactory StateFactory => ChatContext.StateFactory;
    private Dispatcher Dispatcher => ChatContext.Dispatcher;
    private CancellationToken DisposeToken => _disposeTokenSource.Token;
    private ILogger Log => _log ??= Services.LogFor(GetType());

    private IMutableState<NavigationAnchor?> NavigationAnchorState { get; set; } = null!;
    private IMutableState<long> LastAuthorTextEntryLidState { get; set; } = null!;
    private SyncedStateLease<ReadPosition> ReadPositionState { get; set; } = null!;
    private ComputedStateLease<Range<long>> ChatIdRangeState { get; set; } = null!;

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
            NavigationAnchorState = StateFactory.NewMutable(
                (NavigationAnchor?)null,
                StateCategories.Get(GetType(), nameof(NavigationAnchorState)));
            LastAuthorTextEntryLidState = StateFactory.NewMutable(
                0L,
                StateCategories.Get(GetType(), nameof(LastAuthorTextEntryLidState)));
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
            ChatIdRangeState = await ChatUI.LeaseChatIdRangeState(Chat.Id, DisposeToken);
            _lastReadEntryLid = ReadPositionState.Value.EntryLid;
            if (_whenInitializedSource.TrySetResult())
                RegionVisibility.IsVisible.Updated += OnRegionVisibilityChanged;
            _syncLastAuthorEntryLifState = new AsyncChain(nameof(SyncLastAuthorEntryLidState),  SyncLastAuthorEntryLidState)
                .Log(LogLevel.Debug, Log)
                .RetryForever(RetryDelaySeq.Exp(0.5, 3), Log)
                .RunIsolated(_disposeTokenSource.Token);
        }
        catch {
            _whenInitializedSource.TrySetCanceled();
        }
        finally {
            // Async part of this method may run after Dispose,
            // so Dispose won't see a new value of ReadPositionState
            if (_disposeTokenSource.IsCancellationRequested) {
                ReadPositionState.DisposeSilently();
                ChatIdRangeState.DisposeSilently();
            }
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
        ChatIdRangeState.DisposeSilently();
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
        NavigateToEntry(navigateToEntryLid, true);
    }

    public void NavigateToEntry(long entryLid, bool isNext = false)
    {
        // Reset to ensure navigation will happen
        _lastNavigationAnchor = null;
        NavigationAnchorState.Value = new NavigationAnchor(entryLid, isNext);
        NavigationAnchorState.Invalidate();
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

    // Following 5 cases should be handled by this method:
    // - Return data for the first-time request to the last read position if exists, or to the end of the chat messages
    // - Return updated data on invalidation of requested tiles
    // - Return new messages in addition to already rendered messages - monitoring last tiles if rendered near the end
    // - Return last chat messages when the author has submitted a new message - monitoring dedicated state
    // - Return messages around an anchor message we are navigating to
    // If the message data is the same it should return same instances of data tiles to reduce re-rendering
    async Task<VirtualListData<ChatMessageModel>> IVirtualListDataSource<ChatMessageModel>.GetData(
        VirtualListDataQuery query,
        VirtualListData<ChatMessageModel> oldData,
        CancellationToken cancellationToken)
    {
        await WhenInitialized; // No need for .ConfigureAwait(false) here

        // NOTE(AY): The old logic relying on _getDataSuspender was returning oldData,
        // which should never happen, coz it doesn't create any dependencies.
        await _getDataSuspender.WhenResumed();  // No need for .ConfigureAwait(false) here

        // ReSharper disable once ExplicitCallerInfoArgument
        using var activity = BlazorUITrace.StartActivity("ChatView.GetVirtualListData");

        var chat = Chat;
        var chatId = chat.Id;
        activity?.SetTag("AC." + nameof(ChatId), chatId);
        var readEntryLid = ReadPositionState.Value.EntryLid;
        var entryLid = readEntryLid;
        var isFirstRender = oldData.IsNone;
        var mustScrollToEntry = isFirstRender && entryLid != 0;
        var chatIdRange = ChatIdRangeState.Value; // do not subscribe to Id range change
        var isNavigatingToEntry = false;
        // Handle NavigateToEntry
        var navigationAnchor = await NavigationAnchorState.Use(cancellationToken);
        if (navigationAnchor != null && navigationAnchor != _lastNavigationAnchor) {
            isNavigatingToEntry = true;
            _lastNavigationAnchor = navigationAnchor;
            // even if we must navigate to the next item - it's fine to use previous item there
            entryLid = navigationAnchor.EntryLid;
            if (!ItemVisibility.Value.IsFullyVisible(entryLid))
                mustScrollToEntry = true;
        }
        var lastAuthorEntryLid = await LastAuthorTextEntryLidState.Use(cancellationToken).ConfigureAwait(false);
        if (lastAuthorEntryLid > _lastReadEntryLid) {
            // Scroll to the latest Author entry - e.g.m when author submits the new one
            _lastReadEntryLid = lastAuthorEntryLid;
            entryLid = lastAuthorEntryLid;
            mustScrollToEntry = true;
        }
        var idRangeToLoad = GetIdRangeToLoad(query, oldData, mustScrollToEntry ? entryLid : 0, chatIdRange);

        activity?.SetTag("AC." + "IdRange", chatIdRange.AsOneLineString());
        activity?.SetTag("AC." + "ReadEntryLid", readEntryLid);
        activity?.SetTag("AC." + "IdRangeToLoad", idRangeToLoad.AsOneLineString());

        var hasVeryFirstItem = idRangeToLoad.Start <= chatIdRange.Start;
        var hasVeryLastItem = idRangeToLoad.End + 1 >= chatIdRange.End;

        // get tiles from the smallest tile layer
        var idTiles = GetOptimalCoveringTiles(idRangeToLoad, hasVeryLastItem);
        var chatTiles = (await idTiles
            .Select(idTile => Chats.GetTile(Session, chatId, ChatEntryKind.Text, idTile.Range, cancellationToken))
            .Collect())
            .Where(t => !t.IsEmpty)
            .OrderBy(t => t.Entries[0].LocalId)
            .ToList();

        if (isNavigatingToEntry && navigationAnchor!.MoveNext) {
            // update navigate target with the requested next item
            var nextEntry = chatTiles
                .SkipWhile(t => !t.IdTileRange.Contains(entryLid))
                .Take(2)
                .SelectMany(t => t.Entries)
                .SkipWhile(e => e.Id.LocalId <= entryLid)
                .FirstOrDefault();
            if (nextEntry != null)
                entryLid = nextEntry.Id.LocalId;
        }

        var scrollToKey = mustScrollToEntry
            ? GetScrollToKey(chatTiles, entryLid)
            : null;

        // Do not show '-new-' separator after view is scrolled to the end anchor.
        var lastTile = chatTiles[^1];
        if (!_suppressNewMessagesEntry && _itemVisibilityUpdateReceived)
            if (ShouldSuppressNewMessagesEntry(ItemVisibility.Value, lastTile))
                _suppressNewMessagesEntry = true;

        if (chatTiles.Count == 0) {
            var isEmpty = await ChatUI.IsEmpty(chatId, cancellationToken);
            if (isEmpty)
                return new VirtualListData<ChatMessageModel>(new [] { new VirtualListDataTile<ChatMessageModel>(ChatMessageModel.FromEmpty(Chat.Id))}) {
                    HasVeryFirstItem = true,
                    HasVeryLastItem = true,
                    ScrollToKey = null,
                    RequestedStartExpansion = null,
                    RequestedEndExpansion = null,
                };
        }

        var linkPreviews = await chatTiles
            .SelectMany(t => t.Entries)
            .Where(x => !x.LinkPreviewId.IsEmpty)
            .Select(x => MediaLinkPreviews.GetForEntry(x.LinkPreviewId, x.Id, cancellationToken))
            .Collect()
            .ConfigureAwait(false);
        var linkPreviewMap = linkPreviews.SkipNullItems().Distinct().ToDictionary(x => x.Id);
        var dataTiles = GetDataFromChatTiles(
            chatTiles,
            oldData,
            _suppressNewMessagesEntry ? long.MaxValue : _lastReadEntryLid,
            hasVeryFirstItem,
            TimeZoneConverter,
            linkPreviewMap);
        var areSameDataTiles = !oldData.IsNone
            && dataTiles.Count == oldData.Tiles.Count
            && dataTiles
                .Zip(oldData.Tiles)
                .All(pair => ReferenceEquals(pair.First, pair.Second));
        var result = areSameDataTiles && OrdinalEquals(scrollToKey, oldData.ScrollToKey) && !isNavigatingToEntry
            ? oldData
            : new VirtualListData<ChatMessageModel>(dataTiles) {
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
            && chatTiles.Count > 0) {
            var lastEntryLid = chatTiles[^1].Entries[^1].Id.LocalId;
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

    private Tile<long>[] GetOptimalCoveringTiles(
        Range<long> idRangeToLoad,
        bool hasVeryLastItem)
    {
        var secondLayer = IdTileStack.Layers[1];
        var tiles = ArrayBuffer<Tile<long>>.Lease(true);
        try {
            var tileCandidates = secondLayer.GetOptimalCoveringTiles(idRangeToLoad);
            var firstNativeTileIndex = -1;
            var lastNativeTileIndex = -1;
            for (int i = 0; i < tileCandidates.Length; i++) {
                var tile = tileCandidates[i];
                if (tile.Layer != secondLayer)
                    continue;

                firstNativeTileIndex = i;
                break;
            }
            for (int i = tileCandidates.Length -1; i >= 0; i--) {
                var tile = tileCandidates[i];
                if (tile.Layer != secondLayer)
                    continue;

                lastNativeTileIndex = i;
                break;
            }
            if (firstNativeTileIndex < 0 || tileCandidates.Length == 0)
                return tileCandidates; // bigger tile isn't required

            var isFirstTileNative = tileCandidates[0].Layer == secondLayer;
            if (!isFirstTileNative) {
                // replace with tiles of the second layer
                var replacement = tileCandidates[firstNativeTileIndex].Prev();
                tiles.Add(replacement);
            }
            var isLastTileNative = tileCandidates[^1].Layer == secondLayer;
            if (isLastTileNative && hasVeryLastItem) {
                // add second layer tiles except the last one
                tiles.AddSpan(tileCandidates.AsSpan()[firstNativeTileIndex..^1]);
                // replace with smaller tiles as the last tile might be changed
                tiles.AddRange(tileCandidates[^1].Smaller());
                // subscribe to the next new tile
                tiles.Add(tiles[^1].Next());
            }
            else if (hasVeryLastItem) {
                // keep smaller tiles at the end
                tiles.AddSpan(tileCandidates.AsSpan()[firstNativeTileIndex..]);
                // subscribe to the next new tile
                tiles.Add(tiles[^1].Next());
            }
            else {
                // add second layer tiles except the last smaller tiles
                tiles.AddSpan(tileCandidates.AsSpan()[firstNativeTileIndex..(lastNativeTileIndex + 1)]);
                // replace smaller tiles with second layer tiles
                tiles.Add(tileCandidates[lastNativeTileIndex].Next());
            }
            return tiles.ToArray();
        }
        finally {
            tiles.Release();
        }
    }

    private Range<long> GetIdRangeToLoad(
        VirtualListDataQuery query,
        VirtualListData<ChatMessageModel> oldData,
        long scrollToEntryLid,
        Range<long> chatIdRange)
    {
        var queryRange = (query.IsNone, oldData.Tiles.Count == 0) switch {
            (true, true) => new Range<long>(chatIdRange.End - (2 * PageSize), chatIdRange.End),
            (true, false) => new Range<long>(oldData.Tiles[0].Items[0].Entry.LocalId, oldData.Tiles[^1].Items[^1].Entry.LocalId),
            _ => query.KeyRange
                .AsLongRange()
                .Expand(new Range<long>(query.ExpandStartBy, query.ExpandEndBy)),
        };

        // If we are scrolling somewhere - let's load the date near the entryId
        // Last read position might point to already deleted entries, OR it might be corrupted!
        var scrollToEntryRange = scrollToEntryLid > 0 && chatIdRange.Contains(scrollToEntryLid)
            ? new Range<long>(
                scrollToEntryLid -  (2 * PageSize),
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
        var extendedRange = (closeToTheStart: isCloseToTheStart, closeToTheEnd: isCloseToTheEnd) switch {
            (true, true) => chatIdRange.Expand(1), // extend to mitigate outdated id range
            (_, true) => new Range<long>(queryRange.Start, chatIdRange.End + 2),
            (true, _) => new Range<long>(chatIdRange.Start, queryRange.End),
            _ => queryRange,
        };
        return extendedRange;
    }

    private string? GetScrollToKey(List<ChatTile> chatTiles, long scrollToEntryLid)
    {
        var scrollToEntry = chatTiles
            .SkipWhile(t => !t.IdTileRange.Contains(scrollToEntryLid))
            .Take(2)
            .SelectMany(t => t.Entries)
            .SkipWhile(e => e.LocalId < scrollToEntryLid)
            .FirstOrDefault();
        if (scrollToEntry is not null)
            return scrollToEntry.LocalId.Format();

        Log.LogWarning("Failed to find entry to scroll to #{EntryLid}", scrollToEntryLid);
        return null;
    }

    private static List<VirtualListDataTile<ChatMessageModel>> GetDataFromChatTiles(
        List<ChatTile> chatTiles,
        VirtualListData<ChatMessageModel> oldData,
        long lastReadEntryId,
        bool hasVeryFirstItem,
        TimeZoneConverter timeZoneConverter,
        IReadOnlyDictionary<Symbol, Media.LinkPreview> linkPreviews)
    {
        var isBlockStart = true;
        var lastDate = default(DateOnly);
        var isPrevUnread = true;
        var isPrevAudio = (bool?)false;
        var isPrevForward = false;
        var isVeryFirstItem = true;
        var prevForwardChatId = ChatId.None;
        var addWelcomeBlock = hasVeryFirstItem;
        var result = new List<VirtualListDataTile<ChatMessageModel>>(chatTiles.Count);
        var oldDataTileMap = oldData.Tiles
            .ToDictionary(t => {
                var startId = t.Items[0].Entry.LocalId;
                var endId = t.Items[^1].Entry.LocalId;
                var tileLayer = IdTileStack.FirstLayer;
                while (tileLayer != null) {
                    var tile = tileLayer.GetTile(startId);
                    if (tile.Range.Contains(endId))
                        return tile.Range;

                    tileLayer = tileLayer.Larger;
                }

                // this line should never been called, just a safe guard
                return new Range<long>(t.Items[0].Entry.LocalId, t.Items[^1].Entry.LocalId + 1);
            });
        var oldItemsMap = oldData.Tiles
            .SelectMany(t => t.Items)
            .ToDictionary(i => i.Key, i => i);
        for (var i = 0; i < chatTiles.Count; i++) {
            var chatTile = chatTiles[i];
            var nextChatTile = chatTiles.Count > i + 1
                ? chatTiles[i + 1]
                : null;
            var chatMessageModels = new List<ChatMessageModel>();
            if (oldDataTileMap.TryGetValue(chatTile.IdTileRange, out var oldDataTile)) {
                var newMessageLine = oldDataTile.Items.FirstOrDefault(cm => cm.ReplacementKind == ChatMessageReplacementKind.NewMessagesLine);
                if (ReferenceEquals(oldDataTile.Source, chatTile) && (newMessageLine == null || newMessageLine.Entry.LocalId > lastReadEntryId)) {
                    var lastTileItem = oldDataTile.Items[^1];
                    var entry = lastTileItem.Entry;
                    var nextEntry = nextChatTile?.Entries[0];
                    isPrevUnread = entry.LocalId > lastReadEntryId;
                    isBlockStart = ShouldSplit(entry, nextEntry);
                    lastDate = DateOnly.FromDateTime(timeZoneConverter.ToLocalTime(entry.BeginsAt));
                    isPrevAudio = entry.AudioEntryId != null || entry.IsStreaming;
                    isPrevForward = !entry.ForwardedAuthorId.IsNone;
                    prevForwardChatId = entry.ForwardedChatEntryId.ChatId;
                    isVeryFirstItem = false;
                    addWelcomeBlock = false;

                    result.Add(oldDataTile);
                    continue;
                }
            }
            for (var j = 0; j < chatTile.Entries.Count; j++) {
                var entry = chatTile.Entries[j];
                var nextEntry = chatTile.Entries.Count > j + 1
                    ? chatTile.Entries[j + 1]
                    : nextChatTile?.Entries[0];
                var date = DateOnly.FromDateTime(timeZoneConverter.ToLocalTime(entry.BeginsAt));
                var isBlockEnd = ShouldSplit(entry, nextEntry);
                var isForward = !entry.ForwardedAuthorId.IsNone;
                var isForwardFromOtherChat = prevForwardChatId != entry.ForwardedChatEntryId.ChatId;
                var isForwardBlockStart = (isBlockStart && isForward) || (isForward && (!isPrevForward || isForwardFromOtherChat));
                var isUnread = entry.LocalId > lastReadEntryId;
                var isAudio = entry.AudioEntryId != null || entry.IsStreaming;
                var isEntryKindChanged = isPrevAudio is not { } vIsPrevAudio || (vIsPrevAudio ^ isAudio);
                var addDateLine = date != lastDate && (hasVeryFirstItem || !isVeryFirstItem);
                if (addWelcomeBlock) {
                    chatMessageModels.Add(new ChatMessageModel(entry) {
                        ReplacementKind = ChatMessageReplacementKind.WelcomeBlock,
                    });
                    addWelcomeBlock = false;
                }
                if (isUnread && !isPrevUnread)
                    AddItem(new ChatMessageModel(entry) {
                        ReplacementKind = ChatMessageReplacementKind.NewMessagesLine,
                    });
                if (addDateLine)
                    AddItem(new ChatMessageModel(entry) {
                        ReplacementKind = ChatMessageReplacementKind.DateLine,
                        DateLineDate = date,
                    });

                {
                    var item = new ChatMessageModel(entry) {
                        IsBlockStart = isBlockStart,
                        IsBlockEnd = isBlockEnd,
                        HasEntryKindSign = isEntryKindChanged || (isBlockStart && isAudio),
                        IsForwardBlockStart = isForwardBlockStart,
                        LinkPreview = linkPreviews.GetValueOrDefault(entry.LinkPreviewId)
                    };
                    AddItem(item);
                }

                isPrevUnread = isUnread;
                isBlockStart = isBlockEnd;
                lastDate = date;
                isPrevAudio = isAudio;
                isPrevForward = isForward;
                prevForwardChatId = entry.ForwardedChatEntryId.ChatId;
                isVeryFirstItem = false;
            }
            result.Add(new VirtualListDataTile<ChatMessageModel>(chatMessageModels, chatTile));
            continue;

            void AddItem(ChatMessageModel item) {
                var oldItem = oldItemsMap.GetValueOrDefault(item.Key);
                if (oldItem != null && oldItem.Equals(item))
                    item = oldItem;
                chatMessageModels.Add(item);
            }
        }

        return result;

        bool ShouldSplit(
            ChatEntry entry,
            ChatEntry? nextEntry)
        {
            if (nextEntry == null)
                return false;

            if (entry.AuthorId != nextEntry.AuthorId)
                return true;

            var prevEndsAt = entry.EndsAt ?? entry.BeginsAt;
            return nextEntry.BeginsAt - prevEndsAt >= BlockSplitPauseDuration;
        }
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

    private async Task SyncLastAuthorEntryLidState(CancellationToken cancellationToken)
    {
        var chatId = Chat.Id;
        var chatIdRange = ChatIdRangeState.Value;
        var lastIdTile = IdTileStack.Layers[0].GetTile(chatIdRange.ToInclusive().End);
        var lastTile = await Chats.GetTile(Session,
            chatId,
            ChatEntryKind.Text,
            lastIdTile.Range,
            cancellationToken);
        var entryReader = Chats.NewEntryReader(Session, chatId, ChatEntryKind.Text);
        var author = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var authorId = author?.Id;
        var newEntries = entryReader.Observe(lastTile.Entries[^1].LocalId, cancellationToken);
        // ReSharper disable once UseCancellationTokenForIAsyncEnumerable
        await foreach (var newOwnEntry in newEntries.Where(e => e.AuthorId == authorId && e is { IsStreaming: false, AudioEntryId: null }).ConfigureAwait(false))
            LastAuthorTextEntryLidState.Value = newOwnEntry.LocalId;
    }

    private record NavigationAnchor(long EntryLid, bool MoveNext = false);
}
