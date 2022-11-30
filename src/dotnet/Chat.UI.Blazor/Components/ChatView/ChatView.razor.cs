using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using Google.Api.Gax;

namespace ActualChat.Chat.UI.Blazor.Components;

public partial class ChatView : ComponentBase, IVirtualListDataSource<ChatMessageModel>, IDisposable
{
    private const int PageSize = 40;

    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;
    private readonly CancellationTokenSource _disposeToken = new ();
    private readonly TaskSource<Unit> _whenInitializedSource = TaskSource.New<Unit>(true);

    private long? _lastNavigateToEntryId;
    private long? _initialLastReadEntryId;
    private HashSet<long> _fullyVisibleEntryIds = new ();

    [Inject] private ILogger<ChatView> Log { get; init; } = null!;
    [Inject] private Session Session { get; init; } = null!;
    [Inject] private IStateFactory StateFactory { get; init; } = null!;
    [Inject] private ChatUI ChatUI { get; init; } = null!;
    [Inject] private ChatPlayers ChatPlayers { get; init; } = null!;
    [Inject] private IChats Chats { get; init; } = null!;
    [Inject] private IAuthors Authors { get; init; } = null!;
    [Inject] private IReadPositions ReadPositions { get; init; } = null!;
    [Inject] private NavigationManager Nav { get; init; } = null!;
    [Inject] private TimeZoneConverter TimeZoneConverter { get; init; } = null!;
    [Inject] private MomentClockSet Clocks { get; init; } = null!;
    [Inject] private UICommander UICommander { get; init; } = null!;

    private Task WhenInitialized => _whenInitializedSource.Task;
    private IMutableState<long?> NavigateToEntryId { get; set; } = null!;
    private IMutableState<List<string>> VisibleKeys { get; set; } = null!;
    private SyncedStateLease<long?>? LastReadEntryState { get; set; } = null!;

    [CascadingParameter] public Chat Chat { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        Log.LogDebug("Created for chat #{ChatId}", Chat.Id);
        Nav.LocationChanged += OnLocationChanged;
        try {
            NavigateToEntryId = StateFactory.NewMutable<long?>();
            VisibleKeys = StateFactory.NewMutable(new List<string>());
            _ = BackgroundTask.Run(() => MonitorVisibleKeyChanges(_disposeToken.Token), _disposeToken.Token);

            LastReadEntryState = await ChatUI.LeaseReadState(Chat.Id, _disposeToken.Token);
            _initialLastReadEntryId = LastReadEntryState.Value;
        }
        finally {
            _whenInitializedSource.SetResult(Unit.Default);
        }
    }

    public void Dispose()
    {
        Nav.LocationChanged -= OnLocationChanged;
        _disposeToken.Cancel();
        LastReadEntryState?.Dispose();
        LastReadEntryState = null;
    }

    protected override async Task OnParametersSetAsync()
    {
        await WhenInitialized;
        TryNavigateToEntry();
    }

    public async Task NavigateToUnreadEntry()
    {
        long navigateToEntryId;
        var lastReadEntryId = LastReadEntryState?.Value;
        if (lastReadEntryId is { } entryId)
            navigateToEntryId = entryId;
        else {
            var chatIdRange = await Chats.GetIdRange(Session, Chat.Id, ChatEntryKind.Text, _disposeToken.Token);
            navigateToEntryId = chatIdRange.ToInclusive().End;
        }

        // Reset to ensure the navigation will happen
        _initialLastReadEntryId = navigateToEntryId;
        NavigateToEntry(navigateToEntryId);
    }

    public void NavigateToEntry(long localId)
    {
        // reset to ensure navigation will happen
        _lastNavigateToEntryId = null;
        NavigateToEntryId.Value = null;
        NavigateToEntryId.Value = localId;
    }

