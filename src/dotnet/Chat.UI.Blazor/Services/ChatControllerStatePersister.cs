using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassNeverInstantiated.Global
public class ChatControllerStatePersister : StatePersister<ChatListeningInfo[]>
{
    private readonly ListeningChats _listeningChats;
    private readonly ChatController _chatController;
    private readonly IChats _chats;
    private readonly Session _session;
    private readonly UserInteractionUI _userInteractionUI;

    public ChatControllerStatePersister(
        ListeningChats listeningChats,
        ChatController chatController,
        UserInteractionUI userInteractionUI,
        IChats chats,
        Session session,
        IServiceProvider services)
        : base(services)
    {
        _listeningChats = listeningChats;
        _chatController = chatController;
        _userInteractionUI = userInteractionUI;
        _chats = chats;
        _session = session;
    }

    protected override async Task Restore(ChatListeningInfo[]? state, CancellationToken cancellationToken)
    {
        var listeningChats = state;
        if (listeningChats == null || listeningChats.Length == 0)
            return;
        listeningChats = await FilterCanRead(listeningChats).ConfigureAwait(false);
        if (listeningChats.Length == 0)
            return;
        if (listeningChats.Any(c => c.Mode == ChatListeningMode.Active))
            await _userInteractionUI.RequestInteraction().ConfigureAwait(false);
        var tasks = new List<Task>();
        foreach (var chatInfo in listeningChats) {
            var task = _chatController.StartRealtimeListening(chatInfo.ChatId, chatInfo.Mode);
            tasks.Add(task);
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    protected override async Task<ChatListeningInfo[]> Compute(CancellationToken cancellationToken)
    {
        var chatInfos = await _listeningChats.GetChats(cancellationToken).ConfigureAwait(false);
        return chatInfos.ToArray();
    }

    private async Task<ChatListeningInfo[]> FilterCanRead(ChatListeningInfo[] listeningChats)
    {
        var result = new List<ChatListeningInfo>();
        foreach (var chatInfo in listeningChats) {
            var permissions = await _chats.GetPermissions(_session, chatInfo.ChatId, default).ConfigureAwait(false);
            if (!permissions.HasFlag(ChatPermissions.Read))
                continue;
            result.Add(chatInfo);
        }
        return result.ToArray();
    }
}
