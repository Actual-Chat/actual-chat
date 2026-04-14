using ActualChat.UI.Blazor.Services;
using ActualLab.Resilience;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatVideoUI
{
    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
        var baseChains = new[] {
            AsyncChain.From(SyncRecordingLifecycle),
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
    protected virtual async Task<RecordingIntent?> GetRecordingIntent(CancellationToken cancellationToken)
    {
        var chatId = await _recordingChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return null;

        var isScreencasting = await _isScreencasting.Use(cancellationToken).ConfigureAwait(false);
        if (isScreencasting)
            return new ScreencastIntent(chatId);

        var cameraDeviceId = await _selectedCameraDeviceId.Use(cancellationToken).ConfigureAwait(false);
        var blurEnabled = await _isBackgroundBlurEnabled.Use(cancellationToken).ConfigureAwait(false);
        return new WebcamRecordingIntent(chatId, cameraDeviceId, blurEnabled);
    }

    // Recording lifecycle

    private async Task SyncRecordingLifecycle(CancellationToken cancellationToken)
    {
        var cState = await Computed
            .Capture(() => GetRecordingIntent(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        VideoRecorder? recorder = null;
        (Type, ChatId)? activeKey = null;

        try {
            await foreach (var (intent, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
                var intentKey = intent is null ? ((Type, ChatId)?)null : (intent.GetType(), intent.ChatId);
                if (intentKey != activeKey) {
                    if (recorder is not null) {
                        await CompleteRecording(recorder, cancellationToken).ConfigureAwait(false);
                        recorder = null;
                        activeKey = null;
                    }
                }
                if (intent is null)
                    continue;

                if (recorder is null) {
                    // Recording should start
                    try {
                        _errorMessage.Value = null; // Clear any previous error
                        recorder = await VideoRecorder.Create(Hub).ConfigureAwait(false);
                        // Ensure server clock is synced before recording (TIMING_ANCHOR accuracy)
                        var serverTimeSync = Hub.Services.GetService<ServerTimeSync>();
                        if (serverTimeSync != null)
                            await serverTimeSync.EnsureSynced(cancellationToken).ConfigureAwait(false);

                        if (intent is ScreencastIntent screencastIntent) {
                            await recorder.StartScreencast(screencastIntent.ChatId, cancellationToken).ConfigureAwait(false);
                        } else if (intent is WebcamRecordingIntent webcamIntent) {
                            await recorder.SetSelectedCamera(webcamIntent.CameraDeviceId ?? "", cancellationToken).ConfigureAwait(false);
                            await recorder.SetBlurEnabled(webcamIntent.BlurEnabled, cancellationToken).ConfigureAwait(false);
                            await recorder.StartRecording(webcamIntent.ChatId, cancellationToken).ConfigureAwait(false);
                        }
                        else
                            throw new InvalidOperationException($"Unexpected recording intent: {intent}");

                        activeKey = intentKey;
                    }
                    catch (Exception e) when (e is not OperationCanceledException) {
                        OnRecordingError("Failed to start recording");
                        Log.LogWarning(e, "SyncRecordingLifecycle: failed to start recording");
                    }
                }
                else if (intent is WebcamRecordingIntent cameraIntent) {
                    // Camera recording should be updated
                    try {
                        await recorder.SwitchCamera(cameraIntent.CameraDeviceId ?? "", cancellationToken).ConfigureAwait(false);
                        await recorder.ToggleBlur(cameraIntent.BlurEnabled, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is not OperationCanceledException) {
                        Log.LogWarning(e, "SyncRecordingLifecycle: failed to update camera streaming settings");
                    }
                }
            }
        }
        finally {
            // TODO(DF): to think how to properly handle cancellation
            if (recorder is not null)
                await CompleteRecording(recorder, CancellationToken.None).ConfigureAwait(false);
        }
        return;

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

    // Nested types

    protected abstract record RecordingIntent(ChatId ChatId);

    protected sealed record WebcamRecordingIntent(ChatId ChatId, string? CameraDeviceId, bool BlurEnabled)
        : RecordingIntent(ChatId);

    protected sealed record ScreencastIntent(ChatId ChatId) : RecordingIntent(ChatId);
}
