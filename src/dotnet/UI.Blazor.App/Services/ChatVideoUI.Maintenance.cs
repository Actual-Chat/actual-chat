namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatVideoUI
{
    [ComputeMethod]
    protected virtual async Task<ChatId[]> GetChatsInMaintenance(CancellationToken cancellationToken)
    {
        var cameraChatId = await _recordingChatId.Use(cancellationToken).ConfigureAwait(false);
        var screenChatId = await _screenCastChatId.Use(cancellationToken).ConfigureAwait(false);
        var watchingChatId = await _watchingChatId.Use(cancellationToken).ConfigureAwait(false);
        var result = new List<ChatId>();
        foreach (var chatId in new[] { cameraChatId, screenChatId, watchingChatId }.SkipNullItems().Distinct()) {
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
                if (_recordingChatId.Value == chatId)
                    StopRecording();
                if (_screenCastChatId.Value == chatId)
                    StopScreenCasting();
                if (_watchingChatId.Value == chatId)
                    SetWatching(null);
            }
    }
}