    public void TryNavigateToEntry()
    {
        // ignore location changed events if already disposed
        if (_disposeToken.IsCancellationRequested)
            return;

        var entryIdString = Nav.Uri.ToUri().Fragment.TrimStart('#');
        if (long.TryParse(entryIdString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryId) && entryId > 0)
            NavigateToEntry(entryId);
    }

    // Private methods

    async Task<VirtualListData<ChatMessageModel>> IVirtualListDataSource<ChatMessageModel>.GetData(
        VirtualListDataQuery query,
        VirtualListData<ChatMessageModel> oldData,
        CancellationToken cancellationToken)
    {
        await WhenInitialized;

        var chat = Chat;
        var chatId = chat.Id;
        var author = await Authors.GetOwn(Session, chatId, cancellationToken);
        var authorId = author?.Id ?? Symbol.Empty;
        var chatIdRange = await Chats.GetIdRange(Session, chatId, ChatEntryKind.Text, cancellationToken);
        var lastReadEntryId = LastReadEntryState?.Value ?? 0;
        if (LastReadEntryState != null && lastReadEntryId >= chatIdRange.End) {
            // looks like an error, let's reset last read position to the las entry id
            lastReadEntryId = chatIdRange.End - 1;
            LastReadEntryState.Value = lastReadEntryId;
        }
        var entryId = lastReadEntryId;
        var mustScrollToEntry = query.IsNone && entryId != 0;

        // get latest tile to check whether the Author has submitted new entry
        var lastIdTile = IdTileStack.Layers[0].GetTile(chatIdRange.ToInclusive().End);
        var lastTile = await Chats.GetTile(Session,
            chatId,
            ChatEntryKind.Text,
            lastIdTile.Range,
            cancellationToken);
        foreach (var entry in lastTile.Entries) {
            if (entry.AuthorId != authorId || entry.LocalId <= _initialLastReadEntryId)
                continue;

            // scroll only on text entries
            if (entry.IsStreaming || entry.AudioEntryId != null)
                continue;

            // scroll to the latest Author entry - e.g.m when author submits the new one
            _initialLastReadEntryId = entry.LocalId;
            entryId = entry.LocalId;
            mustScrollToEntry = true;
        }

        var isHighlighted = false;
        // handle NavigateToEntry
        var navigateToEntryId = await NavigateToEntryId.Use(cancellationToken);
        if (!mustScrollToEntry)
            if (navigateToEntryId.HasValue && navigateToEntryId != _lastNavigateToEntryId) {
                isHighlighted = true;
                _lastNavigateToEntryId = navigateToEntryId;
                entryId = navigateToEntryId.Value;
                if (!_fullyVisibleEntryIds.Contains(navigateToEntryId.Value))
                    mustScrollToEntry = true;
            }
        var scrollToKey = mustScrollToEntry
            ? entryId.ToString(CultureInfo.InvariantCulture)
            : null;

        // if we are scrolling somewhere - let's load date near the entryId
        var queryRange = mustScrollToEntry
            ? new Range<long>(
                entryId - PageSize,
                entryId + PageSize)
            : query.IsNone
                ? new Range<long>(
                    chatIdRange.End - (2*PageSize),
                    chatIdRange.End)
                : query.KeyRange
                    .AsLongRange()
                    .Expand(new Range<long>((long)query.ExpandStartBy, (long)query.ExpandEndBy));

        var adjustedRange = queryRange.Clamp(chatIdRange);
        // extend requested range if close to chat Id range
        var closeToTheEnd = adjustedRange.End >= chatIdRange.End - (PageSize / 2);
        var closeToTheStart = adjustedRange.Start <= chatIdRange.Start + (PageSize / 2);
        var extendedRange = (closeToTheStart, closeToTheEnd) switch {
            (true, true) => chatIdRange.Expand(1), // extend to mitigate outdated id range
            (_, true) => new Range<long>(adjustedRange.Start, chatIdRange.End).Expand(1),
            (true, _) => new Range<long>(chatIdRange.Start, adjustedRange.End).Expand(1),
            _ => adjustedRange,
        };

        var hasVeryFirstItem = extendedRange.Start <= chatIdRange.Start;
        var hasVeryLastItem = extendedRange.End + 1 >= chatIdRange.End;
        // var oldRange =  oldData.Query.IsNone
        //     ? new Range<long>(0,0)
        //     : oldData.Query.KeyRange
        //         .AsLongRange()
        //         .Expand(new Range<long>((long)oldData.Query.ExpandStartBy, (long)oldData.Query.ExpandEndBy));

        // if (oldRange.Contains(extendedRange)
        //     && oldRange.Size() - extendedRange.Size() < PageSize / 2
        //     && (scrollToKey == null || scrollToKey == oldData.ScrollToKey)
        //     && hasVeryFirstItem == oldData.HasVeryFirstItem
        //     && hasVeryLastItem == oldData.HasVeryLastItem)
        //     return oldData;

        var idTiles = IdTileStack.GetOptimalCoveringTiles(extendedRange);
        var chatTiles = await idTiles
            .Select(idTile => Chats.GetTile(Session, chatId, ChatEntryKind.Text, idTile.Range, cancellationToken))
            .Collect();

        var chatEntries = chatTiles
            .SelectMany(chatTile => chatTile.Entries)
            .Where(e => e.Kind == ChatEntryKind.Text)
            .ToList();

        var chatMessages = ChatMessageModel.FromEntries(
            chatEntries,
            oldData.Items,
            _initialLastReadEntryId,
            hasVeryFirstItem,
            hasVeryLastItem,
            TimeZoneConverter);

        var result = VirtualListData.New(
            new VirtualListDataQuery(extendedRange.AsStringRange()),
            chatMessages,
            hasVeryFirstItem,
            hasVeryLastItem,
            scrollToKey);

        if (isHighlighted)
            // highlight entry when it has already been loaded
            ChatUI.HighlightedChatEntryId.Value = new ChatEntryId(chatId, ChatEntryKind.Text, entryId, AssumeValid.Option);

        return result;
    }

