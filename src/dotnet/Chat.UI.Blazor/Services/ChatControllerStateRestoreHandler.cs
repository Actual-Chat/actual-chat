using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatControllerStateRestoreHandler : StateRestoreHandler<ListenChatInfo[]>
{
    private readonly ListeningChats _listeningChats;
    private readonly ChatController _chatController;
    private readonly IChats _chats;
    private readonly Session _session;
    private readonly InteractionUI _interactionUi;

    public ChatControllerStateRestoreHandler(
        ListeningChats listeningChats,
        ChatController chatController,
        InteractionUI interactionUi,
        IChats chats,
        Session session,
        IServiceProvider services)
        : base(services)
    {
        _listeningChats = listeningChats;
        _chatController = chatController;
        _interactionUi = interactionUi;
        _chats = chats;
        _session = session;
    }

    protected override string StoreItemKey => "listeningChats";

    protected override async Task Restore(ListenChatInfo[]? itemValue)
    {
        var listeningChats = itemValue;
        if (listeningChats == null || listeningChats.Length == 0)
            return;
        listeningChats = await FilterCanRead(listeningChats).ConfigureAwait(false);
        if (listeningChats.Length == 0)
            return;
        if (listeningChats.Any(c => c.Mode == ListenChatMode.Active))
            await _interactionUi.RequestInteraction().ConfigureAwait(false);
        var tasks = new List<Task>();
        foreach (var chatInfo in listeningChats) {
            var task = _chatController.StartRealtimeListening(chatInfo.ChatId, chatInfo.Mode);
            tasks.Add(task);
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    protected override async Task<ListenChatInfo[]> Compute(CancellationToken cancellationToken)
    {
        var chatInfos = await _listeningChats.GetListenChatInfos().ConfigureAwait(false);
        return chatInfos.ToArray();
    }

    private async Task<ListenChatInfo[]> FilterCanRead(ListenChatInfo[] listeningChats)
    {
        var result = new List<ListenChatInfo>();
        foreach (var chatInfo in listeningChats) {
            var permissions = await _chats.GetPermissions(_session, chatInfo.ChatId, default).ConfigureAwait(false);
            if (!permissions.HasFlag(ChatPermissions.Read))
                continue;
            result.Add(chatInfo);
        }
        return result.ToArray();
    }
}
