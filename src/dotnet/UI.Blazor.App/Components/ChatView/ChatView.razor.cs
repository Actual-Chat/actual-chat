using ActualChat.Diagnostics;
using ActualChat.Kvas;
using ActualChat.Pooling;
using ActualChat.UI.Blazor.App.Events;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Diagnostics;
using ActualChat.UI.Blazor.Services;
using ActualLab.Diagnostics;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.App.Components;

public partial class ChatView : ComponentBase, IVirtualListDataSource<ChatMessage>, IDisposable
{
    public static readonly TimeSpan FastUpdateRecency = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan FastUpdateDelay = TimeSpan.FromMilliseconds(20);
    public static readonly TimeSpan SlowUpdateDelay = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan NewMessagesLineDebounceTimeout = TimeSpan.FromSeconds(3);

    private readonly CancellationTokenSource _disposeTokenSource;
    private readonly AsyncTaskMethodBuilder _whenInitializedSource = AsyncTaskMethodBuilderExt.New();

    // ReSharper disable once NotAccessedField.Local
    private Task _updateReadStateTask = null!;
    private SharedResourcePool<ChatId, SyncedState<ReadPosition>>.Lease? _readPositionLease;
    private SharedResourcePool<ChatId, MutableState<ReadPosition>>.Lease? _viewPositionLease;
    private MutableState<ChatViewItemVisibility> _itemVisibility = null!;
    private MutableState<long> _shownReadEntryLid = null!;
    private MutableState<ChatViewNavigation?> _nextNavigation = null!;
    private CpuTimestamp _lastEndAnchorVisibleAt;
    private CpuTimestamp _newMessagesLineShownAt;
    private long _debouncedReadEntryLid;
    private long _lastKnownEntryLid = -1;
    private string _lastNavigatedUri = "";

    private static readonly string HoverMenuJSCreateMethod = $"{BlazorUIAppModule.ImportName}.ChatHoverMenu.create";
    private readonly Dictionary<ChatEntryId, MessageHoverMenu> _hoverMenus = new();
    private Task<IJSObjectReference>? _hoverMenuJsRefTask;
    private DotNetObjectReference<ChatView>? _hoverMenuBlazorRef;
    private bool _isHoverMenuDisposed;
    private ChatEntryId? _activeHoverEntryId;

    private Chat.Chat Chat => ChatContext.Chat;
    private AppUIHub Hub { get; }
    private Session Session => Hub.Session;
    private ICommander Commander => Hub.Commander;
    private ChatUI ChatUI => Hub.ChatUI;
    private IChats Chats => Hub.Chats;
    private IAuthors Authors => Hub.Authors;
    private NavigationManager Nav => Hub.Nav;
    private History History => Hub.History;
    private StateFactory StateFactory => Hub.StateFactory;
    private IStringLocalizer L => Hub.StringLocalizer;
    private CancellationToken DisposeToken { get; }
    private ILogger Log => field ??= Hub.LogFor(GetType());

