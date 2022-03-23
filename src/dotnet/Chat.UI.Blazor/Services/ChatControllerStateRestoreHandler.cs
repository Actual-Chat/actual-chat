using ActualChat.UI.Blazor.Services;
using Blazored.SessionStorage;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatControllerStateRestoreHandler : StateRestoreHandler<string[]>
{
    private readonly ListeningChats _listeningChats;
    private readonly ChatController _chatController;
    private readonly InteractionUI _interactionUi;

    public ChatControllerStateRestoreHandler(
        ListeningChats listeningChats,
        ChatController chatController,
        InteractionUI interactionUi,
        IServiceProvider services)
        : base(services)
    {
        _listeningChats = listeningChats;
        _chatController = chatController;
        _interactionUi = interactionUi;
    }

    protected override string StoreItemKey => "listeningChats";

    protected override async Task Restore(string[]? itemValue)
    {
        var listeningChats = itemValue;
        if (listeningChats == null || listeningChats.Length <= 0)
            return;
        await _interactionUi.RequestInteraction().ConfigureAwait(false);
        var tasks = new List<Task>();
        foreach (var chatId in listeningChats) {
            var task = _chatController.StartRealtimeListening(chatId);
            tasks.Add(task);
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    protected override async Task<string[]> Compute(CancellationToken cancellationToken)
    {
        var chatIds = await _listeningChats.GetChatIds(default).ConfigureAwait(false);
        return chatIds.ToArray();
    }
}
