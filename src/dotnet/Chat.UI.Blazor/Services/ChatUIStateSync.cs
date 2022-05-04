namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatUIStateSync : WorkerBase
{
    // All properties are resolved in lazy fashion because otherwise we'll get a dependency cycle
    private ChatUI? _chatUI;
    private ChatPlayers? _chatPlayers;

    private IServiceProvider Services { get; }
    private ChatPlayers ChatPlayers => _chatPlayers ??= Services.GetRequiredService<ChatPlayers>();
    private ChatUI ChatUI => _chatUI ??= Services.GetRequiredService<ChatUI>();

    public ChatUIStateSync(IServiceProvider services)
        => Services = services;

    // Protected methods

    protected override Task RunInternal(CancellationToken cancellationToken)
        => Task.WhenAll(
            SyncPlaybackState(cancellationToken));

    private async Task SyncPlaybackState(CancellationToken cancellationToken)
    {
        var playbackState = ChatPlayers.ChatPlaybackState;
        var cRealtimePlaybackState = await Computed
            .Capture(ct => ChatUI.GetRealtimeChatPlaybackState(true, ct), cancellationToken)
            .ConfigureAwait(false);

        while (true) {
            await playbackState
                .When(p => p is RealtimeChatPlaybackState { IsPlayingPinned: true }, cancellationToken)
                .ConfigureAwait(false);

            var doneTask = playbackState
                .When(p => p is not RealtimeChatPlaybackState { IsPlayingPinned: true }, cancellationToken);
            while (true) {
                if (!cRealtimePlaybackState.IsConsistent())
                    cRealtimePlaybackState = await cRealtimePlaybackState.Update(cancellationToken).ConfigureAwait(false);
                if (playbackState.Value is not RealtimeChatPlaybackState { IsPlayingPinned: true } rcps)
                    break;
                playbackState.Value = cRealtimePlaybackState.Value ?? rcps;
                var invalidatedTask = cRealtimePlaybackState.WhenInvalidated(cancellationToken);
                var completedTask = await Task.WhenAny(invalidatedTask, doneTask).ConfigureAwait(false);
                if (completedTask == doneTask)
                    break;
            }
        }
        // ReSharper disable once FunctionNeverReturns
    }
}
