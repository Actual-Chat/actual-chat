using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatAppActivity(ChatHub chatHub) : AppActivity(chatHub.Services)
{
    // [ComputeMethod]
    protected override async Task<bool> MustBeBackgroundActive(CancellationToken cancellationToken)
    {
        var playbackState = await chatHub.ChatPlayers.PlaybackState.Use(cancellationToken).ConfigureAwait(false);
        if (!ReferenceEquals(playbackState, null))
            return true;

        var activeChats = await chatHub.ActiveChatsUI.ActiveChats.Use(cancellationToken).ConfigureAwait(false);
        foreach (var c in activeChats)
            if (c.IsListening || c.IsRecording)
                return true;

        return false;
    }
}
