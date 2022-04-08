namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatPageState : WorkerBase
{
    private ChatPlayers? _chatPlayers;

    private IServiceProvider Services { get; }
    private ChatPlayers ChatPlayers => _chatPlayers ??= Services.GetRequiredService<ChatPlayers>();

    public IMutableState<Symbol> ActiveChatId { get; }
    public IMutableState<ImmutableHashSet<Symbol>> PinnedChatIds { get; }
    public IMutableState<bool> IsFocusModeOn { get; }

    public ChatPageState(IServiceProvider services)
    {
        Services = services;
        var stateFactory = services.StateFactory();
        ActiveChatId = stateFactory.NewMutable<Symbol>();
        PinnedChatIds = stateFactory.NewMutable(ImmutableHashSet<Symbol>.Empty);
        IsFocusModeOn = stateFactory.NewMutable<bool>();
        Start();
    }

    [ComputeMethod]
    public virtual async Task<RealtimeChatPlaybackMode> GetRealtimeChatPlaybackMode(CancellationToken cancellationToken)
    {
        var activeChatId = await ActiveChatId.Use(cancellationToken).ConfigureAwait(false);
        var chatIds = ImmutableHashSet<Symbol>.Empty;
        chatIds = activeChatId.IsEmpty ? chatIds : chatIds.Add(activeChatId);
        var isFocusModeOn = await IsFocusModeOn.Use(cancellationToken).ConfigureAwait(false);
        if (!isFocusModeOn) {
            var pinnedChatIds = await PinnedChatIds.Use(cancellationToken).ConfigureAwait(false);
            chatIds = chatIds.Union(pinnedChatIds);
        }
        return new RealtimeChatPlaybackMode(chatIds);
    }

    // Protected methods

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var cRealtimePlaybackMode = await Computed.Capture(GetRealtimeChatPlaybackMode, cancellationToken)
            .ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested) {
            if (!cRealtimePlaybackMode.IsConsistent())
                cRealtimePlaybackMode = await cRealtimePlaybackMode.Update(cancellationToken).ConfigureAwait(false);
            if (ChatPlayers.PlaybackMode.Value is RealtimeChatPlaybackMode rpm)
                ChatPlayers.PlaybackMode.Value = cRealtimePlaybackMode.ValueOrDefault ?? rpm;
            await cRealtimePlaybackMode.WhenInvalidated(cancellationToken).ConfigureAwait(false);
        }
    }
}
