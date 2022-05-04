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
    public virtual async Task<RealtimeChatPlaybackState> GetRealtimeChatPlaybackState(
        bool mustPlayPinned, CancellationToken cancellationToken)
    {
        var activeChatId = await ActiveChatId.Use(cancellationToken).ConfigureAwait(false);
        var chatIds = ImmutableHashSet<Symbol>.Empty;
        chatIds = activeChatId.IsEmpty ? chatIds : chatIds.Add(activeChatId);
        if (mustPlayPinned) {
            var pinnedChatIds = await PinnedChatIds.Use(cancellationToken).ConfigureAwait(false);
            chatIds = chatIds.Union(pinnedChatIds);
        }
        return new RealtimeChatPlaybackState(chatIds, mustPlayPinned);
    }

    // Protected methods

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var playbackState = ChatPlayers.ChatPlaybackState;
        var cRealtimePlaybackState = await Computed
            .Capture(ct => GetRealtimeChatPlaybackState(true, ct), cancellationToken)
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
                if (playbackState.Value is not RealtimeChatPlaybackState { IsPlayingPinned: true } rpm)
                    break;
                playbackState.Value = cRealtimePlaybackState.Value ?? rpm;
                var invalidatedTask = cRealtimePlaybackState.WhenInvalidated(cancellationToken);
                var completedTask = await Task.WhenAny(invalidatedTask, doneTask).ConfigureAwait(false);
                if (completedTask == doneTask)
                    break;
            }
        }
        // ReSharper disable once FunctionNeverReturns
    }
}
