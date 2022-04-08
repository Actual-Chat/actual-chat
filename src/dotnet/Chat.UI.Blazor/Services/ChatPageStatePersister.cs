using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassNeverInstantiated.Global
public class ChatPageStatePersister : StatePersister<ChatPageStatePersister.Model>
{
    private readonly Session _session;
    private readonly IChats _chats;
    private readonly ChatPlayers _chatPlayers;
    private readonly ChatPageState _chatPageState;
    private readonly UserInteractionUI _userInteractionUI;

    public ChatPageStatePersister(
        Session session,
        IChats chats,
        ChatPlayers chatPlayers,
        ChatPageState chatPageState,
        UserInteractionUI userInteractionUI,
        IServiceProvider services)
        : base(services)
    {
        _session = session;
        _chats = chats;
        _chatPlayers = chatPlayers;
        _chatPageState = chatPageState;
        _userInteractionUI = userInteractionUI;
    }

    protected override async Task Restore(Model? state, CancellationToken cancellationToken)
    {
        if (state == null)
            return;

        var pinnedChatIds = await Normalize(state.PinnedChatIds).ConfigureAwait(false);
        _chatPageState.PinnedChatIds.Value = pinnedChatIds.ToImmutableHashSet();
        _chatPageState.IsFocusModeOn.Value = state.IsFocusModeOn;

        if (state.IsRealtimePlaybackOn) {
            await _userInteractionUI.RequestInteraction("audio playback").ConfigureAwait(false);
            _chatPlayers.StartRealtimePlayback();
        }
    }

    protected override async Task<Model> Compute(CancellationToken cancellationToken)
    {
        var pinnedChatIds = await _chatPageState.PinnedChatIds.Use(cancellationToken).ConfigureAwait(false);
        var isFocusModeOn = await _chatPageState.IsFocusModeOn.Use(cancellationToken).ConfigureAwait(false);
        var chatPlayback = await _chatPlayers.PlaybackMode.Use(cancellationToken).ConfigureAwait(false);
        return new Model() {
            PinnedChatIds = pinnedChatIds.ToArray(),
            IsFocusModeOn = isFocusModeOn,
            IsRealtimePlaybackOn = chatPlayback is RealtimeChatPlaybackMode,
        };
    }

    private async Task<Symbol[]> Normalize(Symbol[] chatIds)
    {
        var permissionTasks = chatIds.Select(async chatId => {
            var permissions = await _chats.GetPermissions(_session, chatId, default).ConfigureAwait(false);
            return (chatId, permissions);
        });

        var permissionTuples = await Task.WhenAll(permissionTasks).ConfigureAwait(false);
        var result = new List<Symbol>();
        foreach (var (chatId, permissions) in permissionTuples) {
            if (!permissions.HasFlag(ChatPermissions.Read))
                continue;
            result.Add(chatId);
        }
        return result.ToArray();
    }

    public sealed record Model
    {
        public Symbol[] PinnedChatIds { get; init; } = Array.Empty<Symbol>();
        public bool IsRealtimePlaybackOn { get; init; }
        public bool IsFocusModeOn { get; init; }
    }
}
