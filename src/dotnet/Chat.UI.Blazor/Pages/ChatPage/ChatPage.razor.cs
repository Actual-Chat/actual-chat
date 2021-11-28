using ActualChat.Chat.UI.Blazor.Components;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Stl.Fusion.Blazor;

namespace ActualChat.Chat.UI.Blazor.Pages;

public partial class ChatPage : ComputedStateComponent<ChatPageModel>
{
    private static readonly TileStack<long> IdTileStack = ChatConstants.IdTileStack;

    [Parameter] public string ChatId { get; set; } = "";
    [Inject] private Session Session { get; set; } = default!;
    [Inject] private ChatPageService Service { get; set; } = default!;
    [Inject] private IChats Chats { get; set; } = default!;
    [Inject] private IChatAuthors ChatAuthors { get; set; } = default!;
    [Inject] private IAuth Auth { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private MomentClockSet Clocks { get; set; } = default!;
    [Inject] private ILogger<ChatPage> Log { get; set; } = default!;

    private ChatMediaPlayer? HistoricalPlayer { get; set; }
    private ChatMediaPlayer? RealtimePlayer { get; set; }

    public override async ValueTask DisposeAsync()
    {
        RealtimePlayer?.Dispose();
        HistoricalPlayer?.Dispose();
        await base.DisposeAsync().ConfigureAwait(true);
        GC.SuppressFinalize(this);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (ChatId.IsNullOrEmpty()) {
            Nav.NavigateTo($"/chat/{ChatConstants.DefaultChatId}");
            return;
        }

        await State.Recompute(); // ~ The same happens in base method, so we don't call it
        var chat = State.ValueOrDefault?.Chat;
        if (chat == null || HistoricalPlayer?.ChatId == chat.Id)
            return;

        // ChatId changed, so we must recreate players
        HistoricalPlayer?.Dispose();
        HistoricalPlayer = new(Services) {
            Session = Session,
            ChatId = chat.Id,
        };
        var wasPlaying = RealtimePlayer?.IsPlaying ?? false;
        RealtimePlayer?.Dispose();

        var author = await ChatAuthors.GetSessionChatAuthor(Session, ChatId, default).ConfigureAwait(true);
        RealtimePlayer = new(Services) {
            Session = Session,
            ChatId = chat.Id,
            IsRealTimePlayer = true,
            SilencedAuthorId = author == null ? Option<AuthorId>.None : Option.Some(author.Id),
        };
        if (wasPlaying) {
            _ = BackgroundTask.Run(
                () => RealtimePlayer.Play(),
                Log, "Realtime playback failed");
        }
        StateHasChanged();
    }

    protected override Task<ChatPageModel> ComputeState(CancellationToken cancellationToken)
        => Service.GetChatPageModel(Session, ChatId.NullIfEmpty() ?? ChatConstants.DefaultChatId, cancellationToken);

    private async Task<VirtualListData<ChatMessageModel>> GetMessages(
        VirtualListDataQuery query,
        CancellationToken cancellationToken)
    {
        var model = await Service.GetChatPageModel(
            Session,
            ChatId.NullIfEmpty() ?? ChatConstants.DefaultChatId,
            cancellationToken);
        var chatId = model.Chat?.Id ?? default;
        if (chatId.IsNone)
            return VirtualListData.New(
                Enumerable.Empty<ChatMessageModel>(),
                _ => "", // Unused anyway in this case
                true, true);

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

        var authorIds = chatEntries.Select(e => e.AuthorId).Distinct();
        var authors = (await Task
            .WhenAll(authorIds.Select(id => ChatAuthors.GetAuthor(chatId, id, true, cancellationToken)))
            .ConfigureAwait(false)
            )
            .Where(a => a != null)
            .ToDictionary(a => a!.Id);

        var chatMessageModels = chatEntries.Select(e => new ChatMessageModel(e, authors[e.AuthorId]!));
        var result = VirtualListData.New(
            chatMessageModels,
            m => m.Entry.Id.ToString(CultureInfo.InvariantCulture),
            startId == chatIdRange.Start,
            endId == chatIdRange.End);
        return result;
    }
}
