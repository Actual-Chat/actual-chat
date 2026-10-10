using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatAudioUI
{
    public async Task<ChatId?> GetForegroundChatId(CancellationToken cancellationToken)
    {
        var recordingChatId = await GetRecordingChatId().ConfigureAwait(false);
        if (recordingChatId is not null)
            return recordingChatId;

        var pttChatIds = await GetPttChatIds(cancellationToken).ConfigureAwait(false);
        var listeningChatIds = await GetListeningChatIds().ConfigureAwait(false);
        var lastIncomingVoiceAt = Hub.VoiceActivityUI.SnapshotLastIncomingVoiceAt();
        var candidates = pttChatIds
            .Where(listeningChatIds.Contains)
            .OrderByDescending(id => lastIncomingVoiceAt.GetValueOrDefault(id))
            .ThenBy(id => id.Value);
        foreach (var chatId in candidates) {
            var player = await GetListeningPlayer(chatId, cancellationToken).ConfigureAwait(false);
            var isPaused = player is not null
                && await player.Playback.IsPaused.Use(cancellationToken).ConfigureAwait(false);
            if (isPaused)
                continue;

            var isPlaying = await IsPlaying(chatId, cancellationToken).ConfigureAwait(false);
            if (isPlaying)
                return chatId;

            var hasIncomingVoice = await HasIncomingRealtimeAudio(chatId, cancellationToken).ConfigureAwait(false);
            if (hasIncomingVoice)
                return chatId;
        }
        return null;
    }

    public async Task NavigateToForegroundChat(ChatId chatId, CancellationToken cancellationToken = default)
    {
        await Hub.WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
        await Hub.Dispatcher.InvokeAsync(async () => {
            var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(true);
            if (chat is null || !chat.Rules.CanRead())
                return;

            await Hub.AutoNavigationUI.NavigateTo(Links.Chat(chatId), AutoNavigationReason.Notification)
                .ConfigureAwait(true);
            Hub.PanelsUI.HidePanels();
        }).ConfigureAwait(false);
    }

    // Private methods

    private async Task NavigateOnForeground(CancellationToken cancellationToken)
    {
        var backgroundState = Services.GetRequiredService<BackgroundStateTracker>().IsBackground;
        var changes = backgroundState.Computed.Changes(cancellationToken);
        var wasBackground = false;
        await foreach (var c in changes.ConfigureAwait(false)) {
            var mustNavigate = wasBackground && !c.Value;
            wasBackground = c.Value;
            if (!mustNavigate || IsPttHeadless)
                continue;

            var chatId = await GetForegroundChatId(cancellationToken).ConfigureAwait(false);
            if (chatId is null || backgroundState.Value)
                continue;

            await NavigateToForegroundChat(chatId, cancellationToken).ConfigureAwait(false);
        }
    }
}
