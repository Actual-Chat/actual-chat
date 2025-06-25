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
    private readonly AsyncTaskMethodBuilder _whenInitializedSource = AsyncTaskMethodBuilderExt.New();

    // ReSharper disable once NotAccessedField.Local
    private Task _updateReadStateTask = null!;
    private SharedResourcePool<ChatId, SyncedState<ReadPosition>>.Lease? _readPositionLease;
    private SharedResourcePool<ChatId, MutableState<ReadPosition>>.Lease? _viewPositionLease;
    private MutableState<ChatViewItemVisibility> _itemVisibility = null!;
    private MutableState<long> _shownReadEntryLid = null!;
    private MutableState<Navigation?> _nextNavigation = null!;
    private ChatContext _chatContext = null!;

    private AppUIHub Hub { get; }
    private Session Session => Hub.Session;
    private ICommander Commander => Hub.Commander;
    private ChatUI ChatUI => Hub.ChatUI;
    private IChats Chats => Hub.Chats;
    private IAuthors Authors => Hub.Authors;
    private NavigationManager Nav => Hub.Nav;
    private History History => Hub.History;
    private StateFactory StateFactory => Hub.StateFactory;
    private CancellationToken DisposeToken { get; }

    [field: AllowNull] [field: MaybeNull]
    private ILogger Log => field ??= Hub.LogFor(GetType());

    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);

    public IState<ReadPosition> ReadPosition {
        get {
            if (_readPositionLease is null)
                throw StandardError.Constraint("Accessed too early. Read position is not available yet.");
            return _readPositionLease.Resource;
        }
    }
    public IState<ReadPosition> ViewPosition {
        get {
            if (_viewPositionLease is null)
                throw StandardError.Constraint("Accessed too early. View position is not available yet.");
            return _viewPositionLease.Resource;
        }
    }
    public IState<long> ShownReadEntryLid => _shownReadEntryLid;
    public IState<ChatViewItemVisibility> ItemVisibility => _itemVisibility;
    public Task WhenInitialized => _whenInitializedSource.Task;

    [Parameter, EditorRequired]
    public ChatId ChatId { get; set; } = null!;

    [CascadingParameter] public RegionVisibility RegionVisibility { get; set; } = null!;

    public ChatView(AppUIHub hub)
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
            _viewPositionLease = await ChatUI.LeaseViewPositionState(ChatId, DisposeToken);
            _shownReadEntryLid.Value = _readPositionLease.Resource.Value.EntryLid;
            if (_viewPositionLease.Resource.Value.EntryLid is 0)
                _viewPositionLease.Resource.Value = _readPositionLease.Resource.Value;
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
        => $"ChatView #{ChatId}";

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
                    var uri = localUrl.ToAbsolute(Hub.UrlMapper).ToUri();
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
        if (_viewPositionLease is not null) {
            var visibleEntryLids = itemVisibility.VisibleEntryLids;
            var entryId = visibleEntryLids.Max();
            _viewPositionLease.Resource.Value = new ReadPosition(ChatId, entryId);
        }
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
        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return;

        var entryReader = new ChatEntryReader(Chats, Session, chatId, ChatEntryKind.Text);
        var author = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var authorId = author?.Id;
        var chatIdRange = await Chats
            .GetIdRange(Session, chatId, ChatEntryKind.Text, cancellationToken)
            .ConfigureAwait(false);

        // Getting the very last chat entry
        var chatNews = await Chats.GetNews(Session, chatId, cancellationToken).ConfigureAwait(false);
        var chatIdGap = new Range<long>(chatNews?.TextEntryIdRange.End ?? 0, chatIdRange.End);
        var lastEntry = await entryReader.ReadReverse(chatIdGap, cancellationToken)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        lastEntry ??= chatNews?.LastTextEntry;
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
        var viewEntryLid = itemVisibility.IsEndAnchorVisible
            ? long.MaxValue // We are at the end of the chat so avoid displaying new messages line
            : ViewPosition.Value.EntryLid;
        var hasViewEntry = viewEntryLid != 0 && viewEntryLid != long.MaxValue;
        var nav = await _nextNavigation.Use(cancellationToken)
            ?? (isFirstRender && hasViewEntry ? new Navigation(viewEntryLid, false, false) : null);
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
        if (dataQuery.ExistingIdRange.End + dataQuery.EndOffset + ChatUI.HalfLoadLimit >= chatIdRange.End)
            await cChatIdRange.Use(cancellationToken); // Add dependency on chatIdRange

        activity?.SetTag("AC." + "IdRange", chatIdRange.Format());
        activity?.SetTag("AC." + "ViewEntryLid", viewEntryLid);
        activity?.SetTag("AC." + "ReadEntryLid", readEntryLid);
        activity?.SetTag("AC." + "DataQuery", dataQuery.Format());
        DebugLog?.LogDebug("GetData: #{ChatId} -> {IdRangeToLoad}", chatId, dataQuery.Format());

        var tryUpdateShownReadEntryLid = true;

        rebuildTiles: // Building actual virtual list tiles

        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return VirtualListData<ChatMessage>.None;

        var (items, hasBefore, hasAfter) = await ChatUI.GetChatItems(chatId, dataQuery, readEntryLid, cancellationToken).ConfigureAwait(false);
        if (items.Count == 0) {
            var isEmpty = await ChatUI.IsEmpty(chatId, cancellationToken);
            if (isEmpty)
                return new VirtualListData<ChatMessage>([ChatMessage.Welcome(chatId, chat.IsAiSearchChat())]) {
                    HasVeryFirstItem = true,
                    HasVeryLastItem = true,
                    ScrollToKey = null,
                    NavigationState = nav ?? renderedData.NavigationState,
                    ItemVisibilityState = ItemVisibility.Value,
                };
        }

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
                    TextEntryId.New(chatId, navChatMessage.Id),
                    false);
        }
        var result = new VirtualListData<ChatMessage>(items) {
            Index = renderedData.Index + 1,
            EstimatedCount = (int?)(chatIdRange.End - chatIdRange.Start),
            HasVeryFirstItem = !hasBefore,
            HasVeryLastItem = !hasAfter,
            ScrollToKey = navKey != null && mustScrollToEntry ? navKey : null,
            ScrollToKeyInTheMiddle = nav is { ShowInTheMiddle: true },
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
        var firstLayer = ChatUI.IdTileStack.FirstLayer;
        var secondLayer = ChatUI.IdTileStack.LastLayer;
        var itemVisibility = ItemVisibility.Value;
        var firstItem = oldData.FirstItem;
        var lastItem = oldData.LastItem;
        var keyRange = query.IsNone
            ? firstItem != null
                ? new Range<long>(firstItem.Id, lastItem!.Id + 1)
                : chatIdRange.EnsureNonEmpty()
            : query.KeyRange.ToLongRange(true).EnsureNonEmpty();
        var dataQuery = (!query.IsNone, firstItem != null) switch {
            // Align the query params with the second tile layer

            // No query, no data -> initial load
            (false, false) => new ChatDataQuery(
                secondLayer.GetTile(chatIdRange.End - firstLayer.TileSize).Range,
                -ChatUI.HalfLoadLimit,
                ChatUI.HalfLoadLimit),

            // No query, there is old data, and we are at the end of the list, let's stick to the visible range if possible
            (false, true) when oldData.HasVeryLastItem
                => new ChatDataQuery(
                    new Range<long>(
                        Math.Max(firstItem!.Id, itemVisibility.MinEntryLid),
                        Math.Min(lastItem!.Id, itemVisibility.IsEmpty ? long.MaxValue : itemVisibility.MaxEntryLid)).EnsureNonEmpty(),
                    -ChatUI.HalfLoadLimit,
                    ChatUI.HalfLoadLimit),

            // No query, but there is old data -> retaining it
            (false, true) => new ChatDataQuery(
                keyRange,
                0,
                ChatUI.HalfLoadLimit),

            // Query is there, so data is irrelevant
            _ => new ChatDataQuery(
                keyRange,
                query.MoveRange.Start,
                query.MoveRange.End),
        };

        // If we are scrolling somewhere within idRange, let's extend the range to scrollAnchor & nearby entries.
        if (scrollAnchor != null && chatIdRange.Contains(scrollAnchor.EntryLid))
            dataQuery = new ChatDataQuery(
                secondLayer.GetTile(scrollAnchor.EntryLid).Range,
                -ChatUI.HalfLoadLimit,
                ChatUI.LoadLimit) {
                    NavigateToLid = scrollAnchor.EntryLid,
            };

        return dataQuery;
    }

    // Helpers

    private long UpdateReadPosition(long readEntryLid)
    {
        var readPosition = ReadPosition;
        readEntryLid = Math.Max(readPosition.Value.EntryLid, readEntryLid);
        if (readPosition.Value.EntryLid < readEntryLid)
            ((IMutableState<ReadPosition>)readPosition).Value = new ReadPosition(ChatId, readEntryLid);
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
        bool MustHighlight,
        bool ShowInTheMiddle = true)
    {
        // This record relies on referential equality
        public bool Equals(Navigation? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
}
