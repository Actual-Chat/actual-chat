using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Components;

public partial class ChatView : ComponentBase, IAsyncDisposable
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;
    private readonly CancellationTokenSource _disposeToken = new ();
    private readonly TaskSource<Unit> _initializeTaskSource = TaskSource.New<Unit>(true);

    private long _lastNavigateToEntryId;

    [Inject] private ILogger<ChatView> Log { get; init; } = null!;
    [Inject] private Session Session { get; init; } = null!;
    [Inject] private IStateFactory StateFactory { get; init; } = null!;
    [Inject] private ChatPlayers ChatPlayers { get; init; } = null!;
    [Inject] private IChats Chats { get; init; } = null!;
    [Inject] private IChatAuthors ChatAuthors { get; init; } = null!;
    [Inject] private IChatReadPositions ChatReadPositions { get; init; } = null!;
    [Inject] private UICommandRunner Cmd { get; init; } = null!;
    [Inject] private IAuth Auth { get; init; } = null!;
    [Inject] private NavigationManager Nav { get; init; } = null!;
    [Inject] private MomentClockSet Clocks { get; init; } = null!;
    [Inject] private ChatPageState ChatPageState { get; init; } = null!;

    [CascadingParameter]
    public Chat Chat { get; set; } = null!;

    [Parameter] public IMutableState<int>? UnreadEntryCountState { get; set; }

    private bool InitCompleted => _initializeTaskSource.Task.IsCompleted;

    private IMutableState<long> NavigateToEntryIdState { get; set; } = null!;
    private IMutableState<long> LastReadEntryIdState { get; set; } = null!;
    private IMutableState<List<string>> VisibleKeysState { get; set; } = null!;

    public ValueTask DisposeAsync()
    {
        _disposeToken.Cancel();
        return ValueTask.CompletedTask;
    }

    public async Task NavigateToLastUnreadTopic()
    {
        var lastReadEntryId = await LastReadEntryIdState.Use(_disposeToken.Token);
        // reset to ensure navigation will happen
        _lastNavigateToEntryId = 0;
        NavigateToEntryIdState.Value = lastReadEntryId;
        NavigateToEntryIdState.Invalidate();
    }

    protected override async Task OnInitializedAsync()
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (VisibleKeysState == null)
            try {
                UnreadEntryCountState ??= StateFactory.NewMutable(0);
                NavigateToEntryIdState = StateFactory.NewMutable(0L);
                VisibleKeysState = StateFactory.NewMutable(new List<string>());
                _ = BackgroundTask.Run(() => MonitorVisibleKeyChanges(_disposeToken.Token), _disposeToken.Token);
                _ = BackgroundTask.Run(() => MonitorNewEntries(_disposeToken.Token), _disposeToken.Token);

                var readPosition = await ChatReadPositions.GetReadPosition(Session, Chat.Id, _disposeToken.Token)
                    .ConfigureAwait(true);
                LastReadEntryIdState = StateFactory.NewMutable(readPosition ?? 0L);
                if (readPosition.HasValue) {
                    var idRange = await Chats.GetIdRange(Session, Chat.Id, ChatEntryType.Text, _disposeToken.Token).ConfigureAwait(true);
                    UnreadEntryCountState.Value = Math.Max((int)(idRange.End - readPosition.Value - 1), 0);
                }
            }
            finally {
                _initializeTaskSource.SetResult(Unit.Default);
            }
    }

    protected override bool ShouldRender()
        => InitCompleted;

    private async Task MonitorVisibleKeyChanges(CancellationToken cancellationToken)
    {
        var clock = Clocks.CoarseCpuClock;
        while (!cancellationToken.IsCancellationRequested)
            try {
                await VisibleKeysState.Computed.WhenInvalidated(cancellationToken).ConfigureAwait(true);
                await clock.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(true);
                var visibleKeys = await VisibleKeysState.Use(cancellationToken).ConfigureAwait(true);
                if (visibleKeys.Count == 0)
                    continue;

                var lastVisibleEntryId = visibleKeys
                    .Select(key =>
                        long.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryId)
                            ? (long?)entryId
                            : null)
                    .Where(entryId => entryId.HasValue)
                    .Select(entryId => entryId!.Value)
                    .Max();
                if (LastReadEntryIdState.Value >= lastVisibleEntryId)
                    continue;

                var prevLastReadEntryId = LastReadEntryIdState.Value;
                LastReadEntryIdState.Value = lastVisibleEntryId;

                var diff = lastVisibleEntryId - prevLastReadEntryId;
                UnreadEntryCountState!.Value = Math.Max((int)(UnreadEntryCountState!.Value - diff), 0);

                var command = new IChatReadPositions.UpdateReadPositionCommand(Session, Chat.Id, lastVisibleEntryId);
                await Cmd.Run(command, cancellationToken).ConfigureAwait(true);
            }
            catch (Exception ex) {
                Log.LogWarning(ex,
                    "Error monitoring visible key changes, LastVisibleEntryId = {LastVisibleEntryId}",
                    LastReadEntryIdState.Value);
            }
    }

    private async Task MonitorNewEntries(CancellationToken cancellationToken)
    {
        var chatId = Chat.Id.Value;
        var chatReader = new ChatEntryReader(Chats, Session, chatId, ChatEntryType.Text);
        var chatIdRange = await Chats.GetIdRange(Session, chatId, ChatEntryType.Text, cancellationToken)
            .ConfigureAwait(true);
        var newEntries = chatReader.ReadAllWaitingForNew(chatIdRange.End, cancellationToken);
        var currentAuthor = await ChatAuthors.GetChatAuthor(Session, Chat.Id, cancellationToken).ConfigureAwait(true);
        await foreach (var entry in newEntries.ConfigureAwait(true))
            if (entry.AuthorId == currentAuthor?.Id) {
                // TODO(AK): scroll to the last entry
                LastReadEntryIdState.Value = entry.Id;
                UnreadEntryCountState!.Value = 0;
            }
            else
                UnreadEntryCountState!.Value++;
    }

    private async Task<VirtualListData<ChatMessageModel>> GetMessages(
        VirtualListDataQuery query,
        CancellationToken cancellationToken)
    {
        if (!_initializeTaskSource.Task.IsCompleted)
            await _initializeTaskSource.Task.ConfigureAwait(true);

        var chat = Chat;
        var chatId = chat.Id;
        var chatIdRange = await Chats.GetIdRange(Session, chatId.Value, ChatEntryType.Text, cancellationToken)
            .ConfigureAwait(true);
        // do not add as dependency
        var entryId = LastReadEntryIdState.Value;
        var mustScrollToEntry = query.IsNone && entryId != 0;
        var navigateToEntryId = await NavigateToEntryIdState.Use(cancellationToken).ConfigureAwait(true);
        if (navigateToEntryId != _lastNavigateToEntryId) {
            _lastNavigateToEntryId = navigateToEntryId;
            entryId = navigateToEntryId;
            mustScrollToEntry = true;
        }
        else if (query.ScrollToKey != null) {
            entryId = long.Parse(query.ScrollToKey, NumberStyles.Number, CultureInfo.InvariantCulture);
            mustScrollToEntry = true;
        }
        var queryRange = mustScrollToEntry
            ? IdTileStack.Layers[0].GetTile(entryId).Range.Expand(IdTileStack.Layers[1].TileSize)
            : query.IsNone
                ? new Range<long>(
                    chatIdRange.End - (2 * IdTileStack.Layers[1].TileSize),
                    chatIdRange.End - 1)
                : query.InclusiveRange.AsLongRange()
                    .Expand(new Range<long>((long)query.ExpandStartBy, (long)query.ExpandEndBy));

        var startId = Math.Clamp(queryRange.Start, chatIdRange.Start, chatIdRange.End);
        var endId = Math.Clamp(queryRange.End, chatIdRange.Start, chatIdRange.End);
        var adjustedRange = new Range<long>(startId, endId);

        var idTiles = IdTileStack.GetOptimalCoveringTiles(adjustedRange);
        var chatTiles = await Task
            .WhenAll(idTiles.Select(
                idTile => Chats.GetTile(Session,
                    chatId.Value,
                    ChatEntryType.Text,
                    idTile.Range,
                    cancellationToken)))
            .ConfigureAwait(false);

        var chatEntries = chatTiles
            .SelectMany(chatTile => chatTile.Entries)
            .Where(e => e.Type == ChatEntryType.Text)
            .ToList();

        // AY: Uncomment if you see any issues w/ duplicate entries
        /*
        var duplicateEntries = (
            from e in chatEntries
            let count = chatEntries.Count(e1 => e1.Id == e.Id)
            where count > 1
            select e
            ).ToList();
        if (duplicateEntries.Count > 0) {
            Log.LogCritical("Duplicate entries in Chat #{ChatId}:", chatId);
            foreach (var e in duplicateEntries)
                Log.LogCritical(
                    "- Entry w/ Id = {Id}, Version = {Version}, Type = {Type}, '{Content}'",
                    e.Id, e.Version, e.Type, e.Content);
            chatEntries = chatEntries.DistinctBy(e => e.Id).ToList();
        }
        */

        var attachmentEntryIds = chatEntries
            .Where(c => c.HasAttachments)
            .Select(c => c.Id)
            .ToList();

        var attachmentTasks = await Task
            .WhenAll(attachmentEntryIds.Select(id
                => Chats.GetTextEntryAttachments(Session, chatId, id, cancellationToken)))
            .ConfigureAwait(false);
        var attachments = attachmentTasks
            .Where(c => c.Length > 0)
            .ToDictionary(c => c[0].EntryId, c => c);

        var chatMessages = ChatMessageModel.FromEntries(chatEntries, attachments, LastReadEntryIdState.Value);
        var scrollToKey = mustScrollToEntry
            ? entryId.ToString(CultureInfo.InvariantCulture)
            : null;
        var result = VirtualListData.New(
            query.ScrollToKey == scrollToKey
                ? query
                : new VirtualListDataQuery(adjustedRange.AsStringRange()) { ScrollToKey = scrollToKey },
            chatMessages,
            startId <= chatIdRange.Start,
            endId + 1 >= chatIdRange.End,
            scrollToKey);

        return result;
    }
}
