using ActualChat.Streaming;
using ActualChat.UI.Blazor.Services;
using ActualLab.Resilience;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatVideoUI
{
    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
        var baseChains = new[] {
            AsyncChain.From(SyncWebcamLifecycle),
            AsyncChain.From(SyncScreencastLifecycle),
            AsyncChain.From(MonitorVideoIdleness),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        await (
            from chain in baseChains
            select chain
                .WithTransiencyResolver(TransiencyResolvers.PreferTransient)
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<WebcamRecordingIntent?> GetWebcamIntent(CancellationToken cancellationToken)
    {
        var chatId = await _recordingChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return null;

        var cameraDeviceId = await _selectedCameraDeviceId.Use(cancellationToken).ConfigureAwait(false);
        var blurEnabled = await _isBackgroundBlurEnabled.Use(cancellationToken).ConfigureAwait(false);
        return new WebcamRecordingIntent(chatId, cameraDeviceId, blurEnabled);
    }

    [ComputeMethod]
    protected virtual async Task<ScreencastIntent?> GetScreencastIntent(CancellationToken cancellationToken)
    {
        var chatId = await _screencastChatId.Use(cancellationToken).ConfigureAwait(false);
        return chatId is null ? null : new ScreencastIntent(chatId);
    }

    // Recording lifecycles

    private Task SyncWebcamLifecycle(CancellationToken cancellationToken)
        => RunRecorderLifecycle(
            kind: StreamKind.Webcam,
            captureIntent: () => GetWebcamIntent(cancellationToken),
            startRecorder: (recorder, intent, ct) => StartWebcam(recorder, (WebcamRecordingIntent)intent, ct),
            updateRecorder: (recorder, intent, ct) => UpdateWebcam(recorder, (WebcamRecordingIntent)intent, ct),
            cancellationToken);

    private Task SyncScreencastLifecycle(CancellationToken cancellationToken)
        => RunRecorderLifecycle(
            kind: StreamKind.Screencast,
            captureIntent: () => GetScreencastIntent(cancellationToken),
            startRecorder: (recorder, intent, ct) => recorder.StartScreencast(intent.ChatId, ct),
            updateRecorder: null,
            cancellationToken);

    private async Task RunRecorderLifecycle<TIntent>(
        StreamKind kind,
        Func<Task<TIntent?>> captureIntent,
        Func<VideoRecorder, TIntent, CancellationToken, Task> startRecorder,
        Func<VideoRecorder, TIntent, CancellationToken, Task>? updateRecorder,
        CancellationToken cancellationToken)
        where TIntent : RecordingIntent
    {
        var cState = await Computed
            .Capture(captureIntent, cancellationToken)
            .ConfigureAwait(false);

        VideoRecorder? recorder = null;
        ChatId? activeChatId = null;

        try {
            await foreach (var (intent, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
                var intentChatId = intent?.ChatId;
                if (intentChatId != activeChatId) {
                    if (recorder is not null) {
                        await CompleteRecording(recorder, cancellationToken).ConfigureAwait(false);
                        recorder = null;
                        activeChatId = null;
                    }
                }
                if (intent is null)
                    continue;

                if (recorder is null) {
                    try {
                        _errorMessage.Value = null;
                        recorder = await VideoRecorder.Create(Hub, kind).ConfigureAwait(false);
                        var serverTimeSync = Hub.Services.GetService<ServerTimeSync>();
                        if (serverTimeSync != null)
                            await serverTimeSync.EnsureSynced(cancellationToken).ConfigureAwait(false);
                        await startRecorder(recorder, intent, cancellationToken).ConfigureAwait(false);
                        activeChatId = intent.ChatId;
                    }
                    catch (Exception e) when (e is not OperationCanceledException) {
                        OnRecordingError("Failed to start recording", kind);
                        Log.LogWarning(e, "{Kind} lifecycle: failed to start recording", kind);
                        recorder = null;
                    }
                }
                else if (updateRecorder is not null) {
                    try {
                        await updateRecorder(recorder, intent, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is not OperationCanceledException) {
                        Log.LogWarning(e, "{Kind} lifecycle: failed to update settings", kind);
                    }
                }
            }
        }
        finally {
            // TODO(DF): to think how to properly handle cancellation
            if (recorder is not null)
                await CompleteRecording(recorder, CancellationToken.None).ConfigureAwait(false);
        }

        static async Task CompleteRecording(VideoRecorder recorder, CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var cancellationToken1 = cts.Token;
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            if (!recorder.WhenStopped.IsCompleted)
                await recorder.StopRecording(cancellationToken1).WaitAsync(cancellationToken1).ConfigureAwait(false);
            await recorder.WhenStopped.WaitAsync(cancellationToken1).ConfigureAwait(false);
            await recorder.DisposeAsync().AsTask().WaitAsync(cancellationToken1).ConfigureAwait(false);
        }
    }

    private static async Task StartWebcam(VideoRecorder recorder, WebcamRecordingIntent intent, CancellationToken ct)
    {
        await recorder.SetSelectedCamera(intent.CameraDeviceId ?? "", ct).ConfigureAwait(false);
        await recorder.SetBlurEnabled(intent.BlurEnabled, ct).ConfigureAwait(false);
        await recorder.StartRecording(intent.ChatId, ct).ConfigureAwait(false);
    }

    private async Task UpdateWebcam(VideoRecorder recorder, WebcamRecordingIntent intent, CancellationToken ct)
    {
        // Clear any stale error so VideoStreamingPreview shows the loading spinner
        // (via the .starting class driven by !hasError) instead of the previous
        // failure message while the new camera is being acquired.
        _errorMessage.Value = null;
        await recorder.SwitchCamera(intent.CameraDeviceId ?? "", ct).ConfigureAwait(false);
        await recorder.ToggleBlur(intent.BlurEnabled, ct).ConfigureAwait(false);
    }

    // Nested types

    protected abstract record RecordingIntent(ChatId ChatId);

    protected sealed record WebcamRecordingIntent(ChatId ChatId, string? CameraDeviceId, bool BlurEnabled)
        : RecordingIntent(ChatId);

    protected sealed record ScreencastIntent(ChatId ChatId) : RecordingIntent(ChatId);
}
