using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatVideoUI
{
    private readonly object _warmupLock = new();
    private VideoRecorder? _cameraWarmupRecorder;
    private ChatId _cameraWarmupChatId;

    public async Task<bool> StartCameraWarmup(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsNone)
            return false;
        lock (_warmupLock) {
            if (_cameraWarmupRecorder is not null && _cameraWarmupChatId == chatId)
                return true;
            if (_cameraWarmupRecorder is not null)
                return false;
        }
        VideoRecorder recorder;
        try {
            recorder = await VideoRecorder.Create(Hub, VideoSourceKind.Camera).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "StartCameraWarmup: failed to create recorder");
            return false;
        }
        lock (_warmupLock) {
            if (_cameraWarmupRecorder is not null) {
                _ = recorder.DisposeAsync().AsTask();
                return false;
            }
            _cameraWarmupRecorder = recorder;
            _cameraWarmupChatId = chatId;
        }
        try {
            await recorder.Warmup(chatId, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "StartCameraWarmup: Warmup failed for chat {ChatId}", chatId);
            lock (_warmupLock) {
                if (ReferenceEquals(_cameraWarmupRecorder, recorder)) {
                    _cameraWarmupRecorder = null;
                    _cameraWarmupChatId = default;
                }
            }
            _ = recorder.DisposeAsync().AsTask();
            return false;
        }
    }

    public async Task CancelCameraWarmup(ChatId chatId)
    {
        VideoRecorder? toDispose;
        lock (_warmupLock) {
            if (_cameraWarmupRecorder is null)
                return;
            if (_cameraWarmupChatId != chatId)
                return;
            toDispose = _cameraWarmupRecorder;
            _cameraWarmupRecorder = null;
            _cameraWarmupChatId = default;
        }
        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await toDispose.CancelWarmup(cts.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "CancelCameraWarmup: CancelWarmup invoke failed");
        }
        try {
            await toDispose.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "CancelCameraWarmup: dispose failed");
        }
    }

    // Consumed once by StateSync when a recording intent arrives. Returns the
    // warm recorder if one exists for the matching chat, transferring
    // ownership to the caller. Returns null otherwise — StateSync falls back
    // to VideoRecorder.Create.
    internal VideoRecorder? TryClaimCameraWarmupRecorder(ChatId chatId)
    {
        lock (_warmupLock) {
            if (_cameraWarmupRecorder is null || _cameraWarmupChatId != chatId)
                return null;
            var claimed = _cameraWarmupRecorder;
            _cameraWarmupRecorder = null;
            _cameraWarmupChatId = default;
            return claimed;
        }
    }
}
