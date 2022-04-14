using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Users;
using Microsoft.AspNetCore.Components;
using Stl.Fusion.Blazor;

namespace ActualChat.Chat.UI.Blazor.Components;

public partial class ChatView : ComponentBase, IAsyncDisposable
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;
    private readonly CancellationTokenSource _disposeToken = new ();
    private readonly TaskSource<Unit> _initializeTaskSource = TaskSource.New<Unit>(false);

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

    [CascadingParameter] public Chat Chat { get; set; } = null!;

    private bool InitCompleted => _initializeTaskSource.Task.IsCompleted;
    private long LastReadEntryId { get; set; }
    private IMutableState<List<string>> VisibleKeysState { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (VisibleKeysState == null) {
            VisibleKeysState = StateFactory.NewMutable(new List<string>());
            _ = BackgroundTask.Run(() => MonitorVisibleKeyChanges(_disposeToken.Token));

            var readPosition = await ChatReadPositions.GetReadPosition(Session, Chat.Id, _disposeToken.Token)
                .ConfigureAwait(true);
            if (readPosition.HasValue && LastReadEntryId == 0)
                LastReadEntryId = readPosition.Value;
            _initializeTaskSource.SetResult(Unit.Default);
        }
    }

    protected override bool ShouldRender()
        => InitCompleted;

    public async ValueTask DisposeAsync()
        => _disposeToken.Cancel();

    private async Task MonitorVisibleKeyChanges(CancellationToken cancellationToken)
    {
        var clock = Clocks.CoarseCpuClock;
        while (!cancellationToken.IsCancellationRequested) {
            await clock.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(true);
            await VisibleKeysState.Computed.WhenInvalidated(cancellationToken).ConfigureAwait(true);
            var visibleKeys = await VisibleKeysState.Use(cancellationToken).ConfigureAwait(true);
            if (visibleKeys.Count == 0)
                continue;

            var lastVisibleEntryId =  visibleKeys
                .Select(key => long.TryParse(key, out var entryId) ? (long?)entryId : null)
                .Where(entryId => entryId.HasValue)
                .Select(entryId => entryId!.Value)
                .Max();
            if (LastReadEntryId >= lastVisibleEntryId)
                continue;

            LastReadEntryId = lastVisibleEntryId;
            var command = new IChatReadPositions.UpdateReadPositionCommand(Session, Chat.Id, LastReadEntryId);
            await Cmd.Run(command, cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task<VirtualListData<ChatMessageModel>> GetMessages(
        VirtualListDataQuery query,
        CancellationToken cancellationToken)
    {
        if (!_initializeTaskSource.Task.IsCompleted) {
            await _initializeTaskSource.Task.ConfigureAwait(true);
            // first compute state is useless it will be called again after completion
            // of parent (this) initialization
            return VirtualListData<ChatMessageModel>.None;
        }

        var chat = Chat;
        var chatId = chat.Id;
        var chatIdRange = await Chats.GetIdRange(Session, chatId.Value, ChatEntryType.Text, cancellationToken);
        var isFirstLoad = query.InclusiveRange.Start.IsNullOrEmpty();
        var mustScrollToLastReadPosition = isFirstLoad && LastReadEntryId != 0;
        var queryRange = mustScrollToLastReadPosition
            ? IdTileStack.Layers[0].GetTile(LastReadEntryId).Range.AsStringRange()
            : query.IsExpansionQuery
                ? query.InclusiveRange
                : new Range<long>(
                    chatIdRange.End - IdTileStack.Layers[1].TileSize,
                    chatIdRange.End - 1).AsStringRange();

        var startId = long.Parse(ExtractRealId(queryRange.Start), NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (query.ExpandStartBy > 0)
            startId -= (long)query.ExpandStartBy;
        else if (loadLastReadPosition)
            startId -= IdTileStack.Layers[1].TileSize;
        startId = Math.Clamp(startId, chatIdRange.Start, chatIdRange.End);

        var endId = long.Parse(ExtractRealId(queryRange.End), NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (query.ExpandEndBy > 0)
            endId += (long)query.ExpandEndBy;
        else if (loadLastReadPosition)
            endId += IdTileStack.Layers[1].TileSize;
        endId = Math.Clamp(endId, chatIdRange.Start, chatIdRange.End);

        var idTiles = IdTileStack.GetOptimalCoveringTiles((startId, endId + 1));
        var chatTiles = await Task
            .WhenAll(idTiles.Select(
                idTile => Chats.GetTile(Session, chatId.Value, ChatEntryType.Text, idTile.Range, cancellationToken)))
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
            .WhenAll(attachmentEntryIds.Select(id => Chats.GetTextEntryAttachments(Session, chatId, id, cancellationToken)))
            .ConfigureAwait(false);
        var attachments = attachmentTasks
            .Where(c => c.Length > 0)
            .ToDictionary(c => c[0].EntryId, c => c);

        var chatMessages = ChatMessageModel.FromEntries(chatEntries, attachments, LastReadEntryId);
        var scrollToKey = mustScrollToLastReadPosition
            ? (Symbol)LastReadEntryId.ToString(CultureInfo.InvariantCulture)
            : Symbol.Empty;
        var result = VirtualListData.New(
            query,
            chatMessages,
            startId <= chatIdRange.Start,
            endId + 1 >= chatIdRange.End,
            scrollToKey);

        return result;
    }

    private string ExtractRealId(string id)
    {
        var separatorIndex = id.IndexOf(';');
        if (separatorIndex >= 0)
            id = id.Substring(0, separatorIndex);
        return id;
    }
}
