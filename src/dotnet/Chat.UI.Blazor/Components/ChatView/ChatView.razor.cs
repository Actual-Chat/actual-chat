using System.Text.RegularExpressions;
using ActualChat.Chat.UI.Blazor.Events;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using Stl.Diagnostics;

namespace ActualChat.Chat.UI.Blazor.Components;

public partial class ChatView : ComponentBase, IVirtualListDataSource<ChatMessageModel>, IDisposable
{
    public static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;
    public static readonly long MinLoadLimit = 2 * IdTileStack.Layers[1].TileSize; // 40

    private readonly CancellationTokenSource _disposeTokenSource = new();
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
    private IStateFactory StateFactory => ChatContext.StateFactory;
    private Dispatcher Dispatcher => ChatContext.Dispatcher;
    private CancellationToken DisposeToken => _disposeTokenSource.Token;
    private ILogger Log => _log ??= Services.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);

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
            _syncLastAuthorEntryLidState = new AsyncChain(nameof(SyncLastAuthorEntryLidState),  SyncLastAuthorEntryLidState)
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
        var chatIdRange = ChatIdRangeState.Value;
        var readEntryLid = ReadPositionState.Value.EntryLid;
        var isFirstRender = oldData.IsNone;
        var scrollAnchor = isFirstRender && readEntryLid != 0
            ? new NavigationAnchor(readEntryLid)
            : null;
        var lastAuthorEntryLid = await LastAuthorTextEntryLidState.Use(cancellationToken);
        if (lastAuthorEntryLid > _lastReadEntryLid) {
            // Scroll to the latest Author's entry - e.g.m when the author submits a new one
            _lastReadEntryLid = lastAuthorEntryLid;
            scrollAnchor ??= new NavigationAnchor(lastAuthorEntryLid);
        }
        // Handle NavigateToEntry
        var navigationAnchor = await NavigationAnchorState.Use(cancellationToken);
        if (navigationAnchor != _lastNavigationAnchor) {
            _lastNavigationAnchor = navigationAnchor;
            if (navigationAnchor != null)
                scrollAnchor = navigationAnchor;
        }

        var mustScrollToEntry = scrollAnchor != null && !ItemVisibility.Value.IsFullyVisible(scrollAnchor.EntryLid);
        var idRangeToLoad = GetIdRangeToLoad(query, oldData, scrollAnchor, chatIdRange);
        var hasVeryFirstItem = idRangeToLoad.Start <= chatIdRange.Start;
        var hasVeryLastItem = idRangeToLoad.End >= chatIdRange.End;

        activity?.SetTag("AC." + "IdRange", chatIdRange.Format());
        activity?.SetTag("AC." + "ReadEntryLid", readEntryLid);
        activity?.SetTag("AC." + "IdRangeToLoad", idRangeToLoad.Format());
        Log.LogWarning("GetData: #{ChatId} -> {IdRangeToLoad} of {IdRange}",
            chatId, idRangeToLoad.Format(), chatIdRange.Format());

        // Prefetching new tiles
        var lastIdRangeToLoad = _lastIdRangeToLoad;
        _lastIdRangeToLoad = idRangeToLoad;
        var newIdRanges = idRangeToLoad.Subtract(lastIdRangeToLoad);
        using (var flowSuppressor = ExecutionContextExt.SuppressFlow()) {
            // We don't want dependencies to be captured for prefetch calls
            _ = PrefetchTiles(chatId, newIdRanges.Item1, cancellationToken);
            _ = PrefetchTiles(chatId, newIdRanges.Item2, cancellationToken);
        }

        // Building actual virtual list tiles
        var idTiles = GetRenderTiles(idRangeToLoad, chatIdRange);
        var prevMessage = hasVeryFirstItem ? ChatMessageModel.Welcome(chatId) : null;
        var lastReadEntryLid = _suppressNewMessagesEntry ? long.MaxValue : _lastReadEntryLid;
        var tiles = new List<VirtualListTile<ChatMessageModel>>();
        foreach (var idTile in idTiles) {
            bool? isUnread = null;
            if (lastReadEntryLid < idTile.Range.Start)
                isUnread = true;
            else if (lastAuthorEntryLid >= idTile.Range.End - 1)
                isUnread = false;
            var tile = await ChatUI.GetTile(
                chatId, idTile.Range,
                prevMessage,
                isUnread, isUnread.HasValue ? 0 : lastReadEntryLid,
                cancellationToken);
            if (tile.Items.Count == 0)
                continue;

            tiles.Add(tile);
            prevMessage = tile.Items[^1];
        }
        if (tiles.Count == 0) {
            var isEmpty = await ChatUI.IsEmpty(chatId, cancellationToken);
            if (isEmpty)
                return new VirtualListData<ChatMessageModel>(new [] {
                    new VirtualListTile<ChatMessageModel>(new [] { ChatMessageModel.Welcome(Chat.Id) }),
                }) {
                    HasVeryFirstItem = true,
                    HasVeryLastItem = true,
                    ScrollToKey = null,
                    RequestedStartExpansion = null,
                    RequestedEndExpansion = null,
                };
        }

        var scrollToKey = (string?)null;
        if (mustScrollToEntry && scrollAnchor != null) {
            var entryLid = scrollAnchor.EntryLid;
            var criteria = (Func<ChatMessageModel, bool>)(scrollAnchor.MustPositionAfter
                ? m => m.Entry.LocalId <= entryLid || m.IsReplacement
                : m => m.Entry.LocalId < entryLid || m.IsReplacement);
            var message = tiles
                .SkipWhile(t => criteria.Invoke(t.Items[^1]))
                .SelectMany(t => t.Items)
                .SkipWhile(criteria)
                .FirstOrDefault();
            if (message is not null)
                scrollToKey = message.Entry.LocalId.Format();
            else
                Log.LogWarning("Failed to find entry to scroll to #{EntryLid}", entryLid);
        }

        // Do not show '-new-' separator after view is scrolled to the end anchor
        if (!_suppressNewMessagesEntry && _itemVisibilityUpdateReceived)
            if (ShouldSuppressNewMessagesEntry(tiles, ItemVisibility.Value))
                _suppressNewMessagesEntry = true;

        var result = new VirtualListData<ChatMessageModel>(tiles) {
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
            && tiles.Count > 0) {
            var lastEntryLid = tiles[^1].Items[^1].Entry.LocalId;
            if (lastEntryLid > readEntryLid)
                ReadPositionState.Value = new ReadPosition(chatId,  lastEntryLid);
            else if (readEntryLid >= chatIdRange.End)
                ReadPositionState.Value = new ReadPosition(chatId,chatIdRange.End - 1);
        }

        if (navigationAnchor != null) {
            // highlight entry when it has already been loaded
            var entryLid = new ChatEntryId(chatId, ChatEntryKind.Text, navigationAnchor.EntryLid, AssumeValid.Option);
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

    private Tile<long>[] GetRenderTiles(
        Range<long> idRangeToLoad,
        Range<long> chatIdRange)
    {
        var fastRange = new Range<long>(chatIdRange.End - (2 * IdTileStack.MinTileSize), chatIdRange.End);
        var slowRange = new Range<long>(chatIdRange.Start, fastRange.Start);
        if (slowRange.IsNegative)
            slowRange = default;
        var tiles = ArrayBuffer<Tile<long>>.Lease(true);
        try {
            tiles.AddRange(IdTileStack.GetOptimalCoveringTiles(idRangeToLoad.IntersectWith(slowRange)));
            tiles.AddRange(IdTileStack.FirstLayer.GetCoveringTiles(idRangeToLoad.IntersectWith(fastRange)));
            return tiles.ToArray();
        }
        finally {
            tiles.Release();
        }
    }

    private Range<long> GetIdRangeToLoad(
        VirtualListDataQuery query,
        VirtualListData<ChatMessageModel> oldData,
        NavigationAnchor? scrollAnchor,
        Range<long> chatIdRange)
    {
        var queryRange = (query.IsNone, oldData.Tiles.Count == 0) switch {
            (true, true) => new Range<long>(chatIdRange.End - MinLoadLimit, chatIdRange.End),
            (true, false) => new Range<long>(oldData.Tiles[0].Items[0].Entry.LocalId, oldData.Tiles[^1].Items[^1].Entry.LocalId),
            _ => query.KeyRange
                .ToLongRange()
                .Expand(new Range<long>(query.ExpandStartBy, query.ExpandEndBy)),
        };

        // If we are scrolling somewhere, let's extend the range to scrollAnchor & nearby entries.
        if (scrollAnchor is { } vScrollAnchor) {
            var scrollAnchorRange = new Range<long>(
                vScrollAnchor.EntryLid - MinLoadLimit,
                vScrollAnchor.EntryLid + (MinLoadLimit / 2));
            queryRange = scrollAnchorRange.Overlaps(queryRange)
                ? queryRange.MinMaxWith(scrollAnchorRange)
                : scrollAnchorRange;
        }

        var minTileSize = IdTileStack.MinTileSize;
        // Fix queryRange start
        if (queryRange.Start < chatIdRange.Start)
            queryRange = new Range<long>(chatIdRange.Start, queryRange.End);
        // Fix queryRange end
        if (queryRange.End >= chatIdRange.End - minTileSize)
            queryRange = new Range<long>(queryRange.Start, chatIdRange.End);

        // Expand queryRange to tile boundaries
        queryRange = queryRange.ExpandToTiles(IdTileStack.FirstLayer);
        return queryRange;
    }

    private Task PrefetchTiles(ChatId chatId, Range<long> idRange, CancellationToken cancellationToken)
    {
        if (idRange.IsEmpty)
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
        List<VirtualListTile<ChatMessageModel>> tiles,
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
        var entryReader = Chats.NewEntryReader(Session, chatId, ChatEntryKind.Text);
        var author = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var authorId = author?.Id;
        var newEntries = entryReader.Observe(chatIdRange.End, cancellationToken);
        // ReSharper disable once UseCancellationTokenForIAsyncEnumerable
        await foreach (var newOwnEntry in newEntries.Where(e => e.AuthorId == authorId && e is { IsStreaming: false, AudioEntryId: null }).ConfigureAwait(false))
            LastAuthorTextEntryLidState.Value = newOwnEntry.LocalId;
    }

    // Nested types

    private record NavigationAnchor(long EntryLid, bool MustPositionAfter = false);
}
