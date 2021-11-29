using ActualChat.Chat.UI.Blazor.Services;
using Microsoft.AspNetCore.Components;
using Stl.Fusion.Blazor;

namespace ActualChat.Chat.UI.Blazor.Components;

public partial class ChatView : ComponentBase, IAsyncDisposable
{
    private static readonly TileStack<long> IdTileStack = ChatConstants.IdTileStack;

    [Inject] private Session Session { get; set; } = default!;
    [Inject] private ChatMediaPlayers ChatMediaPlayers { get; set; } = default!;
    [Inject] private IChats Chats { get; set; } = default!;
    [Inject] private IChatAuthors ChatAuthors { get; set; } = default!;
    [Inject] private IAuth Auth { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private MomentClockSet Clocks { get; set; } = default!;
    [Inject] private ILogger<ChatView> Log { get; set; } = default!;
    private ChatMediaPlayer? RealtimePlayer { get; set; }

    [Parameter, EditorRequired, ParameterComparer(typeof(ByReferenceParameterComparer))]
    public Chat Chat { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        RealtimePlayer = await ChatMediaPlayers.GetRealtimePlayer(Chat.Id);
        _ = BackgroundTask.Run(
            () => RealtimePlayer.Play(),
            Log, "Realtime playback failed");
    }

    public ValueTask DisposeAsync()
        => ChatMediaPlayers.DisposePlayers(Chat.Id);

    public override Task SetParametersAsync(ParameterView parameters)
        => this.HasChangedParameters(parameters) ? base.SetParametersAsync(parameters) : Task.CompletedTask;

    private async Task<VirtualListData<ChatMessageModel>> GetMessages(
        VirtualListDataQuery query,
        CancellationToken cancellationToken)
    {
        var chat = Chat;
        var chatId = chat.Id;
        var chatIdRange = await Chats.GetIdRange(Session, chatId.Value, cancellationToken);
        if (query.InclusiveRange == default)
            query = query with {
                InclusiveRange = new(
                    (chatIdRange.End - IdTileStack.MinTileSize).ToString(CultureInfo.InvariantCulture),
                    chatIdRange.End.ToString(CultureInfo.InvariantCulture)),
            };

        var startId = long.Parse(query.InclusiveRange.Start, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (query.ExpandStartBy > 0)
            startId -= (long)query.ExpandStartBy;
        startId = Math.Clamp(startId, chatIdRange.Start, chatIdRange.End);

        var endId = long.Parse(query.InclusiveRange.End, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (query.ExpandEndBy > 0)
            endId += (long)query.ExpandEndBy;
        endId = Math.Clamp(endId, chatIdRange.Start, chatIdRange.End);

        var idTiles = IdTileStack.GetOptimalCoveringTiles((startId, endId + 1));
        var chatTiles = await Task
            .WhenAll(idTiles.Select(idTile => Chats.GetTile(Session, chatId.Value, idTile.Range, cancellationToken)))
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

        var authorIds = chatEntries.Select(e => e.AuthorId).Distinct();
        var authorTasks = await Task
            .WhenAll(authorIds.Select(id => ChatAuthors.GetAuthor(chatId, id, true, cancellationToken)))
            .ConfigureAwait(false);
        var authors = authorTasks
            .Where(a => a != null)
            .ToDictionary(a => a!.Id);

        var chatMessageModels = chatEntries
            .Select(e => new ChatMessageModel(e, authors[e.AuthorId]!));
        var result = VirtualListData.New(
            chatMessageModels,
            m => m.Entry.Id.ToString(CultureInfo.InvariantCulture),
            startId == chatIdRange.Start,
            endId == chatIdRange.End);
        return result;
    }
}
