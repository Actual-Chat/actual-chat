using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Users;
using Microsoft.AspNetCore.Components;
using Stl.Fusion.Blazor;

namespace ActualChat.Chat.UI.Blazor.Components;

public partial class ChatView : ComponentBase, IAsyncDisposable
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;
    private readonly CancellationTokenSource _disposeToken = new ();

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

    [CascadingParameter] public Chat Chat { get; set; } = null!;

    public long LastReadEntryId { get; private set; }
    private IMutableState<ImmutableList<string>> VisibleKeysState { get; set; } = null!;

    protected override async Task OnParametersSetAsync()
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (VisibleKeysState == null) {
            VisibleKeysState = StateFactory.NewMutable(ImmutableList<string>.Empty);
            _ = BackgroundTask.Run(() => MonitorVisibleKeyChanges(_disposeToken.Token));

            var readPosition = await ChatReadPositions.GetReadPosition(Session, Chat.Id, _disposeToken.Token)
                .ConfigureAwait(true);
            if (readPosition.HasValue && LastReadEntryId == 0)
                LastReadEntryId = readPosition.Value;
        }
    }

    public async ValueTask DisposeAsync()
        => _disposeToken.Cancel();

    private async Task MonitorVisibleKeyChanges(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
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
        var chat = Chat;
        var chatId = chat.Id;
        var chatIdRange = await Chats.GetIdRange(Session, chatId.Value, ChatEntryType.Text, cancellationToken);
        var loadLastReadPosition = query.InclusiveRange.IsEmpty && LastReadEntryId != 0;
        var queryRange = loadLastReadPosition
            ? IdTileStack.Layers[0].GetTile(LastReadEntryId).Range.AsStringRange()
            : query.IsExpansionQuery
                ? query.InclusiveRange
                : new Range<long>(
                    chatIdRange.End - IdTileStack.Layers[1].TileSize,
                    chatIdRange.End - 1).AsStringRange();

        var startId = long.Parse(ExtractRealId(queryRange.Start), NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (query.ExpandStartBy > 0)
            startId -= (long)query.ExpandStartBy;
        startId = Math.Clamp(startId, chatIdRange.Start, chatIdRange.End);

        var endId = long.Parse(ExtractRealId(queryRange.End), NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (query.ExpandEndBy > 0)
            endId += (long)query.ExpandEndBy;
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

        var chatMessages = ChatMessageModel.FromEntries(chatEntries, attachments);
        var scrollToKey = loadLastReadPosition
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
