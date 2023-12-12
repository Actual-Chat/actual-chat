using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatAppActivity(ChatUIHub hub) : AppActivity(hub)
{
    // [ComputeMethod]
    protected override async Task<bool> MustBeBackgroundActive(CancellationToken cancellationToken)
    {
        // ReSharper disable once LocalVariableHidesPrimaryConstructorParameter
        var hub = (ChatUIHub)Hub;
        var playbackState = await hub.ChatPlayers.PlaybackState.Use(cancellationToken).ConfigureAwait(false);
        if (!ReferenceEquals(playbackState, null))
            return true;

        var activeChats = await hub.ActiveChatsUI.ActiveChats.Use(cancellationToken).ConfigureAwait(false);
        foreach (var c in activeChats)
            if (c.IsListening || c.IsRecording)
                return true;

        return false;
    }
}
