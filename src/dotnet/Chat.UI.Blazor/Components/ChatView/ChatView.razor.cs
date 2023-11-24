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
    public static readonly long MinLoadLimit = 2 * IdTileStack.Layers[1].TileSize; // 40

    private readonly CancellationTokenSource _disposeTokenSource;
    private readonly TaskCompletionSource _whenInitializedSource = TaskCompletionSourceExt.New();
    private readonly Suspender _getDataSuspender = new();

    private Task _syncLastAuthorEntryLidState = null!;
    private NavigationAnchor? _lastNavigationAnchor;
    private long _lastReadEntryLid;
    private bool _itemVisibilityUpdateReceived;
    private bool _suppressNewMessagesEntry;
    private IMutableState<ChatViewItemVisibility> _itemVisibility = null!;
    private IComputedState<bool>? _isViewportAboveUnreadEntry = null;
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

    private IMutableState<NavigationAnchor?> NavigationAnchorState { get; set; } = null!;
    private IMutableState<(AuthorId AuthorId, long EntryLid)> LastAuthorTextEntryLidState { get; set; } = null!;
    private SyncedStateLease<ReadPosition> ReadPositionState { get; set; } = null!;
    private ComputedStateLease<Range<long>> ChatIdRangeState { get; set; } = null!;

    public IState<bool> IsViewportAboveUnreadEntry => _isViewportAboveUnreadEntry!;
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
            NavigationAnchorState = StateFactory.NewMutable(
                (NavigationAnchor?)null,
                StateCategories.Get(type, nameof(NavigationAnchorState)));
            LastAuthorTextEntryLidState = StateFactory.NewMutable(
                (AuthorId.None, 0L),
                StateCategories.Get(type, nameof(LastAuthorTextEntryLidState)));
            _itemVisibility = StateFactory.NewMutable(
                ChatViewItemVisibility.Empty,
                StateCategories.Get(type, nameof(ItemVisibility)));
            _isViewportAboveUnreadEntry = StateFactory.NewComputed(
                new ComputedState<bool>.Options {
                    UpdateDelayer = FixedDelayer.Instant,
                    InitialValue = false,
                    Category = StateCategories.Get(type, nameof(IsViewportAboveUnreadEntry)),
                },
                ComputeIsViewportAboveUnreadEntry);
            ReadPositionState = await ChatUI.LeaseReadPositionState(Chat.Id, DisposeToken);
            ChatIdRangeState = await ChatUI.LeaseChatIdRangeState(Chat.Id, DisposeToken);
            _lastReadEntryLid = ReadPositionState.Value.EntryLid;
            if (_whenInitializedSource.TrySetResult())
                RegionVisibility.IsVisible.Updated += OnRegionVisibilityChanged;
            _syncLastAuthorEntryLidState = new AsyncChain(nameof(SyncLastAuthorEntryLidState),  SyncLastAuthorEntryLidState)
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

        _disposeTokenSource.CancelAndDisposeSilently();
        _whenInitializedSource.TrySetCanceled();
        RegionVisibility.IsVisible.Updated -= OnRegionVisibilityChanged;
        Nav.LocationChanged -= OnLocationChanged;
        _getDataSuspender.IsSuspended = false;
        _isViewportAboveUnreadEntry.DisposeSilently();
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
            navigateToEntryLid = chatIdRange.MoveEnd(-1).End;
        }

        // Reset to ensure the navigation will happen
        _lastReadEntryLid = navigateToEntryLid;
        NavigateToAnchor(navigateToEntryLid, true);
    }

    public void NavigateToAnchor(long entryLid, bool mustPositionAfter = false)
    {
        // Reset to ensure navigation will happen
        _lastNavigationAnchor = null;
        NavigationAnchorState.Value = new NavigationAnchor(entryLid, mustPositionAfter);
        NavigationAnchorState.Invalidate();
    }

    public void TryNavigateToEntry()
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
            NavigateToAnchor(entryId);
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
        VirtualListDataQuery query,
        VirtualListData<ChatMessage> oldData,
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
        var isFirstRender = oldData.IsNone;
        var scrollAnchor = isFirstRender && readEntryLid != 0
            ? new NavigationAnchor(readEntryLid)
            : null;
        var (authorId, lastAuthorEntryLid) = await LastAuthorTextEntryLidState.Use(cancellationToken);
        if (lastAuthorEntryLid > _lastReadEntryLid) {
            // Scroll to the latest Author's entry - e.g.m when the author submits a new one
            _lastReadEntryLid = lastAuthorEntryLid;
            scrollAnchor ??= new NavigationAnchor(lastAuthorEntryLid);
        }
        // Handle NavigateToEntry
        var navigationAnchor = await NavigationAnchorState.Use(cancellationToken);
        if (navigationAnchor != null && navigationAnchor != _lastNavigationAnchor) {
            _lastNavigationAnchor = navigationAnchor;
            scrollAnchor = navigationAnchor;
        }
        var mustScrollToEntry = scrollAnchor != null && !ItemVisibility.Value.IsFullyVisible(scrollAnchor.EntryLid);
        var chatIdRange = oldData.IsNone || !oldData.HasVeryLastItem
            ? ChatIdRangeState.Value
            : await ChatIdRangeState.Use(cancellationToken).ConfigureAwait(false);
        var idRangeToLoad = GetIdRangeToLoad(query, oldData, scrollAnchor, chatIdRange);
        var hasVeryFirstItem = idRangeToLoad.Start <= chatIdRange.Start;
        var hasVeryLastItem = idRangeToLoad.End >= chatIdRange.End;

        activity?.SetTag("AC." + "IdRange", chatIdRange.Format());
        activity?.SetTag("AC." + "ReadEntryLid", readEntryLid);
        activity?.SetTag("AC." + "IdRangeToLoad", idRangeToLoad.Format());
        // DebugLog?.LogDebug("GetData: #{ChatId} -> {IdRangeToLoad}", chatId, idRangeToLoad.Format());

        // Prefetching new tiles
        var lastIdRangeToLoad = _lastIdRangeToLoad;
        _lastIdRangeToLoad = idRangeToLoad;
        var newIdRanges = idRangeToLoad.Subtract(lastIdRangeToLoad);
        using (var __ = ExecutionContextExt.SuppressFlow()) {
            // We don't want dependencies to be captured for prefetch calls
            _ = PrefetchTiles(chatId, newIdRanges.Item1, cancellationToken);
            _ = PrefetchTiles(chatId, newIdRanges.Item2, cancellationToken);
        }

        // Building actual virtual list tiles
        var idTiles = GetIdTilesToLoad(idRangeToLoad, chatIdRange);
        var prevMessage = hasVeryFirstItem ? ChatMessage.Welcome(chatId) : null;
        var lastReadEntryLid = _suppressNewMessagesEntry ? long.MaxValue : _lastReadEntryLid;
        var tiles = new List<VirtualListTile<ChatMessage>>();
        while (true) {
            foreach (var idTile in idTiles) {
                var lastReadEntryLidArg = lastReadEntryLid < idTile.Range.Start
                    ? 0
                    : lastReadEntryLid >= idTile.Range.End - 1
                        ? long.MaxValue
                        : lastAuthorEntryLid;
                var tile = await ChatUI
                    .GetTile(chatId,
                        idTile.Range,
                        prevMessage,
                        lastReadEntryLidArg,
                        cancellationToken);
                if (tile.Items.Count == 0)
                    continue;

                tiles.Add(tile);
#if false
            // Uncomment for debugging:
            DebugLog?.LogDebug("Tile: #{IdRange}, {IsUnread}, {LastReadEntryLid}",
                idTile.Range.Format(), isUnread, lastReadEntryLidArg);
            foreach (var item in tile.Items)
                DebugLog?.LogDebug("- {Key}: {ReplacementKind}", item.Key, item.ReplacementKind);
#endif
                prevMessage = tile.Items[^1];
            }
            var lastOwnItem = tiles
                .SelectMany(t => t.Items)
                .Reverse()
                .FirstOrDefault(i => i.Entry.AuthorId == authorId);
            if (lastOwnItem != null && lastOwnItem.Flags.HasFlag(ChatMessageFlags.Unread)) {
                var lastOwnEntryLid = lastOwnItem.Entry.LocalId;
                lastReadEntryLid = lastOwnEntryLid;
                if (LastAuthorTextEntryLidState.Value.EntryLid < lastOwnEntryLid)
                    LastAuthorTextEntryLidState.Value = (authorId, lastOwnEntryLid);
                if (ReadPositionState.Value.EntryLid < lastOwnEntryLid)
                    ReadPositionState.Value = new ReadPosition(Chat.Id, lastOwnEntryLid);
            }
            else
                break;
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


        var scrollToKey = (string?)null;
        var highlightEntryLid = scrollAnchor != null && scrollAnchor == navigationAnchor
            ? (long?)scrollAnchor.EntryLid
            : null;
        if (mustScrollToEntry && scrollAnchor != null) {
            var entryLid = scrollAnchor.EntryLid;
            var criteria = (Func<ChatMessage, bool>)(scrollAnchor.MustPositionAfter
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

        // Do not show '-new-' separator after view is scrolled to the end anchor
        if (!_suppressNewMessagesEntry && _itemVisibilityUpdateReceived)
            if (ShouldSuppressNewMessagesEntry(tiles, ItemVisibility.Value))
                _suppressNewMessagesEntry = true;

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
            NavigateToAnchor(@event.ChatEntryId.LocalId);
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
        NavigationAnchor? scrollAnchor,
        Range<long> chatIdRange)
    {
        var secondLayer = IdTileStack.Layers[1];
        var minTileSize = IdTileStack.MinTileSize;
        var range = (query.IsNone, oldData.Tiles.Count == 0) switch {
            (true, true) => new Range<long>(secondLayer.GetTile(chatIdRange.End - MinLoadLimit).Start,
                chatIdRange.End + minTileSize),
            (true, false) when
                Math.Abs(oldData.Tiles[^1].Items[^1].Entry.LocalId - chatIdRange.End) <= minTileSize // reduce range when new messages were added
                => new Range<long>(secondLayer.GetTile(
                            oldData.Tiles.SelectMany(t => t.Items)
                                .Reverse()
                                .Skip((int)MinLoadLimit * 2)
                                .FirstOrDefault()
                                ?.Entry.LocalId
                            ?? oldData.Tiles[0].Items[0].Entry.LocalId
                        )
                        .Start,
                    chatIdRange.End + minTileSize),
            (true, false) => new Range<long>(oldData.Tiles[0].Items[0].Entry.LocalId,
                oldData.Tiles[^1].Items[^1].Entry.LocalId),
            _ => query.KeyRange
                .ToLongRange()
                .Expand(new Range<long>(query.ExpandStartBy, query.ExpandEndBy)),
        };

        // If we are scrolling somewhere, let's extend the range to scrollAnchor & nearby entries.
        if (scrollAnchor is { } vScrollAnchor) {
            var scrollAnchorRange = new Range<long>(
                vScrollAnchor.EntryLid - MinLoadLimit,
                vScrollAnchor.EntryLid + (MinLoadLimit / 2));
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
            range = new Range<long>(range.Start, chatIdRange.End + minTileSize);

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

    private bool ShouldSuppressNewMessagesEntry(
        List<VirtualListTile<ChatMessage>> tiles,
        ChatViewItemVisibility itemVisibility)
    {
        if (!itemVisibility.IsEndAnchorVisible)
            return false;

        var mustShow = _lastReadEntryLid != 0 && itemVisibility.VisibleEntryLids.Contains(_lastReadEntryLid);
        // If user still sees '-new-' separator while they has reached the end anchor, keep separator displayed.
        if (mustShow)
            return false;

        var lastVisibleEntryLid = itemVisibility.MaxEntryLid;
        if (tiles.Count == 0)
            return true;

        var lastEntryLid = tiles[^1].Items.MaxBy(m => m.Entry.LocalId)!.Entry.LocalId;
        return lastVisibleEntryLid >= lastEntryLid;
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
        var entryReader = ChatContext.NewEntryReader(ChatEntryKind.Text);
        var author = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var authorId = author?.Id ?? AuthorId.None;
        var newEntries = entryReader.Observe(chatIdRange.End, cancellationToken);
        // ReSharper disable once UseCancellationTokenForIAsyncEnumerable
        var authorEntries = newEntries
            .Where(e => e.AuthorId == authorId && e is { IsStreaming: false, AudioEntryId: null })
            .ConfigureAwait(false);
        await foreach (var newOwnEntry in authorEntries) {
            LastAuthorTextEntryLidState.Value = (authorId, newOwnEntry.LocalId);
            if (ReadPositionState.Value.EntryLid < newOwnEntry.LocalId)
                ReadPositionState.Value = new ReadPosition(Chat.Id, newOwnEntry.LocalId);
        }
    }

    // Nested types

    private record NavigationAnchor(long EntryLid, bool MustPositionAfter = false);
}
