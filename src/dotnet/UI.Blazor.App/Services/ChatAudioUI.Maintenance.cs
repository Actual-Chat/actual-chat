namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatAudioUI
{
    [ComputeMethod]
    protected virtual async Task<ChatId[]> GetChatsInMaintenance(CancellationToken cancellationToken)
    {
        var activeChats = await ActiveChatsUI.ActiveChats.Use(cancellationToken).ConfigureAwait(false);
        var replay = await _replayState.Use(cancellationToken).ConfigureAwait(false);
        var chatIds = activeChats.Select(c => c.ChatId).ToHashSet();
        if (replay is not null)
            chatIds.Add(replay.ChatId);

        var result = new List<ChatId>();
        foreach (var chatId in chatIds) {
            var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
            if (chat?.MaintenanceMode is { } mode && mode != MaintenanceMode.None)
                result.Add(chatId);
        }
        return result.ToArray();
    }

    // Private methods

    private async Task StopChatsInMaintenance(CancellationToken cancellationToken)
    {
        var computed = await Computed.Capture(() => GetChatsInMaintenance(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var state in computed.Changes(cancellationToken).ConfigureAwait(false))
            foreach (var chatId in state.Value) {
                if (_replayState.Value?.ChatId == chatId)
                    StopReplay();
                await ActiveChatsUI.RemoveActiveChat(chatId).ConfigureAwait(false);
            }
    }
}
