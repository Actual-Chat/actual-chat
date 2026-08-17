using ActualChat.UI.Blazor.App.Components.VideoPanel;
using ActualChat.UI.Blazor.Resources;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatVideoUI
{
    /// <summary>
    /// Primary stream kind for a single-value UI surface. ScreenCast takes precedence
    /// when both are active. Prefer <see cref="IsOwnCameraRecording"/> / <see cref="IsOwnScreenCasting"/>
    /// for independent checks.
    /// </summary>
    [ComputeMethod]
    public virtual async Task<VideoSourceKind?> GetOwnSourceKind(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (await IsOwnScreenCasting(chatId, cancellationToken).ConfigureAwait(false))
            return VideoSourceKind.ScreenCast;
        if (await IsOwnCameraRecording(chatId, cancellationToken).ConfigureAwait(false))
            return VideoSourceKind.Camera;
        return null;
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<VideoSourceKind>> GetOwnSourceKinds(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var result = new List<VideoSourceKind>(2);
        if (await IsOwnCameraRecording(chatId, cancellationToken).ConfigureAwait(false))
            result.Add(VideoSourceKind.Camera);
        if (await IsOwnScreenCasting(chatId, cancellationToken).ConfigureAwait(false))
            result.Add(VideoSourceKind.ScreenCast);
        return result;
    }

    [ComputeMethod]
    public virtual async Task<bool> IsOwnCameraRecording(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (!await IsVideoAvailable(chatId, cancellationToken).ConfigureAwait(false))
            return false;

        var recordingChatId = await _recordingChatId.Use(cancellationToken).ConfigureAwait(false);
        return recordingChatId == chatId;
    }

    [ComputeMethod]
    public virtual async Task<bool> IsOwnScreenCasting(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (!await IsVideoAvailable(chatId, cancellationToken).ConfigureAwait(false))
            return false;

        var screenCastChatId = await _screenCastChatId.Use(cancellationToken).ConfigureAwait(false);
        return screenCastChatId == chatId;
    }

    [ComputeMethod]
    public virtual async Task<string?> GetLastVideoRecorderError(CancellationToken cancellationToken = default)
        => await _cameraErrorMessage.Use(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<string?> GetLastVideoRecorderError(VideoSourceKind kind, CancellationToken cancellationToken = default)
        => await GetErrorState(kind).Use(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<bool> IsAnyOwnStreaming(CancellationToken cancellationToken = default)
        => await _recordingChatId.Use(cancellationToken).ConfigureAwait(false) is not null
            || await _screenCastChatId.Use(cancellationToken).ConfigureAwait(false) is not null;

    public void StartScreenCasting(ChatId chatId)
        => _ = StartScreenCastingInternal(chatId);

    public void ResumeVideoStreaming(ChatId chatId)
        => _ = ResumeVideoStreamingInternal(chatId);

    /// <summary>
    /// Stops all of the current user's outgoing streams (camera + screencast).
    /// </summary>
    public void StopStreaming()
    {
        StopRecording();
        StopScreenCasting();
    }

    /// <summary>
    /// Stops the camera stream only. ScreenCast (if any) keeps running.
    /// </summary>
    public void StopRecording()
    {
        _recordingChatId.Value = null;
        ClearRecordingError(VideoSourceKind.Camera);
    }

    /// <summary>
    /// Stops the screencast stream only. Camera (if any) keeps running.
    /// </summary>
    public void StopScreenCasting()
    {
        _screenCastChatId.Value = null;
        ClearRecordingError(VideoSourceKind.ScreenCast);
    }

    public void SetBackgroundBlur(bool enabled)
        => _isBackgroundBlurEnabled.Value = enabled;

    public void OnRecordingStarted(ChatId chatId, VideoSourceKind kind)
    {
        // Clear any previous error (e.g. the user cycled past a failing camera
        // and landed on a working one) so VideoStreamingPreview drops the overlay.
        ClearRecordingError(kind);
        Hub.AnalyticEvents.RaiseVideoStreamStarted(kind);
    }

    public void OnRecordingStopped(VideoSourceKind kind)
    {
        if (kind == VideoSourceKind.ScreenCast)
            StopScreenCasting();
        else
            StopRecording();
    }

    public void OnRecordingError(string error, VideoSourceKind kind)
        => OnRecordingError("", "", error, kind);

    public void OnRecordingError(string code, string arg, string message, VideoSourceKind kind)
    {
        if (kind == VideoSourceKind.ScreenCast && IsScreenCastAlreadyActiveError(message)) {
            ClearRecordingError(VideoSourceKind.ScreenCast);
            _screenCastChatId.Value = null;
            _ = ShowScreenCastAlreadyActiveModal(CancellationToken.None);
            return;
        }

        GetErrorState(kind).Value = Localize(code, arg, message);
        // Camera keeps the session alive — the user can cycle cameras to recover
        // (see VideoRecorder.switchCamera — it restarts from the interrupted state).
        // ScreenCast has no such retry path: a failed getDisplayMedia (user cancel,
        // permission denied) means the user doesn't want to share, so turn the
        // toggle off by clearing the intent.
        if (kind == VideoSourceKind.ScreenCast)
            _screenCastChatId.Value = null;
    }

    // Private methods

    private void StartVideoStreaming(ChatId chatId, string? cameraDeviceId = null, bool isBackgroundBlurEnabled = false)
    {
        _recordingChatId.Value = chatId;
        _lastRecordingChatId.Value = chatId;
        CameraUI.SetSelectedDevice(cameraDeviceId);
        _isBackgroundBlurEnabled.Value = isBackgroundBlurEnabled;
        OpenVideoPanel(chatId);
    }

    private async Task ResumeVideoStreamingInternal(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (!await IsVideoAvailable(chatId, cancellationToken).ConfigureAwait(false))
            return;

        // Resume recording without overwriting camera/blur settings preserved from the previous recording
        _recordingChatId.Value = chatId;
        await OpenVideoPanelInternal(chatId, cancellationToken).ConfigureAwait(false);
    }

    private async Task StartScreenCastingInternal(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (!await IsVideoAvailable(chatId, cancellationToken).ConfigureAwait(false))
            return;

        if (_screenCastChatId.Value == chatId) {
            await ShowScreenCastAlreadyActiveModal(cancellationToken).ConfigureAwait(true);
            return;
        }

        try {
            var activeStreams = await GetActiveVideoStreams(chatId, cancellationToken).ConfigureAwait(true);
            if (activeStreams.Any(s => s.SourceKind == VideoSourceKind.ScreenCast)) {
                await ShowScreenCastAlreadyActiveModal(cancellationToken).ConfigureAwait(true);
                return;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to check active screencast before starting screen share");
        }

        // Additive: does not stop camera.
        _screenCastChatId.Value = chatId;
        OpenVideoPanel(chatId);
    }

    private async Task ShowScreenCastAlreadyActiveModal(CancellationToken cancellationToken)
        => await ModalUI.Show(new ScreenCastAlreadyActiveModal.Model(), cancellationToken).ConfigureAwait(true);

    private string Localize(string code, string arg, string message)
        // Codes come from video-recorder.ts; anything else is browser or server
        // wording we don't own, so it reaches the user untranslated.
        => code switch {
            "cameraUnavailable" => arg.IsNullOrEmpty()
                ? L.Video_CameraUnavailable
                : L.Video_CameraUnavailableNamed_Format(arg),
            "restartRequired" => L.Video_RestartRequired,
            _ => message,
        };

    private static bool IsScreenCastAlreadyActiveError(string error)
        => error.Contains("Another screencast is already active", StringComparison.OrdinalIgnoreCase)
            || error.Contains("screen sharing is already active", StringComparison.OrdinalIgnoreCase)
            || error.Contains("screen share is already active", StringComparison.OrdinalIgnoreCase);

    private MutableState<string?> GetErrorState(VideoSourceKind kind)
        => kind == VideoSourceKind.ScreenCast ? _screenCastErrorMessage : _cameraErrorMessage;

    private void ClearRecordingError(VideoSourceKind kind)
        => GetErrorState(kind).Value = null;
}
