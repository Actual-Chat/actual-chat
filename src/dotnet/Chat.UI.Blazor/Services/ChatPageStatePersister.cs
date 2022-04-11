using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatPageStatePersister : StatePersister<ChatPageStatePersister.Model>
{
    private static TimeSpan ChatActivationTimeout { get; } = TimeSpan.FromSeconds(1);

    private readonly Session _session;
    private readonly IChats _chats;
    private readonly ChatPlayers _chatPlayers;
    private readonly AudioRecorder _audioRecorder;
    private readonly ChatPageState _chatPageState;
    private readonly UserInteractionUI _userInteractionUI;

    public ChatPageStatePersister(
        Session session,
        IChats chats,
        ChatPlayers chatPlayers,
        AudioRecorder audioRecorder,
        ChatPageState chatPageState,
        UserInteractionUI userInteractionUI,
        IServiceProvider services)
        : base(services)
    {
        _session = session;
        _chats = chats;
        _chatPlayers = chatPlayers;
        _audioRecorder = audioRecorder;
        _chatPageState = chatPageState;
        _userInteractionUI = userInteractionUI;
    }

    protected override Task Restore(Model? state, CancellationToken cancellationToken)
    {
        if (state == null)
            return Task.CompletedTask;

        // We'll be waiting for chat activation, so let's do the rest as background task
        _ = BackgroundTask.Run(async () => {
            var pinnedChatIds = await Normalize(state.PinnedChatIds).ConfigureAwait(false);
            _chatPageState.PinnedChatIds.Value = pinnedChatIds.ToImmutableHashSet();

            // Let's wait for activation of the last active chat before any further actions
            using var timoutCts = new CancellationTokenSource(ChatActivationTimeout);
            try {
                await _chatPageState.ActiveChatId
                    .When(chatId => chatId == state.ActiveChatId, timoutCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // Intended
            }

            var activeChatId = _chatPageState.ActiveChatId.Value;
            if (activeChatId.IsEmpty || activeChatId != state.ActiveChatId)
                return;

            // Let's try to activate recording first
            if (state.IsRecordingOn) {
                var permissions = await _chats.GetPermissions(_session, activeChatId, cancellationToken).ConfigureAwait(false);
                if (permissions.HasFlag(ChatPermissions.Write)) {
                    await _userInteractionUI.RequestInteraction("audio recording").ConfigureAwait(false);
                    var startRecordingProcess = _audioRecorder.StartRecording(activeChatId);
                    await startRecordingProcess.WhenStarted.ConfigureAwait(false);
                }
            }
            if (state.IsRealtimePlaybackOn) {
                var permissions = await _chats.GetPermissions(_session, activeChatId, cancellationToken).ConfigureAwait(false);
                if (permissions.HasFlag(ChatPermissions.Read)) {
                    await _userInteractionUI.RequestInteraction("audio playback").ConfigureAwait(false);
                    _chatPlayers.StartRealtimePlayback(state.MustPlayPinned);
                }
            }
        }, Log, "Error while restoring ChatPageState");
        return Task.CompletedTask;
    }

    protected override async Task<Model> Compute(CancellationToken cancellationToken)
    {
        var activeChatId = await _chatPageState.ActiveChatId.Use(cancellationToken).ConfigureAwait(false);
        var pinnedChatIds = await _chatPageState.PinnedChatIds.Use(cancellationToken).ConfigureAwait(false);
        var audioRecorderState = await _audioRecorder.State.Use(cancellationToken).ConfigureAwait(false);
        var playbackState = await _chatPlayers.PlaybackState.Use(cancellationToken).ConfigureAwait(false);
        var realtimeChatPlaybackState = playbackState as RealtimeChatPlaybackState;
        return new Model() {
            ActiveChatId = activeChatId,
            PinnedChatIds = pinnedChatIds.ToArray(),
            IsRealtimePlaybackOn = realtimeChatPlaybackState != null,
            MustPlayPinned = realtimeChatPlaybackState?.IsPlayingPinned == true,
            IsRecordingOn = audioRecorderState?.ChatId == activeChatId,
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
        public Symbol ActiveChatId { get; init; }
        public Symbol[] PinnedChatIds { get; init; } = Array.Empty<Symbol>();
        public bool IsRealtimePlaybackOn { get; init; }
        public bool MustPlayPinned { get; init; }
        public bool IsRecordingOn { get; init; }
    }
}
