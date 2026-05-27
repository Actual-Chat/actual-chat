using ActualChat.UI.Blazor.Services;
using ActualLab.Resilience;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatVideoUI
{
    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
        var baseChains = new[] {
            AsyncChain.From(SyncCameraLifecycle),
            AsyncChain.From(SyncScreenCastLifecycle),
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
    protected virtual async Task<CameraRecordingIntent?> GetCameraIntent(CancellationToken cancellationToken)
    {
        var chatId = await _recordingChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return null;

        var cameraDeviceId = await CameraUI.GetSelectedDeviceId(cancellationToken).ConfigureAwait(false);
        var blurEnabled = await _isBackgroundBlurEnabled.Use(cancellationToken).ConfigureAwait(false);
        return new CameraRecordingIntent(chatId, cameraDeviceId, blurEnabled);
    }

    [ComputeMethod]
    protected virtual async Task<ScreenCastIntent?> GetScreenCastIntent(CancellationToken cancellationToken)
    {
        var chatId = await _screenCastChatId.Use(cancellationToken).ConfigureAwait(false);
        return chatId is null ? null : new ScreenCastIntent(chatId);
    }

    // Recording lifecycles

    private Task SyncCameraLifecycle(CancellationToken cancellationToken)
        => RunRecorderLifecycle(
            kind: VideoSourceKind.Camera,
            captureIntent: () => GetCameraIntent(cancellationToken),
            startRecorder: (recorder, intent, ct) => StartCamera(recorder, (CameraRecordingIntent)intent, ct),
            updateRecorder: (recorder, intent, ct) => UpdateCamera(recorder, (CameraRecordingIntent)intent, ct),
            cancellationToken);

    private Task SyncScreenCastLifecycle(CancellationToken cancellationToken)
        => RunRecorderLifecycle(
            kind: VideoSourceKind.ScreenCast,
            captureIntent: () => GetScreenCastIntent(cancellationToken),
            startRecorder: (recorder, intent, ct) => recorder.StartScreenCast(
                intent.ChatId, Hub.VideoQualityUI.OutboundDeviceScreencastCap, ct),
            updateRecorder: null,
            cancellationToken);

    private async Task RunRecorderLifecycle<TIntent>(
        VideoSourceKind kind,
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
                        ClearRecordingError(kind);
                        // Reuse a recorder that the JoinVideoCallModal already
                        // warmed up for this chat — the encoder + HW slot stay
                        // live across modal close → intent fire, so the join
                        // transition is just OpenGate (see StartCamera).
                        recorder = kind == VideoSourceKind.Camera
                            ? TryClaimCameraWarmupRecorder(intent.ChatId)
                            : null;
                        if (recorder is null)
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

    private async Task StartCamera(VideoRecorder recorder, CameraRecordingIntent intent, CancellationToken ct)
    {
        await recorder.SetSelectedCamera(intent.CameraDeviceId ?? "", ct).ConfigureAwait(false);
        await recorder.SetBlurEnabled(intent.BlurEnabled, ct).ConfigureAwait(false);
        if (recorder.IsWarmedUp) {
            // Modal-pre-warmed recorder. Flip the gate; no re-acquire,
            // no fresh HW encoder slot.
            await recorder
                .OpenGate(Hub.VideoQualityUI.OutboundDeviceCameraCap, ct)
                .ConfigureAwait(false);
            return;
        }
        await recorder
            .StartRecording(intent.ChatId, Hub.VideoQualityUI.OutboundDeviceCameraCap, ct)
            .ConfigureAwait(false);
    }

    private async Task UpdateCamera(VideoRecorder recorder, CameraRecordingIntent intent, CancellationToken ct)
    {
        // Clear any stale error so VideoStreamingPreview shows the loading spinner
        // (via the .starting class driven by !hasError) instead of the previous
        // failure message while the new camera is being acquired.
        ClearRecordingError(VideoSourceKind.Camera);
        await recorder.SwitchCamera(intent.CameraDeviceId ?? "", ct).ConfigureAwait(false);
        await recorder.ToggleBlur(intent.BlurEnabled, ct).ConfigureAwait(false);
    }

    // Nested types

    protected abstract record RecordingIntent(ChatId ChatId);

    protected sealed record CameraRecordingIntent(ChatId ChatId, string? CameraDeviceId, bool BlurEnabled)
        : RecordingIntent(ChatId);

    protected sealed record ScreenCastIntent(ChatId ChatId) : RecordingIntent(ChatId);
}