    private async Task MonitorVisibleKeyChanges(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
            try {
                await VisibleKeys.Computed.WhenInvalidated(cancellationToken);
                var visibleKeys = await VisibleKeys.Use(cancellationToken);
                if (visibleKeys.Count == 0) {
                    ChatUI.VisibleIdRange.Value = new(0, 0);
                    continue;
                }

                var visibleEntryIds = visibleKeys
                    .Select(key =>
                        long.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryId)
                            ? (long?)entryId
                            : null)
                    .Where(entryId => entryId.HasValue)
                    .Select(entryId => entryId!.Value)
                    .ToHashSet();
                ChatUI.VisibleIdRange.Value = new (visibleEntryIds.Min(), visibleEntryIds.Max());

                var maxVisibleEntryId = visibleEntryIds.Max();
                var minVisibleEntryId = visibleEntryIds.Min();
                visibleEntryIds.Remove(maxVisibleEntryId);
                visibleEntryIds.Remove(minVisibleEntryId);
                await InvokeAsync(() => { _fullyVisibleEntryIds = visibleEntryIds; });

                if (LastReadEntryState?.Value >= maxVisibleEntryId)
                    continue;

                if (LastReadEntryState != null)
                    LastReadEntryState.Value = maxVisibleEntryId;
            }
            catch (Exception e) when(e is not OperationCanceledException) {
                Log.LogWarning(e,
                    "Error monitoring visible key changes, LastReadEntryId = {LastReadEntryId}",
                    LastReadEntryState?.Value);
            }
    }

    // Event handlers

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
        => TryNavigateToEntry();

    private Task OnNavigateToChatEntry(NavigateToChatEntryEvent @event, CancellationToken cancellationToken)
    {
        if (@event.ChatEntryId.ChatId == Chat.Id)
            NavigateToEntry(@event.ChatEntryId.LocalId);
        return Task.CompletedTask;
    }
}
