namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatVideoUI
{
    // Bounded modal sit-time; after this the warmup encoder is torn down
    // to release the HW slot and stop draining battery. Re-warmup happens
    // on the next StartCameraWarmup call if the user re-engages the modal.
    private static readonly TimeSpan WarmupIdleTimeout = TimeSpan.FromMinutes(5);

    private readonly Lock _warmupLock = new();
    private VideoRecorder? _cameraWarmupRecorder;
    private ChatId? _cameraWarmupChatId;
    private string? _cameraWarmupDeviceId;
    private bool _cameraWarmupBlurEnabled;
    private CancellationTokenSource? _cameraWarmupIdleCts;

    public async Task<bool> StartCameraWarmup(
        ChatId chatId,
        string? deviceId,
        bool isBlurEnabled,
        CancellationToken cancellationToken)
    {
        lock (_warmupLock) {
            if (_cameraWarmupRecorder is not null && _cameraWarmupChatId == chatId)
                return true;
            if (_cameraWarmupRecorder is not null)
                return false;
        }
        VideoRecorder recorder;
        try {
            recorder = await VideoRecorder.Create(Hub).ConfigureAwait(false);
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
            _cameraWarmupDeviceId = deviceId;
            _cameraWarmupBlurEnabled = isBlurEnabled;
        }
        // Start each attempt clean: warmup success never fires OnRecordingStarted,
        // so a previous failure's error would otherwise linger and mask a now-working
        // camera. A failed attempt re-sets it via the JS OnRecordingError callback.
        ClearRecordingError(VideoSourceKind.Camera);
        try {
            // Apply settings BEFORE warmup so MediaCapture.captureCameraStream
            // requests the right camera from the start — otherwise the
            // browser picks a default (often back-cam on mobile).
            await recorder.SetSelectedCamera(deviceId ?? "", cancellationToken).ConfigureAwait(false);
            await recorder.SetBlurEnabled(isBlurEnabled, cancellationToken).ConfigureAwait(false);
            await recorder.Warmup(chatId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "StartCameraWarmup: Warmup failed for chat {ChatId}", chatId);
            lock (_warmupLock) {
                if (ReferenceEquals(_cameraWarmupRecorder, recorder)) {
                    _cameraWarmupRecorder = null;
                    _cameraWarmupChatId = default;
                    _cameraWarmupDeviceId = null;
                }
            }
            _ = recorder.DisposeAsync().AsTask();
            return false;
        }
        // Arm the idle timeout. Cancelled by TryClaim or CancelCameraWarmup;
        // otherwise fires after WarmupIdleTimeout and tears down.
        var idleCts = new CancellationTokenSource();
        lock (_warmupLock) {
            _cameraWarmupIdleCts?.Cancel();
            _cameraWarmupIdleCts?.Dispose();
            _cameraWarmupIdleCts = idleCts;
        }
        _ = AutoExpireWarmup(chatId, idleCts.Token);
        return true;
    }

    // Modal preview lets the user change camera/blur. Mirror those changes
    // onto the warmup recorder so it streams the right device on Join.
    // Device change forces a full re-warmup (JS switchCamera bounces into
    // startRecording, which would convert warmup → live). Blur-only changes
    // hot-apply.
    public async Task<bool> UpdateCameraWarmupSettings(
        ChatId chatId,
        string? deviceId,
        bool isBlurEnabled,
        CancellationToken cancellationToken)
    {
        VideoRecorder? recorder;
        string? prevDeviceId;
        bool prevBlurEnabled;
        lock (_warmupLock) {
            if (_cameraWarmupRecorder is null || _cameraWarmupChatId != chatId) {
                recorder = null;
                prevDeviceId = null;
                prevBlurEnabled = false;
            }
            else {
                recorder = _cameraWarmupRecorder;
                prevDeviceId = _cameraWarmupDeviceId;
                prevBlurEnabled = _cameraWarmupBlurEnabled;
                _cameraWarmupDeviceId = deviceId;
                _cameraWarmupBlurEnabled = isBlurEnabled;
            }
        }
        // No live warmup — never started, or a prior attempt failed (e.g. the
        // target camera was busy). Start fresh so a failed switch can recover.
        if (recorder is null)
            return await StartCameraWarmup(chatId, deviceId, isBlurEnabled, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(prevDeviceId, deviceId, StringComparison.Ordinal)) {
            Log.LogInformation(
                "UpdateCameraWarmupSettings: deviceId changed ({Prev} -> {Next}) — re-warming",
                prevDeviceId, deviceId);
            await CancelCameraWarmup(chatId).ConfigureAwait(false);
            return await StartCameraWarmup(chatId, deviceId, isBlurEnabled, cancellationToken).ConfigureAwait(false);
        }
        if (prevBlurEnabled != isBlurEnabled) {
            try {
                await recorder.ToggleBlur(isBlurEnabled, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogWarning(e, "UpdateCameraWarmupSettings: ToggleBlur failed");
            }
        }
        return true;
    }

    private async Task AutoExpireWarmup(ChatId chatId, CancellationToken cancellationToken)
    {
        try {
            await Task.Delay(WarmupIdleTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            return;
        }
        Log.LogInformation(
            "Warmup idle timeout reached for chat {ChatId} — disposing recorder", chatId);
        await CancelCameraWarmup(chatId).ConfigureAwait(false);
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
            _cameraWarmupDeviceId = null;
            _cameraWarmupIdleCts?.Cancel();
            _cameraWarmupIdleCts?.Dispose();
            _cameraWarmupIdleCts = null;
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
            _cameraWarmupDeviceId = null;
            _cameraWarmupIdleCts?.Cancel();
            _cameraWarmupIdleCts?.Dispose();
            _cameraWarmupIdleCts = null;
            return claimed;
        }
    }
}
