using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class BackgroundActivityProvider(ChatHub chatHub): IBackgroundActivityProvider
{
    // [ComputeMethod]
    public virtual async Task<bool> GetIsActive(CancellationToken cancellationToken)
    {
        var activeChats = await chatHub.ActiveChatsUI.ActiveChats.Use(cancellationToken).ConfigureAwait(false);
        return activeChats.Any(ac => ac.IsListening || ac.IsRecording);
    }
}
