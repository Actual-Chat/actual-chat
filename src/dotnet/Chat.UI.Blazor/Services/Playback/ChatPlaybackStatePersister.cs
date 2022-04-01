using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassNeverInstantiated.Global
public class ChatPlaybackStatePersister : StatePersister<ChatPlaybackInfo[]>
{
    private readonly Session _session;
    private readonly IChats _chats;
    private readonly ChatPlayback _chatPlayback;
    private readonly ChatPlaybackState _chatPlaybackState;
    private readonly UserInteractionUI _userInteractionUI;

    public ChatPlaybackStatePersister(
        Session session,
        IChats chats,
        ChatPlayback chatPlayback,
        ChatPlaybackState chatPlaybackState,
        UserInteractionUI userInteractionUI,
        IServiceProvider services)
        : base(services)
    {
        _session = session;
        _chats = chats;
        _chatPlayback = chatPlayback;
        _chatPlaybackState = chatPlaybackState;
        _userInteractionUI = userInteractionUI;
    }

    protected override async Task Restore(ChatPlaybackInfo[]? state, CancellationToken cancellationToken)
    {
        var playbackInfos = state;
        if (playbackInfos == null)
            return;

        playbackInfos = await GetPlayableOnly(playbackInfos).ConfigureAwait(false);
        if (playbackInfos.Length == 0)
            return;

        await _userInteractionUI.RequestInteraction("audio playback").ConfigureAwait(false);
        var tasks = playbackInfos.Select(x => _chatPlayback.StartRealtime(x.ChatId, default));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    protected override async Task<ChatPlaybackInfo[]> Compute(CancellationToken cancellationToken)
    {
        var playbackInfos = await _chatPlaybackState.GetList(cancellationToken).ConfigureAwait(false);
        return playbackInfos.ToArray();
    }

    private async Task<ChatPlaybackInfo[]> GetPlayableOnly(ChatPlaybackInfo[] playbackInfos)
    {
        var result = new List<ChatPlaybackInfo>();
        foreach (var playbackInfo in playbackInfos) {
            var permissions = await _chats.GetPermissions(_session, playbackInfo.ChatId, default).ConfigureAwait(false);
            if (!permissions.HasFlag(ChatPermissions.Read))
                continue;
            result.Add(playbackInfo);
        }
        return result.ToArray();
    }
}
