using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class BackgroundActivities(ChatHub chatHub) : SafeAsyncDisposableBase, IBackgroundActivities
{
    protected override Task DisposeAsync(bool disposing)
        => Task.CompletedTask;

    // [ComputeMethod]
    public virtual async Task<bool> IsActiveInBackground(CancellationToken cancellationToken)
    {
        var playbackState = await chatHub.ChatPlayers.PlaybackState.Use(cancellationToken).ConfigureAwait(false);
        var activeChats = await chatHub.ActiveChatsUI.ActiveChats.Use(cancellationToken).ConfigureAwait(false);
        return activeChats.Any(ac => ac.IsListening || ac.IsRecording) || !ReferenceEquals(playbackState, null);
    }
}