    private bool HasUrlEntryLid
        // NOTE: ?n= means NavigateToUrlFragment is about to set _nextNavigation, and it races the list's
        // initial GetData - ComponentBase renders before OnParametersSetAsync's task completes
        => new LocalUrl(History.Uri).IsChat(out var chatId, out long entryLid)
            && entryLid > 0
            && chatId == Chat.Id;
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);
    private bool IsNewMessagesLineDebounceActive
        => _newMessagesLineShownAt != default
            && _newMessagesLineShownAt.Elapsed < NewMessagesLineDebounceTimeout;

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

    [CascadingParameter] public ChatContext ChatContext { get; set; } = null!;
    [CascadingParameter] public RegionVisibility RegionVisibility { get; set; } = null!;
    [Parameter] public string NavigationSlotName { get; set; } = LayoutSlots.SubFooter;

    public ChatView(AppUIHub hub)
    {
        Hub = hub;
        _disposeTokenSource = new CancellationTokenSource();
        DisposeToken = _disposeTokenSource.Token;
    }

    protected override async Task OnInitializedAsync()
    {
        Log.LogDebug("Created for chat #{ChatId}", Chat.Id);
        ChatSwitchTracer.Mark("ChatView.OnInitializedAsync: entered", Chat.Id);
        Nav.LocationChanged += OnLocationChanged;
        try {
            var type = GetType();
            _itemVisibility = StateFactory.NewMutable(
                ChatViewItemVisibility.Empty,
                StateCategories.Get(type, nameof(ItemVisibility)));
            _nextNavigation = StateFactory.NewMutable(
                (ChatViewNavigation?)null,
                StateCategories.Get(type, nameof(_nextNavigation)));
            _shownReadEntryLid = StateFactory.NewMutable(
                0L,
                StateCategories.Get(type, nameof(ShownReadEntryLid)));
            ChatSwitchTracer.Mark("ChatView.OnInitializedAsync: LeaseReadPositionState -> in");
            _readPositionLease = await ChatUI.LeaseReadPositionState(Chat.Id, DisposeToken);
            ChatSwitchTracer.Mark("ChatView.OnInitializedAsync: LeaseReadPositionState <- out");
            _viewPositionLease = await ChatUI.LeaseViewPositionState(Chat.Id, DisposeToken);
            ChatSwitchTracer.Mark("ChatView.OnInitializedAsync: LeaseViewPositionState <- out");
            var readPosition = _readPositionLease.Resource.Value;
            _shownReadEntryLid.Value = readPosition.EntryLid;
            if (_viewPositionLease.Resource.Value.EntryLid is 0 && readPosition.EntryLid > 0)
                _viewPositionLease.Resource.Value = readPosition;
            _whenInitializedSource.TrySetResult();
            ChatSwitchTracer.Mark("ChatView.OnInitializedAsync: WhenInitialized SET",
                $"readEntryLid={readPosition.EntryLid}");
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

    protected override Task OnParametersSetAsync()
        => NavigateToUrlFragment();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        _hoverMenuBlazorRef = DotNetObjectReference.Create(this);
        _hoverMenuJsRefTask = Hub.JS
            .InvokeAsync<IJSObjectReference>(HoverMenuJSCreateMethod, _hoverMenuBlazorRef)
            .AsTask();
        var jsRef = await _hoverMenuJsRefTask.ConfigureAwait(true);
        // Dispose may have run while the JS controller was being created — tear it down right away
        if (_isHoverMenuDisposed)
            await jsRef.DisposeSilentlyAsync("dispose").ConfigureAwait(true);
    }

    public void Dispose()
    {
        if (_disposeTokenSource.IsCancellationRequested)
            return;

        ChatSwitchTracer.Mark("ChatView.Dispose (outgoing view)", Chat.Id);
        _disposeTokenSource.CancelAndDisposeSilently();
        _whenInitializedSource.TrySetCanceled();
        _readPositionLease.DisposeSilently();
        ChatUI.ResetItemVisibility(Chat.Id);
        Nav.LocationChanged -= OnLocationChanged;
        _isHoverMenuDisposed = true;
        if (_hoverMenuJsRefTask is { IsCompletedSuccessfully: true } jsRefTask)
            _ = jsRefTask.Result.DisposeSilentlyAsync("dispose");
        _hoverMenuBlazorRef.DisposeSilently();
    }

    // Hover menu coordinator: each ChatEntryMessageView registers its inline MessageHoverMenu here by
    // entry id; the JS-side hover-intent (180ms) routes show/hide to exactly one menu at a time.

    public void RegisterHoverMenu(ChatEntryId entryId, MessageHoverMenu menu)
        => _hoverMenus[entryId] = menu;

    public void UnregisterHoverMenu(ChatEntryId entryId, MessageHoverMenu menu)
    {
        if (_hoverMenus.TryGetValue(entryId, out var registered) && ReferenceEquals(registered, menu))
            _hoverMenus.Remove(entryId);
    }

    [JSInvokable]
    public void OnHoverShow(string entryId)
    {
        if (ChatEntryId.TryParse(entryId) is not { } id || !_hoverMenus.TryGetValue(id, out var menu))
            return; // Recycled or scrolled off — don't show a stale menu
        if (_activeHoverEntryId == id)
            return;

        HideActiveHoverMenu();
        _activeHoverEntryId = id;
        menu.Show();
    }

    [JSInvokable]
    public void OnHoverHide()
    {
        HideActiveHoverMenu();
        _activeHoverEntryId = null;
    }

    private void HideActiveHoverMenu()
    {
        if (_activeHoverEntryId is { } activeId && _hoverMenus.TryGetValue(activeId, out var active))
            active.Hide();
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

    public async Task NavigateTo(long entryLid, bool highlight, bool updateReadPosition = false, bool keepConversationsCollapsed = false)
    {
        await WhenInitialized;
        if (updateReadPosition)
            _shownReadEntryLid.Value = UpdateReadPosition(entryLid);
        ChatSwitchTracer.Mark("ChatView.NavigateTo: nav set", $"entryLid={entryLid}, highlight={highlight}");
        _nextNavigation.Value = new ChatViewNavigation(entryLid, highlight, KeepConversationsCollapsed: keepConversationsCollapsed);
    }

    public override string ToString()
        => $"ChatView #{Chat.Id}";

    // Event handlers

    private async Task NavigateToUrlFragment()
    {
        await WhenInitialized;
        // Ignore location changed events if already disposed
        if (DisposeToken.IsCancellationRequested)
            return;

        var sUri = History.Uri;
        var localUrl = new LocalUrl(sUri);
        if (!localUrl.IsChat(out var urlChatId, out long entryId) || entryId <= 0)
            return;

        // NOTE: The outgoing ChatView is still alive and subscribed during a swap, so without this it
        // scrolls itself to an entry lid belonging to the chat being switched to
        if (urlChatId != Chat.Id)
            return;

        // NOTE: OnParametersSetAsync lands here on every ChatContext change, so one ?n= navigation
        // otherwise repeats (measured 4x) - re-scrolling and re-highlighting each time
        if (sUri == _lastNavigatedUri)
            return;

        _lastNavigatedUri = sUri;
        ChatSwitchTracer.Mark("ChatView.NavigateToUrlFragment: entry nav", $"entryLid={entryId}");
        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        _ = History
            .When(x => x.Url != sUri, cancellationToken)
            .ContinueWith(_ => cts.CancelAndDisposeSilently(), TaskScheduler.Default);
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
        await NavigateTo(entryId, true);
    }

    private void OnItemVisibilityChanged(VirtualListItemVisibility virtualListItemVisibility)
    {
        var identity = virtualListItemVisibility.ListIdentity;
        if (identity != Chat.Id.Value) {
            Log.LogWarning(
                $"{nameof(OnItemVisibilityChanged)} received wrong identity {{Identity}} while expecting {{ActualIdentity}}",
                identity,
                Chat.Id.Value);
            return;
        }

        var lastItemVisibility = ItemVisibility.Value;
        var itemVisibility = new ChatViewItemVisibility(virtualListItemVisibility);
        if (itemVisibility.IsIdenticalTo(lastItemVisibility)
            && !ReferenceEquals(lastItemVisibility, ChatViewItemVisibility.Empty))
            return;

        _itemVisibility.Value = itemVisibility;
        if (!WhenInitialized.IsCompletedSuccessfully)
            return;

        if (itemVisibility.IsEmpty) {
            // Retracting matters as much as publishing: consumers gate on "this chat is visible at
            // its tail", and a retained last-known value keeps that true long after the view is gone.
            ChatUI.ResetItemVisibility(Chat.Id);
            return;
        }

        ChatUI.SetItemVisibility(itemVisibility);
        var isUserPresent = ChatUI.IsUserPresent();
        if (itemVisibility.IsEndAnchorVisible) {
            _lastEndAnchorVisibleAt = CpuTimestamp.Now;
            if (isUserPresent)
                _ = UpdateReadPositionToTheLastId(Chat.Id);
        }
        else if (isUserPresent)
            UpdateReadPosition(itemVisibility.MaxEntryLid);
        if (_viewPositionLease is not null) {
            // Not gated on presence: this one restores the scroll position, so it tracks
            // the rendered viewport rather than what the user has actually read.
            var entryId = itemVisibility.MaxMessageLid;
            _viewPositionLease.Resource.Value = new ReadPosition(Chat.Id, entryId);
        }
        return;

        async Task UpdateReadPositionToTheLastId(ChatId chatId)
        {
            var chatInfo = await ChatUI.Get(chatId, DisposeToken).ConfigureAwait(true);
            var lastId = chatInfo?.News?.TextEntryLidRange.End - 1;
            if (lastId is not > 0)
                return;

            UpdateReadPosition(lastId.Value);
        }
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        // Clearing the guard here is what keeps it a per-navigation one rather than a permanent
        // "this URL was handled once" - navigating back to the same ?n= URL has to work again
        _lastNavigatedUri = "";
        _ = NavigateToUrlFragment();
    }

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
        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return;

        var entryReader = new ChatEntryReader(Chats, Session, chatId);
        var author = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var authorId = author?.Id;
        var chatIdRange = await Chats
            .GetIdRange(Session, chatId, cancellationToken)
            .ConfigureAwait(false);

        // Getting the very last chat entry
        var chatNews = await Chats.GetNews(Session, chatId, cancellationToken).ConfigureAwait(false);
        var chatLidGap = new Range<long>(chatNews?.TextEntryLidRange.End ?? 0, chatIdRange.End);
        var lastEntry = await entryReader.ReadReverse(chatLidGap, cancellationToken)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        lastEntry ??= chatNews?.LastTextEntry;
        var lastEntryLid = lastEntry?.LocalId ?? 0;

        // Observing new entries
        var entries = entryReader.Observe(chatIdRange.End, cancellationToken);
        await foreach (var entry in entries.ConfigureAwait(false)) {
            // An observed entry carries a real server-delivered lid, so the anti-synthetic-lid clamp
            // in UpdateReadPosition may trust it - without this the eager advance below is clamped
            // back to the tail GetData knew before its update delay, i.e. silently no-ops.
            _lastKnownEntryLid = Math.Max(_lastKnownEntryLid, entry.LocalId);
            if (entry.AuthorId != authorId) {
                lastEntryLid = entry.LocalId;
                // Pinned to the end = the entry is (about to be) on screen; advance immediately
                // instead of waiting for the IntersectionObserver round trip, so the raw unread
                // count never rises for the chat being watched. The !IsEmpty guard keeps a retained
                // pinned flag from marking anything read while no items are actually rendered.
                var itemVisibility = ItemVisibility.Value;
                if (itemVisibility.IsPinnedToEnd && !itemVisibility.IsEmpty && ChatUI.IsUserPresent())
                    UpdateReadPosition(lastEntryLid);
                continue;
            }

            var shownReadEntryLid = _shownReadEntryLid.Value;
            var lastEntryWasShownAsRead = lastEntryLid == shownReadEntryLid;
            lastEntryLid = entry.LocalId;
            if (lastEntryWasShownAsRead) {
                _shownReadEntryLid.Value = lastEntryLid;
                ResetNewMessagesLineState();
                UpdateReadPosition(lastEntryLid);
            }
            if (entry.IsContentStreaming || entry.HasAudio)
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
        var chatId = Chat.Id;
        var startedAt = CpuTimestamp.Now;
        var isFirstGetData = renderedData.IsNone && query.IsNone;
        if (isFirstGetData)
            ChatSwitchTracer.Mark("ChatView.GetData#1: entered", chatId);
        await WhenInitialized;
        if (isFirstGetData)
            ChatSwitchTracer.Mark("ChatView.GetData#1: WhenInitialized awaited");

        var isChatViewVisible = RegionVisibility.IsVisible;
        if (!isChatViewVisible.Value) {
            ChatSwitchTracer.Mark("ChatView.GetData: SUSPENDED - region not visible", chatId);
            // Chat is invisible now, let's suspend & await for it to become visible
            ChatUI.ResetItemVisibility(chatId);
            using (Computed.BeginIsolation())
                await isChatViewVisible.Computed.When(x => x, cancellationToken);
            _shownReadEntryLid.Value = ReadPosition.Value.EntryLid;
            ResetNewMessagesLineState();
            _itemVisibility.Value = ChatViewItemVisibility.Empty;
            // A report from the pre-suspend rendering may have landed while we awaited visibility
            ChatUI.ResetItemVisibility(chatId);
            ChatSwitchTracer.Mark("ChatView.GetData: RESUMED - region visible", chatId);
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

        var buildStartedAt = CpuTimestamp.Now;
        // ReSharper disable once ExplicitCallerInfoArgument
        using var activity = AppUIInstruments.ActivitySource.StartActivity(GetType(), "GetVirtualListData");

        activity?.SetTag("AC." + nameof(ChatId), chatId.Value);

        // Handling NavigateTo + default navigation
        var itemVisibility = ItemVisibility.Value;
        var isFirstRender = renderedData.IsNone && query.IsNone;
        var readEntryLid = GetReadEntryLid();
        var viewEntryLid = ViewPosition.Value.EntryLid;
        var hasViewEntry = viewEntryLid > 0 && viewEntryLid != long.MaxValue;
        var nav = await _nextNavigation.Use(cancellationToken)
            ?? (isFirstRender && hasViewEntry ? new ChatViewNavigation(viewEntryLid, false, false, true) : null);
        if (ReferenceEquals(nav, renderedData.NavigationState)) // Handles null case as well
            nav = null;

        var mustScrollToEntry = nav != null && ItemVisibility.Value.IsScrollRequired(nav.EntryLid);
        if (isFirstGetData)
            ChatSwitchTracer.Mark("ChatView.GetData#1: nav resolved", $"nav={nav?.EntryLid}");
        Computed<Range<long>> cChatIdRange;
        using (Computed.BeginIsolation())
            cChatIdRange = await Computed.Capture(
                () => Chats.GetIdRange(Session, chatId, cancellationToken),
                cancellationToken);
        if (isFirstGetData)
            ChatSwitchTracer.Mark("ChatView.GetData#1: GetIdRange done");
        // Rethrows via Use() to register the dependency - an isolated failure has nothing to recover on,
        // so the view would stay stale for Fusion's 30s error horizon rather than until access returns.
        if (cChatIdRange.HasError)
            await cChatIdRange.Use(cancellationToken);

        var chatIdRange = cChatIdRange.Value;
        _lastKnownEntryLid = chatIdRange.End - 1;
        var dataQuery = GetChatDataQuery(query,
            renderedData,
            nav,
            chatIdRange);
        if (dataQuery.ExistingLidRange.End + dataQuery.EndOffset + ChatUI.HalfLoadLimit >= chatIdRange.End)
            await cChatIdRange.Use(cancellationToken); // Add dependency on chatIdRange

        DebugLog?.LogDebug(
            "GetData: query keyRange={KeyRange} moveRange={MoveRange} -> dataQuery={DataQuery}, nav={Nav}",
            query.IsNone ? "None" : query.KeyRange.Format(),
            query.IsNone ? "None" : query.MoveRange.Format(),
            dataQuery.Format(),
            nav != null);

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

        if (isFirstGetData)
            ChatSwitchTracer.Mark("ChatView.GetData#1: GetChatItems -> in", dataQuery);
        var (items, hasBefore, hasAfter, isTailCoverageStale)
            = await ChatUI.GetChatItems(chatId, dataQuery, readEntryLid, cancellationToken).ConfigureAwait(false);
        if (isFirstGetData)
            ChatSwitchTracer.Mark("ChatView.GetData#1: GetChatItems <- out", $"{items.Count} items");
        // A build rendered from serve-stale meta may not un-know the end - the fresh rebuild UseIfReady
        // guarantees is what's entitled to that verdict, and it lands within a cycle. Left standing, the
        // flip costs a 1500px end spacer that an End-pinned list follows down into the skeletons and back
        // out on the next rebuild: the reported ~10Hz flicker.
        if (query.IsNone && nav == null && renderedData.HasVeryLastItem && hasAfter && isTailCoverageStale)
            hasAfter = false;
        var isWindowUnresolved = false;
        if (items.Count == 0) {
            var isEmpty = await ChatUI.IsEmpty(chatId, cancellationToken);
            if (isEmpty)
                return new VirtualListData<ChatMessage>([ChatMessage.Welcome(chatId)]) {
                    HasVeryFirstItem = true,
                    HasVeryLastItem = true,
                    ScrollToKey = null,
                    NavigationState = nav ?? renderedData.NavigationState,
                    ItemVisibilityState = ItemVisibility.Value,
                };

            // A chat that has entries but produced none is a hole, not a loaded window - most often the
            // live block's hidden tail swallowing every entry in range before its card exists.
            isWindowUnresolved = true;
            Log.LogWarning(
                "GetData: #{ChatId} produced no items for a non-empty chat, dataQuery = {DataQuery}",
                chatId, dataQuery.Format());
        }

        UpdateNewMessagesLineDebounce(items, readEntryLid);
        DebugLog?.LogDebug(
            "GetData: loaded {Count} items ({RowCount} rows), first={First}, last={Last}, hasBefore={HasBefore}, hasAfter={HasAfter}, staleTail={IsTailCoverageStale}, navKey={NavEntryLid}, mustScroll={MustScroll}",
            items.Count,
            items.Sum(i => i.GetLeafMessages().Count()),
            items.Count > 0 ? items[0].Id : 0,
            items.Count > 0 ? items[^1].Id : 0,
            hasBefore,
            hasAfter,
            isTailCoverageStale,
            nav?.EntryLid,
            mustScrollToEntry);

        if (tryUpdateShownReadEntryLid && TryUpdateShownReadEntryLid(items, ref readEntryLid)) {
            tryUpdateShownReadEntryLid = false;
            goto rebuildTiles;
        }

        if (nav == null) {
            var renderedMetadata = renderedData.Metadata as ChatViewMetadata;
            var isSummarized = chat.IsSummarized ?? false;
            var renderedIsSummarized = renderedMetadata?.IsSummarized ?? isSummarized;
            if (renderedIsSummarized != isSummarized) {
                // If we are changing the summary state, we need to navigate to a predictable position
                var currentIds = items
                    .SelectMany(it => it.GetLeafMessages())
                    .Select(it => it.Id)
                    .ToHashSet();
                var visibleIds = itemVisibility.VisibleMessageLids
                    .Where(id => id != 0 && id != long.MaxValue)
                    .ToList();
                var keptIds = visibleIds
                    .Where(id => currentIds.Contains(id))
                    .ToList();
                if (keptIds.Count == 0)
                    // No visible items are kept, so we need to scroll to the end
                    keptIds.Add(items[^1].Id);

                nav = new ChatViewNavigation(
                    keptIds.Max(),
                    false,
                    false,
                    true);
                mustScrollToEntry = true;
            }
        }

        // Locating navigation entry
        string? navKey = null;
        if (nav != null) {
            // A skip-key target can never be reached - the list skips those when it names its last item
            // and never reports them visible - so it unpins for a jump it then re-requests every render.
            var navChatMessage = items
                .SelectMany(item => item.GetLeafMessages())
                .LastOrDefault(x => x.Id <= nav.EntryLid && !x.ShouldSkipKey);
            navKey = navChatMessage?.Key.Value;
            if (navChatMessage == null)
                Log.LogWarning("GetData: entry not found in the loaded set: #{EntryLid}", nav.EntryLid);
            else if (nav.MustHighlight)
                // TODO(AK): Implement highlighting of conversations
                ChatUI.HighlightEntry(
                    ChatEntryId.New(chatId, navChatMessage.Id),
                    false);
        }
        // Determine scroll target
        var scrollToKey = navKey != null && mustScrollToEntry ? navKey : null;
        var scrollToKeyInTheMiddle = nav is { ShowInTheMiddle: true };

        // When NewMessagesLine exists, prefer scrolling to the first unread message.
        // Scan forward with GetLeafMessages() to skip replacement items (DateLine, ConversationStart)
        // that may be inserted between NewMessagesLine and the actual unread entry - those are skip-key,
        // and so is the line itself, so aiming at one leaves the list re-requesting a jump every render.
        var newMessagesLineIndex = items.FirstIndexOf(i => i.Kind == ChatMessageKind.NewMessagesLine);
        var firstUnreadKey = newMessagesLineIndex < 0
            ? null
            : items
                .Skip(newMessagesLineIndex + 1)
                .SelectMany(item => item.GetLeafMessages())
                .FirstOrDefault(message => !message.ShouldSkipKey)
                ?.Key.Value;
        if (firstUnreadKey != null) {
            if (scrollToKey == null && itemVisibility.IsEmpty) {
                // Tab resume: no explicit nav, viewport empty — scroll to first unread
                scrollToKey = firstUnreadKey;
                scrollToKeyInTheMiddle = true;
            }
            else if (isFirstRender && scrollToKey != null
                && nav is { MustHighlight: false, ShouldRestoreViewPosition: true }) {
                // Initial open: restoring view position — redirect to first unread instead.
                // Gated on isFirstRender to avoid hijacking summary-toggle navigation.
                scrollToKey = firstUnreadKey;
                scrollToKeyInTheMiddle = true;
            }
        }

        var buildMs = (long)buildStartedAt.Elapsed.TotalMilliseconds;
        if (buildMs > 1000)
            Log.LogWarning(
                "GetData: #{ChatId} took {BuildMs}ms to build ({TotalMs}ms incl. init/pacing)",
                chatId, buildMs, (long)startedAt.Elapsed.TotalMilliseconds);

        var result = new VirtualListData<ChatMessage>(items) {
            Index = renderedData.Index + 1,
            EstimatedCount = (int?)(chatIdRange.End - chatIdRange.Start),
            // An unresolved window must claim neither end, or the blank it renders becomes permanent:
            // with both ends in, the list stops querying, both spacers collapse to zero so there are no
            // skeletons left to retry from, and every position guard short-circuits on an empty item list.
            HasVeryFirstItem = !hasBefore && !isWindowUnresolved,
            HasVeryLastItem = !hasAfter && !isWindowUnresolved,
            ScrollToKey = scrollToKey,
            ScrollToKeyInTheMiddle = scrollToKeyInTheMiddle,
            NavigationState = nav ?? renderedData.NavigationState,
            ItemVisibilityState = ItemVisibility.Value,
            Metadata = new ChatViewMetadata(chat.IsSummarized ?? false),
        };

        if (isFirstGetData)
            ChatSwitchTracer.Mark("ChatView.GetData#1: exit - data built",
                $"{items.Count} items, build={buildMs}ms, scrollToKey={scrollToKey}");

        // do not return new instance if data is the same to prevent re-renders
        return !mustScrollToEntry && result.IsSimilarTo(renderedData)
            ? renderedData
            : result;
    }

    private ChatDataQuery GetChatDataQuery(
        VirtualListDataQuery query,
        VirtualListData<ChatMessage> oldData,
        ChatViewNavigation? navigation,
        Range<long> chatLidRange)
    {
        // NOTE: Changing the ranges this produces? Review ChatUI.Prefetch - it guesses this query's load zone
        // in advance, and only helps while the guess still lands on the same tiles.
        var entryTiles = ChatUI.EntryIdTiles;
        var itemVisibility = ItemVisibility.Value;
        var firstItem = oldData.FirstItem;
        var lastItem = oldData.LastItem;
        var initialLoadLimit = ChatUI.InitialLoadLimit;
        var keyRange = query.IsNone
            ? firstItem != null
                ? new Range<long>(firstItem.Id, lastItem!.Id + 1)
                : chatLidRange.EnsureNonEmpty()
            : query.KeyRange.ToLongRange(true).EnsureNonEmpty();
        var caseName = (!query.IsNone, firstItem != null) switch {
            (false, false) => "no-query+no-data",
            (false, true) when oldData.HasVeryLastItem => "no-query+has-data+hasVeryLastItem",
            (false, true) => "no-query+has-data",
            _ => "has-query",
        };
        var dataQuery = (!query.IsNone, firstItem != null) switch {
            // Align the query params with the entry tile boundaries

            // No query, no data -> initial load
            (false, false) => new ChatDataQuery(
                entryTiles.GetTile(chatLidRange.End - entryTiles.TileSize).Range,
                -initialLoadLimit / 2,
                initialLoadLimit / 2),

            // No query, there is old data, and we are at the end of the list, let's stick to the visible range if possible
            (false, true) when oldData.HasVeryLastItem
                => new ChatDataQuery(
                    new Range<long>(
                        Math.Max(firstItem!.Id, itemVisibility.MinMessageLid),
                        Math.Min(lastItem!.Id, itemVisibility.IsEmpty ? long.MaxValue : itemVisibility.MaxMessageLid)).EnsureNonEmpty(),
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
                query.MoveRange.End) {
                    // Pin the visible range so a contracting offset can't unload a visible item (e.g. a
                    // very large message at the load-zone edge), which would drop the scroll anchor.
                    VisibleLidRange = itemVisibility.IsEmpty
                        ? default
                        : new Range<long>(itemVisibility.MinMessageLid, itemVisibility.MaxMessageLid + 1),
                },
        };

        // If we are scrolling somewhere within idRange, let's extend the range to navigation & nearby entries.
        if (navigation != null && chatLidRange.Contains(navigation.EntryLid)) {
            caseName += "+navigation";
            // The anchor lands at the top of the viewport unless ShowInTheMiddle, so most of the load zone
            // is needed below it - hence the 1:2 split rather than an even one.
            dataQuery = new ChatDataQuery(
                entryTiles.GetTile(navigation.EntryLid).Range,
                -initialLoadLimit / 3,
                initialLoadLimit * 2 / 3) {
                    Navigation = navigation,
            };
        }

        DebugLog?.LogDebug(
            "GetChatDataQuery: case={Case}, result={DataQuery}, chatIdRange={IdRange}",
            caseName, dataQuery.Format(), chatLidRange.Format());
        ChatSwitchTracer.Mark($"ChatView.GetChatDataQuery: case={caseName}",
            $"#{Chat.Id} {dataQuery.Format()}");

        return dataQuery;
    }

    // Helpers

    private long GetReadEntryLid()
    {
        if (IsNewMessagesLineDebounceActive)
            return _debouncedReadEntryLid;

        // Sticky end: treat "recently at end" or "currently at end" the same way
        var isAtEnd = ItemVisibility.Value.IsEndAnchorVisible;
        var wasRecentlyAtEnd = _lastEndAnchorVisibleAt != default
            && _lastEndAnchorVisibleAt.Elapsed < NewMessagesLineDebounceTimeout;
        if (isAtEnd || wasRecentlyAtEnd) {
            _newMessagesLineShownAt = default;
            return long.MaxValue;
        }
        return ReadPosition.Value.EntryLid;
    }

    private void UpdateNewMessagesLineDebounce(IReadOnlyList<ChatMessage> items, long readEntryLid)
    {
        var hasNewMessagesLine = items.Any(i => i.Kind == ChatMessageKind.NewMessagesLine);
        if (hasNewMessagesLine && _newMessagesLineShownAt == default) {
            _newMessagesLineShownAt = CpuTimestamp.Now;
            _debouncedReadEntryLid = readEntryLid;
        } else if (!hasNewMessagesLine)
            _newMessagesLineShownAt = default;
    }

    private void ResetNewMessagesLineState()
    {
        _newMessagesLineShownAt = default;
        _lastEndAnchorVisibleAt = default;
    }

    private long UpdateReadPosition(long readEntryLid)
    {
        // Last line of defence against a synthetic lid becoming a read position: the server stores
        // read positions forward-only, so one overshoot silently swallows the unread state of every
        // real entry it covers.
        if (_lastKnownEntryLid >= 0)
            readEntryLid = Math.Min(readEntryLid, _lastKnownEntryLid);
        var readPosition = ReadPosition;
        readEntryLid = Math.Max(readPosition.Value.EntryLid, readEntryLid);
        if (readPosition.Value.EntryLid < readEntryLid)
            ((MutableState<ReadPosition>)readPosition).Value = new ReadPosition(Chat.Id, readEntryLid);
        return readEntryLid;
    }

    private bool TryUpdateShownReadEntryLid(IReadOnlyList<ChatMessage> items, ref long readEntryLid)
    {
        var itemVisibility = ItemVisibility.Value;
        if (items.Count == 0)
            return false; // Not loaded yet or wrong load range

        if (!ChatUI.IsUserPresent()) {
            DebugLog?.LogDebug("TryUpdateShownReadEntryLid: the user isn't at the screen");
            return false;
        }

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
            .FirstOrDefault(i => i.Kind == ChatMessageKind.NewMessagesLine);
        var hasNewMessagesLine = newMessagesLine != null;
        if (!hasNewMessagesLine) {
            DebugLog?.LogDebug("TryUpdateShownReadEntryLid: no new messages line");
            return false; // No new messages line
        }

        if (IsNewMessagesLineDebounceActive)
            return false; // Don't remove NewMessagesLine during debounce period

        // We see end anchor, when the new message appears so we can update shownReadEntryLid
        var lastEntryLid = items.LastOrDefault(i => !i.Kind.IsPlaceholder())?.Id ?? -1;
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
        var chatId = Chat.Id;
        var entryReader = new ChatEntryReader(Chats, Session, chatId);
        var chatLidRange = await Chats.GetIdRange(Session, chatId, cancellationToken).ConfigureAwait(false);
        var range = new Range<long>(minEntryLid, minEntryLid + (20 * ChatUI.EntryIdTiles.TileSize))
            .IntersectWith(chatLidRange);
        return await entryReader.GetFirst(range, cancellationToken).ConfigureAwait(false);
    }

    private void UpdateGroupChatUsageList()
    {
        var chatId = Chat.Id;
        if (chatId.Kind == ChatKind.Peer)
            return;

        _ = BackgroundTask.Run(async () => {
                var chat = await Chats.Get(Session, chatId, DisposeToken);
                if (chat == null)
                    return;

                var command = new ChatUsages_RegisterUsage {
                    Session = Session,
                    Kind = ChatUsageListKind.ViewedGroupChats,
                    ChatId = chatId,
                };
                await Commander.Call(command, DisposeToken);
            },
            ex => Log.LogDebug(ex, "Failed to register view group chat"),
            DisposeToken);
    }

    // Nested types

    private record ChatViewMetadata(bool IsSummarized);
}
