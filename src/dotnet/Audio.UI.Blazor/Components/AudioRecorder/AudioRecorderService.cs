namespace ActualChat.Audio.UI.Blazor.Components;

public record RecorderStatusChange(string OldChatId, string NewChatId);

public class AudioRecorderService : IDisposable
{
    private Symbol _chatId = "";
    private readonly AudioRecorderController _audioRecorderController;
    private readonly IComputedState<Symbol> _recorderStatusMonitor;
    private readonly AudioRecorderStatus _recorderStatus;

    public event Action<RecorderStatusChange> RecorderStatusChanged = _ => {};

    public AudioRecorderService(AudioRecorderController audioRecorderController,
        AudioRecorderStatus recorderStatus,
        IStateFactory stateFactory)
    {
        _audioRecorderController = audioRecorderController;
        _recorderStatus = recorderStatus;
        _recorderStatusMonitor = stateFactory.NewComputed(
            Symbol.Empty,
            UpdateDelayer.ZeroDelay,
            MonitorRecorderStatus);
    }

    [ComputeMethod]
    public virtual Task<Symbol> GetRecordingChat(CancellationToken cancellationToken = default)
        => Task.FromResult(_chatId);

    public async Task<bool> StartRecording(Symbol chatId)
    {
        if (chatId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(chatId));
        if (string.Equals(_chatId, chatId, StringComparison.Ordinal))
            return false;
        _chatId = chatId;
        using (Computed.Invalidate())
            _ = GetRecordingChat();
        await _audioRecorderController.StartRecording(chatId).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> StopRecording()
    {
        if (_chatId.IsEmpty)
            return false;
        _chatId = Symbol.Empty;
        using (Computed.Invalidate())
            _ = GetRecordingChat();
        await _audioRecorderController.StopRecording().ConfigureAwait(false);
        return true;
    }

    public void Dispose()
        => _recorderStatusMonitor.Dispose();

    private async Task<Symbol> MonitorRecorderStatus(IComputedState<Symbol> state, CancellationToken cancellationToken) {
        var newChatId = await _recorderStatus.GetRecordingChat(cancellationToken).ConfigureAwait(false);
        var oldChatId = state.LatestNonErrorValue;
        if (oldChatId != newChatId) {
            using var _ = Computed.SuspendDependencyCapture();
            var change = new RecorderStatusChange(oldChatId, newChatId);
            RecorderStatusChanged.Invoke(change);
        }
        return newChatId;
    }
}
