using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Stl.Fusion.Blazor;
using Stl.Fusion.UI;

namespace ActualChat.Chat.UI.Blazor.Pages;

public partial class ChatPage : ComputedStateComponent<ChatPageModel>
{
    [Inject] private Session Session { get; set; } = default!;
    [Inject] private UICommandRunner Cmd { get; set; } = default!;
    [Inject] private ChatPageService Service { get; set; } = default!;
    [Inject] private IChatService Chats { get; set; } = default!;
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
            _nav.NavigateTo($"/chat/{ChatConstants.DefaultChatId}");
            return;
        }

        await State.Recompute(); // ~ The same happens in base method, so we don't call it
        var chat = State.ValueOrDefault?.Chat;
        if (chat == null || HistoricalPlayer?.ChatId == chat.Id)
            return;

        // ChatId changed, so we must recreate players
        HistoricalPlayer?.Dispose();
        HistoricalPlayer = new (Services) {
            Session = Session,
            ChatId = chat.Id,
            IsRealTimePlayer = false,
        };
        RealtimePlayer?.Dispose();
        RealtimePlayer = new (Services) {
            Session = Session,
            ChatId = chat.Id,
            IsRealTimePlayer = true,
        };
        StateHasChanged();
    }

    protected override Task<ChatPageModel> ComputeState(CancellationToken cancellationToken)
        => Service.GetChatPageModel(Session, ChatId.NullIfEmpty() ?? ChatConstants.DefaultChatId, cancellationToken);

    private async Task<VirtualListData<ChatEntry>> GetMessages(
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
                Enumerable.Empty<ChatEntry>(),
                entry => entry.Id.ToString(CultureInfo.InvariantCulture),
                true,
                true);

        var idLogCover = ChatConstants.IdTiles;
        var chatIdRange = await Chats.GetIdRange(Session, chatId.Value, cancellationToken);
        if (query.InclusiveRange == default)
            query = query with {
                InclusiveRange = new (
                    (chatIdRange.End - idLogCover.MinTileSize).ToString(CultureInfo.InvariantCulture),
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

        var ranges = idLogCover.GetTileCover((startId, endId + 1));
        var entryLists = await Task
            .WhenAll(ranges.Select(r => Chats.GetEntries(Session, chatId.Value, r, cancellationToken)))
            .ConfigureAwait(false);

        var chatEntries = entryLists.SelectMany(entries => entries);
        var result = VirtualListData.New(
            chatEntries.Where(ce => ce.Type == ChatEntryType.Text),
            entry => entry.Id.ToString(CultureInfo.InvariantCulture),
            startId == chatIdRange.Start,
            endId == chatIdRange.End);
        return result;
    }
}
