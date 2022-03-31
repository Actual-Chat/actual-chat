using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassNeverInstantiated.Global
public class AudioRecorderStatePersister : StatePersister<string>
{
    private readonly AudioRecorder _audioRecorder;
    private readonly AudioRecorderState _audioRecorderState;
    private readonly UserInteractionUI _userInteractionUI;
    private readonly IChats _chats;
    private readonly Session _session;

    public AudioRecorderStatePersister(
        AudioRecorder audioRecorder,
        AudioRecorderState audioRecorderState,
        UserInteractionUI userInteractionUI,
        IChats chats,
        Session session,
        IServiceProvider services)
        : base(services)
    {
        _audioRecorder = audioRecorder;
        _audioRecorderState = audioRecorderState;
        _chats = chats;
        _session = session;
        _userInteractionUI = userInteractionUI;
    }

    protected override async Task Restore(string? state, CancellationToken cancellationToken)
    {
        var recordingChatId = state;
        if (string.IsNullOrEmpty(recordingChatId))
            return;

        var permissions = await _chats.GetPermissions(_session, recordingChatId, default).ConfigureAwait(false);
        if (!permissions.HasFlag(ChatPermissions.Write))
            return;

        await _userInteractionUI.RequestInteraction("audio recording").ConfigureAwait(false);
        var startRecordingProcess = _audioRecorder.StartRecording(recordingChatId);
        await startRecordingProcess.WhenStarted.ConfigureAwait(false);
    }

    protected override async Task<string> Compute(CancellationToken cancellationToken)
        => await _audioRecorderState.GetRecordingToggleChatId(cancellationToken).ConfigureAwait(false);
}
