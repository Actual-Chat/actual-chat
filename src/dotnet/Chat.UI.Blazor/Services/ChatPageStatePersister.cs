using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassNeverInstantiated.Global
public class ChatPageStatePersister : StatePersister<Symbol[]>
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

    protected override async Task Restore(Symbol[]? state, CancellationToken cancellationToken)
    {
        var pinnedChatIds = state;
        if (pinnedChatIds == null)
            return;

        pinnedChatIds = await GetPlayableOnly(pinnedChatIds).ConfigureAwait(false);
        if (pinnedChatIds.Length == 0)
            return;

        await _userInteractionUI.RequestInteraction("audio playback").ConfigureAwait(false);
        _chatPageState.PinnedChatIds.Value = pinnedChatIds.ToImmutableHashSet();
        _chatPlayers.StartRealtimePlayback();
    }

    protected override async Task<Symbol[]> Compute(CancellationToken cancellationToken)
    {
        var pinnedChatIds = await _chatPageState.PinnedChatIds.Use(cancellationToken).ConfigureAwait(false);
        return pinnedChatIds.ToArray();
    }

    private async Task<Symbol[]> GetPlayableOnly(Symbol[] chatIds)
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
}
