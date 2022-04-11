namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatPageState : WorkerBase
{
    private ChatPlayers? _chatPlayers;

    private IServiceProvider Services { get; }
    private ChatPlayers ChatPlayers => _chatPlayers ??= Services.GetRequiredService<ChatPlayers>();

    public IMutableState<Symbol> ActiveChatId { get; }
    public IMutableState<ImmutableHashSet<Symbol>> PinnedChatIds { get; }

    public ChatPageState(IServiceProvider services)
    {
        Services = services;
        var stateFactory = services.StateFactory();
        ActiveChatId = stateFactory.NewMutable<Symbol>();
        PinnedChatIds = stateFactory.NewMutable(ImmutableHashSet<Symbol>.Empty);
        Start();
    }

    [ComputeMethod]
    public virtual async Task<RealtimeChatPlaybackMode> GetRealtimeChatPlaybackMode(
        bool mustPlayPinned, CancellationToken cancellationToken)
    {
        var activeChatId = await ActiveChatId.Use(cancellationToken).ConfigureAwait(false);
        var chatIds = ImmutableHashSet<Symbol>.Empty;
        chatIds = activeChatId.IsEmpty ? chatIds : chatIds.Add(activeChatId);
        if (mustPlayPinned) {
            var pinnedChatIds = await PinnedChatIds.Use(cancellationToken).ConfigureAwait(false);
            chatIds = chatIds.Union(pinnedChatIds);
        }
        return new RealtimeChatPlaybackMode(chatIds, mustPlayPinned);
    }

    // Protected methods

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var playbackMode = ChatPlayers.PlaybackMode;
        var cRealtimePlaybackMode = await Computed
            .Capture(ct => GetRealtimeChatPlaybackMode(true, ct), cancellationToken)
            .ConfigureAwait(false);

        while (true) {
            await playbackMode
                .When(p => p is RealtimeChatPlaybackMode { IsPlayingPinned: true }, cancellationToken)
                .ConfigureAwait(false);

            var doneTask = playbackMode
                .When(p => p is not RealtimeChatPlaybackMode { IsPlayingPinned: true }, cancellationToken);
            while (true) {
                if (!cRealtimePlaybackMode.IsConsistent())
                    cRealtimePlaybackMode = await cRealtimePlaybackMode.Update(cancellationToken).ConfigureAwait(false);
                if (playbackMode.Value is not RealtimeChatPlaybackMode { IsPlayingPinned: true } rpm)
                    break;
                playbackMode.Value = cRealtimePlaybackMode.Value ?? rpm;
                var invalidatedTask = cRealtimePlaybackMode.WhenInvalidated(cancellationToken);
                var completedTask = await Task.WhenAny(invalidatedTask, doneTask).ConfigureAwait(false);
                if (completedTask == doneTask)
                    break;
            }
        }
        // ReSharper disable once FunctionNeverReturns
    }
}
