namespace ActualChat.Audio.UI.Blazor.Components;

public class AudioRecorderState
{
    private readonly object _lock = new();
    private Symbol _recordingChatId = "";
    private Symbol _recordingToggleChatId = "";

    [ComputeMethod]
    public virtual Task<Symbol> GetRecordingChatId(CancellationToken cancellationToken = default)
        => Task.FromResult(_recordingChatId);

    [ComputeMethod]
    public virtual Task<Symbol> GetRecordingToggleChatId(CancellationToken cancellationToken = default)
        => Task.FromResult(_recordingToggleChatId);

    public void SetRecordingChatId(Symbol chatId)
    {
        lock (_lock) {
            if (_recordingChatId == chatId)
                return;
            _recordingChatId = chatId;
            _recordingToggleChatId = chatId;
        }
        using (Computed.Invalidate()) {
            _ = GetRecordingChatId();
            _ = GetRecordingToggleChatId();
        }
    }

    public void SetRecordingToggleChatId(Symbol chatId)
    {
        lock (_lock) {
            if (_recordingToggleChatId == chatId)
                return;
            _recordingToggleChatId = chatId;
        }
        using (Computed.Invalidate())
            _ = GetRecordingToggleChatId();
    }
}
